using GearFlow.Operation.Domain;
using GearFlow.Operation.Application;
using Xunit;
namespace Operation.Tests;

public class OperationTests
{
    private static readonly Guid NowId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private static WorkOperation New() => new(Guid.NewGuid(), "mechanic", Now);
    private static WorkOperation Awaiting()
    {
        var o = New(); o.StartDiagnostic("mechanic", Now); o.CompleteDiagnostic("mechanic", Now); return o;
    }
    private static WorkOperation Executing()
    {
        var o = Awaiting(); o.AcceptExecution([], true, "system", Now); return o;
    }

    [Fact]
    public void CompleteFlow_WithStockDelay_RecordsLocalHistory()
    {
        var o = New(); var job = Guid.NewGuid();
        o.StartDiagnostic("mechanic", Now.AddMinutes(1));
        o.CompleteDiagnostic("mechanic", Now.AddMinutes(2));
        o.AcceptExecution([job], false, "system", Now.AddMinutes(3));
        Assert.Equal(OperationStatus.AwaitingPartsOrConsumables, o.Status);
        o.ResumeExecution("system", Now.AddMinutes(4));
        o.ExecuteJob(job, "mechanic", Now.AddMinutes(5));
        o.FinalizeExecution("mechanic", Now.AddMinutes(6));
        Assert.Equal(OperationStatus.Finalized, o.Status);
        Assert.Equal(Now.AddMinutes(5), Assert.Single(o.Jobs).ExecutedAt);
        Assert.Equal(7, o.History.Count);
        Assert.Equal("mechanic", o.History[^1].Actor);
        Assert.Equal(Now, o.CreatedAt);
    }
    [Fact]
    public void Rejection_CancelsOnlyWhileAwaitingApproval()
    {
        var o = Awaiting(); o.RejectBudget("customer", Now);
        Assert.Equal(OperationStatus.Canceled, o.Status);
        Assert.Throws<OperationConflictException>(() => o.StartDiagnostic("mechanic", Now));
    }
    [Fact]
    public void InvalidTransitions_DoNotChangeStateOrHistory()
    {
        var o = New();
        Action[] commands = [() => o.CompleteDiagnostic("m", Now), () => o.AcceptExecution([], true, "m", Now),
            () => o.ResumeExecution("m", Now), () => o.ExecuteJob(Guid.NewGuid(), "m", Now),
            () => o.FinalizeExecution("m", Now), () => o.RejectBudget("m", Now)];
        foreach (var command in commands) Assert.Throws<OperationConflictException>(command);
        Assert.Equal(OperationStatus.Received, o.Status); Assert.Single(o.History);
    }
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ActorIsRequired_WithoutPartialMutation(string actor)
    {
        Assert.Throws<ArgumentException>(() => new WorkOperation(Guid.NewGuid(), actor, Now));
        var o = Awaiting();
        Assert.Throws<ArgumentException>(() => o.AcceptExecution([Guid.NewGuid()], true, actor, Now));
        Assert.Empty(o.Jobs); Assert.Equal(3, o.History.Count);
        Assert.Throws<ArgumentException>(() => o.RejectBudget(actor, Now));
        Assert.Equal(OperationStatus.AwaitingApproval, o.Status);
        var executing = Executing();
        Assert.Throws<ArgumentException>(() => executing.ExecuteJob(Guid.NewGuid(), actor, Now));
    }
    [Fact]
    public void EmptyOrderIsRejected() => Assert.Throws<ArgumentException>(() => new WorkOperation(Guid.Empty, "m", Now));
    [Fact]
    public void ApprovedJobIdsMustBeValidAndUnique()
    {
        var o = Awaiting(); var id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => o.AcceptExecution([id, id], true, "m", Now));
        Assert.Throws<ArgumentException>(() => o.AcceptExecution([Guid.Empty], true, "m", Now));
        Assert.Empty(o.Jobs); Assert.Equal(OperationStatus.AwaitingApproval, o.Status);
    }
    [Fact]
    public void ExecutionRejectsUnknownAndRepeatedJobs()
    {
        var o = Awaiting(); var id = Guid.NewGuid(); o.AcceptExecution([id], true, "m", Now);
        Assert.Throws<OperationConflictException>(() => o.ExecuteJob(Guid.NewGuid(), "m", Now));
        o.ExecuteJob(id, "m", Now);
        Assert.Throws<OperationConflictException>(() => o.ExecuteJob(id, "m", Now.AddMinutes(1)));
        Assert.Equal(Now, o.Jobs[0].ExecutedAt);
    }
    [Fact]
    public void FinalizationPreservesLegacyGuardWithoutNewJobRule()
    {
        var o = Awaiting(); o.AcceptExecution([Guid.NewGuid()], true, "m", Now);
        o.FinalizeExecution("m", Now);
        Assert.Equal(OperationStatus.Finalized, o.Status);
        Assert.Null(o.Jobs[0].ExecutedAt);
    }
    [Fact]
    public void QueueUsesLegacyPriorityAndExcludesTerminalOperations()
    {
        var received = New(); var diagnostic = New(); diagnostic.StartDiagnostic("m", Now);
        var awaiting = Awaiting(); var stock = Awaiting(); stock.AcceptExecution([], false, "m", Now);
        var executing = Executing(); var canceled = Awaiting(); canceled.RejectBudget("m", Now);
        var finalized = Executing(); finalized.FinalizeExecution("m", Now);
        Assert.Equal(new[] { executing, awaiting, stock, diagnostic, received },
            ExecutionQueue.Order([received, canceled, stock, diagnostic, finalized, awaiting, executing]));
    }
    [Fact]
    public void QueueUsesOldestFirstWithinStatus()
    {
        var older = New(); var newer = new WorkOperation(Guid.NewGuid(), "m", Now.AddHours(1));
        Assert.Equal(new[] { older, newer }, ExecutionQueue.Order([newer, older]));
    }

    [Fact]
    public void CancelDiagnosticIsIdempotentAndCannotUndoPhysicalExecution()
    {
        foreach (var o in new[] { New(), Awaiting() })
        {
            o.CancelDiagnostic("system", Now); var count = o.History.Count;
            o.CancelDiagnostic("system", Now);
            Assert.Equal(OperationStatus.Canceled, o.Status); Assert.Equal(count, o.History.Count);
        }
        var executing = Executing();
        Assert.Throws<OperationConflictException>(() => executing.CancelDiagnostic("system", Now));
        executing.FinalizeExecution("m", Now);
        Assert.Throws<OperationConflictException>(() => executing.CancelDiagnostic("system", Now));
        Assert.Throws<ArgumentException>(() => New().CancelDiagnostic(" ", Now));
    }
    [Fact]
    public async Task ApplicationAcknowledgesDiagnosticCancellation()
    {
        var uow = new FakeUnitOfWork { Current = New() }; var service = new OperationService(uow, new FixedClock());
        var id = uow.Current.ServiceOrderId;
        await service.CancelDiagnostic(id, id, "system");
        Assert.Equal("DiagnosticCanceled.v1", Assert.Single(uow.Events).Type);
        await Assert.ThrowsAsync<ArgumentException>(() => service.StartDiagnostic(id, Guid.NewGuid(), "m"));
    }

    private sealed class FakeUnitOfWork : IOperationUnitOfWork
    {
        public WorkOperation? Current { get; set; }
        public List<OperationEvent> Events { get; } = [];
        public bool FailCommit { get; set; }
        public Task<WorkOperation?> Load(Guid id, CancellationToken ct) => Task.FromResult(Current);
        public Task Commit(WorkOperation operation, OperationEvent pendingEvent, CancellationToken ct)
        {
            if (FailCommit) throw new IOException("database unavailable");
            Current = operation; Events.Add(pendingEvent); return Task.CompletedTask;
        }
    }
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
    [Fact]
    public async Task ApplicationCommitsStateAndPendingEventTogether()
    {
        var uow = new FakeUnitOfWork(); var service = new OperationService(uow, new FixedClock());
        var id = Guid.NewGuid(); var correlation = id; var job = Guid.NewGuid();
        await service.Receive(id, correlation, "m");
        await service.StartDiagnostic(id, correlation, "m");
        await service.CompleteDiagnostic(id, correlation, "m");
        await service.AcceptExecution(id, correlation, [job], false, "m");
        await service.ResumeExecution(id, correlation, "m");
        await service.ExecuteJob(id, correlation, job, "m");
        await service.FinalizeExecution(id, correlation, "m");
        Assert.Equal(7, uow.Events.Count);
        Assert.All(uow.Events, e => { Assert.Equal(id, e.ServiceOrderId); Assert.Equal(correlation, e.CorrelationId); Assert.Equal(Now, e.OccurredAt); });
        Assert.Equal(7, uow.Events.Select(e => e.EventId).Distinct().Count());
        Assert.Equal("ExecutionFinished.v1", uow.Events[^1].Type);
        Assert.Equal(OperationStatus.Finalized, uow.Current!.Status);
    }
    [Fact]
    public async Task ApprovalWithStockAndRejectionEmitDifferentEvents()
    {
        var uow = new FakeUnitOfWork { Current = Awaiting() }; var service = new OperationService(uow, new FixedClock());
        var correlation = uow.Current.ServiceOrderId;
        await service.AcceptExecution(uow.Current.ServiceOrderId, correlation, [], true, "m");
        Assert.Equal("ExecutionStarted.v1", uow.Events[^1].Type);
        uow.Current = Awaiting();
        await service.RejectBudget(uow.Current.ServiceOrderId, uow.Current.ServiceOrderId, "m");
        Assert.Equal("OperationCanceled.v1", uow.Events[^1].Type);
    }
    [Fact]
    public async Task MissingOrderAndInvalidCorrelationDoNotCommit()
    {
        var uow = new FakeUnitOfWork(); var service = new OperationService(uow, new FixedClock());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.StartDiagnostic(NowId, NowId, "m"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.Receive(Guid.NewGuid(), Guid.Empty, "m"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.StartDiagnostic(Guid.NewGuid(), Guid.Empty, "m"));
        Assert.Empty(uow.Events);
    }
    [Fact]
    public async Task DuplicateReceiveIsConflictWithoutOverwritingOperation()
    {
        var o = New(); var uow = new FakeUnitOfWork { Current = o }; var service = new OperationService(uow, new FixedClock());
        await Assert.ThrowsAsync<OperationConflictException>(() => service.Receive(o.ServiceOrderId, o.ServiceOrderId, "m"));
        Assert.Same(o, uow.Current); Assert.Empty(uow.Events);
    }
    [Fact]
    public async Task InvalidTransitionDoesNotCommit()
    {
        var uow = new FakeUnitOfWork { Current = New() }; var service = new OperationService(uow, new FixedClock());
        await Assert.ThrowsAsync<OperationConflictException>(() => service.FinalizeExecution(uow.Current.ServiceOrderId, uow.Current.ServiceOrderId, "m"));
        Assert.Empty(uow.Events);
    }
    [Fact]
    public async Task CommitFailurePropagatesWithoutClaimingSuccess()
    {
        var uow = new FakeUnitOfWork { FailCommit = true }; var service = new OperationService(uow, new FixedClock());
        await Assert.ThrowsAsync<IOException>(() => service.Receive(NowId, NowId, "m"));
        Assert.Null(uow.Current); Assert.Empty(uow.Events);
    }
}
