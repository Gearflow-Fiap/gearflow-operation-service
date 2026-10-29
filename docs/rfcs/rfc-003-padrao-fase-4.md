# RFC-003 — Padrão de integração da fase 4

Status: proposta recomendada por Operation para revisão do grupo, 29/10/2026. Não representa acordo já aprovado por Inventory/Budget/OS. Implementação atual e pendências estão no final.

## Repositórios e responsabilidades

| Componente | Responsabilidade | Não assume |
|---|---|---|
| inventory-service | peças, insumos, preços unitários, saldo e reservas | orçamento, execução, cobrança |
| operation-service | catálogo de mão de obra, diagnóstico com snapshot, fila e execução de linhas aprovadas | pagamento ou reserva de estoque |
| budget-service | orçamento, aprovação/rejeição, cobrança Pix, confirmação e reembolso | execução física e saldo de estoque |
| os-service (alvo ainda necessário) | abertura/consulta da OS, estado global e coordenação persistente da Saga | tabelas internas dos outros serviços |
| infra-broker | provisionar RabbitMQ, usuários/permissões, configuração operacional e persistência | executar Saga ou regra comercial |

Os quatro repositórios citados pelo grupo são três serviços de negócio e um de infraestrutura. Isso ainda deixa sem dono a entrada da OS e a coordenação do fluxo. Recomendação: manter OS separado, como discutido anteriormente, e criar/implementar gearflow-os-service. Não colocar regras de negócio no infra-broker. Se o grupo quiser eliminar OS, será necessária uma decisão explícita sobre qual serviço assume essas responsabilidades, revisando diagramas e contratos antes da implementação.

Cada serviço tem banco próprio, migrations próprias e acesso exclusivo às suas tabelas. Pode compartilhar servidor na demonstração, com databases/usuários distintos. Não compartilhar entidades EF, repositórios ou consultas SQL entre serviços. Identity/Customers podem permanecer temporariamente no legado, com APIs/contratos explícitos; retirar a operação do monólito não exige reescrever autenticação e cadastros nesta mesma entrega.

## Preços e consumíveis

Recomendação: política **per-unit-away-from-zero-v1** para novos diagnósticos da fase 4.

- BRL; preço unitário em centavos inteiros, nunca float.
- Mão de obra: quantidade 1 por linha.
- Peças: quantidade inteira positiva.
- Insumos: quantidade decimal positiva, até três casas. Inventory deve definir a unidade de cada item (litro, kg, unidade etc.); quantidade e preço devem usar a mesma unidade.
- Total de linha = quantidade × preço unitário, arredondado para centavos com midpoint away from zero. Somar os totais já arredondados com verificação de overflow.
- Exemplo: 1,5 L × R$ 20,00/L = R$ 30,00; 0,125 × 101 centavos = 13 centavos.
- Operation consulta fontes autoritativas no backend e congela nome, ID, quantidade, preço, total, moeda, pricedAt e pricingPolicyId no diagnóstico. O cliente envia IDs/quantidades, nunca preço.
- Budget preserva e valida o mesmo snapshot/política; mudança posterior de catálogo não altera orçamento existente. Divergência de total/política é rejeitada explicitamente. Revisões futuras exigem novo ciclo/contrato, não edição silenciosa de snapshot aprovado.
- O legado somava o preço do insumo por linha sem multiplicar quantidade. Não recalcular dados históricos. Não ativar uma política diferente em Budget sem compatibilidade acordada.

## HTTP para consulta, broker para processo

Consulta de preços: Operation lê mão de obra do próprio catálogo e peças/insumos por API Inventory. Usar inicialmente os GET existentes /api/inventory/parts/{id} (priceCents) e /consumables/{id} (unitPriceCents). Endpoint em lote fica para evolução combinada, sem presumir que já existe. Timeout finito, concorrência limitada, cancelamento e nenhum fallback com preço zero ou cache desatualizado silencioso. Indisponibilidade deixa diagnóstico aberto para repetir a intenção; não vira DiagnosticFailed automaticamente.

Fluxo de negócio: RabbitMQ com MassTransit, entrega ao menos uma vez. Cada consumidor possui fila própria; Operation e OS não disputam uma mesma fila dos eventos Budget. Eventos descrevem fatos; comandos têm destinatário lógico único. Contratos públicos tipados e versionados em pacote próprio com schemas/exemplos, sem dependência dos domínios internos. Correlação, causação e IDs idempotentes devem ser definidos nos envelopes antes de conectar o broker.

Outbox transacional em cada produtor e inbox/idempotência nos consumidores. ACK somente após commit; retry limitado e fila de erro para falhas permanentes. Não prometer exactly-once. Orquestrador registra estado e compensações; timeouts não significam automaticamente que um comando não executou.

## Fluxo recomendado e Saga

1. OS aceita pedido e coordena criação da operação.
2. Operation realiza diagnóstico e publica snapshot fechado.
3. Budget gera orçamento; cliente aprova ou rejeita.
4. Após aprovação, OS coordena reserva de estoque e pagamento conforme política acordada.
5. OS autoriza execução somente quando pré-condições comerciais e de estoque estiverem confirmadas. Autorização não inicia o trabalho físico.
6. Funcionário inicia execução no Operation, conclui linhas aprovadas e finaliza.
7. OS reflete o estado global e conclui o processo.

Antes da execução física, falhas podem exigir cancelar orçamento, liberar reserva e reembolsar pagamento já confirmado. Após início físico, não desfazer execução por uma compensação financeira automática; encaminhar para tratamento definido pelo negócio. Estoque/pagamento podem correr em sequência diferente, mas o grupo deve fixar uma e testá-la com compensação. Proposta inicial: reservar antes de criar cobrança para reduzir pagamentos sem peças; cobrança expirada/rejeitada libera a reserva.

Budget é produtor dos nove eventos enviados na imagem: BudgetGenerated, BudgetGenerationFailed, BudgetApproved, BudgetRejected, BudgetCanceled, PaymentRequested, PaymentConfirmed, PaymentFailed e PaymentRefunded. OS/coordenador usa esses fatos para avançar a Saga. Operation não deve interpretar PaymentConfirmed como autorização suficiente: falta saber estoque e demais pré-condições. Operation pode ter projeções desses eventos se houver necessidade concreta; não duplicar a Saga em dois serviços.

## Segurança mínima

Staff: JWT validado por assinatura HS256, issuer, audience e expiração; sub identifica usuário; proposta de nova claim confiável actor_type=staff (customer para cliente). Operation exige staff, obtém ator do token e não aceita identidade no body. Emissor/Lambda precisam alinhar claims; o token atual do monólito não emite actor_type. Até isso ocorrer, configuração obrigatória de claim/valor impede liberar acesso por suposição. security_stamp não equivale a validação remota de revogação; não consultar banco Identity de outro serviço.

Serviço para serviço: proposta JWT curto emitido pela identidade para sub=gearflow-operation-service, actor_type=service, aud=gearflow-inventory-service, scope=inventory.prices.read. Inventory precisa aceitar essa policy nas consultas necessárias, sem conceder escrita de estoque. Emissor e mecanismo de obter/renovar token ainda devem ser implementados/alinhados. Não versionar token ou segredo nem colocar JWT em eventos. Não tratar a configuração como integração já funcionando. Para demo, uma credencial de serviço fornecida pelo ambiente é possível, desde que restrita, temporária e documentada; evitar usar token de funcionário como identidade permanente do serviço.

## Contratos HTTP do Operation

Staff usa GET da operação para obter ETag com versão. POST exige Idempotency-Key GUID e If-Match forte (ex.: "3"). Falta de If-Match: 428; versão divergente: 412; intenção reutilizada com ação/OS/ator/conteúdo divergente: 409. Replay exato retorna resultado original e ETag da época, não o estado atual. Nova consulta obtém estado atual.

Finish diagnostic aceita apenas consumables [{lineId,consumableId,quantity}]; serviços e peças originais vêm da OS. Linha executada é identificada por budgetJobId, preservando vínculo com diagnosticLineId/catalogJobId. Sem endpoints públicos de receber OS, autorizar execução ou compensar cancelamento: entram pelos adapters de integração autorizados.

## Entrega incremental e revisão do grupo

Implementado até incremento 4: núcleo e persistência. Incremento 5A entregou HTTP/staff/erros/precondições/OpenAPI com testes; o 5B acrescentou catálogo PostgreSQL e adapter HTTP Inventory com credencial do ambiente. Ausência de configuração/preços válidos mantém 503, sem preço inventado. Homologação coletiva de política, emissor/claims e credencial real ainda é necessária. Incremento 6 implementa broker e contratos homologados. Infra-broker foi inspecionado nos incrementos 6/7B.1, sem alterações: há divergência de envelope/topologia documentada no kit de integração.

Para o grupo revisar: confirmar OS/Saga, aprovar per-unit-v1 e unidades de insumos, definir emissor/claims/credencial de serviço e validar payloads públicos com exemplos de falha. Mudanças nesses itens devem atualizar esta RFC e ADRs, preservando compatibilidade.


## Revisão 7B.1 — 29/10/2026

Usuário ainda não sabe confirmar o owner OS/Saga. Budget documenta OS separado, mas não substitui decisão coletiva. [Kit para revisão](../integration/README.md) registra fontes, divergências, D-01..D-06 e cenários H-01..H-11. Nenhum acordo pendente foi marcado aprovado. Mensageria Operation já está implementada/validada localmente desde o incremento 6; homologação dos contratos/topologia com outros serviços segue pendente.
