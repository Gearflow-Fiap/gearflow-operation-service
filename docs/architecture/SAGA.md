# Saga de referência e tratamento de falhas

**Status:** Proposed; revisar com OS, Budget e Inventory. Orquestrador proposto: state machine persistida no OS, via MassTransit/RabbitMQ. Um ciclo de negócio por ServiceOrderId nesta v1; não reutilizar uma OS cancelada para nova tentativa comercial. Alterações de orçamento e reabertura ficam para RFC posterior.

## Caminho principal

| Etapa | Ação / evento | Estado conceitual da Saga |
|---|---|---|
| 1 | Abre OS, grava Saga e StartDiagnostic na mesma transação | AwaitingDiagnostic |
| 2 | DiagnosticQueued; mecânico inicia; DiagnosticStarted | InDiagnostic |
| 3 | DiagnosticFinished com snapshot imutável -> GenerateBudget | GeneratingBudget |
| 4 | BudgetGenerated; aguarda BudgetApproved/Rejected | AwaitingApproval |
| 5 | BudgetApproved -> RequestPayment -> PaymentConfirmed | AwaitingPayment |
| 6 | ReserveStock -> StockReserved ou StockInsufficient | ReservingStock / AwaitingStock |
| 7 | StartExecution com snapshot aprovado -> ExecutionAuthorized | ReadyForExecution |
| 8 | API inicia reparo físico -> ExecutionStarted | InExecution |
| 9 | API conclui reparo -> ExecutionFinished | AwaitingStockConsumption |
| 10 | ConsumeStock -> StockConsumed | Finalized |
| 11 | Entrega via API OS | Delivered |

Saga envia StartExecution somente com orçamento/pagamento/reserva confirmados. O participante confia no comando autenticado do orquestrador e valida a identidade e o snapshot local; não consulta três bancos para reconstruir autorização.

Pagamento antes da reserva é hipótese extraída do Budget: pode resultar em reembolso por falta de estoque. Timeouts de aprovação/pagamento/estoque devem ser configuração de negócio aprovada (sem números presumidos aqui). Timeout inclui identificador da etapa esperada; mensagens agendadas antigas são ignoradas após saída da etapa.

## OS sem itens de estoque

Inventory atual exige ReserveStock.Items não vazio. Se o snapshot só contiver mão de obra, a Saga pula ReserveStock e ConsumeStock. Autoriza execução e, ao receber ExecutionFinished, finaliza OS diretamente. Não enviar uma reserva vazia nem esperar um StockConsumed que nunca existirá. No snapshot com estoque, agrupar linhas por ItemType/ItemId e somar quantidades antes de ReserveStock (Inventory rejeita itens repetidos).

## Compensação e disputa com início físico

Antes de reembolsar/liberar estoque, a Saga precisa impedir que um StartExecution atrasado ou o clique do mecânico inicie o reparo:

1. Entrar em Compensating e enviar CancelOperation ao Operation.
2. Aguardar OperationCanceled. O Operation grava estado terminal/tombstone e responde mesmo se nunca foi criado.
3. Depois confirmar ReleaseStock (quando houver reserva possível), cancelamento/reembolso do pagamento e cancelamento do orçamento, em ordem compatível com as ações realmente feitas.
4. Só marcar OS Canceled depois de todas as compensações aplicáveis confirmadas.

CancelOperation e início físico disputam a MESMA linha/versionamento no banco Operation. Se cancelamento vencer, início falha e nenhum reparo é autorizado localmente. Se início vencer, OperationCancellationRejected com código ExecutionAlreadyStarted informa ponto sem volta. Saga entra em RecoveryRequired: não liberar estoque nem estornar automaticamente enquanto reparo físico estiver em curso. Alertar e reconciliar. Timeouts não são prova de cancelamento.

No reparo iniciado/concluído, falha técnica de mensagem/consumo exige retry, DLQ e recuperação; não existe rollback físico automático. Uma política comercial de ressarcimento após reparo seria outro processo, fora desta baseline.

## Matriz de falhas

| Situação | Comportamento esperado |
|---|---|
| Preço indisponível durante finish do diagnóstico | HTTP 503, não mudar estado; repetir mesma intenção HTTP; sem DiagnosticFinished parcial |
| Diagnóstico impossível por regra de negócio | DiagnosticFailed explícito; Saga coordena cancelamento |
| Cliente rejeita / prazo expira antes de reparo | Fence CancelOperation; compensações aplicáveis; aguardar confirmações |
| StockInsufficient | Esperar reposição; StockReplenished dispara nova tentativa de reserva; não iniciar reparo |
| Pagamento confirmado chega durante compensação | Registrar o fato e solicitar refund se aplicável; não retomar caminho feliz |
| Mensagem duplicada | Inbox + regra de negócio; não duplicar trabalho, saldo, orçamento ou cobrança |
| Evento atrasado após estado terminal | Não reabrir Saga/operação; registrar para reconciliação |
| Evento chega fora de ordem | Conferir etapa, IDs de comando e versão; armazenar resultado adiantado válido quando necessário ou reconciliar, sem descartar silenciosamente e travar |
| Serviço reinicia após commit | Outbox entrega pendências; Saga persiste andamento e retoma |
| Falha permanente / contrato inválido | Sem retry infinito; fila de erro/quarentena e alerta; resposta de rejeição só quando mensagem é válida e correlacionável |
| Refund/liberação indisponível | Continuar Compensating; repetir com idempotência; não declarar cancelamento concluído |
| Cancelamento perde corrida com início | RecoveryRequired; não liberar reserva baseada apenas no timeout |

## Entrega, deduplicação e confirmação

Cada passo altera apenas o banco local e registra evento na mesma transação. A entrega é pelo menos uma vez; não prometemos exactly-once fim a fim. Duplicatas com mesmo MessageId não repetem efeitos. Novo comando semanticamente repetido tem regra de negócio estável, responde resultado consistente e não altera snapshot/histórico indevidamente. Consistência global é eventual.

Rejeições de negócio correlacionáveis do Operation publicam OperationCommandRejected e não são tratadas como falha transitória de rede. Broker indisponível após commit mantém mensagem na outbox; exceção de banco antes de commit desfaz estado e evento juntos. Monitorar idade da outbox e Sagas sem progresso.

## Limites dos documentos anteriores

Budget ainda atribui reserva/baixa ao Execution e finaliza direto em ExecutionFinished. Inventory ADR-0003 propõe ConsumeStock/StockConsumed e está Proposed. Esta baseline adota esse passo e acrescenta confirmação de cancelamento antes de liberação para fechar a corrida com reparo físico. Nenhum documento externo foi alterado automaticamente.
