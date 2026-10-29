using GearFlow.Operation.Application;
using GearFlow.Operation.Domain;
using Xunit;
using static Operation.Tests.DomainFixture;

namespace Operation.Tests;

public class OperationTests
{
    [Fact]
    public async Task FullLocalFlowPreservesItemsPricesMappingAuditAndPendingEventData()
    {
        var fixture = new ApplicationFixture();
        await fixture.Executing(2); fixture.Clock.Advance();
        await fixture.Service.ExecuteJob(fixture.Context(actor: "staff:2"), Id(40)); fixture.Clock.Advance();
        await fixture.Service.FinalizeExecution(fixture.Context());
        var detail = await fixture.Queries.Get(Id(1));
        Assert.Equal(OperationStatus.ExecutionCompleted, detail.Status);
        Assert.Equal(Id(2), detail.VehicleId); Assert.Equal(45, detail.ServiceOrderCode);
        Assert.Equal(2, detail.RequestedJobs.Count); Assert.Equal(2, detail.RequestedParts[0].Quantity);
        Assert.Equal(1.5m, detail.Consumables[0].Quantity);
        Assert.Equal(2900, detail.Diagnostic!.TotalPriceCents); // 2*1000 + 2*300 + 1.5*200.
        Assert.Equal("per-unit-away-from-zero-v1", detail.Diagnostic.PricingPolicyId);
        Assert.Equal("staff:2", detail.Jobs[0].ExecutedBy); Assert.Null(detail.Jobs[1].ExecutedAt);
        Assert.Equal(new[] { OperationEventKind.DiagnosticQueued, OperationEventKind.DiagnosticStarted,
            OperationEventKind.DiagnosticFinished, OperationEventKind.ExecutionAuthorized,
            OperationEventKind.ExecutionStarted, OperationEventKind.JobExecuted, OperationEventKind.ExecutionFinished }, fixture.Store.Events.Select(e => e.Kind));
        Assert.All(fixture.Store.Events, e =>
        {
            Assert.Equal(Id(1), e.ServiceOrderId); Assert.NotEqual(Guid.Empty, e.CausationId);
            Assert.Equal(e.Version, e.Data.Version); Assert.Equal(e.ServiceOrderId, e.Data.ServiceOrderId);
        });
        var finishedDiagnostic = fixture.Store.Events.Single(e => e.Kind == OperationEventKind.DiagnosticFinished);
        Assert.Equal(OperationStatus.AwaitingExecutionAuthorization, finishedDiagnostic.Data.Status);
        Assert.Empty(finishedDiagnostic.Data.Jobs); // A projeção antiga não mudou quando execução avançou.
        Assert.Equal(4, finishedDiagnostic.Data.Diagnostic!.Lines.Count);
        Assert.Equal(7, fixture.Store.ReceiptCount);
    }

    [Fact]
    public async Task MissingOperationAndInvalidInputsHaveNoSideEffects()
    {
        var fixture = new ApplicationFixture();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Service.StartDiagnostic(fixture.Context()));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.Receive(fixture.Context(), Guid.Empty, [new(Id(10), Id(100))], []));
        Assert.Throws<ArgumentException>(() => new CommandContext(Guid.Empty, Id(1), "m"));
        Assert.Throws<ArgumentException>(() => new CommandContext(Id(1), Guid.Empty, "m"));
        Assert.Throws<ArgumentException>(() => new CommandContext(Id(1), Id(2), " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CommandContext(Id(1), Id(2), "m", -1));
        await Assert.ThrowsAsync<ArgumentNullException>(() => fixture.Service.StartDiagnostic(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => fixture.Service.Receive(fixture.Context(), Id(2), null!, []));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.Receive(fixture.Context(), Id(2), [null!], []));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.CancelOperation(fixture.Context(), " "));
        Assert.Empty(fixture.Store.Events); Assert.Equal(0, fixture.Store.ReceiptCount);
    }

    [Fact]
    public async Task ReplayReturnsOriginalResultBeforeVersionCheckOrExternalRead()
    {
        var fixture = new ApplicationFixture(); var receive = fixture.Context(expectedVersion: 0);
        var first = await fixture.Receive(receive);
        fixture.Clock.Advance(); await fixture.Service.StartDiagnostic(fixture.Context());
        var result = await fixture.Receive(receive);
        Assert.Equal(first, result); Assert.Equal(OperationStatus.Received, result.Status);
        Assert.Equal(OperationStatus.InDiagnostic, fixture.Store.Peek(Id(1))!.Status);
        var finish = fixture.Context(expectedVersion: 2);
        var originalFinish = await fixture.Service.CompleteDiagnostic(finish, [Consumable()]);
        fixture.Prices.Failure = new PricingUnavailableException("down");
        Assert.Equal(originalFinish, await fixture.Service.CompleteDiagnostic(finish, [Consumable()]));
        Assert.Equal(1, fixture.Prices.Calls);
        Assert.Equal(3, fixture.Store.Events.Count); Assert.Equal(3, fixture.Store.ReceiptCount);
    }

    [Theory]
    [InlineData("actor")] [InlineData("order")] [InlineData("body")] [InlineData("action")]
    public async Task ReusingIntentWithDifferentMeaningIsRejected(string difference)
    {
        var fixture = new ApplicationFixture(); var context = fixture.Context(); await fixture.Receive(context);
        Task<CommandResult> Attempt() => difference switch
        {
            "actor" => fixture.Receive(fixture.Context(context.IntentId, actor: "other")),
            "order" => fixture.Receive(fixture.Context(context.IntentId, Id(99))),
            "body" => fixture.Service.Receive(context, Id(3), [new(Id(10), Id(100))], [new(Id(20), Id(200), 2)], 45),
            _ => fixture.Service.StartDiagnostic(context)
        };
        await Assert.ThrowsAsync<IntentConflictException>(Attempt);
        Assert.Single(fixture.Store.Events); Assert.Equal(1, fixture.Store.ReceiptCount);
    }

    [Fact]
    public async Task RepeatedBusinessReceiveUsesInitialVehicleAndNeverRegressesWork()
    {
        var fixture = new ApplicationFixture(); await fixture.Receive();
        await fixture.Service.UpdateVehicle(fixture.Context(), Id(9));
        var result = await fixture.Receive();
        Assert.False(result.StateChanged); Assert.Equal(Id(9), fixture.Store.Peek(Id(1))!.VehicleId);
        Assert.Equal(Id(2), fixture.Store.Peek(Id(1))!.InitialVehicleId);
        await Assert.ThrowsAsync<OperationConflictException>(() => fixture.Service.Receive(fixture.Context(), Id(9),
            [new(Id(10), Id(100))], [new(Id(20), Id(200), 2)], 45));
        Assert.Equal(2, fixture.Store.Events.Count); Assert.Equal(3, fixture.Store.ReceiptCount);
    }

    [Fact]
    public async Task CommitFailureOnLoadedOperationDoesNotPersistMutationReceiptOrEventAndCanRetry()
    {
        var fixture = new ApplicationFixture(); await fixture.Receive();
        var context = fixture.Context(); var before = fixture.Store.Peek(Id(1))!;
        fixture.Store.CommitFailure = new IOException("commit failed after preparing candidate");
        await Assert.ThrowsAsync<IOException>(() => fixture.Service.StartDiagnostic(context));
        var after = fixture.Store.Peek(Id(1))!;
        Assert.Equal(before.Status, after.Status); Assert.Equal(before.Version, after.Version);
        Assert.Equal(before.History, after.History); Assert.Null(after.DiagnosticStartedAt);
        Assert.Null(await fixture.Store.FindReceipt(context.IntentId, default));
        Assert.Single(fixture.Store.Events);
        fixture.Store.CommitFailure = null;
        await fixture.Service.StartDiagnostic(context);
        Assert.Equal(OperationStatus.InDiagnostic, fixture.Store.Peek(Id(1))!.Status);
        Assert.Equal(2, fixture.Store.Events.Count);
    }

    [Fact]
    public async Task CommitFailureDuringCreationAndTombstoneCreationLeavesNothingVisible()
    {
        var fixture = new ApplicationFixture(); fixture.Store.CommitFailure = new IOException("down");
        await Assert.ThrowsAsync<IOException>(() => fixture.Receive());
        await Assert.ThrowsAsync<IOException>(() => fixture.Service.CancelOperation(fixture.Context(), "Cancel"));
        Assert.Null(fixture.Store.Peek(Id(1))); Assert.Empty(fixture.Store.Events); Assert.Equal(0, fixture.Store.ReceiptCount);
    }

    [Fact]
    public async Task CancelBeforeReceiveCreatesFenceAndRejectsLateWork()
    {
        var fixture = new ApplicationFixture(); var cancel = fixture.Context();
        await fixture.Service.CancelOperation(cancel, "Cancel before intake");
        Assert.True(fixture.Store.Peek(Id(1))!.IsTombstone);
        await fixture.Service.CancelOperation(fixture.Context(), "Cancel before intake");
        await fixture.Service.CancelOperation(cancel, "Cancel before intake");
        await Assert.ThrowsAsync<OperationConflictException>(() => fixture.Receive());
        await Assert.ThrowsAsync<OperationConflictException>(() => fixture.Service.StartDiagnostic(fixture.Context()));
        Assert.Single(fixture.Store.Events); Assert.Equal(2, fixture.Store.ReceiptCount);
    }

    [Fact]
    public async Task FailedDiagnosticAndDeactivationBlockFurtherWorkWithoutFakeFinancialEffects()
    {
        var fixture = new ApplicationFixture(); await fixture.Diagnosing();
        await fixture.Service.FailDiagnostic(fixture.Context(), "Impossible");
        var result = await fixture.Service.FailDiagnostic(fixture.Context(), "Impossible");
        Assert.False(result.StateChanged);
        await Assert.ThrowsAsync<OperationConflictException>(() => fixture.Service.CompleteDiagnostic(fixture.Context(), []));
        Assert.Equal(0, fixture.Prices.Calls);
        await fixture.Service.CancelOperation(fixture.Context(), "Cancel");
        Assert.Equal(OperationStatus.Canceled, fixture.Store.Peek(Id(1))!.Status);
        var other = new ApplicationFixture(); await other.Receive();
        await other.Service.Deactivate(other.Context(), "Withdraw");
        Assert.False((await other.Service.Deactivate(other.Context(), "Withdraw")).StateChanged);
        await Assert.ThrowsAsync<OperationConflictException>(() => other.Service.StartDiagnostic(other.Context()));
        await Assert.ThrowsAsync<OperationConflictException>(() => other.Service.UpdateVehicle(other.Context(), Id(8)));
        Assert.Equal(OperationEventKind.OperationDeactivated, other.Store.Events[^1].Kind);
    }

    [Fact]
    public async Task NewIntentForAlreadyPerformedWorkDoesNotDuplicateProgressOrReprice()
    {
        var fixture = new ApplicationFixture(); await fixture.Executing();
        Assert.False((await fixture.Service.StartExecution(fixture.Context())).StateChanged);
        Assert.False((await fixture.Service.CompleteDiagnostic(fixture.Context(), [Consumable()])).StateChanged);
        Assert.Equal(1, fixture.Prices.Calls);
        await Assert.ThrowsAsync<OperationConflictException>(() => fixture.Service.CompleteDiagnostic(fixture.Context(), []));
        await fixture.Service.ExecuteJob(fixture.Context(), Id(40));
        Assert.False((await fixture.Service.ExecuteJob(fixture.Context(), Id(40))).StateChanged);
        await fixture.Service.FinalizeExecution(fixture.Context());
        Assert.False((await fixture.Service.FinalizeExecution(fixture.Context())).StateChanged);
        Assert.False((await fixture.Service.ExecuteJob(fixture.Context(), Id(40))).StateChanged);
        Assert.Equal(7, fixture.Store.Events.Count);
    }

    [Fact]
    public async Task AuthorizationChecksIdentityTermsAndPreservesOriginalAuthorizationOnRetry()
    {
        var fixture = new ApplicationFixture(); await fixture.Diagnosed();
        var context = fixture.Context(); var operation = fixture.Store.Peek(Id(1))!;
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.AuthorizeExecution(context, Authorization(operation)));
        await Assert.ThrowsAsync<ArgumentNullException>(() => fixture.Service.AuthorizeExecution(context, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => fixture.Service.AuthorizeExecution(null!, Authorization(operation)));
        await fixture.Service.AuthorizeExecution(context, Authorization(operation, authorizationId: context.IntentId));
        var another = fixture.Context();
        Assert.False((await fixture.Service.AuthorizeExecution(another, Authorization(operation, authorizationId: another.IntentId))).StateChanged);
        Assert.Equal(context.IntentId, fixture.Store.Peek(Id(1))!.Authorization!.AuthorizationId);
        await fixture.Service.StartExecution(fixture.Context());
        await Assert.ThrowsAsync<OperationConflictException>(() => fixture.Service.CancelOperation(fixture.Context(), "Too late"));
        Assert.Equal(OperationStatus.InExecution, fixture.Store.Peek(Id(1))!.Status);
    }

    [Fact]
    public async Task StaleVersionAndCancellationTokenLeaveNoSuccessfulReceipt()
    {
        var fixture = new ApplicationFixture(); await fixture.Receive();
        var stale = fixture.Context(expectedVersion: 0);
        await Assert.ThrowsAsync<OperationConcurrencyException>(() => fixture.Service.StartDiagnostic(stale));
        Assert.Null(await fixture.Store.FindReceipt(stale.IntentId, default));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.StartDiagnostic(fixture.Context(), cancellation.Token));
        Assert.Single(fixture.Store.Events);
        await fixture.Service.StartDiagnostic(fixture.Context());
        using var late = new CancellationTokenSource(); fixture.Prices.OnRequest = late.Cancel;
        var finish = fixture.Context();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.CompleteDiagnostic(finish, [Consumable()], late.Token));
        Assert.Equal(OperationStatus.InDiagnostic, fixture.Store.Peek(Id(1))!.Status);
        Assert.Null(await fixture.Store.FindReceipt(finish.IntentId, default));
        Assert.Equal(2, fixture.Store.Events.Count);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FailedCommitAfterPricingOrProgressLeavesOriginalSnapshotUntouched(bool progress)
    {
        var fixture = new ApplicationFixture();
        if (progress) await fixture.Executing(); else await fixture.Diagnosing();
        var context = fixture.Context(); var before = fixture.Store.Peek(Id(1))!;
        var events = fixture.Store.Events.Count; var receipts = fixture.Store.ReceiptCount;
        fixture.Store.CommitFailure = new IOException("failed after candidate prepared");
        await Assert.ThrowsAsync<IOException>(() => progress
            ? fixture.Service.ExecuteJob(context, Id(40))
            : fixture.Service.CompleteDiagnostic(context, [Consumable()]));
        var after = fixture.Store.Peek(Id(1))!;
        Assert.Equal(before.Version, after.Version); Assert.Equal(before.History, after.History);
        Assert.Equal(before.Jobs, after.Jobs); Assert.Equal(before.Diagnostic?.DiagnosticId, after.Diagnostic?.DiagnosticId);
        Assert.Equal(before.Consumables, after.Consumables);
        Assert.Equal(events, fixture.Store.Events.Count); Assert.Equal(receipts, fixture.Store.ReceiptCount);
        Assert.Null(await fixture.Store.FindReceipt(context.IntentId, default));
        fixture.Store.CommitFailure = null;
        if (progress) await fixture.Service.ExecuteJob(context, Id(40));
        else await fixture.Service.CompleteDiagnostic(context, [Consumable()]);
        Assert.Equal(events + 1, fixture.Store.Events.Count);
    }

    [Fact]
    public async Task CollectionReorderingDoesNotChangeIntentButDifferentAuthorizationTermsDo()
    {
        var fixture = new ApplicationFixture(); var context = fixture.Context();
        var jobs = new[] { new RequestedJob(Id(10), Id(100)), new RequestedJob(Id(11), Id(100)) };
        var result = await fixture.Service.Receive(context, Id(2), jobs, [new(Id(20), Id(200), 2)]);
        Assert.Equal(result, await fixture.Service.Receive(context, Id(2), jobs.Reverse(), [new(Id(20), Id(200), 2)]));
        await fixture.Service.StartDiagnostic(fixture.Context());
        await fixture.Service.CompleteDiagnostic(fixture.Context(), []);
        var authorize = fixture.Context(); var operation = fixture.Store.Peek(Id(1))!;
        var authorization = Authorization(operation, authorizationId: authorize.IntentId);
        await fixture.Service.AuthorizeExecution(authorize, authorization);
        var changed = Authorization(operation, authorizationId: authorize.IntentId, budgetId: Id(999));
        await Assert.ThrowsAsync<IntentConflictException>(() => fixture.Service.AuthorizeExecution(authorize, changed));
        Assert.Equal(4, fixture.Store.Events.Count);
    }

    [Fact]
    public async Task CancelReadyWorkPreservesOriginalDiagnosticAndBlocksLatePhysicalStart()
    {
        var fixture = new ApplicationFixture(); await fixture.Ready();
        await fixture.Service.CancelOperation(fixture.Context(), "Stock timeout");
        await Assert.ThrowsAsync<OperationConflictException>(() => fixture.Service.StartExecution(fixture.Context()));
        var detail = await fixture.Queries.Get(Id(1));
        Assert.Equal(OperationStatus.Canceled, detail.Status); Assert.False(detail.IsActive);
        Assert.NotNull(detail.Diagnostic); Assert.NotNull(detail.Authorization);
        Assert.Equal("Stock timeout", detail.CancellationReason);
        Assert.Equal(OperationEventKind.OperationCanceled, fixture.Store.Events[^1].Kind);
    }

}
