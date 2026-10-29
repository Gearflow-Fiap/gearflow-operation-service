using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using GearFlow.Operation.Application;
using GearFlow.Operation.Domain;
using GearFlow.Operation.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using W = GearFlow.Contracts.Operation.V1;

namespace GearFlow.Operation.Infrastructure.Messaging;

public sealed class PermanentMessageException(string message) : Exception(message);

internal sealed class BrokerInboxRow
{
    public Guid MessageId { get; set; }
    public Guid CorrelationId { get; set; }
    public string Fingerprint { get; set; } = "";
    public string CommandName { get; set; } = "";
    public string? TraceParent { get; set; }
    public string? TraceState { get; set; }
    public bool IsCompleted { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public Guid? ReplyId { get; set; }
    public string? ReplyType { get; set; }
    public string? ReplyJson { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

public sealed class OperationCommandHandler(IDbContextFactory<OperationDbContext> factory, OperationService service, TimeProvider clock)
{
    public async Task Handle(object message, Guid? transportId, Guid? transportCorrelation, CancellationToken ct)
    {
        var command = Prepare(message);
        if (command.Id == Guid.Empty || command.Order == Guid.Empty || command.Id == command.Order || command.At == default || command.At.Offset != TimeSpan.Zero ||
            transportId != command.Id || transportCorrelation != command.Order)
            throw new PermanentMessageException("Identidade/correlação ou data do envelope inválida.");
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(message.GetType().FullName + ":" + OperationWire.Serialize(message))));
        await using var db = await factory.CreateDbContextAsync(ct);
        // Registrar hash antes do efeito: mesmo um crash entre as duas transações preserva a identidade do payload.
        await using (var registration = await db.Database.BeginTransactionAsync(ct))
        {
            await Lock(db, command.Id, ct);
            var existing = await db.Set<BrokerInboxRow>().AsNoTracking().SingleOrDefaultAsync(item => item.MessageId == command.Id, ct);
            if (existing is not null)
            {
                if (existing.Fingerprint != fingerprint) throw new PermanentMessageException("MessageId reutilizado com conteúdo divergente.");
                if (existing.IsCompleted) return;
            }
            else
            {
                db.Add(new BrokerInboxRow { MessageId = command.Id, CorrelationId = command.Order, Fingerprint = fingerprint,
                    CommandName = message.GetType().Name, ReceivedAt = clock.GetUtcNow(), TraceParent = OperationTelemetry.TraceParent, TraceState = OperationTelemetry.TraceState });
                await db.SaveChangesAsync(ct);
            }
            await registration.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await Lock(db, command.Id, ct);
        var row = await db.Set<BrokerInboxRow>().SingleAsync(item => item.MessageId == command.Id, ct);
        if (row.IsCompleted) return;
        object? reply = null;
        try
        {
            var result = await command.Apply(new(command.Id, command.Order, "system:os"), ct);
            // Evento de uma mudança já está atomicamente persistido no journal/outbox da aplicação.
            // Novo comando semanticamente repetido precisa de confirmação própria, sem nova versão.
            if (!result.StateChanged) reply = await Confirmation(message, result, ct);
        }
        catch (Exception error) when (error is OperationConflictException or KeyNotFoundException)
        {
            reply = message is W.CancelOperation
                ? new W.OperationCancellationRejected(command.Order, Guid.NewGuid(), command.Id, clock.GetUtcNow(), null, "CancellationNotAllowed")
                : new W.OperationCommandRejected(command.Order, Guid.NewGuid(), command.Id, clock.GetUtcNow(), null, message.GetType().Name, "StateConflict");
        }
        catch (IntentConflictException) { throw new PermanentMessageException("Intenção já usada com outro conteúdo."); }
        if (reply is not null)
        {
            row.ReplyId = (Guid)reply.GetType().GetProperty("MessageId")!.GetValue(reply)!;
            row.ReplyType = reply.GetType().Name; row.ReplyJson = OperationWire.Serialize(reply);
        }
        row.IsCompleted = true; row.ProcessedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }
    private static async Task Lock(OperationDbContext db, Guid id, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s';", ct);
        var key = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes("transport:" + id.ToString("D"))));
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key});", ct);
    }
    private async Task<object> Confirmation(object message, CommandResult result, CancellationToken ct)
    {
        var id = Guid.NewGuid(); var order = result.ServiceOrderId; var at = clock.GetUtcNow();
        var persisted = message is W.StartExecution or W.CancelOperation or W.DeactivateOperation ? await new PostgresOperationStore(factory).Load(order, ct) : null;
        return message switch
        {
            W.StartDiagnostic command => new W.DiagnosticQueued(order, id, command.MessageId, at, result.Version, "system:os", result.DiagnosticId!.Value),
            W.StartExecution command => new W.ExecutionAuthorized(order, id, command.MessageId, at, result.Version, "system:os", command.Snapshot.DiagnosticId,
                command.BudgetId, command.BudgetRevision, persisted!.Authorization!.AuthorizationId),
            W.CancelOperation command => new W.OperationCanceled(order, id, command.MessageId, at, result.Version, "system:os", persisted!.CancellationReason!),
            W.UpdateOperationVehicle command => new W.VehicleUpdated(order, id, command.MessageId, at, result.Version, "system:os", command.VehicleId),
            W.DeactivateOperation command => new W.OperationDeactivated(order, id, command.MessageId, at, result.Version, "system:os", persisted!.DeactivationReason!),
            _ => throw new PermanentMessageException("Comando desconhecido.")
        };
    }
    private Prepared Prepare(object message)
    {
        try
        {
            return message switch
            {
                W.StartDiagnostic command => Receive(command),
                W.StartExecution command => Authorize(command),
                W.CancelOperation command when ValidReason(command.Reason) => new(command.CorrelationId, command.MessageId, command.OccurredOn, (context, ct) => service.CancelOperation(context, command.Reason, ct)),
                W.UpdateOperationVehicle command when command.VehicleId != Guid.Empty => new(command.CorrelationId, command.MessageId, command.OccurredOn, (context, ct) => service.UpdateVehicle(context, command.VehicleId, ct)),
                W.DeactivateOperation command when ValidReason(command.Reason) => new(command.CorrelationId, command.MessageId, command.OccurredOn, (context, ct) => service.Deactivate(context, command.Reason, ct)),
                _ => throw new PermanentMessageException("Contrato inválido ou comando desconhecido.")
            };
        }
        catch (Exception error) when (error is ArgumentException or OverflowException or NullReferenceException)
        { throw new PermanentMessageException("Payload de comando inválido."); }
    }
    private Prepared Receive(W.StartDiagnostic command)
    {
        if (command.ServiceOrderCode is <= 0 || command.VehicleId == Guid.Empty || command.RequestedJobs is null || command.RequestedParts is null ||
            command.RequestedJobs.Length == 0 || command.RequestedJobs.Length + command.RequestedParts.Length > DiagnosticSnapshot.MaximumLines)
            throw new PermanentMessageException("Pedido inicial inválido.");
        var jobs = command.RequestedJobs.Select(item => new RequestedJob(item.LineId, item.CatalogJobId)).ToArray();
        var parts = command.RequestedParts.Select(item => new RequestedPart(item.LineId, item.PartId, item.Quantity)).ToArray();
        if (jobs.Select(item => item.LineId).Concat(parts.Select(item => item.LineId)).Distinct().Count() != jobs.Length + parts.Length)
            throw new PermanentMessageException("LineId repetido.");
        return new(command.CorrelationId, command.MessageId, command.OccurredOn, (context, ct) => service.Receive(context, command.VehicleId, jobs, parts, command.ServiceOrderCode, ct));
    }
    private Prepared Authorize(W.StartExecution command)
    {
        var snapshot = command.Snapshot;
        if (snapshot is null || snapshot.Currency != "BRL" || snapshot.Items is null || snapshot.Items.Length > DiagnosticSnapshot.MaximumLines ||
            snapshot.PricedAt == default || snapshot.PricedAt.Offset != TimeSpan.Zero || command.Jobs is null || command.Jobs.Length > DiagnosticSnapshot.MaximumLines)
            throw new PermanentMessageException("Snapshot inválido.");
        var domain = new DiagnosticSnapshot(snapshot.DiagnosticId, snapshot.Items.Select(line => new DiagnosticLine(line.LineId,
            (OperationItemType)line.ItemType, line.ItemId, line.Name, line.Quantity, line.UnitPriceCents, line.LineTotalCents)), snapshot.PricedAt, snapshot.PricingPolicyId);
        if (domain.TotalPriceCents != snapshot.TotalPriceCents) throw new PermanentMessageException("Total divergente.");
        var authorization = new ExecutionAuthorization(command.MessageId, command.BudgetId, command.BudgetRevision, domain,
            command.Jobs.Select(job => new ApprovedJob(job.BudgetJobId, job.DiagnosticLineId)));
        return new(command.CorrelationId, command.MessageId, command.OccurredOn, (context, ct) => service.AuthorizeExecution(context, authorization, ct));
    }
    private static bool ValidReason(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 2000;
    private sealed record Prepared(Guid Order, Guid Id, DateTimeOffset At, Func<CommandContext, CancellationToken, Task<CommandResult>> Apply);
}
