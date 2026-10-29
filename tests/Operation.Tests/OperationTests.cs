using GearFlow.Operation.Application;
using GearFlow.Operation.Domain;
using Xunit;
using static Operation.Tests.DomainFixture;

namespace Operation.Tests;

// Regressão da aplicação inicial após alteração de assinaturas; não substitui o passo 3.
public class OperationTests
{
    private sealed class MemoryUnitOfWork : IOperationUnitOfWork
    {
        public WorkOperation? Current { get; set; }
        public bool FailCommit { get; set; }
        public List<OperationEvent> Events { get; } = [];
        public Task<WorkOperation?> Load(Guid id, CancellationToken ct) => Task.FromResult(Current);
        public Task Commit(WorkOperation operation, OperationEvent pendingEvent, CancellationToken ct)
        {
            if (FailCommit) throw new IOException("database unavailable");
            Current = operation; Events.Add(pendingEvent); return Task.CompletedTask;
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task ApplicationStillCommitsLocalFlowAfterDomainSignatureMigration()
    {
        var uow = new MemoryUnitOfWork(); var service = new OperationService(uow, new FixedClock(Time));
        await service.Receive(Id(1), Id(1), Id(2), [new(Id(10), Id(100))], [new(Id(20), Id(200), 2)], "m");
        await service.StartDiagnostic(Id(1), Id(1), "m");
        var snapshot = Snapshot(uow.Current!, [Consumable()], pricedAt: Time);
        await service.CompleteDiagnostic(Id(1), Id(1), snapshot, [Consumable()], "m");
        await service.AuthorizeExecution(Id(1), Id(1), Authorization(uow.Current!), "s");
        Assert.Equal(OperationStatus.ReadyForExecution, uow.Current!.Status);
        Assert.Equal("ExecutionAuthorized.v1", uow.Events[^1].Type);
        await service.StartExecution(Id(1), Id(1), "m");
        await service.ExecuteJob(Id(1), Id(1), Id(40), "m");
        await service.FinalizeExecution(Id(1), Id(1), "m");
        Assert.Equal(7, uow.Events.Count);
        Assert.All(uow.Events, message =>
        {
            Assert.Equal(Id(1), message.ServiceOrderId); Assert.Equal(Id(1), message.CorrelationId); Assert.Equal(Time, message.OccurredAt);
        });
        Assert.Equal(7, uow.Events.Select(message => message.EventId).Distinct().Count());
        Assert.Equal("ExecutionFinished.v1", uow.Events[^1].Type);
        Assert.Equal(OperationStatus.ExecutionCompleted, uow.Current.Status);
    }

    [Fact]
    public async Task MissingOperationAndInvalidCorrelationDoNotCommit()
    {
        var uow = new MemoryUnitOfWork(); var service = new OperationService(uow, new FixedClock(Time));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.StartDiagnostic(Id(1), Id(1), "m"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.Receive(Id(1), Guid.Empty, Id(2), [new(Id(10), Id(100))], [], "m"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.StartDiagnostic(Id(1), Id(2), "m"));
        Assert.Empty(uow.Events);
    }

    [Fact]
    public async Task DuplicateReceiveRemainsExplicitlyPendingApplicationIdempotencyWork()
    {
        var uow = new MemoryUnitOfWork { Current = Received() }; var service = new OperationService(uow, new FixedClock(Time));
        var original = uow.Current;
        await Assert.ThrowsAsync<OperationConflictException>(() => service.Receive(Id(1), Id(1), Id(2),
            [new(Id(10), Id(100))], [], "m"));
        Assert.Same(original, uow.Current); Assert.Empty(uow.Events);
    }

    [Fact]
    public async Task InvalidTransitionDoesNotCommitAnEvent()
    {
        var uow = new MemoryUnitOfWork { Current = Received() }; var service = new OperationService(uow, new FixedClock(Time));
        await Assert.ThrowsAsync<OperationConflictException>(() => service.FinalizeExecution(Id(1), Id(1), "m"));
        Assert.Empty(uow.Events); Assert.Equal(1, uow.Current.Version);
    }

    [Fact]
    public async Task FailedCommitOnCreationDoesNotClaimSuccess()
    {
        var uow = new MemoryUnitOfWork { FailCommit = true }; var service = new OperationService(uow, new FixedClock(Time));
        await Assert.ThrowsAsync<IOException>(() => service.Receive(Id(1), Id(1), Id(2), [new(Id(10), Id(100))], [], "m"));
        Assert.Null(uow.Current); Assert.Empty(uow.Events);
        // Este fake não comprova rollback de um agregado já carregado. Continua pendente no passo 3.
    }

    [Fact]
    public async Task NoOpCancellationDoesNotCreateAnotherLocalEvent()
    {
        var uow = new MemoryUnitOfWork { Current = Received() }; var service = new OperationService(uow, new FixedClock(Time));
        await service.CancelOperation(Id(1), Id(1), "Cancelar", "s");
        await service.CancelOperation(Id(1), Id(1), "Cancelar", "s");
        Assert.Equal("OperationCanceled.v1", Assert.Single(uow.Events).Type);
    }

    [Fact]
    public async Task RepeatedAuthorizationDoesNotCreateAnotherLocalEvent()
    {
        var uow = new MemoryUnitOfWork { Current = Ready() }; var service = new OperationService(uow, new FixedClock(At(4)));
        await service.AuthorizeExecution(Id(1), Id(1), Authorization(uow.Current), "s");
        Assert.Empty(uow.Events);
    }
}
