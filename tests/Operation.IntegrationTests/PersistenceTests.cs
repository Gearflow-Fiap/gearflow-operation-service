using GearFlow.Operation.Application;
using GearFlow.Operation.Domain;
using GearFlow.Operation.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Operation.IntegrationTests;

[Collection("Postgres")]
public sealed class PersistenceTests(PostgresFixture postgres)
{
    private static OperationWrite Write(WorkOperation operation, long? previousVersion, OperationEventKind kind,
        Guid? intent = null, Guid? eventId = null, string fingerprint = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")
    {
        var id = intent ?? Guid.NewGuid();
        var result = new CommandResult(operation.ServiceOrderId, operation.Status, operation.Version, operation.DiagnosticId,
            operation.Authorization?.BudgetId, true);
        var pending = new OperationEvent(eventId ?? Guid.NewGuid(), operation.ServiceOrderId, id, kind, operation.Version,
            operation.UpdatedAt, operation.History[^1].Actor, OperationDetails.From(operation));
        return new(operation, previousVersion, new(id, fingerprint, result), Array.AsReadOnly(new[] { pending }));
    }

    [Fact]
    public async Task MigrationsApplyIdempotentlyAndCanRollbackOnlyTheOwnedEmptyDatabase()
    {
        var test = await postgres.Create(migrate: false);
        await using (var db = test.Factory.CreateDbContext())
        {
            await db.Database.MigrateAsync(); await db.Database.MigrateAsync();
            Assert.Single(await db.Database.GetAppliedMigrationsAsync());
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.False(db.Database.HasPendingModelChanges());
            await db.GetService<IMigrator>().MigrateAsync("0");
            Assert.Empty(await db.Database.GetAppliedMigrationsAsync());
        }
        await using var fresh = test.Factory.CreateDbContext();
        await fresh.Database.MigrateAsync(); Assert.Equal(0, await fresh.Operations.CountAsync());
    }

    [Fact]
    public async Task FullFlowRestoresAcrossFreshContextsWithReceiptHistoryAndOutboxIntact()
    {
        var test = await postgres.Create(); await test.Executing();
        test.Clock.Advance(); await test.Service().ExecuteJob(test.Context(), test.BudgetJobId);
        test.Clock.Advance(); await test.Service().FinalizeExecution(test.Context());
        var fresh = new PostgresOperationStore(new OperationContextFactory(test.ConnectionString));
        var operation = (await fresh.Load(test.OrderId, default))!;
        Assert.Equal(OperationStatus.ExecutionCompleted, operation.Status); Assert.Equal(7, operation.Version);
        Assert.Equal(test.VehicleId, operation.InitialVehicleId); Assert.Equal(test.VehicleId, operation.VehicleId);
        Assert.Equal(test.Part.Quantity, operation.RequestedParts[0].Quantity);
        Assert.Equal(1.5m, operation.Consumables[0].Quantity); Assert.Equal(1900, operation.Diagnostic!.TotalPriceCents);
        Assert.Equal(test.BudgetJobId, operation.Jobs[0].BudgetJobId); Assert.NotNull(operation.Jobs[0].ExecutedAt);
        Assert.Equal((1, 7, 7, 7), await test.Counts());
        var pending = await fresh.ReadPendingEvents(); Assert.Equal(7, pending.Count);
        Assert.Equal(OperationEventKind.ExecutionFinished, pending[^1].Kind);
        Assert.Equal(1900, pending[^1].Data.Diagnostic!.TotalPriceCents);
        Assert.Equal(operation.Jobs[0].ExecutedAt, pending[^1].Data.Jobs[0].ExecutedAt);
        Assert.Equal(7, pending.Select(item => item.CausationId).Distinct().Count());
    }

    [Fact]
    public async Task ExactAndSemanticReplaySurviveNewStoreWithoutDuplicateRows()
    {
        var test = await postgres.Create(); var context = test.Context(); var first = await test.Receive(context);
        await test.Service().StartDiagnostic(test.Context());
        Assert.Equal(first, await test.Receive(context));
        Assert.False((await test.Receive()).StateChanged);
        Assert.Equal((1, 2, 3, 2), await test.Counts());
        var receipt = await test.Store.FindReceipt(context.IntentId, default);
        Assert.Equal(first, receipt!.Result);
        await Assert.ThrowsAsync<IntentConflictException>(() => test.Service().Receive(context, Guid.NewGuid(), [test.Job], [test.Part], 45));
        Assert.Equal((1, 2, 3, 2), await test.Counts());
    }

    [Fact]
    public async Task DetachedReadCannotChangePersistedVehicleOrHistory()
    {
        var test = await postgres.Create(); await test.Receive();
        var loaded = (await test.Store.Load(test.OrderId, default))!;
        loaded.UpdateVehicle(Guid.NewGuid(), "staff:test", test.Clock.Now);
        Assert.Equal(2, loaded.Version);
        var fresh = (await test.Store.Load(test.OrderId, default))!;
        Assert.Equal(test.VehicleId, fresh.VehicleId); Assert.Equal(1, fresh.Version); Assert.Single(fresh.History);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FailureAfterFirstSaveRollsBackRootHistoryReceiptAndOutbox(bool existing)
    {
        var test = await postgres.Create(); if (existing) await test.Receive();
        var before = await test.Counts(); var context = test.Context();
        var failing = test.Service(new InterceptingFactory(test.ConnectionString, new FailSecondSaveInterceptor()));
        await Assert.ThrowsAsync<IOException>(() => existing ? failing.StartDiagnostic(context)
            : failing.Receive(context, test.VehicleId, [test.Job], [test.Part], 45));
        Assert.Equal(before, await test.Counts()); Assert.Null(await test.Store.FindReceipt(context.IntentId, default));
        var operation = await test.Store.Load(test.OrderId, default);
        if (existing)
        {
            Assert.Equal(OperationStatus.Received, operation!.Status); Assert.Equal(1, operation.Version);
            await test.Service().StartDiagnostic(context);
        }
        else { Assert.Null(operation); await test.Receive(context); }
    }

    [Fact]
    public async Task RealUniqueConstraintFailureAfterRootInsertDoesNotLeakAnyPartialRecord()
    {
        var test = await postgres.Create(); await test.Receive();
        var firstEvent = (await test.Store.ReadPendingEvents())[0];
        var other = new WorkOperation(Guid.NewGuid(), Guid.NewGuid(), [new(Guid.NewGuid(), Guid.NewGuid())], [], "staff:test", test.Clock.Now);
        var write = Write(other, null, OperationEventKind.DiagnosticQueued, eventId: firstEvent.EventId);
        await Assert.ThrowsAsync<DbUpdateException>(() => test.Store.Commit(write, default));
        Assert.Equal((1, 1, 1, 1), await test.Counts());
        Assert.Null(await test.Store.Load(other.ServiceOrderId, default));
        Assert.Null(await test.Store.FindReceipt(write.Receipt.IntentId, default));
    }

    [Fact]
    public async Task ConcurrentSameIntentReturnsWinnerBeforeCheckingStaleCreateVersion()
    {
        var test = await postgres.Create(); var intent = Guid.NewGuid();
        var one = new WorkOperation(test.OrderId, test.VehicleId, [test.Job], [test.Part], "staff:test", test.Clock.Now);
        var two = new WorkOperation(test.OrderId, test.VehicleId, [test.Job], [test.Part], "staff:test", test.Clock.Now);
        var results = await Task.WhenAll(test.Store.Commit(Write(one, null, OperationEventKind.DiagnosticQueued, intent), default),
            test.Store.Commit(Write(two, null, OperationEventKind.DiagnosticQueued, intent), default));
        Assert.Equal(results[0], results[1]); Assert.Equal((1, 1, 1, 1), await test.Counts());
    }

    [Fact]
    public async Task ConcurrentSameIntentWithDifferentFingerprintHasOneWinner()
    {
        var test = await postgres.Create(); var intent = Guid.NewGuid();
        var one = new WorkOperation(test.OrderId, test.VehicleId, [test.Job], [test.Part], "staff:test", test.Clock.Now);
        var two = one.Copy();
        var outcomes = await Task.WhenAll(Record.ExceptionAsync(() => test.Store.Commit(Write(one, null, OperationEventKind.DiagnosticQueued, intent), default)),
            Record.ExceptionAsync(() => test.Store.Commit(Write(two, null, OperationEventKind.DiagnosticQueued, intent,
                fingerprint: new string('B', 64)), default)));
        Assert.Single(outcomes, item => item is null); Assert.Single(outcomes, item => item is IntentConflictException);
        Assert.Equal((1, 1, 1, 1), await test.Counts());
    }

    [Fact]
    public async Task IndependentConnectionsCannotOverwriteTheSameReadVersion()
    {
        var test = await postgres.Create(); await test.Receive();
        var candidates = new List<OperationWrite>();
        for (var i = 0; i < 5; i++)
        {
            var operation = (await test.Store.Load(test.OrderId, default))!;
            operation.StartDiagnostic("staff:test", test.Clock.Now);
            candidates.Add(Write(operation, 1, OperationEventKind.DiagnosticStarted));
        }
        var outcomes = await Task.WhenAll(candidates.Select(write => Record.ExceptionAsync(() => test.Store.Commit(write, default))));
        Assert.Single(outcomes, item => item is null);
        Assert.Equal(4, outcomes.Count(item => item is OperationConcurrencyException));
        Assert.Equal((1, 2, 2, 2), await test.Counts());
    }

    [Fact]
    public async Task CancellationAndPhysicalStartCompeteOnOnePersistedVersion()
    {
        var test = await postgres.Create(); await test.Diagnosed();
        var context = test.Context(); var operation = (await test.Store.Load(test.OrderId, default))!;
        await test.Service().AuthorizeExecution(context, new(context.IntentId, Guid.NewGuid(), 1, operation.Diagnostic!, [new(test.BudgetJobId, test.Job.LineId)]));
        var start = (await test.Store.Load(test.OrderId, default))!; var cancel = start.Copy();
        start.StartExecution("staff:test", test.Clock.Now); cancel.CancelOperation("Timeout", "staff:test", test.Clock.Now);
        var outcomes = await Task.WhenAll(Record.ExceptionAsync(() => test.Store.Commit(Write(start, 4, OperationEventKind.ExecutionStarted), default)),
            Record.ExceptionAsync(() => test.Store.Commit(Write(cancel, 4, OperationEventKind.OperationCanceled), default)));
        Assert.Single(outcomes, item => item is null); Assert.Single(outcomes, item => item is OperationConcurrencyException);
        var final = (await test.Store.Load(test.OrderId, default))!;
        if (final.Status == OperationStatus.InExecution)
            await Assert.ThrowsAsync<OperationConflictException>(() => test.Service().CancelOperation(test.Context(), "Cancel"));
        else
        {
            Assert.Equal(OperationStatus.Canceled, final.Status);
            await Assert.ThrowsAsync<OperationConflictException>(() => test.Service().StartExecution(test.Context()));
        }
        Assert.Equal((1, 5, 5, 5), await test.Counts());
    }

    [Fact]
    public async Task TombstonePersistsAcrossContextsAndBlocksLateReceive()
    {
        var test = await postgres.Create(); await test.Service().CancelOperation(test.Context(), "Cancel before intake");
        Assert.True((await test.Store.Load(test.OrderId, default))!.IsTombstone);
        await Assert.ThrowsAsync<OperationConflictException>(() => test.Receive());
        Assert.Equal((1, 1, 1, 1), await test.Counts());
    }

    [Fact]
    public async Task QueriesPageFilterAndAggregateInPostgresAndDoNotLoadStateForLists()
    {
        var test = await postgres.Create(); await test.Executing(); test.Clock.Advance(); await test.Service().FinalizeExecution(test.Context());
        var summary = await test.Queries.Durations();
        Assert.Equal(new PhaseAverage(1, 1), summary.Diagnostic); Assert.Equal(new PhaseAverage(1, 1), summary.ExecutionQueue);
        Assert.Equal(new PhaseAverage(1, 1), summary.Execution);
        Assert.Empty((await test.Queries.Queue()).Items);
        var page = await test.Queries.List(0, 1000, OperationStatus.ExecutionCompleted);
        Assert.Equal(1, page.TotalCount); Assert.Equal(100, page.PageSize); Assert.Equal(test.OrderId, Assert.Single(page.Items).ServiceOrderId);
        Assert.Empty((await test.Queries.List(int.MaxValue, 100)).Items);
        var history = await test.Queries.History(test.OrderId, 2, 2);
        Assert.Equal(new long[] { 3, 4 }, history.Items.Select(item => item.Version)); Assert.Equal(6, history.TotalCount);
        Assert.Empty((await test.Queries.History(test.OrderId, int.MaxValue, 100)).Items);
        Assert.Null(await test.Store.FindReceipt(Guid.NewGuid(), default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => test.Queries.History(Guid.NewGuid()));
        await using var db = test.Factory.CreateDbContext();
        var row = await db.Operations.SingleAsync(); row.StateJson = "{}"; await db.SaveChangesAsync();
        Assert.Single((await test.Queries.List()).Items); // Projeção SQL não precisa do snapshot/histórico.
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => test.Queries.Get(test.OrderId));
    }

    [Fact]
    public async Task EmptyDurationSummaryAndReadValidationAreExplicit()
    {
        var test = await postgres.Create();
        Assert.Equal(new PhaseAverage(null, 0), (await test.Queries.Durations()).Execution);
        Assert.Null(await test.Store.Load(Guid.NewGuid(), default));
        Assert.Empty(await test.Store.ReadPendingEvents());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => test.Store.ReadPendingEvents(0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => test.Store.GetPage(new(OperationListScope.All), new(0, 1), default));
        await Assert.ThrowsAsync<ArgumentException>(() => test.Store.GetPage(new((OperationListScope)99), new(1, 1), default));
    }
}
