using GearFlow.Operation.Infrastructure.Messaging;
using MassTransit;
using Xunit;
using W = GearFlow.Contracts.Operation.V1;

namespace Operation.UnitTests.Contracts;

public class EventTopologyTests
{
    private sealed class ExistingNames : IEntityNameFormatter
    {
        public string FormatEntityName<T>() => "existing:" + typeof(T).FullName;
    }

    [Fact]
    public void EveryPublishedEventHasItsAgreedNameAndStableWireIdentity()
    {
        var expected = new Dictionary<Type, string>
        {
            [typeof(W.DiagnosticQueued)] = "operation.diagnostic-queued",
            [typeof(W.DiagnosticStarted)] = "operation.diagnostic-started",
            [typeof(W.DiagnosticFinished)] = "operation.diagnostic-finished",
            [typeof(W.DiagnosticFailed)] = "operation.diagnostic-failed",
            [typeof(W.ExecutionAuthorized)] = "operation.execution-authorized",
            [typeof(W.ExecutionStarted)] = "operation.execution-started",
            [typeof(W.JobExecuted)] = "operation.job-executed",
            [typeof(W.ExecutionFinished)] = "operation.execution-finished",
            [typeof(W.VehicleUpdated)] = "operation.vehicle-updated",
            [typeof(W.OperationDeactivated)] = "operation.deactivated",
            [typeof(W.OperationCanceled)] = "operation.canceled",
            [typeof(W.OperationCommandRejected)] = "operation.command-rejected",
            [typeof(W.OperationCancellationRejected)] = "operation.cancellation-rejected",
        };
        Assert.Equal(13, W.OperationEventNames.All.Count);
        Assert.Equal(13, W.OperationEventNames.All.Values.Distinct().Count());
        var formatter = new OperationEventEntityNameFormatter(new ExistingNames());
        var format = typeof(OperationEventEntityNameFormatter).GetMethod(nameof(IEntityNameFormatter.FormatEntityName))!;
        foreach (var (type, exchange) in expected)
        {
            Assert.Equal(exchange, W.OperationEventNames.All[type]);
            Assert.Equal(exchange, format.MakeGenericMethod(type).Invoke(formatter, null));
            Assert.Equal("GearFlow.Contracts.Operation.V1", type.Namespace);
        }
    }

    [Fact]
    public void CommandsAndFrameworkTypesRetainTheirExistingEntityNames()
    {
        var fallback = new ExistingNames(); var formatter = new OperationEventEntityNameFormatter(fallback);
        Assert.Equal(fallback.FormatEntityName<W.StartDiagnostic>(), formatter.FormatEntityName<W.StartDiagnostic>());
        Assert.Equal(fallback.FormatEntityName<W.StartExecution>(), formatter.FormatEntityName<W.StartExecution>());
        Assert.Equal(fallback.FormatEntityName<W.CancelOperation>(), formatter.FormatEntityName<W.CancelOperation>());
        Assert.Equal(fallback.FormatEntityName<W.UpdateOperationVehicle>(), formatter.FormatEntityName<W.UpdateOperationVehicle>());
        Assert.Equal(fallback.FormatEntityName<W.DeactivateOperation>(), formatter.FormatEntityName<W.DeactivateOperation>());
        Assert.Equal(fallback.FormatEntityName<Fault<W.StartDiagnostic>>(), formatter.FormatEntityName<Fault<W.StartDiagnostic>>());
    }
}
