# Casos de uso e organização da aplicação

A avaliação M5 mantém a organização explícita atual. [ADR-014](../architecture/adr-014-casos-de-uso-e-handlers.md) registra as alternativas, o custo conhecido e os motivos para reavaliar handlers no futuro.

## Mapa dos comandos

As rotas abaixo são relativas a /api/operation/service-orders/{id}. Mensagens são os tipos do contrato Operation V1; seus nomes não devem ser confundidos com as ações físicas HTTP.

| Método de OperationService | Arquivo parcial | Entrada atual | Responsabilidade |
|---|---|---|---|
| Receive | Intake | Mensagem StartDiagnostic | Registrar pedido inicial e enfileirar diagnóstico; não iniciar atividade física |
| StartDiagnostic | Diagnostics | POST /diagnostic/start | Registrar início físico do diagnóstico |
| CompleteDiagnostic | Diagnostics | POST /diagnostic/finish | Consultar preços e congelar diagnóstico/snapshot |
| FailDiagnostic | Diagnostics | POST /diagnostic/fail | Registrar motivo de impossibilidade de diagnóstico |
| AuthorizeExecution | Execution | Mensagem StartExecution | Persistir autorização/termos aprovados; não iniciar reparo físico |
| StartExecution | Execution | POST /execution/start | Iniciar trabalho físico após autorização persistida |
| ExecuteJob | Execution | POST /execution/jobs/{budgetJobId}/done | Registrar linha aprovada executada |
| FinalizeExecution | Execution | POST /execution/finish | Concluir execução local após todas as linhas |
| UpdateVehicle | Administration | Mensagem UpdateOperationVehicle | Atualizar veículo conforme invariantes da operação |
| Deactivate | Administration | Mensagem DeactivateOperation | Desativar logicamente com motivo |
| CancelOperation | Administration | Mensagem CancelOperation | Cancelar trabalho permitido ou registrar tombstone antes de recebimento |

Fontes: [OperationService](../../src/Operation.Application/Operations/Commands/OperationService.Pipeline.cs), [rotas](../../src/Operation.Api/Endpoints/Operations/OperationEndpoints.cs) e [adapter de comandos de mensagem](../../src/Operation.Infrastructure/Messaging/OperationCommandHandler.cs). O adapter técnico usa atualmente system:os como ator explícito; isso não confirma o dono coletivo da OS/Saga, ainda pendente no 7B.

OperationQueries reúne Get, List, Queue, History e Durations usando apenas IOperationReadStore. Cadastro de mão de obra permanece em IJobCatalog. Não criar handlers de OperationService para consultas/catálogo apenas para uniformizar nomes.

## Dependências e pipeline

OperationService usa IOperationUnitOfWork, DiagnosticPricer e TimeProvider. O pricer encapsula política explícita e IPriceCatalog; somente CompleteDiagnostic consulta preços. Não há envio ao broker, acesso a HttpContext, EF ou consulta de bancos de outros serviços dentro dos casos de uso.

O fluxo compartilhado em OperationService.Pipeline permanece:

1. Validar contexto/cancelamento e iniciar telemetria.
2. Calcular fingerprint canônico com ação, OS, ator e payload.
3. Buscar receipt: replay exato retorna o resultado histórico; intenção com outro significado falha.
4. Somente sem replay, carregar o estado e conferir ExpectedVersion quando presente.
5. Executar o caso de uso e suas consultas externas necessárias.
6. Produzir resultado, receipt e evento de negócio somente se houve mudança.
7. Conferir cancelamento e chamar Commit uma vez com versão armazenada para CAS, estado, receipt e eventos.
8. O adapter garante a transação; registrar resultado da telemetria e devolver o resultado vencedor do commit.

O receipt é gerado também para nova intenção sem mudança; o evento de negócio depende de StateChanged. A validação de shape da entrada e a normalização de alguns argumentos ocorrem antes desse pipeline, como antes. Replay precede versão e consultas externas, não significa que um corpo HTTP inválido deixa de ser validado.

A inbox de transporte possui suas próprias fases e confirmações. O único commit descrito acima é o contrato de efeito da aplicação; não é uma afirmação de que toda entrega RabbitMQ usa uma única transação incluindo inbox. O adapter recupera a janela entre registro de inbox, efeito atômico da aplicação e conclusão da inbox com o receipt/idempotência existentes.

## Critérios para evolução

Manter as partes na mesma classe enquanto os comandos continuarem coesos sobre a mesma operação/pipeline. Se um comando adquirir coordenação ou dependências independentes, avaliar sua extração com um executor compartilhado e testes de equivalência. DiagnosticPricer resolvido no serviço completo é um custo conhecido; necessidade de isolar falhas de configuração de preço nos demais comandos é um gatilho concreto para reavaliação.

Uma extração deve reduzir dependências ou responsabilidades observáveis. Não duplicar Execute, criar transação em cada handler, usar nomes do contrato como ação física, nem mover autorização do funcionário para consumers. MediatR só será avaliado se seu papel no dispatch/behaviors tiver utilidade concreta além do encaminhamento de chamadas.

## Evidências já existentes no código

| Garantia | Cenário de teste existente |
|---|---|
| Replay histórico antes de versão/preço | OperationTests.ReplayReturnsOriginalResultBeforeVersionCheckOrExternalRead |
| Reutilização de intenção com outro significado | OperationTests.ReusingIntentWithDifferentMeaningIsRejected |
| Falha no commit preserva estado/receipt/evento | OperationTests.CommitFailureOnLoadedOperationDoesNotPersistMutationReceiptOrEventAndCanRetry |
| Repetição sem novo preço/progresso | OperationTests.NewIntentForAlreadyPerformedWorkDoesNotDuplicateProgressOrReprice |
| Cancelamento e versão obsoleta | OperationTests.StaleVersionAndCancellationTokenLeaveNoSuccessfulReceipt |
| Caminho HTTP mantém ator e replay | HttpTests.HttpDiagnosticFlowPersistsAuthenticatedActorAndReturnsHistoricalReplay |
| Fronteiras das camadas | LayerDependencyTests.ApplicationDoesNotDependOnTransportPersistenceOrHost |

Testes de aplicação ficam em [OperationTests](../../tests/Operation.UnitTests/Application/OperationTests.cs). A evidência M5 informa o que foi reexecutado nesta etapa documental; a regressão completa do M4 permanece referenciada separadamente. Nenhum teste de reflexão foi criado para obrigar um framework ou impedir uma evolução arquitetural legítima.
