using GearFlow.Operation.Domain;
using Xunit;

namespace Operation.Tests;

internal static class DomainFixture
{
    public static readonly DateTimeOffset Time = new(2026, 10, 28, 20, 0, 0, TimeSpan.Zero);
    public static DateTimeOffset At(int minute) => Time.AddMinutes(minute);
    public static Guid Id(int value) => Guid.Parse($"10000000-0000-4000-8000-{value:000000000000}");

    public static WorkOperation Received(int jobCount = 1, bool withPart = true, Guid? orderId = null, DateTimeOffset? createdAt = null) =>
        new(orderId ?? Id(1), Id(2), Enumerable.Range(0, jobCount).Select(i => new RequestedJob(Id(10 + i), Id(100))),
            withPart ? [new RequestedPart(Id(20), Id(200), 2)] : [], "staff:1", createdAt ?? Time, 45, Id(3));

    public static WorkOperation Diagnosing(int jobCount = 1)
    {
        var operation = Received(jobCount);
        operation.StartDiagnostic("staff:1", At(1));
        return operation;
    }

    public static DiagnosticConsumable Consumable() => new(Id(30), Id(300), 1.5m);

    public static DiagnosticSnapshot Snapshot(WorkOperation operation, IEnumerable<DiagnosticConsumable>? consumables = null,
        DateTimeOffset? pricedAt = null, string policy = "fixture-supplied")
    {
        var lines = operation.RequestedJobs.Select(job => new DiagnosticLine(job.LineId, OperationItemType.Labor,
                job.CatalogJobId, "Mão de obra", 1, 1000, 1000))
            .Concat(operation.RequestedParts.Select(part => new DiagnosticLine(part.LineId, OperationItemType.Part,
                part.PartId, "Peça", part.Quantity, 300, 300 * part.Quantity)))
            .Concat((consumables ?? []).Select(item => new DiagnosticLine(item.LineId, OperationItemType.Consumable,
                item.ConsumableId, "Insumo", item.Quantity, 200, 200)));
        return new(operation.DiagnosticId!.Value, lines, pricedAt ?? At(2), policy);
    }

    public static WorkOperation Diagnosed(int jobCount = 1)
    {
        var operation = Diagnosing(jobCount);
        operation.CompleteDiagnostic(Snapshot(operation, [Consumable()]), [Consumable()], "staff:1", At(3));
        return operation;
    }

    public static ExecutionAuthorization Authorization(WorkOperation operation, Guid? authorizationId = null,
        DiagnosticSnapshot? snapshot = null, Guid? budgetId = null) =>
        new(authorizationId ?? Id(5), budgetId ?? Id(4), 1, snapshot ?? operation.Diagnostic!,
            operation.RequestedJobs.Select((job, index) => new ApprovedJob(Id(40 + index), job.LineId)));

    public static WorkOperation Ready(int jobCount = 1)
    {
        var operation = Diagnosed(jobCount);
        operation.AuthorizeExecution(Authorization(operation), "system:saga", At(4));
        return operation;
    }

    public static WorkOperation Executing(int jobCount = 1)
    {
        var operation = Ready(jobCount);
        operation.StartExecution("staff:1", At(5));
        return operation;
    }

    public static void Unchanged(WorkOperation operation, Action action) => Unchanged<OperationConflictException>(operation, action);

    public static void Unchanged<TException>(WorkOperation operation, Action action) where TException : Exception
    {
        var before = (operation.Status, operation.Version, operation.UpdatedAt, operation.VehicleId,
            operation.IsActive, operation.Diagnostic, operation.Authorization, operation.CancellationReason,
            operation.DeactivationReason, operation.DiagnosticFailureReason);
        var times = (operation.DiagnosticStartedAt, operation.DiagnosticCompletedAt, operation.ExecutionAuthorizedAt,
            operation.ExecutionStartedAt, operation.ExecutionCompletedAt);
        var history = operation.History.ToArray();
        var jobs = operation.Jobs.ToArray();
        var consumables = operation.Consumables.ToArray();
        Assert.ThrowsAny<TException>(action);
        Assert.Equal(before, (operation.Status, operation.Version, operation.UpdatedAt, operation.VehicleId,
            operation.IsActive, operation.Diagnostic, operation.Authorization, operation.CancellationReason,
            operation.DeactivationReason, operation.DiagnosticFailureReason));
        Assert.Equal(times, (operation.DiagnosticStartedAt, operation.DiagnosticCompletedAt, operation.ExecutionAuthorizedAt,
            operation.ExecutionStartedAt, operation.ExecutionCompletedAt));
        Assert.Equal(history, operation.History);
        Assert.Equal(jobs, operation.Jobs);
        Assert.Equal(consumables, operation.Consumables);
    }
}
