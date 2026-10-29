using GearFlow.Operation.Domain;
using Xunit;
using static Operation.UnitTests.Support.DomainFixture;

namespace Operation.UnitTests.Domain;

public class IntakeTests
{
    [Fact]
    public void IntakePreservesOrderVehicleRequestedItemsAndDistinctOccurrences()
    {
        var operation = Received(2);
        Assert.Equal(Id(1), operation.ServiceOrderId);
        Assert.Equal(Id(2), operation.VehicleId);
        Assert.Equal(Id(3), operation.DiagnosticId);
        Assert.Equal(45, operation.ServiceOrderCode);
        Assert.Equal(2, operation.RequestedJobs.Count);
        Assert.Equal(operation.RequestedJobs[0].CatalogJobId, operation.RequestedJobs[1].CatalogJobId);
        Assert.NotEqual(operation.RequestedJobs[0].LineId, operation.RequestedJobs[1].LineId);
        Assert.Equal(2, Assert.Single(operation.RequestedParts).Quantity);
        Assert.Equal(OperationStatus.Received, operation.Status);
        Assert.True(operation.IsActive); Assert.False(operation.IsTombstone);
        Assert.Equal(1, operation.Version);
        Assert.Equal(OperationAction.Received, Assert.Single(operation.History).Action);
    }

    [Fact]
    public void IntakeCopiesCallerCollectionsAndExposesReadOnlyViews()
    {
        var jobs = new List<RequestedJob> { new(Id(10), Id(100)) };
        var parts = new List<RequestedPart> { new(Id(20), Id(200), 2) };
        var operation = new WorkOperation(Id(1), Id(2), jobs, parts, "staff:1", Time);
        jobs.Clear(); parts[0] = new(Id(21), Id(201), 99);
        Assert.Single(operation.RequestedJobs); Assert.Equal(2, operation.RequestedParts[0].Quantity);
        Assert.Throws<NotSupportedException>(() => ((IList<RequestedJob>)operation.RequestedJobs).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<RequestedPart>)operation.RequestedParts).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<OperationHistory>)operation.History).Clear());
        Assert.NotNull(operation.DiagnosticId); Assert.Null(operation.ServiceOrderCode);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void IntakeRejectsEmptyIdentities(int target)
    {
        Assert.Throws<ArgumentException>(() => new WorkOperation(target == 0 ? Guid.Empty : Id(1),
            target == 1 ? Guid.Empty : Id(2), [new(Id(10), Id(100))], [], "staff:1", Time,
            diagnosticId: target == 2 ? Guid.Empty : Id(3)));
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)]
    public void ReadableCodeIsPositiveWhenSupplied(int code) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkOperation(Id(1), Id(2), [new(Id(10), Id(100))], [], "m", Time, code));

    [Fact]
    public void IntakeRequiresJobsAndGloballyUniqueLineIds()
    {
        Assert.Throws<ArgumentException>(() => new WorkOperation(Id(1), Id(2), [], [], "m", Time));
        Assert.Throws<ArgumentException>(() => new WorkOperation(Id(1), Id(2),
            [new(Id(10), Id(100)), new(Id(10), Id(101))], [], "m", Time));
        Assert.Throws<ArgumentException>(() => new WorkOperation(Id(1), Id(2),
            [new(Id(10), Id(100))], [new(Id(10), Id(200), 2)], "m", Time));
        Assert.Throws<ArgumentException>(() => new WorkOperation(Id(1), Id(2),
            Enumerable.Range(1, 201).Select(i => new RequestedJob(Id(i), Id(999))), [], "m", Time));
    }

    [Fact]
    public void IntakeRejectsNullEnumerablesAndElements()
    {
        Assert.Throws<ArgumentNullException>(() => new WorkOperation(Id(1), Id(2), null!, [], "m", Time));
        Assert.Throws<ArgumentNullException>(() => new WorkOperation(Id(1), Id(2), [new(Id(10), Id(100))], null!, "m", Time));
        Assert.Throws<ArgumentException>(() => new WorkOperation(Id(1), Id(2), [null!], [], "m", Time));
    }

    [Theory]
    [InlineData("")] [InlineData(" ")]
    public void ActorIsRequiredAtIntakeAndMutationWithoutPartialChange(string actor)
    {
        Assert.Throws<ArgumentException>(() => new WorkOperation(Id(1), Id(2), [new(Id(10), Id(100))], [], actor, Time));
        var operation = Received();
        Unchanged<ArgumentException>(operation, () => operation.StartDiagnostic(actor, At(1)));
    }

    [Fact]
    public void VehicleChangesBeforeDiagnosticCompletionButNotAfter()
    {
        var operation = Received();
        Assert.True(operation.UpdateVehicle(Id(8), "staff:1", At(1)));
        Assert.False(operation.UpdateVehicle(Id(8), "staff:1", At(1)));
        operation.StartDiagnostic("m", At(2));
        Assert.True(operation.UpdateVehicle(Id(9), "staff:1", At(2)));
        Assert.Equal(Id(9), operation.VehicleId);
        Unchanged<ArgumentException>(operation, () => operation.UpdateVehicle(Guid.Empty, "m", At(2)));
        operation.CompleteDiagnostic(Snapshot(operation), [], "m", At(3));
        Unchanged(operation, () => operation.UpdateVehicle(Id(2), "m", At(4)));
    }

    [Fact]
    public void ActorAndTimestampsAreNormalizedAndPastMutationIsRejected()
    {
        var local = Time.ToOffset(TimeSpan.FromHours(-3));
        var operation = new WorkOperation(Id(1), Id(2), [new(Id(10), Id(100))], [], "  staff:1  ", local);
        Assert.Equal(TimeSpan.Zero, operation.CreatedAt.Offset);
        Assert.Equal(Time, operation.UpdatedAt);
        Assert.Equal("staff:1", operation.History[0].Actor);
        Unchanged(operation, () => operation.StartDiagnostic("m", Time.AddTicks(-1)));
        operation.StartDiagnostic(" m ", local.AddMinutes(1));
        Assert.Equal("m", operation.History[^1].Actor);
        Assert.Equal(TimeSpan.Zero, operation.DiagnosticStartedAt!.Value.Offset);
    }
}
