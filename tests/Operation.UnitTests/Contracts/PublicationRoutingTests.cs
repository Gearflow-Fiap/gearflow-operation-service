using GearFlow.Operation.Infrastructure.Messaging;
using Xunit;
using W = GearFlow.Contracts.Operation.V1;

namespace Operation.UnitTests.Contracts;

public class PublicationRoutingTests
{
    [Fact]
    public void InformationalFactsDoNotRequireSubscribersButSagaFactsDo()
    {
        var order = Guid.NewGuid(); var id = Guid.NewGuid(); var cause = Guid.NewGuid(); var at = DateTimeOffset.UtcNow;
        Assert.False(MassTransitOperationPublisher.RequiresSagaRoute(new W.DiagnosticQueued(order, id, cause, at, 1, "system:os", Guid.NewGuid())));
        Assert.False(MassTransitOperationPublisher.RequiresSagaRoute(new W.DiagnosticStarted(order, id, cause, at, 2, "staff:test", Guid.NewGuid())));
        Assert.True(MassTransitOperationPublisher.RequiresSagaRoute(new W.DiagnosticFinished(order, id, cause, at, 3, "staff:test", null!)));
        Assert.True(MassTransitOperationPublisher.RequiresSagaRoute(new W.DiagnosticFailed(order, id, cause, at, 3, "staff:test", Guid.NewGuid(), "failed", "reason")));
        Assert.True(MassTransitOperationPublisher.RequiresSagaRoute(new W.ExecutionFinished(order, id, cause, at, 5, "staff:test", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), [])));
        Assert.True(MassTransitOperationPublisher.RequiresSagaRoute(new W.OperationCanceled(order, id, cause, at, 4, "system:os", "reason")));
        Assert.True(MassTransitOperationPublisher.RequiresSagaRoute(new W.OperationCommandRejected(order, id, cause, at, null, "StartExecution", "StateConflict")));
        Assert.True(MassTransitOperationPublisher.RequiresSagaRoute(new W.OperationCancellationRejected(order, id, cause, at, null, "CancellationNotAllowed")));
    }
}
