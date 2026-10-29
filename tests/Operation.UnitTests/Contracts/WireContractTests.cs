using System.Text.Json;
using GearFlow.Operation.Application;
using GearFlow.Operation.Domain;
using GearFlow.Operation.Infrastructure.Messaging;
using MassTransit;
using Xunit;
using W = GearFlow.Contracts.Operation.V1;

namespace Operation.UnitTests.Contracts;

public class WireContractTests
{
    [Fact]
    public async Task OversizedReasonCannotProduceAnEventOutsideThePublishedSchema()
    {
        var fixture = new ApplicationFixture(); await fixture.Diagnosing();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.FailDiagnostic(fixture.Context(), new string('x', 2001)));
        Assert.Equal(2, fixture.Store.Peek(DomainFixture.Id(1))!.Version);
        await fixture.Service.FailDiagnostic(fixture.Context(), new string('x', 2000));
        Assert.Equal(2000, fixture.Store.Peek(DomainFixture.Id(1))!.DiagnosticFailureReason!.Length);
    }

    [Fact]
    public void EveryManifestMessageHasMatchingClrUrnAndRealSerializerRoundTrip()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Contracts");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")));
        var messages = manifest.RootElement.GetProperty("messages").EnumerateArray().ToArray(); Assert.Equal(18, messages.Length);
        foreach (var item in messages)
        {
            var type = typeof(W.StartDiagnostic).Assembly.GetType(item.GetProperty("namespace").GetString() + "." + item.GetProperty("name").GetString())!;
            Assert.NotNull(type); Assert.Equal(item.GetProperty("urn").GetString(), MessageUrn.ForTypeString(type));
            var original = File.ReadAllText(Path.Combine(root, item.GetProperty("example").GetString()!));
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { RespectRequiredConstructorParameters = true };
            var value = JsonSerializer.Deserialize(original, type, options)!;
            Assert.Equal(JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(original)), JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(OperationWire.Serialize(value))));
            var extra = original.TrimEnd()[..original.TrimEnd().LastIndexOf('}')] + ",\"futureOptional\":true}";
            Assert.NotNull(JsonSerializer.Deserialize(extra, type, options));
            Assert.True(File.Exists(Path.Combine(root, item.GetProperty("schema").GetString()!)));
        }
    }

    [Fact]
    public void CommercialEnumsAreStringsAndRejectNumericOrUnknownValues()
    {
        Assert.Equal("\"Labor\"", JsonSerializer.Serialize(W.CommercialItemType.Labor));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<W.CommercialItemType>("0"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<W.CommercialItemType>("\"Unknown\""));
        Assert.Throws<InvalidDataException>(() => OperationWire.Deserialize("Unknown", "{}"));
        Assert.Throws<InvalidDataException>(() => OperationWire.Deserialize(nameof(W.OperationCanceled), "null"));
    }

    [Fact]
    public void EveryInternalActionHasAnExplicitVersionedProjectionWithoutLeakingAggregateHistory()
    {
        var operations = new List<(WorkOperation Operation, OperationEventKind Kind)>();
        var operation = DomainFixture.Received(); Save(OperationEventKind.DiagnosticQueued);
        operation.UpdateVehicle(DomainFixture.Id(99), "staff:1", DomainFixture.At(1)); Save(OperationEventKind.VehicleUpdated);
        operation.StartDiagnostic("staff:1", DomainFixture.At(2)); Save(OperationEventKind.DiagnosticStarted);
        operation.CompleteDiagnostic(DomainFixture.Snapshot(operation), [], "staff:1", DomainFixture.At(3)); Save(OperationEventKind.DiagnosticFinished);
        operation.AuthorizeExecution(DomainFixture.Authorization(operation), "system:os", DomainFixture.At(4)); Save(OperationEventKind.ExecutionAuthorized);
        operation.StartExecution("staff:1", DomainFixture.At(5)); Save(OperationEventKind.ExecutionStarted);
        operation.ExecuteJob(DomainFixture.Id(40), "staff:1", DomainFixture.At(6)); Save(OperationEventKind.JobExecuted);
        operation.FinalizeExecution("staff:1", DomainFixture.At(7)); Save(OperationEventKind.ExecutionFinished);
        operation = DomainFixture.Diagnosing(); operation.FailDiagnostic("Impossível", "staff:1", DomainFixture.At(2)); Save(OperationEventKind.DiagnosticFailed);
        operation = DomainFixture.Received(); operation.Deactivate("Duplicada", "staff:1", DomainFixture.At(1)); Save(OperationEventKind.OperationDeactivated);
        operation = WorkOperation.CreateCanceled(DomainFixture.Id(1), "Cancelada", "system:os", DomainFixture.Time); Save(OperationEventKind.OperationCanceled);
        Assert.Equal(11, operations.Count);
        foreach (var (state, kind) in operations)
        {
            var value = new OperationEvent(Guid.NewGuid(), state.ServiceOrderId, Guid.NewGuid(), kind, state.Version, state.UpdatedAt, state.History[^1].Actor, OperationDetails.From(state));
            var wire = OperationWire.Project(value); var json = OperationWire.Serialize(wire);
            using var parsed = JsonDocument.Parse(json);
            Assert.False(parsed.RootElement.TryGetProperty("history", out _)); Assert.False(parsed.RootElement.TryGetProperty("data", out _));
            Assert.Equal(value.EventId, parsed.RootElement.GetProperty("messageId").GetGuid());
            Assert.Equal(value.Version, parsed.RootElement.GetProperty("operationVersion").GetInt64());
            Assert.Equal(json, OperationWire.Serialize(OperationWire.Deserialize(wire.GetType().Name, json)));
        }
        void Save(OperationEventKind kind) => operations.Add((operation.Copy(), kind));
    }
}

