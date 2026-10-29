using GearFlow.Operation.Domain;

namespace GearFlow.Operation.Application;

public sealed partial class OperationService
{
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
}
