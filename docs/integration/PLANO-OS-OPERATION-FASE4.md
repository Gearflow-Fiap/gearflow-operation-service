# Plano de integração OS–Operation — Fase 4

Data: 11/11/2026. Escopo: análise e plano; nenhuma correção de aplicação ou infraestrutura executada.

## Base e limites

- Operation local: `cbcb156f98c46dbd269f5ec54da6914eddc4f745`.
- OS: HEAD `7d7706b4da2a0c619c1ecb4ec650c5dc4eea5a4f`, com alterações locais preexistentes consideradas na leitura. Congelar/revisar esse diff antes da implementação.
- Legado Fase 3: `C:/WS/gearflow-app`, consultado como referência funcional.
- Enunciado: `C:/Users/miran/Downloads/SOAT - Fase 4 - Tech challenge.pdf`, especialmente páginas 2–6.
- Auditoria RabbitMQ enviada pelo usuário: constatações confrontadas com os arquivos locais. Broker, EKS, Secrets e imagens implantadas não foram inspecionados. Billing e Inventory não foram auditados nesta análise.

Não é possível afirmar integração completa apenas com contratos compatíveis: o aceite exige os dois processos, bancos separados e RabbitMQ real, incluindo falhas e reentrega.

## Exigências acadêmicas que orientam o plano

O enunciado exige pelo menos três microsserviços com repositórios, infraestrutura e bancos próprios; SQL e NoSQL no conjunto; nenhum acesso ao banco alheio; comunicação assíncrona; Saga com compensação; testes unitários em todos, BDD completo, cobertura mínima de 80% por serviço, qualidade no CI e deploy Kubernetes independente. Também exige integração de pagamento com Mercado Pago e manutenção da observabilidade da Fase 3.

A divisão OS/Billing/Operation apresentada no PDF é sugerida. O mínimo de três serviços e as demais exigências são obrigatórios. Este plano cobre OS/Operation; Billing, Inventory, NoSQL, Mercado Pago e as evidências gerais continuam dependências da entrega, sem validação nesta inspeção.

## Diagnóstico atual

| Ponto | Evidência local | Consequência |
|---|---|---|
| Entrada do diagnóstico inválida | OS `SagaOrchestrator.OnOpened` envia os dois arrays vazios; `RequestedService` só tem Id/Description; abertura recebe strings | Operation rejeita pedido sem serviço e encaminha contrato inválido ao tratamento de erro |
| Nomes dos eventos corrigidos no código | Operation `OperationMessaging` chama `ConfigureOperationEventNames`; mapa contém 13 nomes `operation.*` | Achado 2 da auditoria foi resolvido no código local; ainda validar imagem e broker |
| Assinaturas essenciais compatíveis | OS assina diagnóstico concluído/falhou, execução concluída, cancelamento e duas rejeições | Compatibilidade estática dos nomes; não comprova entrega |
| Identidade comercial presumida | OS cria `ApprovedJob(item.LineId, item.LineId)` | Falta provar/persistir identidade real da linha de orçamento; coincidência de GUIDs não comprova vínculo |
| Autorização confundida com início | OS muda para InProgress no pagamento; Operation só autoriza e aguarda ação HTTP humana | Histórico pode anunciar execução antes do início físico |
| Compensação antecipada | OS cancela localmente e enfileira cancelamento, liberação e estorno juntos | Operation pode recusar cancelamento depois de início físico quando efeitos financeiros já ocorreram |
| Falhas de consumo descartadas | OS confirma mensagens ilegíveis, desconhecidas e resultados de negócio recusados; exceções voltam à fila sem limite explícito nesse consumidor | Perda de eventos ou repetição contínua, sem política de reconciliação suficiente |
| Confirmação de publicação insuficiente | Outbox OS marca ProcessedOn após BasicPublish; canal criado sem configuração explícita de publisher confirms; mandatory=false | Falta evidência de aceitação pelo broker antes de considerar enviado |
| Idempotência semântica incompleta na OS | Retornos de sucesso para fatos já aplicados podem disparar novos comandos em PaymentConfirmed/StockReserved/ExecutionFinished | Novo MessageId do mesmo fato pode repetir efeitos; inbox por MessageId não resolve sozinho |
| Base Kubernetes Operation desabilitada | Messaging e Dispatch false; pod sem label de acesso ao broker | Deploy base não assegura participação na Saga; overrides reais desconhecidos |
| Documentação diverge do domínio | Documentação exige todos os jobs executados; `FinalizeExecution` permite concluir sem essa verificação | Definir regra pela paridade e documentar o comportamento verdadeiro |

## Referência funcional da Fase 3

No legado, `ApproveBudgetHandler` reserva estoque e avança para execução quando possível; `ExecuteJobHandler` usa BudgetJobId real; `FinalizeServiceOrderHandler` finaliza e consome estoque. Esse último não verifica se todos os jobs foram marcados executados. Assim, bloquear conclusão por jobs pendentes seria mudança de regra, não correção automática de integração.

Preservar os resultados de negócio: abertura, diagnóstico, orçamento, aprovação, espera/reposição de peças, execução, consumo e entrega. Documentar as mudanças introduzidas pela distribuição, incluindo pagamento antes da autorização e separação entre autorização e início físico. Não compartilhar entidades, DbContext ou tabelas do monólito.

## Plano sequenciado

### 1. Fixar contrato e responsabilidades — ambos

Adotar `Operation.Contracts` e `docs/contracts/runtime-v1` como baseline, não a proposta antiga em `docs/contracts/v1`. Preferir pacote de contratos versionado na OS; alternativa é contrato duplicado com teste obrigatório de equivalência. Manter URNs `GearFlow.Contracts.Operation.V1`, envelope MassTransit, camelCase, enums string, datas UTC, dinheiro em centavos e CorrelationId=ServiceOrderId. MessageId permanece estável em cada reenvio da mesma mensagem; causação identifica o comando correspondente.

OS é dona do ciclo global e orquestradora; Operation é dono de diagnóstico, autorização e progresso físico. Billing é dono de linhas/identidade comercial e pagamento; Inventory é dono da reserva e baixa. Registrar decisão no README/ADR, com diagrama e tabela de contratos.

Aceite: um comando serializado pelo produtor real da OS é desserializado pelo consumidor real do Operation, sem adaptação manual no teste.

### 2. Corrigir abertura — OS, validação preservada no Operation

Trocar serviços de texto por linhas estruturadas com CatalogJobId real e LineId estável; persistir esses campos com migração. Para registros antigos sem catálogo, exigir associação explícita antes do envio, sem fabricar IDs ou inferir por descrição. Catálogo deve ser obtido por API/contrato público, nunca banco alheio. RequestedParts pode continuar vazio quando não há peças solicitadas; quando houver, enviar PartId e quantidade válidos. Validar limite conjunto de 200 linhas e IDs únicos.

ServiceOrderCode é opcional e hoje é montado antes do SaveChanges que gera Code no PostgreSQL: manter null como decisão documentada ou reservar o código antes do outbox, na mesma transação. Não enviar 0.

Aceite: abrir OS com serviço existente produz DiagnosticQueued e operação persistida com as mesmas identidades; payload vazio cai no erro sem efeito parcial.

### 3. Fechar autorização e estados — OS, confirmação no Operation, dependência Billing

Persistir o snapshot exato de DiagnosticFinished, validar identidade, moeda, política e totais. Remover os fallbacks EmptySnapshot/diagnóstico vazio do caminho de integração. Carregar BudgetId, revisão suportada e mapeamento real BudgetJobId→DiagnosticLineId por contrato público do Billing; se ele não fornece isso, tratar como bloqueio externo e ajustar seu contrato. O Operation local suporta apenas revisão 1: rejeitar explicitamente outra revisão até haver suporte.

Enviar StartExecution somente com pagamento confirmado, pré-condições de estoque atendidas e snapshot íntegro. Consumir ExecutionAuthorized e ExecutionStarted para distinguir pagamento, autorização e início físico; manter consulta/histórico coerentes. Considerar DiagnosticQueued/DiagnosticStarted para confirmar recepção e progresso. JobExecuted é necessário se a OS expuser progresso por serviço; não é obrigatório assinar os 13 eventos indiscriminadamente.

ExecutionFinished habilita consumo de estoque; entrega exige StockConsumed quando houve reserva. Definir se conclusão parcial mantém a paridade atual; corrigir docs e testes conforme essa decisão, sem acrescentar bloqueio por interpretação da documentação antiga.

Aceite: vínculo comercial verdadeiro, snapshot idêntico, nenhuma execução anunciada antes do fato físico e nenhuma entrega antes da baixa aplicável.

### 4. Tornar compensação segura — principalmente OS

Persistir estado de cancelamento solicitado, comando pendente e sua identidade. Publicar CancelOperation e aguardar OperationCanceled com causação compatível antes de liberar reserva e solicitar estorno/cancelamento de orçamento. Quando o cancelamento vier espontaneamente do Operation, iniciar apenas as compensações restantes. Registrar confirmação de cada etapa e evitar duplicação.

OperationCancellationRejected não conclui cancelamento: interrompe rollback automático e abre reconciliação/recuperação. Timeout também não comprova cancelamento. Tratar corridas entre início físico, cancelamento e conclusão com estado/versionamento persistente. Após reparo iniciado, recuperação é de negócio; não prometer desfazer trabalho físico.

Aceite: recusa ou timeout não gera ReleaseStock/RefundPayment; confirmação permite cada compensação uma vez; reinício retoma etapas pendentes.

### 5. Garantir entrega e recuperação — ambos

Na OS, habilitar publisher confirms e detectar mensagens sem rota; marcar outbox entregue após confirmação. Coordenar múltiplos dispatchers com lock/claim; aceitar entrega pelo menos uma vez com consumidores idempotentes. Guardar estado/inbox/outbox atomicamente. Só gerar novos comandos quando houver transição/efeito novo, inclusive para eventos equivalentes com outro MessageId.

Validar metadados do envelope e corpo; preservar causação e versão operacional. Tratar evento atrasado por fase/versão, sem assumir ordem global: OperationVersion pode ter lacunas porque a OS assina apenas parte dos fatos. Separar duplicata, evento prematuro, contrato inválido e falha transitória. Usar retry limitado, fila de erro/quarentena e replay documentado, sem ACK silencioso de evento necessário recusado ou requeue infinito. Definir timeout/reconciliação de comandos pendentes, inclusive inválidos que vão para `_error` sem resposta de negócio.

Aceite: queda entre publish e commit, reentrega, evento fora de ordem e mensagem inválida não causam perda silenciosa, repetição financeira ou Saga parada sem diagnóstico.

### 6. Habilitar ambiente — Operation e infraestrutura; conferir OS

Criar perfil/overlay integrado com Messaging__Enabled=true, Messaging__DispatchEnabled=true e RabbitMq__Host/Port/VirtualHost/Username/Password corretos. Conferir mesmo vhost da OS, Secrets individuais, ACLs de leitura/escrita/configuração, label `gearflow.io/rabbitmq-client=true` no pod e autorização do namespace na política real. Validar tipos/durabilidade de exchanges e filas antes do rollout.

A OS já declara exchanges fanout `operation.*`; manter o mapa atual do Operation. Não adicionar bridges ou publicação dupla sem necessidade. Conferir pendências em exchanges antigas e planejar replay/migração controlada se existirem. Readiness e alertas devem expor consumo/despacho desativado ou indisponível. Implantar consumidores/topologia antes de habilitar produtores.

Aceite: dois serviços implantados conectam com suas credenciais e trocam comandos/eventos sem intervenção manual; broker indisponível gera alerta e recuperação.

### 7. Homologar e gerar evidências da Fase 4 — ambos e integração geral

Executar testes com RabbitMQ e bancos reais isolados. O teste precisa publicar pela outbox OS e receber no handler Operation, e fazer o inverso pelo publisher Operation e consumidor OS. Testes unitários separados não substituem essa prova.

| Cenário | Resultado exigido |
|---|---|
| BDD feliz completo | Abertura → diagnóstico → orçamento → aprovação → reserva quando aplicável → Mercado Pago/Billing → autorização → ação física → conclusão → consumo → entrega |
| Sem peças | Nenhuma reserva/baixa artificial; entrega após conclusão válida |
| Estoque insuficiente/reposição | Aguarda peças e retoma sem cobrança/reserva duplicada |
| Falha de orçamento/pagamento/diagnóstico | Compensações confirmadas e histórico explicável |
| Cancelamento antes do reparo | Confirma OperationCanceled antes dos efeitos compensatórios |
| Cancelamento após início e corrida com StartExecution | Recusa reconciliada, sem estorno/liberação indevidos |
| Duplicata, novo ID equivalente e fora de ordem | Sem novos efeitos indevidos; tratamento persistente de evento prematuro |
| Broker/processo indisponível e reinício | Outbox/inbox recuperam e Saga converge |
| Payload inválido, ID reutilizado e snapshot divergente | Erro/rejeição observável, sem efeito parcial; replay seguro |

Gerar cobertura ≥80% por serviço, BDD que realmente atravesse processos, quality gate, pipeline de deploy Kubernetes independente e trace distribuído. Atualizar Swagger/collection, READMEs, diagrama e roteiro do vídeo de até 15 minutos com fluxo feliz, falha/compensação, deploy e rastreamento. Manter checklist separado para SQL+NoSQL, Mercado Pago e branch protection: não estão comprovados por este plano.

## Ordem sugerida de entregas

1. Contrato, ADR de paridade e cenários de aceite.
2. OS: migração/API de serviços estruturados e StartDiagnostic válido.
3. OS + dependência Billing: snapshot, vínculo comercial e estados de autorização/início.
4. OS: cancelamento confirmado, idempotência e recuperação persistente.
5. Ambos: transporte confiável, perfil Kubernetes e observabilidade.
6. Integração real, BDD, cobertura e evidências acadêmicas.

Cada entrega deve incluir seus testes relevantes. Não liberar o fluxo completo antes das correções de compensação e confirmação de entrega.

## Verificação realizada nesta análise

- Leitura dos contratos, orquestrador, handlers, parser, outboxes, topologia, manifestos e documentação local; comparação pontual com abertura/execução/finalização do legado e leitura textual do PDF.
- Operation: 170 testes unitários passaram, nenhum falhou ou foi ignorado.
- OS: comando de teste sem restore retornou sem relatório de execução; não havia project.assets.json no caminho consultado. Resultado inconclusivo, sem alegar aprovação.
- Não foram executados teste integrado entre serviços, validação de cobertura ou deploy. Compatibilidade de runtime continua pendente.
