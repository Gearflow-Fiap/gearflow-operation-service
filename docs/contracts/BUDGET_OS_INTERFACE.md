# Interface proposta com Budget e OS

**Status:** Proposed. Os campos abaixo são baseline para o responsável Budget revisar; não há código Budget verificado. Não alterar a integração Mercado Pago com base neste arquivo sem consultar a documentação oficial e implementar testes do provedor. Não define URLs/status específicos da API externa.

## Responsabilidades

OS fornece customer snapshot mínimo ao Budget e coordena próximos passos. Operation publica DiagnosticFinished; não envia GenerateBudget por conta própria. Budget é dono de aprovação/pagamento e confirma fatos ao OS, sem autorizar reparo ou escrever no banco Operation.

Proposta para novos tipos Billing: GearFlow.Contracts.Billing.V1, a ratificar junto ao pacote comum da RFC-003. Campos do corpo em camelCase. Todos levam correlationId (OS), messageId e occurredOn UTC. Eventos levam causationId (comando, intenção HTTP ou identificador GUID de processamento persistido do webhook), não operationVersion. Budget pode ter sua própria versão de agregado, não reutilizar versão Operation.

## DTOs comuns

- CustomerSnapshot: customerId GUID, name string não vazia, email string válida; OS obtém por API/snapshot confiável. Apenas Budget recebe esses dados; não propagar ao Operation.
- Items: CommercialLine[] exatamente como DiagnosticFinished, com lineId estável, quantidades, preços e totais. Budget valida e preserva; não substitui preço por valor do cliente.
- Currency=BRL; BudgetRevision=1; v1 sem revisões/descontos/aprovação parcial.
- Campos reason/code são códigos de negócio estáveis e descrição sem stack trace/dados sensíveis. IDs e montantes não vazios/não negativos; snapshot não vazio e total checado.

## Comandos

| Tipo | Campos além dos comuns | Fila proposta | Semântica |
|---|---|---|---|
| GenerateBudget | diagnosticId, customer (CustomerSnapshot), currency, items (CommercialLine[]), totalPriceCents | billing-generate-budget | Criar orçamento único da OS com snapshot; retornar mesmo resultado para intenção repetida |
| RequestPayment | budgetId, budgetRevision=1, amountCents, currency | billing-request-payment | Cobrar exatamente total do orçamento aprovado; divergência rejeitada |
| CancelBudget | reason | billing-cancel-budget | Cancelar orçamento da OS ou criar fence se ainda não existe; não iniciar nova cobrança |
| RefundPayment | reason | billing-refund-payment | Reconciliar pagamentos da OS e estornar confirmado/cancelar pendente, com confirmação segura |

CancelBudget/RefundPayment são dirigidos por correlationId para funcionarem antes de existir BudgetId/PaymentId conhecido pelo OS (mensagens fora de ordem). IDs internos são retornados nos eventos. Não assumir sucesso só por não encontrar pagamento naquele instante: callback/confirmação tardia precisa permanecer sob fence e ser reconciliada.

## Eventos

| Tipo | Campos além dos comuns | Semântica |
|---|---|---|
| BudgetGenerated | diagnosticId, budgetId, budgetRevision=1, totalPriceCents, currency | Orçamento persistido; envio de e-mail pode ter outbox própria |
| BudgetApproved | budgetId, budgetRevision=1 | Decisão autenticada/validada do cliente; somente OS solicita cobrança |
| BudgetRejected | budgetId, budgetRevision=1, reason | Rejeição registrada |
| BudgetGenerationFailed | diagnosticId, code, reason | Falha de negócio definitiva ao gerar orçamento |
| PaymentRequested | budgetId, paymentId, amountCents, currency | Cobrança criada, não pagamento confirmado |
| PaymentConfirmed | budgetId, paymentId, amountCents, currency | Provedor consultado e montante/moeda/identidade verificados |
| PaymentFailed | budgetId, code, reason | Falha definitiva; paymentId só se existente (campo opcional) |
| BudgetCanceled | reason | Fence comercial estabelecido; cobrar novamente está bloqueado |
| PaymentRefunded | reason | Todas as transações aplicáveis da OS reconciliadas e revertidas/canceladas, ou ausência segura confirmada; paymentId opcional |

Proposta de PaymentRefunded inclui cancelamento de pendente para manter uma confirmação de compensação. O responsável Budget deve validar se prefere eventos distintos PaymentCanceled/RefundNotRequired; nesse caso, alterar state machine e fixtures conjuntamente ANTES do release, não interpretar eventos de forma implícita.

## Casos obrigatórios para o Budget

- Mesmo MessageId não duplica cobrança; repetir semântica também usa chave de negócio/provedor e resultado estável.
- Confirmar pagamento consultando provedor, não confiar apenas em webhook. Duplicatas e eventos fora de ordem não reabrem orçamento cancelado.
- CancelBudget antes de GenerateBudget grava fence; geração atrasada não envia nova cobrança.
- RefundPayment antes de PaymentConfirmed registra intenção de compensar. Confirmação tardia é reconciliada e estornada; não pode ser ignorada nem autorizar execução.
- Falha técnica do reembolso mantém Saga Compensating/RecoveryRequired até resultado conhecido; não publicar PaymentRefunded prematuramente.
- OS correlaciona DiagnosticId/BudgetId/revisão e montante; não iniciar execução por PaymentRequested.

## Contrato do OS para Operation

OS usa exatamente StartDiagnostic, StartExecution e CancelOperation definidos no [contrato Operation](README.md). Os eventos gerados pela API Operation referenciam Idempotency-Key em causationId, não um comando fictício da Saga. O OS pode possuir estados internos adicionais; eles não devem ser copiados para o enum local do Operation.

Para demonstração sem OS pronto, implementar simulador dos comandos/eventos com as mesmas classes do pacote. Simulador não substitui implementação da Saga nem BDD integrado exigido pelo desafio.
