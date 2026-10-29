# API HTTP proposta do Operation

**Status:** Proposed; nenhum endpoint desta lista está implementado. Base /api/operation; catálogo /api/catalog/jobs conforme legado, rota final a validar com frontend/gateway. Emitir OpenAPI a partir do código no incremento da API; esta especificação textual não é um Swagger já funcional.

## Revisão de paridade

A [matriz do passo 1](../planning/STEP_1_PARITY.md) prevalece para delimitar o que é herdado e o que é extensão proposta. Em especial, finish com items livres, fila acionável nova e desativação do catálogo não existiam assim no legado. Nos passos 2/3 preservar serviços/peças recebidos da OS e acrescentar insumos; não permitir substituição silenciosa do pedido inicial. HTTP real continua fora desses dois incrementos locais.

## Operação e progresso

| Método/rota | Entrada | Saída / regra |
|---|---|---|
| GET /api/operation/queue?page=1&pageSize=20 | filtros status opcionais | 200 {items,page,pageSize,totalCount}; page>=1, pageSize 1..100 |
| GET /api/operation/service-orders/{id} | GUID OS | 200 snapshot local/status/version/jobs; 404 se desconhecida |
| GET /api/operation/service-orders/{id}/history?page=1&pageSize=20 | GUID OS + paginação | 200 histórico de transições e ações auditadas |
| POST /api/operation/service-orders/{id}/diagnostic/start | body vazio | 200 {status,operationVersion}; DiagnosticStarted |
| POST /api/operation/service-orders/{id}/diagnostic/finish | {items:[{itemType,itemId,quantity}]} | 200 {diagnosticId,status,operationVersion}; resolve preços e gera lineIds/snapshot |
| POST /api/operation/service-orders/{id}/diagnostic/fail | {reason} | 200 {status,operationVersion}; DiagnosticFailed; bloqueia finish/start até cancelamento |
| POST /api/operation/service-orders/{id}/execution/start | body vazio | Só ReadyForExecution; 200 {status,operationVersion}; início físico |
| POST /api/operation/service-orders/{id}/execution/jobs/{lineId}/done | body vazio | Linha Labor aprovada; 200 {lineId,executedOn,operationVersion}; JobExecuted |
| POST /api/operation/service-orders/{id}/execution/finish | body vazio | Só InExecution; 200 {status,operationVersion}; ExecutionFinished |

A falha explícita de diagnóstico leva ao estado local DiagnosticFailed, que bloqueia conclusão/início posterior enquanto a Saga cancela. CancelOperation continua permitido nesse estado. Não interpretar falha transitória de consulta de preço como falha de diagnóstico.

Não haverá endpoint público de aprovar orçamento, reservar estoque, receber autorização externa ou cancelar como compensação. Essas funções seguem por comando da Saga. Se um operador precisar cancelar uma OS, deve solicitar ao OS Service, que coordena o processo.

## Idempotência e concorrência HTTP

Escritas operacionais exigem Idempotency-Key como GUID e If-Match com versão devolvida no ETag/consulta. Ausência de precondição -> 428; versão divergente -> 412. Persistir chave, rota/OS/ator, hash semântico do corpo e resultado na mesma transação de estado/outbox.

Repetição da MESMA intenção e mesmo corpo retorna resultado original sem repetir efeito/evento, mesmo se a operação já avançou; consultar journal antes de validar If-Match novamente. Reutilizar chave com rota/corpo/ator diferente -> 409. Novo pedido para job já concluído devolve resultado estável sem nova execução. Cliques com versões concorrentes precisam recarregar estado quando houver conflito.

Quem define autor é a autenticação, não o body. API retorna após commit local, sem prometer que OS global já recebeu evento. Resposta 200 não significa pagamento/estoque processados no mesmo request. Não aceitar preço do cliente no finish.

## Fila alvo

InExecution primeiro; ReadyForExecution em seguida; InDiagnostic; Received. Depois CreatedAt e ID para desempate determinístico. AwaitingExecutionAuthorization (diagnóstico pronto aguardando comercial) aparece em consulta/filtro próprio, fora da fila acionável. Esta ordenação é proposta de evolução da fila: o código atual ainda mantém prioridade herdada com AwaitingApproval/WaitingParts. Validar UX antes de trocar.

## Catálogo de mão de obra

Propor GET /api/catalog/jobs e /{id}; POST para criar; PUT /{id} para atualizar; DELETE /{id} para desativar. DTO: id,name,description,priceCents,isActive. Desativar bloqueia novos diagnósticos; não remove snapshots existentes. Escritas de catálogo autenticadas, validação/concorrência próprias; definir permissão de manutenção com frontend, sem presumir papel já emitido.

## Erros e segurança

ProblemDetails com código estável e traceId: 400 validação; 401 sem credencial válida; 403 sem autorização staff; 404 operação inexistente; 409 conflito de estado/intenção; 412 versão; 428 precondição; 503 dependência de preço indisponível. Não retornar stack trace, JWT ou segredos.

Validar staff por política explícita acordada na RFC-002; RequireAuthorization sozinho não distingue cliente de mecânico. Autenticação de integração de preço é questão separada. Rate limit/CORS na borda não substituem autorização na API.

## Suporte local implementado no passo 3

OperationService e OperationQueries agora fornecem ações/queries por interfaces. Nenhuma rota deste arquivo foi exposta em HTTP. O host futuro deve mapear Idempotency-Key para CommandContext.IntentId, ator de identidade confiável e If-Match para ExpectedVersion (obrigatoriedade HTTP continua no host; comandos de broker podem omitir versão). Mapear exceções/DTOs e não aceitar preço do cliente. Replay devolve resultado histórico; consulta Get devolve estado atual. Ver [casos de uso](../planning/STEP_3_APPLICATION.md).
