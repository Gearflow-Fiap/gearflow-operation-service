# Passo 3 — casos de uso locais e portas da aplicação

Implementação em 28/10/2026. Escopo: Application + pequeno suporte do domínio a cópia destacada e identidade inicial de veículo. Não há host HTTP, banco, broker ou integração real com os outros serviços. As interfaces de storage/read/catalog têm doubles somente nos testes.

## Casos de uso de escrita

[OperationService](../../src/Operation.Application/OperationService.cs) recebe CommandContext (IntentId, ServiceOrderId, Actor, ExpectedVersion opcional) e executa:

| Método | Responsabilidade |
|---|---|
| Receive | Preservar veículo/serviços/peças/código da OS. Repetição semântica não recria trabalho nem desfaz atualização posterior do veículo |
| StartDiagnostic | Iniciar diagnóstico; repetição no mesmo estado não acrescenta histórico |
| CompleteDiagnostic | Obter preços por porta, aplicar política explícita, preservar pedido inicial e montar snapshot; sem aceitar preços do navegador |
| FailDiagnostic | Registrar impossibilidade de negócio, distinta de falha temporária de preço |
| UpdateVehicle | Aplicar atualização antes de concluir diagnóstico; estado posterior bloqueia troca |
| Deactivate | Soft-delete local prediagnóstico/conclusão, sem confundir com rejeição do orçamento |
| AuthorizeExecution | Conferir snapshot e mapear BudgetJobId; AuthorizationId deve ser a intenção recebida |
| StartExecution | Início físico separado da autorização; não cobra nem reserva estoque |
| ExecuteJob | Registrar progresso da linha do orçamento; repetição não executa outra vez |
| FinalizeExecution | Concluir localmente, sem declarar consumo/entrega global; preserva guard legado de não exigir todos os jobs marcados |
| CancelOperation | Cancelar antes do reparo; quando ainda não existe, criar tombstone e impedir recebimento atrasado |

O backend/consumer deve construir Actor de identidade confiável; não usar ator livre do body de usuário. Autorização externa de pagamento/reserva permanece responsabilidade da coordenação. Código não assina os nove eventos financeiros para implementar uma segunda Saga.

## Pipeline de intenção e gravação

1. Validar contexto/cancelamento e copiar entrada; gerar fingerprint canônico SHA-256 de ação, OS, ator e dados relevantes.
2. Consultar receipt por IntentId. Mesmo fingerprint retorna CommandResult original ANTES de validar versão ou consultar catálogo. Reuso da chave com outra ação/OS/ator/body é conflito.
3. Carregar agregado destacado e guardar versão lida; se ExpectedVersion foi fornecida, conferir precondição. Valor 0 significa esperar ausência.
4. Executar validações/caso de uso sobre a cópia; erro não grava sucesso.
5. Construir resultado e evento pendente congelado se houve alteração real. Sem alteração, ainda guardar receipt, sem duplicar histórico/evento de negócio.
6. Enviar OperationWrite ao storage: estado, versão esperada, receipt e pending events. Commit precisa ser atômico, checar versão/ausência e IntentId único. Replay concorrente verifica receipt antes da versão e devolve o resultado vencedor.

[Contrato da unidade de trabalho](../../src/Operation.Application/Ports.cs). **Ainda não há implementação de banco.** IsolatedStore, em tests, modela esse contrato em memória, com cópias e falhas injetáveis; seus resultados não comprovam ACID/índices/outbox de PostgreSQL.

ExpectedVersion é precondição e não entra no hash da intenção. Replay exato retorna resultado histórico, mesmo que a operação tenha avançado; o cliente consulta Get para estado atual. Uma tentativa que falhou antes do commit não deixa receipt e pode ser repetida com versão atualizada. NoOp por nova intenção mantém versão/histórico e armazena confirmação própria.

O fingerprint interno tem versão operation-application-command-v1, ordena coleções por IDs, normaliza quantidade decimal (G29) e datas UTC. Reordenação de linhas ou escala 1.5/1.500 não muda significado. Alterar esse formato no futuro exige migração/compatibilidade com receipts retidos. Não limpar journals/tombstones sem política de replay.

## Repetição semântica versus replay

| Situação | Resultado local |
|---|---|
| Mesmo IntentId + mesmo conteúdo | Resultado original, sem novo commit/efeito/preço; execuções simultâneas podem consultar antes do primeiro commit, mas só uma grava |
| Mesmo IntentId + conteúdo diferente | IntentConflictException |
| Novo IntentId + Receive idêntico | NoOp baseado no veículo INICIAL e linhas iniciais; não reverte VehicleId atualizado |
| Novo IntentId + diagnóstico já concluído, mesmos consumíveis | NoOp sem reprecificar; conteúdo diferente falha |
| Nova autorização com mesmos termos | NoOp, preserva AuthorizationId original; termos diferentes falham |
| Novo pedido para job já executado / execução já concluída | NoOp; não acrescenta progresso/histórico |
| CancelOperation antes de Receive | Tombstone; recebimento/trabalho tardios rejeitados |
| Cancelamento após reparo iniciado | Conflito; nenhum estorno/liberação no Operation |

Receipts confirmam o resultado LOCAL. O futuro adapter de mensageria deverá mapear confirmação/replay para replies/eventos tipados e garantir entrega pela outbox, inclusive confirmação de um comando semanticamente repetido. Não presumir que ausência de novo evento de negócio dispensa confirmação da Saga. O registro atual não é um consumer MassTransit nem uma resposta HTTP já publicada.

## Preços autoritativos e política explícita

[DiagnosticPricer](../../src/Operation.Application/DiagnosticPricer.cs) usa IPriceCatalog. A implementação real deverá consultar Catalog/Inventory por credenciais e APIs apropriadas. O núcleo consulta uma única vez cada par tipo/ID; linhas repetidas continuam distintas. Cotação exige exatamente os itens solicitados, nome válido, moeda BRL, preço >=0 e data dentro da janela do diagnóstico. Itens ausentes, extras, duplicados, datas inválidas e overflow geram erro sem concluir parcialmente. Zero explícito é aceito; preço ausente não vira zero.

Não há política default no construtor do pricer:

| Modo disponível | Insumos | Identificação gravada |
|---|---|---|
| LegacyLinePriceV1 | Total da linha = preço consultado, preservando soma observada no monólito | legacy-consumable-line-v1 |
| PerUnitV1 | Total = quantidade * unitário, arredondado em centavos com midpoint away from zero | per-unit-away-from-zero-v1 |

Para ambos, mão de obra usa uma unidade e peças usam quantidade inteira * unitário. Somas/conversões são checadas contra overflow. As duas políticas permitem testar a divergência; **nenhuma foi escolhida como regra financeira coletiva**. Os testes de fixture usam PerUnitV1 por padrão apenas para montar cenários. Antes de integrar Budget, acordar fórmula/política e contrato real; não configurar um host silenciosamente com uma opção.

PricingUnavailableException indica falha transitória do adapter; InvalidPriceQuoteException indica resposta inválida/incompleta. Cancelamento de request é propagado e rechecado antes do commit. Erro de cotação não publica DiagnosticFailed: esse evento é reservado à falha de negócio explícita do diagnóstico.

## Consultas e DTOs

[OperationQueries](../../src/Operation.Application/OperationQueries.cs) expõe Get, List, Queue, History e Durations. [IOperationReadStore](../../src/Operation.Application/Ports.cs) recebe filtros e paginação normalizada; o adapter real deve executar essas operações no banco, sem carregar todas as OS.

- List: ativas por padrão, filtro opcional de estado, opção explícita de incluir inativas, CreatedAt desc e ID asc.
- Queue: somente acionáveis/ativas, ordem InExecution -> ReadyForExecution -> InDiagnostic -> Received, antiguidade e ID.
- Paginação: page>=1, pageSize entre 1 e 100; offset long sem overflow para páginas grandes. Página vazia mantém total e não equivale a recurso ausente.
- History: OS ausente é falha explícita; itens ordenados por Version e paginados.
- Get: DTO congelado com pedido inicial, diagnóstico, autorização, progresso, histórico, razões e durações. Arrays/listas do agregado não são expostos para mutação.

## Durações, sem efeito externo antes do commit

[DurationCalculator](../../src/Operation.Application/DurationCalculator.cs) separa fila de diagnóstico (CreatedAt->DiagnosticStartedAt), diagnóstico, fila de reparo (ExecutionAuthorizedAt->ExecutionStartedAt) e reparo físico. Fase incompleta retorna null; fase concluída de duração zero conta como amostra válida. Isso é melhoria explícita frente ao cálculo legado que ignorava intervalo zero. Média possui contagem de amostras por fase, não um único contador global.

Resumo considera operações ativas. Finalização->entrega continua no OS. O helper Summarize é referência de regra; adapter SQL deverá agregar no banco. Não há exporter OpenTelemetry/New Relic neste incremento. Calcular por dados persistidos evita duplicar métricas por retry de comando; eventos pendentes carregam a projeção/durações para integração futura.

## Suporte de domínio

WorkOperation.Copy copia listas internas mutáveis e compartilha apenas objetos/coleções imutáveis. InitialVehicleId preserva identidade do recebimento sem impedir UpdateVehicle. Não há serialização de EF/contrato público ou mecanismo de rehidratação de banco implementados.

## Limites e próxima etapa

Passo 3 conclui o fluxo LOCAL por interfaces e testes, não um serviço executável em rede. Próximo incremento: PostgreSQL/EF, journal persistido, CAS/índices, migrations e outbox/inbox com testes contra banco real. Depois API/JWT e clients de preços, contratos compartilhados tipados, RabbitMQ/Saga/BDD, CI/CD/Kubernetes/observabilidade. Manter ADRs/RFCs de integração em revisão coletiva.
