using GearFlow.Operation.Domain;

namespace GearFlow.Operation.Application;

/// <summary>Casos de uso locais; estado, resultado da intenção e evento são enviados a um commit atômico.</summary>
public sealed class OperationService(IOperationUnitOfWork unitOfWork, DiagnosticPricer pricing, TimeProvider clock)
{
    public Task<CommandResult> Receive(CommandContext context, Guid vehicleId, IEnumerable<RequestedJob> requestedJobs,
        IEnumerable<RequestedPart> requestedParts, int? serviceOrderCode = null, CancellationToken ct = default)
    {
        var jobs = Copy(requestedJobs); var parts = Copy(requestedParts);
        var payload = new { VehicleId = vehicleId, ServiceOrderCode = serviceOrderCode,
            Jobs = jobs.OrderBy(item => item.LineId).ToArray(), Parts = parts.OrderBy(item => item.LineId).ToArray() };
        return Execute(context, "Receive", payload, OperationEventKind.DiagnosticQueued, operation =>
        {
            if (operation is not null)
            {
                if (operation.IsTombstone || operation.Status == OperationStatus.Canceled)
                    throw new OperationConflictException("Recebimento atrasado para operação cancelada.");
                if (operation.InitialVehicleId != vehicleId || operation.ServiceOrderCode != serviceOrderCode ||
                    !operation.RequestedJobs.OrderBy(item => item.LineId).SequenceEqual(jobs.OrderBy(item => item.LineId)) ||
                    !operation.RequestedParts.OrderBy(item => item.LineId).SequenceEqual(parts.OrderBy(item => item.LineId)))
                    throw new OperationConflictException("Recebimento conflitante com o pedido inicial.");
                return Task.FromResult(operation);
            }
            return Task.FromResult(new WorkOperation(context.ServiceOrderId, vehicleId, jobs, parts, context.Actor, clock.GetUtcNow(), serviceOrderCode));
        }, ct);
    }

    public Task<CommandResult> StartDiagnostic(CommandContext context, CancellationToken ct = default) =>
        Change(context, "StartDiagnostic", new { }, OperationEventKind.DiagnosticStarted, operation =>
        {
            Active(operation);
            if (operation.Status != OperationStatus.InDiagnostic) operation.StartDiagnostic(context.Actor, clock.GetUtcNow());
        }, ct);

    public Task<CommandResult> CompleteDiagnostic(CommandContext context, IEnumerable<DiagnosticConsumable> consumables,
        CancellationToken ct = default)
    {
        var items = Copy(consumables);
        return Execute(context, "CompleteDiagnostic", CommandFingerprint.Consumables(items), OperationEventKind.DiagnosticFinished,
            async existing =>
            {
                var operation = Found(existing);
                Active(operation);
                if (operation.Diagnostic is not null)
                {
                    if (!operation.Consumables.OrderBy(item => item.LineId).SequenceEqual(items.OrderBy(item => item.LineId)))
                        throw new OperationConflictException("Conclusão repetida com consumíveis divergentes.");
                    return operation;
                }
                var snapshot = await pricing.Price(operation, items, ct);
                operation.CompleteDiagnostic(snapshot, items, context.Actor, clock.GetUtcNow());
                return operation;
            }, ct);
    }

    public Task<CommandResult> FailDiagnostic(CommandContext context, string reason, CancellationToken ct = default)
    {
        var detail = Reason(reason);
        return Change(context, "FailDiagnostic", new { Reason = detail }, OperationEventKind.DiagnosticFailed, operation =>
        {
            Active(operation);
            if (operation.Status == OperationStatus.DiagnosticFailed && operation.DiagnosticFailureReason == detail) return;
            operation.FailDiagnostic(detail, context.Actor, clock.GetUtcNow());
        }, ct);
    }

    public Task<CommandResult> UpdateVehicle(CommandContext context, Guid vehicleId, CancellationToken ct = default) =>
        Change(context, "UpdateVehicle", new { VehicleId = vehicleId }, OperationEventKind.VehicleUpdated,
            operation => operation.UpdateVehicle(vehicleId, context.Actor, clock.GetUtcNow()), ct);

    public Task<CommandResult> Deactivate(CommandContext context, string reason, CancellationToken ct = default)
    {
        var detail = Reason(reason);
        return Change(context, "Deactivate", new { Reason = detail }, OperationEventKind.OperationDeactivated,
            operation => operation.Deactivate(detail, context.Actor, clock.GetUtcNow()), ct);
    }

    public Task<CommandResult> AuthorizeExecution(CommandContext context, ExecutionAuthorization authorization, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorization);
        if (authorization.AuthorizationId != context.IntentId)
            throw new ArgumentException("AuthorizationId deve identificar a intenção do comando de autorização.", nameof(authorization));
        return Change(context, "AuthorizeExecution", CommandFingerprint.Authorization(authorization), OperationEventKind.ExecutionAuthorized,
            operation => operation.AuthorizeExecution(authorization, context.Actor, clock.GetUtcNow()), ct);
    }

    public Task<CommandResult> StartExecution(CommandContext context, CancellationToken ct = default) =>
        Change(context, "StartExecution", new { }, OperationEventKind.ExecutionStarted, operation =>
        {
            Active(operation);
            if (operation.Status != OperationStatus.InExecution) operation.StartExecution(context.Actor, clock.GetUtcNow());
        }, ct);

    public Task<CommandResult> ExecuteJob(CommandContext context, Guid budgetJobId, CancellationToken ct = default) =>
        Change(context, "ExecuteJob", new { BudgetJobId = budgetJobId }, OperationEventKind.JobExecuted, operation =>
        {
            Active(operation);
            if (!operation.Jobs.Any(job => job.BudgetJobId == budgetJobId && job.ExecutedAt.HasValue))
                operation.ExecuteJob(budgetJobId, context.Actor, clock.GetUtcNow());
        }, ct);

    public Task<CommandResult> FinalizeExecution(CommandContext context, CancellationToken ct = default) =>
        Change(context, "FinalizeExecution", new { }, OperationEventKind.ExecutionFinished, operation =>
        {
            Active(operation);
            if (operation.Status != OperationStatus.ExecutionCompleted) operation.FinalizeExecution(context.Actor, clock.GetUtcNow());
        }, ct);

    public Task<CommandResult> CancelOperation(CommandContext context, string reason, CancellationToken ct = default)
    {
        var detail = Reason(reason);
        return Execute(context, "CancelOperation", new { Reason = detail }, OperationEventKind.OperationCanceled, existing =>
        {
            var operation = existing ?? WorkOperation.CreateCanceled(context.ServiceOrderId, detail, context.Actor, clock.GetUtcNow());
            if (existing is not null) operation.CancelOperation(detail, context.Actor, clock.GetUtcNow());
            return Task.FromResult(operation);
        }, ct);
    }

    private Task<CommandResult> Change(CommandContext context, string action, object payload, OperationEventKind kind,
        Action<WorkOperation> change, CancellationToken ct) => Execute(context, action, payload, kind, existing =>
        {
            var operation = Found(existing);
            change(operation);
            return Task.FromResult(operation);
        }, ct);

    private async Task<CommandResult> Execute(CommandContext context, string action, object payload, OperationEventKind kind,
        Func<WorkOperation?, Task<WorkOperation>> change, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ct.ThrowIfCancellationRequested();
        var fingerprint = CommandFingerprint.Create(action, context, payload);
        var previous = await unitOfWork.FindReceipt(context.IntentId, ct);
        if (previous is not null)
        {
            if (previous.Fingerprint != fingerprint) throw new IntentConflictException("Intenção reutilizada com ação, OS, ator ou conteúdo diferentes.");
            return previous.Result; // Replay vem antes de precondição de versão e de consultas externas.
        }
        var existing = await unitOfWork.Load(context.ServiceOrderId, ct);
        var storedVersion = existing?.Version;
        if (context.ExpectedVersion.HasValue && context.ExpectedVersion.Value != (storedVersion ?? 0))
            throw new OperationConcurrencyException("Versão esperada diverge do estado atual.");
        var operation = await change(existing);
        var changed = !storedVersion.HasValue || operation.Version != storedVersion.Value;
        var result = new CommandResult(operation.ServiceOrderId, operation.Status, operation.Version,
            operation.DiagnosticId, operation.Authorization?.BudgetId, changed);
        var events = changed ? new[] { new OperationEvent(Guid.NewGuid(), operation.ServiceOrderId, context.IntentId,
            kind, operation.Version, operation.UpdatedAt, context.Actor, OperationDetails.From(operation)) } : [];
        var receipt = new CommandReceipt(context.IntentId, fingerprint, result);
        ct.ThrowIfCancellationRequested();
        return await unitOfWork.Commit(new(operation, storedVersion, receipt, Array.AsReadOnly(events)), ct);
    }

    private static WorkOperation Found(WorkOperation? operation) => operation ?? throw new KeyNotFoundException("Operação não encontrada.");
    private static void Active(WorkOperation operation)
    {
        if (!operation.IsActive) throw new OperationConflictException("Operação inativa/cancelada não pode executar trabalho.");
    }
    private static string Reason(string reason) => string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 2000
        ? throw new ArgumentException("Motivo obrigatório, com até 2000 caracteres.", nameof(reason)) : reason.Trim();
    private static T[] Copy<T>(IEnumerable<T> items) where T : class
    {
        ArgumentNullException.ThrowIfNull(items);
        var copy = items.ToArray();
        if (copy.Any(item => item is null)) throw new ArgumentException("Item nulo.", nameof(items));
        return copy;
    }
}
