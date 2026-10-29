using GearFlow.Operation.Application;
using GearFlow.Operation.Domain;
using Xunit;
using static Operation.UnitTests.Support.DomainFixture;

namespace Operation.UnitTests.Application;

public class IntentConcurrencyTests
{
    private static void SynchronizeNextTwoCommits(IsolatedStore store)
    {
        var arrivals = 0; var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.BeforeCommit = async _ =>
        {
            if (Interlocked.Increment(ref arrivals) == 2) gate.TrySetResult();
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(5));
        };
    }

    [Fact]
    public async Task ConcurrentSameIntentReturnsWinningResultWithoutDuplicatingEffects()
    {
        var fixture = new ApplicationFixture(); SynchronizeNextTwoCommits(fixture.Store);
        var context = fixture.Context();
        var results = await Task.WhenAll(fixture.Receive(context), fixture.Receive(context));
        Assert.Equal(results[0], results[1]); Assert.Single(fixture.Store.Events);
        Assert.Equal(1, fixture.Store.ReceiptCount); Assert.Equal(1, fixture.Store.Commits);
        Assert.Equal(results[0].DiagnosticId, fixture.Store.Peek(Id(1))!.DiagnosticId);
    }

    [Fact]
    public async Task ConcurrentSameIntentWithDifferentBodyCannotWinTwice()
    {
        var fixture = new ApplicationFixture(); SynchronizeNextTwoCommits(fixture.Store); var context = fixture.Context();
        var first = Record.ExceptionAsync(() => fixture.Receive(context));
        var second = Record.ExceptionAsync(() => fixture.Service.Receive(context, Id(3), [new(Id(10), Id(100))], [new(Id(20), Id(200), 2)], 45));
        var results = await Task.WhenAll(first, second);
        Assert.Single(results, error => error is null); Assert.Single(results, error => error is IntentConflictException);
        Assert.Single(fixture.Store.Events); Assert.Equal(1, fixture.Store.ReceiptCount);
    }

    [Fact]
    public async Task DifferentIntentCommandsUsingSameStoredVersionCannotOverwriteOneAnother()
    {
        var fixture = new ApplicationFixture(); await fixture.Receive(); SynchronizeNextTwoCommits(fixture.Store);
        var first = Record.ExceptionAsync(() => fixture.Service.StartDiagnostic(fixture.Context()));
        var second = Record.ExceptionAsync(() => fixture.Service.Deactivate(fixture.Context(), "Withdraw"));
        var results = await Task.WhenAll(first, second);
        Assert.Single(results, error => error is null); Assert.Single(results, error => error is OperationConcurrencyException);
        Assert.Equal(2, fixture.Store.Events.Count); Assert.Equal(2, fixture.Store.ReceiptCount);
        Assert.Equal(2, fixture.Store.Peek(Id(1))!.Version);
        Assert.Equal(1, fixture.Store.Events[0].Data.Version);
        // Verifica o protocolo de CAS no double de storage; constraints/isolamento reais exigem PostgreSQL depois.
    }

    [Fact]
    public async Task VersionFailureHappensBeforePricingAndReceiptCanBeRetriedAfterReload()
    {
        var fixture = new ApplicationFixture(); await fixture.Diagnosing();
        var intent = Id(9000);
        await Assert.ThrowsAsync<OperationConcurrencyException>(() => fixture.Service.CompleteDiagnostic(
            fixture.Context(intent, expectedVersion: 1), [Consumable()]));
        Assert.Equal(0, fixture.Prices.Calls);
        await fixture.Service.CompleteDiagnostic(fixture.Context(intent, expectedVersion: 2), [Consumable()]);
        Assert.Equal(1, fixture.Prices.Calls);
    }
}
