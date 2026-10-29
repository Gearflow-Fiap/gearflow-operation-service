# Contratos de integração Operation v1

**Status:** Proposed, baseline 2026-10-24; revisão coletiva e implementação pendentes. **Artefato:** JSON Schema Draft 2020-12 + exemplos + manifesto. Não há pacote NuGet publicado, consumers ou compatibilidade MassTransit validada neste incremento.

## Revisão de paridade em 28/10/2026

Antes de implementar esta proposta, aplicar as lacunas da [matriz do passo 1](../planning/STEP_1_PARITY.md): entrada sem peças solicitadas, serviços não vazios, atualização de veículo/desativação e identidade de linha. O JSON Schema ainda representa a proposta anterior e não comprova paridade funcional com a fase 3. Nenhum formato externo foi homologado por esta revisão.

## Como usar esta especificação

1. Ler semântica e regras abaixo antes de implementar tipos.
2. Escolher mensagem no [manifesto](v1/manifest.json); seu schema aponta para uma definição em [operation.schema.json](v1/operation.schema.json).
3. Validar o corpo da mensagem contra a definição correspondente, não apenas contra a raiz do bundle (a raiz só declara definições). Validação JSON não substitui regras de negócio.
4. Implementar records tipados no pacote/namespace acordados pela [RFC-003](../rfcs/rfc-003-distribuicao-de-contratos.md), gerar exemplos pelo serializador real e testar envelope/URN.
5. Combinar a publicação do pacote e a revisão de contratos antes de ligar serviços reais.

Os exemplos são fixtures independentes de cada tipo, inclusive caminhos alternativos; não são um log cronológico único. IDs são demonstrativos e não representam cadastros existentes.

## Convenções wire e transporte

- Novos tipos: `GearFlow.Contracts.Operation.V1.<Nome>`, assembly proposto `GearFlow.Contracts`. Não modificar namespace dos contratos Inventory existentes.
- Payload JSON em camelCase, enums string, GUID não vazio, instantes UTC terminados em Z, moeda BRL. Em C#, usar DateTime/DateTimeOffset normalizado em UTC e decimal para quantidade. Identidade CLR/URN e serialização ainda precisam do teste de integração.
- Não inventar wrapper com `type/payload`: schema descreve corpo lógico que o MassTransit colocará no envelope próprio. No broker, preencher MessageId/CorrelationId também nos headers/propriedades do transporte com os valores do corpo; divergência é erro de contrato.
- `correlationId` = ServiceOrderId; não duplicar um segundo campo divergente de OS. `messageId` identifica uma intenção/evento e não é o ID da OS. Reentrega mantém ID e payload.
- Comandos vêm do OS por Send a filas fixas; eventos do Operation por Publish. Saga possui consumidores dos eventos. Não exigir ordem global de mensagens.
- `traceparent`/`tracestate` são metadados de observabilidade no transporte, não credenciais. Eventos não carregam JWT, CPF, e-mail ou endereço. `actorId` é identificação de auditoria derivada de claim/identidade de serviço, nunca autorização fornecida livremente pelo cliente.
- Consumidores toleram campos adicionais opcionais. Campo obrigatório novo, mudança de enum/semântica ou unidade exige revisão de compatibilidade. Não reaproveitar MessageId com conteúdo diferente.

## Campos comuns

| Campo | Comandos | Eventos | Regra |
|---|---|---|---|
| correlationId | obrigatório | obrigatório | GUID da OS; um ciclo comercial nesta v1 |
| messageId | obrigatório | obrigatório | GUID único da mensagem; distinto de correlationId |
| occurredOn | obrigatório | obrigatório | UTC; dado de auditoria, não critério de ordem |
| causationId | não se aplica ao corpo de comando | obrigatório | MessageId do comando causador ou Idempotency-Key da intenção HTTP |
| operationVersion | não se aplica | obrigatório | versão monotônica da operação local; não versão do schema |
| actorId | não se aplica | obrigatório | staff:<id> ou system:<serviço>, auditoria |

Resposta ao comando referencia causationId=MessageId recebido. Eventos produzidos pela API referenciam o GUID de Idempotency-Key persistido com a intenção. Autorização válida em execução aponta para o primeiro StartExecution aceito em authorizationMessageId.

## Mensagens do Operation

| Mensagem | Emissor -> destinatário | Conteúdo específico | Efeito |
|---|---|---|---|
| StartDiagnostic | OS -> Operation | vehicleId, requestedJobIds (pode vazio) | Criar entrada Received e DiagnosticId; não iniciar trabalho humano |
| StartExecution | OS -> Operation | diagnosticId, budgetId, budgetRevision=1, currency, approvedItems | Validar snapshot e autorizar fila; não iniciar reparo físico |
| CancelOperation | OS -> Operation | reason | Fence antes do início; responde confirmação ou rejeição |
| DiagnosticQueued | Operation -> OS | diagnosticId | Confirma inclusão; início ainda pendente |
| DiagnosticStarted | Operation -> OS | diagnosticId | Mecânico iniciou diagnóstico |
| DiagnosticFinished | Operation -> OS | diagnosticId, currency, pricedAt, items, totalPriceCents | Snapshot imutável para GenerateBudget |
| DiagnosticFailed | Operation -> OS | diagnosticId, code, reason | Falha de negócio DiagnosticNotPossible; Saga cancela/compensa |
| ExecutionAuthorized | Operation -> OS | diagnosticId, budgetId, budgetRevision, authorizationMessageId | ReadyForExecution |
| ExecutionStarted | Operation -> OS | diagnosticId, budgetId, authorizationMessageId | Início físico, ponto sem volta |
| JobExecuted | Operation -> OS | diagnosticId, budgetId, lineId | Progresso de uma linha Labor aprovada |
| ExecutionFinished | Operation -> OS | diagnosticId, budgetId, authorizationMessageId, executedLineIds | Conclusão local; não comprova baixa de estoque |
| OperationCanceled | Operation -> OS | reason | Fence confirmado, inclusive sem operação prévia |
| OperationCancellationRejected | Operation -> OS | code, reason | ExecutionAlreadyStarted/Completed: preservar reserva e recuperar |
| OperationCommandRejected | Operation -> OS | commandMessageId, code, reason | Comando válido mas estado/snapshot incompatível |

Os exemplos e schemas especificam obrigatoriedade/tipos de cada campo. `OperationCommandRejected` é resposta ao comando, não erro genérico de infraestrutura. `DiagnosticFailed` é decisão operacional explícita, não falha temporária do preço. V1 não define `ExecutionFailed` que acionaria rollback físico: falha durante reparo permanece visível e recuperável, sem estorno/liberação automáticos. Revisar isso com o documento antigo do Budget.

## CommercialLine e regras além do JSON Schema

`lineId` identifica a linha comercial desde o diagnóstico até orçamento e progresso. `itemId` identifica o cadastro (Job/Part/Consumable). Linhas com mesmo item podem existir com lineId diferente; Inventory exige agregação por tipo/item antes da reserva.

| Campo | Regra |
|---|---|
| lineId | GUID único dentro do diagnóstico; preservado pelo Budget |
| itemType | Labor, Part ou Consumable |
| itemId / name | Identidade e nome snapshotted do catálogo confiável |
| quantity | Labor=1; Part inteiro positivo; Consumable decimal positivo com até 3 casas |
| unitPriceCents | Inteiro >=0, preço por unidade; zero somente explícito no catálogo |
| lineTotalCents | Inteiro >=0, round(quantity*unitPriceCents, midpoint away from zero) |

No máximo 200 linhas por diagnóstico nesta baseline, limite proposto para proteger contratos/consultas. Total de cada linha e do orçamento cabe em int32 não negativo; rejeitar overflow. `totalPriceCents` deve ser soma checada das linhas. Regras de igualdade entre campos, unicidade por lineId e correspondência de snapshots são semânticas, não integralmente expressas pelo schema.

- Operation gera os lineIds; Budget preserva linhas e preços. V1 não aceita desconto, substituição, revisão de diagnóstico/orçamento nem aprovação parcial.
- StartExecution deve ter exatamente o snapshot DiagnosticFinished aceito, com diagnosticId correto; divergência responde SnapshotMismatch. Saga só envia após aprovação, pagamento e reserva (quando aplicável).
- JobExecuted e executedLineIds só referenciam linhas Labor do snapshot aprovado. Mantemos a regra inicial do legado de permitir conclusão explícita sem exigir todos os jobs marcados; executedLineIds mostra o progresso real e não autoriza inferir que toda linha foi marcada. Se o grupo quiser bloquear, decidir como mudança de negócio antes da API.
- Exclusão/alteração posterior de catálogo não muda o snapshot; falha de preço não gera total zero ou snapshot parcial.

## Idempotência, concorrência e mensagens fora de ordem

| Cenário | Regra alvo |
|---|---|
| Mesmo MessageId e mesmo conteúdo | Sem repetir efeito; inbox/journal mantém resultado; não incrementar versão |
| Mesmo MessageId com conteúdo diferente | Quarentena/erro de contrato; nunca aceitar nova intenção com ID reutilizado |
| Novo MessageId, StartDiagnostic idêntico | Confirma mesmo DiagnosticId; não criar nova operação |
| Novo StartDiagnostic com VehicleId/requestedJobIds divergentes | ConflictingCommand, não substituir pedido existente |
| StartExecution repetido, mesmo snapshot | Sem nova autorização nem regressão; mantém authorizationMessageId original |
| StartExecution antes do diagnóstico / snapshot errado | InvalidState / SnapshotMismatch; Saga não deve tentar avançar cegamente |
| CancelOperation antes da criação | Criar tombstone Canceled e responder; criação/autorização atrasada rejeitada |
| CancelOperation repetido | Reconfirmar Canceled sem duplicar histórico ou mudar estado |
| CancelOperation x início físico | Concorrência no mesmo agregado; só um vence; quem perde observa o estado definitivo |
| Evento global atrasado | Saga verifica etapa, IDs e versão; não reabre estado terminal |
| Falha de commit | Estado e outbox não persistem; retry com a mesma intenção |

Journal/inbox deduplicam intenção mesmo após janela técnica do broker conforme retenção definida na operação. No replay exato, preservar a identidade do resultado já registrado e garantir sua entrega pela outbox; duplicatas podem chegar e são inofensivas. Em estado Canceled, todo novo comando de criação/autorização é rejeitado com OperationCanceled, mesmo com snapshot anteriormente válido. Em ExecutionCompleted, repetição válida não reabre a operação. Replay de evento original pode chegar atrasado e não deve reabrir a Saga. Novo comando semanticamente repetido pode gerar nova confirmação com novo MessageId/causationId, mesma versão e mesmo resultado. OperationVersion não define uma ordem exclusiva para confirmações repetidas; Saga valida também a identidade e a causa, não descarta tudo com versão igual.

## Fila e casos de integração

Filas de comandos:

- `operation-start-diagnostic` -> StartDiagnostic.
- `operation-start-execution` -> StartExecution.
- `operation-cancel-operation` -> CancelOperation.

Consumers e filas `_error` com alerta; parâmetros de retry/timeout ainda dependem da configuração acordada. Não usar retry infinito para InvalidState/SnapshotMismatch. Não mandar comandos humanos por broker a partir do frontend.

## Fronteiras externas

- [Inventory existente](INVENTORY_COMPATIBILITY.md): preservar nomes, campos e namespaces; tratar OS sem estoque.
- [Interface proposta para Budget/OS](BUDGET_OS_INTERFACE.md): proposta para o responsável validar, sem afirmar implementação.
- [API HTTP](../development/API_ENDPOINTS.md): caminhos e idempotência propostos.
- [Critérios de testes](../development/TEST_STRATEGY.md): fixtures, contrato, concorrência e BDD.

## Validar os exemplos

Com PowerShell 7, executar `pwsh -File docs/contracts/Validate-Examples.ps1` a partir da raiz do repositório. O script seleciona a definição correta de cada mensagem. Não valida envelope/URN MassTransit nem as regras semânticas entre campos/serviços; essas exigem testes do pacote e integração.
