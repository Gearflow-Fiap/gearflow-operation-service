using GearFlow.Operation.Application;
using GearFlow.Operation.Domain;
using Xunit;
using static Operation.UnitTests.Support.DomainFixture;

namespace Operation.UnitTests.Application;

public class ApplicationQueryTests
{
    private static WorkOperation Build(int id, int stage, DateTimeOffset? createdAt = null)
    {
        var operation = Received(orderId: Id(id), createdAt: createdAt);
        if (stage >= 1) operation.StartDiagnostic("m", At(1));
        if (stage >= 2) operation.CompleteDiagnostic(Snapshot(operation), [], "m", At(3));
        if (stage >= 3) operation.AuthorizeExecution(Authorization(operation), "s", At(4));
        if (stage >= 4) operation.StartExecution("m", At(5));
        return operation;
    }

    [Fact]
    public async Task DetailsAreFrozenAndDoNotExposeMutableStoredAggregate()
    {
        var fixture = new ApplicationFixture(); await fixture.Executing();
        var detail = await fixture.Queries.Get(Id(1));
        Assert.Equal(Time, detail.CreatedAt); Assert.Equal(fixture.Clock.Now, detail.UpdatedAt);
        Assert.Throws<NotSupportedException>(() => ((IList<JobProgressDto>)detail.Jobs).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<DiagnosticLineDto>)detail.Diagnostic!.Lines).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<ApprovedJobDto>)detail.Authorization!.Jobs).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<RequestedPartDto>)detail.RequestedParts).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<HistoryDto>)detail.History).Clear());
        fixture.Clock.Advance();
        await fixture.Service.ExecuteJob(fixture.Context(), Id(40));
        Assert.Equal(fixture.Clock.Now, (await fixture.Queries.Get(Id(1))).UpdatedAt);
        Assert.NotEqual(detail.UpdatedAt, (await fixture.Queries.Get(Id(1))).UpdatedAt);
        Assert.Null(detail.Jobs[0].ExecutedAt);
        Assert.NotNull((await fixture.Queries.Get(Id(1))).Jobs[0].ExecutedAt);
        Assert.Equal(OperationStatus.InExecution, detail.Status);
    }

    [Fact]
    public async Task QueueAndAllListsHaveDifferentScopesWithNormalizedPagesAndStableOrder()
    {
        var fixture = new ApplicationFixture();
        fixture.Store.Seed(Build(1, 0)); fixture.Store.Seed(Build(2, 1)); fixture.Store.Seed(Build(3, 2));
        fixture.Store.Seed(Build(4, 3)); fixture.Store.Seed(Build(5, 4));
        var inactive = Build(6, 0); inactive.Deactivate("Withdraw", "s", At(1)); fixture.Store.Seed(inactive);
        var canceled = Build(7, 3); canceled.CancelOperation("Cancel", "s", At(5)); fixture.Store.Seed(canceled);
        var first = await fixture.Queries.Queue(0, 2);
        Assert.Equal(1, first.Page); Assert.Equal(2, first.PageSize); Assert.Equal(4, first.TotalCount);
        Assert.Equal(new[] { Id(5), Id(4) }, first.Items.Select(item => item.ServiceOrderId));
        var second = await fixture.Queries.Queue(2, 2);
        Assert.Equal(new[] { Id(2), Id(1) }, second.Items.Select(item => item.ServiceOrderId));
        var waiting = await fixture.Queries.List(pageSize: 1000, status: OperationStatus.AwaitingExecutionAuthorization);
        Assert.Equal(Id(3), Assert.Single(waiting.Items).ServiceOrderId); Assert.Equal(100, waiting.PageSize);
        var all = await fixture.Queries.List(includeInactive: true);
        Assert.Equal(7, all.TotalCount); Assert.Equal(Enumerable.Range(1, 7).Select(Id), all.Items.Select(item => item.ServiceOrderId));
        var filtered = await fixture.Queries.Queue(status: OperationStatus.AwaitingExecutionAuthorization);
        Assert.Empty(filtered.Items);
        Assert.Empty((await fixture.Queries.List(int.MaxValue, 100)).Items);
        Assert.Equal(0, fixture.Store.ReceiptCount); Assert.Empty(fixture.Store.Events);
        Assert.Throws<NotSupportedException>(() => ((IList<OperationListItem>)all.Items).Clear());
    }

    [Fact]
    public async Task GeneralListOrdersNewestFirstThenOrderIdAndExcludesInactiveByDefault()
    {
        var fixture = new ApplicationFixture(); fixture.Store.Seed(Build(1, 0)); fixture.Store.Seed(Build(3, 0, At(1)));
        fixture.Store.Seed(Build(2, 0, At(1)));
        var result = await fixture.Queries.List(pageSize: 0);
        Assert.Equal(1, result.PageSize); Assert.Equal(Id(2), Assert.Single(result.Items).ServiceOrderId);
        Assert.Equal(3, result.TotalCount);
        var inactive = Build(4, 0); inactive.Deactivate("x", "s", At(1)); fixture.Store.Seed(inactive);
        Assert.Equal(3, (await fixture.Queries.List()).TotalCount);
    }

    [Fact]
    public async Task HistoryIsPaginatedByVersionAndQueriesFailExplicitlyWhenAbsent()
    {
        var fixture = new ApplicationFixture(); await fixture.Diagnosed();
        var first = await fixture.Queries.History(Id(1), -1, 0);
        Assert.Equal(3, first.TotalCount); Assert.Equal(1, first.Page); Assert.Equal(1, first.PageSize);
        Assert.Equal(OperationAction.Received, Assert.Single(first.Items).Action);
        var second = await fixture.Queries.History(Id(1), 2, 1);
        Assert.Equal(2, Assert.Single(second.Items).Version);
        Assert.Empty((await fixture.Queries.History(Id(1), int.MaxValue, 100)).Items);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Queries.Get(Id(99)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Queries.History(Id(99)));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Queries.Get(Guid.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Queries.History(Guid.Empty));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Queries.List(status: (OperationStatus)99));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Queries.Queue(status: (OperationStatus)99));
        Assert.Equal(214748364600L, PageRequest.Normalize(int.MaxValue, 100).Offset);
    }

    [Fact]
    public async Task PhaseDurationsSeparateQueueAndPhysicalWorkAndDoNotInventMissingSamples()
    {
        var fixture = new ApplicationFixture();
        var completed = Executing(); completed.FinalizeExecution("m", At(7)); fixture.Store.Seed(completed);
        fixture.Store.Seed(Received(orderId: Id(2)));
        var canceled = Build(3, 2); canceled.CancelOperation("x", "s", At(4)); fixture.Store.Seed(canceled);
        var detail = await fixture.Queries.Get(Id(1));
        Assert.Equal(new PhaseDurations(1, 2, 1, 2), detail.Durations);
        var summary = await fixture.Queries.Durations();
        Assert.Equal(new PhaseAverage(1, 1), summary.DiagnosticQueue);
        Assert.Equal(new PhaseAverage(2, 1), summary.Diagnostic);
        Assert.Equal(new PhaseAverage(1, 1), summary.ExecutionQueue);
        Assert.Equal(new PhaseAverage(2, 1), summary.Execution);
        var empty = await new ApplicationFixture().Queries.Durations();
        Assert.Equal(new PhaseAverage(null, 0), empty.Diagnostic);
        Assert.Equal(new PhaseDurations(null, null, null, null), (await fixture.Queries.Get(Id(2))).Durations);
        Assert.Equal(0, fixture.Store.ReceiptCount);
    }

    [Fact]
    public void ZeroLengthCompletedPhaseIsARealSampleNotMissingData()
    {
        var operation = Received(); operation.StartDiagnostic("m", Time);
        operation.CompleteDiagnostic(Snapshot(operation, pricedAt: Time), [], "m", Time);
        Assert.Equal(new PhaseAverage(0, 1), DurationCalculator.Summarize([operation]).Diagnostic);
    }
}
