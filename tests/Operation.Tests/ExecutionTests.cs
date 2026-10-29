using GearFlow.Operation.Domain;
using Xunit;
using static Operation.Tests.DomainFixture;

namespace Operation.Tests;

public class ExecutionTests
{
    [Fact]
    public void AuthorizationQueuesWorkWithoutClaimingPhysicalStartOrConsumingStock()
    {
        var operation = Diagnosed(2);
        var authorization = Authorization(operation);
        Assert.True(operation.AuthorizeExecution(authorization, "system:saga", At(4)));
        Assert.Equal(OperationStatus.ReadyForExecution, operation.Status);
        Assert.Equal(At(4), operation.ExecutionAuthorizedAt); Assert.Null(operation.ExecutionStartedAt);
        Assert.Equal(Id(4), operation.Authorization!.BudgetId);
        Assert.Equal(2, operation.Jobs.Count);
        Assert.Equal(Id(100), operation.Jobs[0].CatalogJobId);
        Assert.Equal(Id(100), operation.Jobs[1].CatalogJobId);
        Assert.NotEqual(operation.Jobs[0].BudgetJobId, operation.Jobs[1].BudgetJobId);
        Assert.Throws<NotSupportedException>(() => ((IList<JobProgress>)operation.Jobs).Clear());
        Unchanged(operation, () => operation.ExecuteJob(Id(40), "m", At(5)));
        operation.StartExecution("m", At(5));
        Assert.Equal(At(5), operation.ExecutionStartedAt);
        Assert.Equal(OperationStatus.InExecution, operation.Status);
    }

    [Fact]
    public void AuthorizationCopiesMappingAndRequiresExactlyTheApprovedLaborLines()
    {
        var operation = Diagnosed();
        var maps = new List<ApprovedJob> { new(Id(40), Id(10)) };
        var authorization = new ExecutionAuthorization(Id(5), Id(4), 1, operation.Diagnostic!, maps);
        maps.Clear();
        Assert.Single(authorization.Jobs);
        Assert.Throws<NotSupportedException>(() => ((IList<ApprovedJob>)authorization.Jobs).Clear());
        Assert.Throws<ArgumentException>(() => new ApprovedJob(Guid.Empty, Id(10)));
        Assert.Throws<ArgumentException>(() => new ApprovedJob(Id(40), Guid.Empty));
        Assert.Throws<ArgumentException>(() => new ExecutionAuthorization(Guid.Empty, Id(4), 1, operation.Diagnostic!, []));
        Assert.Throws<ArgumentException>(() => new ExecutionAuthorization(Id(5), Guid.Empty, 1, operation.Diagnostic!, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExecutionAuthorization(Id(5), Id(4), 2, operation.Diagnostic!, maps));
        Assert.Throws<ArgumentNullException>(() => new ExecutionAuthorization(Id(5), Id(4), 1, null!, []));
        Assert.Throws<ArgumentException>(() => new ExecutionAuthorization(Id(5), Id(4), 1, operation.Diagnostic!, []));
        Assert.Throws<ArgumentException>(() => new ExecutionAuthorization(Id(5), Id(4), 1, operation.Diagnostic!, [new(Id(40), Id(20))]));
        Assert.Throws<ArgumentException>(() => new ExecutionAuthorization(Id(5), Id(4), 1, operation.Diagnostic!, [new(Id(40), Id(10)), new(Id(41), Id(10))]));
        var two = Diagnosed(2);
        Assert.Throws<ArgumentException>(() => new ExecutionAuthorization(Id(5), Id(4), 1, two.Diagnostic!, [new(Id(40), Id(10)), new(Id(40), Id(11))]));
        Assert.Throws<ArgumentException>(() => new ExecutionAuthorization(Id(5), Id(4), 1, operation.Diagnostic!, [null!]));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void AuthorizationRejectsAnySnapshotDivergenceWithoutPartialProgress(int change)
    {
        var operation = Diagnosed();
        var original = operation.Diagnostic!;
        var lines = original.Lines.ToList();
        if (change == 0) lines[0] = new(lines[0].LineId, lines[0].ItemType, lines[0].ItemId, "Nome diferente", 1, 1000, 1000);
        if (change == 1) lines[0] = new(lines[0].LineId, lines[0].ItemType, lines[0].ItemId, lines[0].Name, 1, 1001, 1001);
        if (change == 2) lines.RemoveAt(1);
        var snapshot = new DiagnosticSnapshot(change == 3 ? Id(999) : original.DiagnosticId, lines,
            change == 4 ? At(3) : original.PricedAt, change == 5 ? "other-policy" : original.PricingPolicyId);
        var authorization = Authorization(operation, snapshot: snapshot);
        Unchanged(operation, () => operation.AuthorizeExecution(authorization, "system:saga", At(4)));
    }

    [Fact]
    public void AuthorizationRequiresCompletedDiagnosticAndRejectsNullPayload()
    {
        var operation = Received();
        var other = Diagnosed();
        Unchanged(operation, () => operation.AuthorizeExecution(Authorization(other), "s", At(4)));
        var diagnosed = Diagnosed();
        Unchanged<ArgumentNullException>(diagnosed, () => diagnosed.AuthorizeExecution(null!, "s", At(4)));
        Unchanged<ArgumentException>(diagnosed, () => diagnosed.AuthorizeExecution(Authorization(diagnosed), " ", At(4)));
        Unchanged(operation, () => operation.StartExecution("m", At(4)));
    }

    [Fact]
    public void SemanticallyRepeatedAuthorizationDoesNotRegressWorkOrReplaceFirstAuthorization()
    {
        var operation = Ready(); var version = operation.Version;
        var repeated = Authorization(operation, authorizationId: Id(55));
        Assert.False(operation.AuthorizeExecution(repeated, "s", At(4)));
        Assert.Equal(version, operation.Version); Assert.Equal(Id(5), operation.Authorization!.AuthorizationId);
        operation.StartExecution("m", At(5));
        Assert.False(operation.AuthorizeExecution(repeated, "s", At(6)));
        Assert.Equal(OperationStatus.InExecution, operation.Status);
        operation.FinalizeExecution("m", At(7));
        version = operation.Version;
        Assert.False(operation.AuthorizeExecution(repeated, "s", At(8)));
        Assert.Equal(version, operation.Version); Assert.Equal(OperationStatus.ExecutionCompleted, operation.Status);
        Unchanged(operation, () => operation.AuthorizeExecution(Authorization(operation, budgetId: Id(99)), "s", At(8)));
        var conflictingMap = new ExecutionAuthorization(Id(5), Id(4), 1, operation.Diagnostic!, [new(Id(99), Id(10))]);
        Unchanged(operation, () => operation.AuthorizeExecution(conflictingMap, "s", At(8)));
    }

    [Fact]
    public void SameTermsCanBeReorderedWithoutBeingMistakenForDifferentBudget()
    {
        var operation = Ready(2);
        var original = operation.Diagnostic!;
        var reversed = new DiagnosticSnapshot(original.DiagnosticId, original.Lines.Reverse(), original.PricedAt, original.PricingPolicyId);
        var authorization = new ExecutionAuthorization(Id(99), Id(4), 1, reversed, operation.Authorization!.Jobs.Reverse());
        var version = operation.Version;
        Assert.False(operation.AuthorizeExecution(authorization, "s", At(5)));
        Assert.Equal(version, operation.Version);
    }

    [Fact]
    public void JobsAreExecutedByBudgetLineAndRepeatedCatalogJobsHaveIndependentProgress()
    {
        var operation = Executing(2);
        Unchanged(operation, () => operation.ExecuteJob(Id(100), "m", At(6))); // catalog ID is not a budget line.
        Unchanged(operation, () => operation.ExecuteJob(Id(10), "m", At(6))); // diagnostic line also is not BudgetJobId.
        Unchanged<ArgumentException>(operation, () => operation.ExecuteJob(Guid.Empty, "m", At(6)));
        Unchanged<ArgumentException>(operation, () => operation.ExecuteJob(Id(40), " ", At(6)));
        operation.ExecuteJob(Id(40), " staff:2 ", At(6));
        Assert.Equal(At(6), operation.Jobs[0].ExecutedAt);
        Assert.Equal("staff:2", operation.Jobs[0].ExecutedBy);
        Assert.Null(operation.Jobs[1].ExecutedAt);
        Assert.Equal(Id(40), operation.History[^1].SubjectId);
        Unchanged(operation, () => operation.ExecuteJob(Id(40), "m", At(7)));
        operation.ExecuteJob(Id(41), "staff:3", At(7));
        Assert.Equal(At(7), operation.Jobs[1].ExecutedAt);
    }

    [Fact]
    public void FinalizationPreservesLegacyRuleWithoutNewPendingJobGuard()
    {
        var operation = Executing(2);
        operation.ExecuteJob(Id(40), "m", At(6));
        operation.FinalizeExecution("m", At(7));
        Assert.Equal(OperationStatus.ExecutionCompleted, operation.Status);
        Assert.Equal(At(7), operation.ExecutionCompletedAt);
        Assert.Null(operation.Jobs[1].ExecutedAt);
        Unchanged(operation, () => operation.FinalizeExecution("m", At(8)));
        Unchanged(operation, () => operation.ExecuteJob(Id(41), "m", At(8)));
        var ready = Ready();
        Unchanged(ready, () => ready.FinalizeExecution("m", At(6)));
    }

    [Fact]
    public void VersionAndHistoryDescribeActualActionsInOrder()
    {
        var operation = Executing();
        operation.ExecuteJob(Id(40), "m", At(6));
        operation.FinalizeExecution("m", At(7));
        Assert.Equal(Enumerable.Range(1, 7).Select(i => (long)i), operation.History.Select(item => item.Version));
        Assert.Equal(operation.Version, operation.History.Count);
        Assert.Equal(new[] { OperationAction.Received, OperationAction.DiagnosticStarted, OperationAction.DiagnosticCompleted,
            OperationAction.ExecutionAuthorized, OperationAction.ExecutionStarted, OperationAction.JobExecuted,
            OperationAction.ExecutionCompleted }, operation.History.Select(item => item.Action));
        Assert.Equal(At(7), operation.UpdatedAt);
    }
}
