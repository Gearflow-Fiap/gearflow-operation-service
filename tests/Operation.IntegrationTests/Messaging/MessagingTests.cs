using Microsoft.AspNetCore.Hosting;
using GearFlow.Operation.Application;
using GearFlow.Operation.Infrastructure.Messaging;
using GearFlow.Operation.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;
using W = GearFlow.Contracts.Operation.V1;

namespace Operation.IntegrationTests.Messaging;

[Collection("Postgres")]
public class MessagingTests(PostgresFixture postgres)
{
    [Fact]
    public async Task DurableQueueDeliversCommandAfterConsumerDisconnectAndReconnect()
    {
        var h = await postgres.Create(); await using var broker = await BrokerHarness.Start(h);
        await broker.StopConsumers();
        var command = Receive(h); await broker.Send("operation-start-diagnostic", command, command.MessageId, h.OrderId);
        Assert.Null(await h.Store.Load(h.OrderId, default));
        await broker.StartConsumers(); await broker.WaitInbox(command.MessageId); await broker.Flush();
        Assert.Equal(1, (await broker.Event<W.DiagnosticQueued>(command.MessageId)).OperationVersion);
        Assert.Equal((1, 1, 1, 1), await h.Counts());
    }
    [Fact]
    public async Task BrokerVehicleAndDeactivationCommandsPreserveOriginalRequestAndAudit()
    {
        var h = await postgres.Create(); await using var broker = await BrokerHarness.Start(h);
        var receive = Receive(h); await broker.Send("operation-start-diagnostic", receive, receive.MessageId, h.OrderId); await broker.WaitInbox(receive.MessageId);
        var vehicle = new W.UpdateOperationVehicle(h.OrderId, Guid.NewGuid(), h.Clock.Now, Guid.NewGuid());
        await broker.Send("operation-update-vehicle", vehicle, vehicle.MessageId, h.OrderId); await broker.WaitInbox(vehicle.MessageId);
        var deactivate = new W.DeactivateOperation(h.OrderId, Guid.NewGuid(), h.Clock.Now, "Duplicada");
        await broker.Send("operation-deactivate", deactivate, deactivate.MessageId, h.OrderId); await broker.WaitInbox(deactivate.MessageId); await broker.Flush();
        Assert.Equal(vehicle.VehicleId, (await broker.Event<W.VehicleUpdated>(vehicle.MessageId)).VehicleId);
        Assert.Equal("Duplicada", (await broker.Event<W.OperationDeactivated>(deactivate.MessageId)).Reason);
        var data = await h.Queries.Get(h.OrderId); Assert.False(data.IsActive); Assert.Equal(h.VehicleId, data.InitialVehicleId); Assert.Equal(3, data.Version);
    }
    [Fact]
    public async Task ParallelDispatchersClaimDifferentRowsAndPreserveAllEventIdentities()
    {
        var h = await postgres.Create(); await h.Executing(); await h.Service().ExecuteJob(h.Context(), h.BudgetJobId); await h.Service().FinalizeExecution(h.Context());
        var publisher = new RecordingPublisher();
        var counts = await Task.WhenAll(new OperationOutboxDispatcher(h.Factory, publisher, h.Clock).Dispatch(), new OperationOutboxDispatcher(h.Factory, publisher, h.Clock).Dispatch());
        Assert.Equal(7, counts.Sum()); Assert.Equal(7, publisher.Items.Select(item => item.Id).Distinct().Count()); Assert.Empty(await h.Store.ReadPendingEvents());
    }
    [Fact]
    public async Task InvalidMetadataNeverCreatesInboxAndSnapshotMismatchIsDurableBusinessRejection()
    {
        var h = await postgres.Create(); var handler = Handler(h); var receive = Receive(h);
        await Assert.ThrowsAsync<PermanentMessageException>(() => handler.Handle(receive, Guid.NewGuid(), h.OrderId, default));
        await Assert.ThrowsAsync<PermanentMessageException>(() => handler.Handle(receive with { RequestedParts = null! }, receive.MessageId, h.OrderId, default));
        await using (var db = h.Factory.CreateDbContext()) Assert.Empty(await db.Set<BrokerInboxRow>().ToArrayAsync());
        await h.Diagnosed(); var snapshot = OperationWire.Snapshot((await h.Queries.Get(h.OrderId)).Diagnostic!) with { PricingPolicyId = "divergent-policy" };
        var authorization = new W.StartExecution(h.OrderId, Guid.NewGuid(), h.Clock.Now, Guid.NewGuid(), 1, snapshot, [new(h.BudgetJobId, h.Job.LineId)]);
        await handler.Handle(authorization, authorization.MessageId, h.OrderId, default);
        await using var fresh = h.Factory.CreateDbContext(); var outcome = await fresh.Set<BrokerInboxRow>().SingleAsync();
        Assert.Equal(nameof(W.OperationCommandRejected), outcome.ReplyType); Assert.True(outcome.IsCompleted); Assert.Equal(3, (await h.Queries.Get(h.OrderId)).Version);
        await Assert.ThrowsAsync<PermanentMessageException>(() => handler.Handle(authorization with { Snapshot = snapshot with { TotalPriceCents = 0 } }, authorization.MessageId, h.OrderId, default));
    }

    [Fact]
    public async Task UnroutablePublishRemainsPendingInsteadOfBeingMarkedDelivered()
    {
        var h = await postgres.Create(); await using var broker = await BrokerHarness.Start(h, observeEvents: false);
        await h.Receive();
        await Assert.ThrowsAsync<MassTransit.MessageReturnedException>(() => broker.Flush());
        Assert.Single(await h.Store.ReadPendingEvents());
    }

    [Fact]
    public async Task ApiHostStartsConsumersAndDeliversOutboxWithoutManualFlush()
    {
        var h = await postgres.Create(); await using var broker = await BrokerHarness.Start(h); await broker.StopConsumers();
        await using var baseApi = new ApiFactory(h);
        await using var api = baseApi.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Messaging:Enabled", "true").UseSetting("Messaging:DispatchEnabled", "true");
            builder.UseSetting("RabbitMq:Host", broker.Options.Host).UseSetting("RabbitMq:Port", broker.Options.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("RabbitMq:VirtualHost", broker.Vhost).UseSetting("RabbitMq:Username", broker.Options.Username).UseSetting("RabbitMq:Password", broker.Options.Password);
        });
        using var client = api.CreateClient();
        var command = Receive(h); await broker.Send("operation-start-diagnostic", command, command.MessageId, h.OrderId); await broker.WaitInbox(command.MessageId);
        Assert.Equal(1, (await broker.Event<W.DiagnosticQueued>(command.MessageId)).OperationVersion);
        await BrokerHarness.Until(async () => (await h.Store.ReadPendingEvents()).Count == 0);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task SemanticConfirmationUsesPersistedReasonInsteadOfReplacingAudit(bool deactivate)
    {
        var h = await postgres.Create(); if (deactivate) await h.Receive();
        var firstId = Guid.NewGuid(); var secondId = Guid.NewGuid();
        object first = deactivate ? new W.DeactivateOperation(h.OrderId, firstId, h.Clock.Now, "Original") : new W.CancelOperation(h.OrderId, firstId, h.Clock.Now, "Original");
        object second = deactivate ? new W.DeactivateOperation(h.OrderId, secondId, h.Clock.Now, "Outra solicitação") : new W.CancelOperation(h.OrderId, secondId, h.Clock.Now, "Outra solicitação");
        var handler = Handler(h); await handler.Handle(first, firstId, h.OrderId, default); await handler.Handle(second, secondId, h.OrderId, default);
        await using var db = h.Factory.CreateDbContext(); var reply = (await db.Set<BrokerInboxRow>().SingleAsync(row => row.MessageId == secondId)).ReplyJson!;
        using var json = System.Text.Json.JsonDocument.Parse(reply); Assert.Equal("Original", json.RootElement.GetProperty("reason").GetString());
        var publisher = new RecordingPublisher(); await new OperationOutboxDispatcher(h.Factory, publisher, h.Clock).Dispatch();
        Assert.All(publisher.Items.Where(item => item.Body is W.OperationCanceled or W.OperationDeactivated), item =>
        {
            using var body = System.Text.Json.JsonDocument.Parse(OperationWire.Serialize(item.Body)); Assert.Equal("Original", body.RootElement.GetProperty("reason").GetString());
        });
        Assert.Equal(deactivate ? 2 : 1, (await h.Queries.Get(h.OrderId)).Version);
    }

    private static W.StartDiagnostic Receive(IntegrationHarness h, Guid? id = null) => new(h.OrderId, id ?? Guid.NewGuid(), h.Clock.Now, h.VehicleId, 45,
        [new(h.Job.LineId, h.Job.CatalogJobId)], [new(h.Part.LineId, h.Part.PartId, h.Part.Quantity)]);
    private static OperationCommandHandler Handler(IntegrationHarness h, Microsoft.EntityFrameworkCore.IDbContextFactory<OperationDbContext>? factory = null) => new(factory ?? h.Factory, h.Service(), h.Clock);

    [Fact]
    public async Task RabbitCommandsAndPublishedEventsRunFullOperationWithStableEnvelopeAndIdempotency()
    {
        var h = await postgres.Create(); await using var broker = await BrokerHarness.Start(h);
        var receive = Receive(h);
        await broker.Send("operation-start-diagnostic", receive, receive.MessageId, h.OrderId); await broker.WaitInbox(receive.MessageId); await broker.Flush();
        var queued = await broker.Event<W.DiagnosticQueued>(receive.MessageId); Assert.Equal(1, queued.OperationVersion);
        await broker.Send("operation-start-diagnostic", receive, receive.MessageId, h.OrderId); await broker.WaitInbox(receive.MessageId);
        var again = receive with { MessageId = Guid.NewGuid() };
        await broker.Send("operation-start-diagnostic", again, again.MessageId, h.OrderId); await broker.WaitInbox(again.MessageId); await broker.Flush();
        var confirmed = await broker.Event<W.DiagnosticQueued>(again.MessageId); Assert.Equal(queued.DiagnosticId, confirmed.DiagnosticId); Assert.Equal(1, confirmed.OperationVersion);
        h.Clock.Advance(); await h.Service().StartDiagnostic(h.Context()); h.Clock.Advance(); await h.Service().CompleteDiagnostic(h.Context(), [h.Consumable]);
        await broker.Flush();
        var diagnostic = (await h.Queries.Get(h.OrderId)).Diagnostic!;
        var authorization = new W.StartExecution(h.OrderId, Guid.NewGuid(), h.Clock.Now, Guid.NewGuid(), 1, OperationWire.Snapshot(diagnostic), [new(h.BudgetJobId, h.Job.LineId)]);
        await broker.Send("operation-start-execution", authorization, authorization.MessageId, h.OrderId); await broker.WaitInbox(authorization.MessageId); await broker.Flush();
        Assert.Equal(authorization.MessageId, (await broker.Event<W.ExecutionAuthorized>(authorization.MessageId)).AuthorizationMessageId);
        var repeatedAuthorization = authorization with { MessageId = Guid.NewGuid() };
        await broker.Send("operation-start-execution", repeatedAuthorization, repeatedAuthorization.MessageId, h.OrderId); await broker.WaitInbox(repeatedAuthorization.MessageId); await broker.Flush();
        var authorizationReply = await broker.Event<W.ExecutionAuthorized>(repeatedAuthorization.MessageId);
        Assert.Equal(authorization.MessageId, authorizationReply.AuthorizationMessageId); Assert.Equal(4, authorizationReply.OperationVersion);
        h.Clock.Advance(); await h.Service().StartExecution(h.Context()); h.Clock.Advance(); await h.Service().ExecuteJob(h.Context(), h.BudgetJobId);
        h.Clock.Advance(); var finish = h.Context(); await h.Service().FinalizeExecution(finish); await broker.Flush();
        Assert.Equal(new[] { h.BudgetJobId }, (await broker.Event<W.ExecutionFinished>(finish.IntentId)).ExecutedBudgetJobIds);
        Assert.Equal((1, 7, 9, 7), await h.Counts());
        var cancel = new W.CancelOperation(h.OrderId, Guid.NewGuid(), h.Clock.Now, "Compensação");
        await broker.Send("operation-cancel-operation", cancel, cancel.MessageId, h.OrderId); await broker.WaitInbox(cancel.MessageId); await broker.Flush();
        Assert.Equal("CancellationNotAllowed", (await broker.Event<W.OperationCancellationRejected>(cancel.MessageId)).Code);
        await broker.Send("operation-cancel-operation", cancel, cancel.MessageId, h.OrderId);
        Assert.Equal(0, await broker.ErrorCount("operation-cancel-operation"));
    }

    [Fact]
    public async Task RabbitMalformedAndReusedMessageIdsGoToErrorWithoutAnotherBusinessEffect()
    {
        var h = await postgres.Create(); await using var broker = await BrokerHarness.Start(h);
        var command = Receive(h); await broker.Send("operation-start-diagnostic", command, command.MessageId, h.OrderId); await broker.WaitInbox(command.MessageId);
        await broker.Send("operation-start-diagnostic", command with { VehicleId = Guid.NewGuid() }, command.MessageId, h.OrderId);
        await BrokerHarness.Until(async () => await broker.ErrorCount("operation-start-diagnostic") >= 1);
        var malformed = command with { MessageId = Guid.NewGuid(), RequestedJobs = [] };
        await broker.Send("operation-start-diagnostic", malformed, malformed.MessageId, h.OrderId);
        await BrokerHarness.Until(async () => await broker.ErrorCount("operation-start-diagnostic") >= 2);
        Assert.Equal((1, 1, 1, 1), await h.Counts());
    }

    [Fact]
    public async Task CrashAfterBusinessCommitRecoversPendingInboxWithoutRepeatingHistory()
    {
        var h = await postgres.Create(); var command = Receive(h);
        var broken = Handler(h, new InterceptingFactory(h.ConnectionString, new FailSecondSaveInterceptor()));
        await Assert.ThrowsAsync<IOException>(() => broken.Handle(command, command.MessageId, h.OrderId, default));
        Assert.Equal((1, 1, 1, 1), await h.Counts());
        await using (var db = h.Factory.CreateDbContext()) Assert.False((await db.Set<BrokerInboxRow>().SingleAsync()).IsCompleted);
        await Handler(h).Handle(command, command.MessageId, h.OrderId, default);
        Assert.Equal((1, 1, 1, 1), await h.Counts());
        await using var fresh = h.Factory.CreateDbContext(); Assert.True((await fresh.Set<BrokerInboxRow>().SingleAsync()).IsCompleted);
    }

    [Fact]
    public async Task TombstoneAndBusinessRejectionsHaveDurableRepliesAndFences()
    {
        var h = await postgres.Create(); var handler = Handler(h);
        var cancel = new W.CancelOperation(h.OrderId, Guid.NewGuid(), h.Clock.Now, "Cancelada antes de criar");
        await handler.Handle(cancel, cancel.MessageId, h.OrderId, default);
        var receive = Receive(h); await handler.Handle(receive, receive.MessageId, h.OrderId, default);
        await using var db = h.Factory.CreateDbContext(); var rows = await db.Set<BrokerInboxRow>().ToArrayAsync();
        Assert.Equal(2, rows.Length); Assert.NotNull(rows.Single(row => row.MessageId == receive.MessageId).ReplyJson);
        Assert.True((await h.Queries.Get(h.OrderId)).IsTombstone); Assert.Equal((1, 1, 1, 1), await h.Counts());
    }

    [Fact]
    public async Task DispatcherRetriesWithSameIdentityAfterPublishButFailedDatabaseSave()
    {
        var h = await postgres.Create(); await h.Receive(); var publisher = new RecordingPublisher();
        var broken = new OperationOutboxDispatcher(new InterceptingFactory(h.ConnectionString, new FailFirstSaveInterceptor()), publisher, h.Clock);
        await Assert.ThrowsAsync<IOException>(() => broken.Dispatch()); Assert.Single(await h.Store.ReadPendingEvents());
        await new OperationOutboxDispatcher(h.Factory, publisher, h.Clock).Dispatch();
        Assert.Equal(2, publisher.Items.Count); Assert.Equal(publisher.Items[0].Id, publisher.Items[1].Id); Assert.Equal(OperationWire.Serialize(publisher.Items[0].Body), OperationWire.Serialize(publisher.Items[1].Body)); Assert.Empty(await h.Store.ReadPendingEvents());
    }
}
internal sealed class RecordingPublisher : IOperationPublisher
{
    public List<(Guid Id, object Body)> Items { get; } = [];
    public Task Publish(object message, Guid messageId, Guid correlationId, Guid causationId, CancellationToken ct) { lock (Items) Items.Add((messageId, message)); return Task.CompletedTask; }
}
internal sealed class FailFirstSaveInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
{
    public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
        Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default) => throw new IOException("Failure after Publish before marking delivery.");
}





