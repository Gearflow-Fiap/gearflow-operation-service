using GearFlow.Operation.Application;
using Xunit;
using static Operation.Tests.DomainFixture;

namespace Operation.Tests;

public class QueueTests
{
    [Fact]
    public void ActionableQueueExcludesCommercialWaitInactiveFailedAndTerminalWork()
    {
        var received = Received(); var diagnosing = Diagnosing(); var diagnosed = Diagnosed();
        var ready = Ready(); var executing = Executing();
        var inactive = Received(); inactive.Deactivate("Inativa", "s", At(1));
        var failed = Diagnosing(); failed.FailDiagnostic("Impossível", "m", At(2));
        var canceled = Ready(); canceled.CancelOperation("Cancelar", "s", At(5));
        var completed = Executing(); completed.FinalizeExecution("m", At(6));
        var queue = ExecutionQueue.Order([received, diagnosing, diagnosed, ready, executing, inactive, failed, canceled, completed]);
        Assert.Equal(new[] { executing, ready, diagnosing, received }, queue);
    }

    [Fact]
    public void QueueBreaksTiesByAgeAndOrderIdWithoutChangingAggregates()
    {
        var newer = Received(orderId: Id(1), createdAt: At(1));
        var olderHighId = Received(orderId: Id(3)); var olderLowId = Received(orderId: Id(2));
        var queue = ExecutionQueue.Order([newer, olderHighId, olderLowId]);
        Assert.Equal(new[] { olderLowId, olderHighId, newer }, queue);
        Assert.All(queue, operation => Assert.Equal(1, operation.Version));
    }
}
