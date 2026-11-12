# GearFlow.Contracts.Operation — preview

Contratos do participante Operation da fase 4. Namespace wire: `GearFlow.Contracts.Operation.V1`; target .NET 10. Pacote `1.0.0-preview.1` para revisão/homologação local, sem publicação em registry.

Sem dependências do domínio, EF ou host. Não contém nem duplica contratos Inventory/Budget. Enums comerciais serializam como strings e rejeitam números/valores desconhecidos. Adições opcionais são toleradas; alterações obrigatórias ou de semântica exigem evolução acordada.

Comandos: StartDiagnostic (recebe pedido, não inicia trabalho humano), StartExecution (autoriza, não inicia reparo), CancelOperation, UpdateOperationVehicle e DeactivateOperation.

No MassTransit, preencher MessageId e CorrelationId do transporte com os valores do corpo:

```csharp
await endpoint.Send(command, context =>
{
    context.MessageId = command.MessageId;
    context.CorrelationId = command.CorrelationId;
});
```

Reentrega mantém ID e payload. Ator não é recebido no body; quem pode enviar depende de credenciais/ACL do broker. JWT não trafega nas mensagens.

StartExecution inclui snapshot completo com pricingPolicyId e associação BudgetJobId -> DiagnosticLineId. RequestedJobs e RequestedParts preservam as linhas de origem da OS. A execução física continua em endpoints autenticados do Operation.

Schemas, exemplos e manifesto estão em `contracts/runtime-v1/` dentro do nupkg e em `docs/contracts/runtime-v1` no repositório. Referem-se ao corpo lógico, não a um envelope JSON próprio: usar envelope/serializador MassTransit. O URN é definido pelo namespace e nome do tipo, não pelo nome da assembly.

A topologia proposta no infra-broker (gearflow.events/envelope próprio) precisa de revisão coletiva para interoperar com esse padrão. Homologação com OS/Budget/Inventory completos ainda pendente. O pacote não implementa Saga nem pagamento/estoque.

## Nomes dos exchanges de eventos

OperationEventNames.All fornece o mapeamento tipado canônico abaixo, sem dependência de transporte. Configurar o mesmo SetEntityName no produtor e nos consumidores MassTransit. Apenas referenciar o pacote não altera a topologia.

| Contrato | Exchange RabbitMQ |
|---|---|
| `DiagnosticFailed` | `operation.diagnostic-failed` |
| `DiagnosticFinished` | `operation.diagnostic-finished` |
| `DiagnosticQueued` | `operation.diagnostic-queued` |
| `DiagnosticStarted` | `operation.diagnostic-started` |
| `ExecutionAuthorized` | `operation.execution-authorized` |
| `ExecutionFinished` | `operation.execution-finished` |
| `ExecutionStarted` | `operation.execution-started` |
| `JobExecuted` | `operation.job-executed` |
| `OperationCanceled` | `operation.canceled` |
| `OperationCancellationRejected` | `operation.cancellation-rejected` |
| `OperationCommandRejected` | `operation.command-rejected` |
| `OperationDeactivated` | `operation.deactivated` |
| `VehicleUpdated` | `operation.vehicle-updated` |

URN/namespace V1 e payloads permanecem iguais. Os comandos conservam suas filas atuais. Antes de ativar o produtor, provisionar os novos bindings dos assinantes; não há publicação paralela nos exchanges CLR antigos.
