using GearFlow.Operation.Domain;
using Xunit;
using static Operation.Tests.DomainFixture;

namespace Operation.Tests;

public class DetachedCopyTests
{
    [Fact]
    public void CopyPreservesFullStateButSubsequentProgressAndHistoryAreIndependent()
    {
        var original = Executing(2); original.ExecuteJob(Id(40), "m", At(6));
        var copy = original.Copy();
        Assert.Equal(original.ServiceOrderId, copy.ServiceOrderId); Assert.Equal(original.DiagnosticId, copy.DiagnosticId);
        Assert.Equal(original.VehicleId, copy.VehicleId); Assert.Equal(original.InitialVehicleId, copy.InitialVehicleId);
        Assert.Equal(original.ServiceOrderCode, copy.ServiceOrderCode); Assert.Equal(original.Version, copy.Version);
        Assert.Equal(original.CreatedAt, copy.CreatedAt); Assert.Equal(original.UpdatedAt, copy.UpdatedAt);
        Assert.Equal(original.History, copy.History); Assert.Equal(original.Jobs, copy.Jobs);
        Assert.Equal(original.RequestedJobs, copy.RequestedJobs); Assert.Equal(original.RequestedParts, copy.RequestedParts);
        Assert.Equal(original.Consumables, copy.Consumables); Assert.Same(original.Diagnostic, copy.Diagnostic);
        Assert.Same(original.Authorization, copy.Authorization);
        copy.ExecuteJob(Id(41), "other", At(7)); copy.FinalizeExecution("other", At(8));
        Assert.Null(original.Jobs[1].ExecutedAt); Assert.NotNull(copy.Jobs[1].ExecutedAt);
        Assert.Equal(OperationStatus.InExecution, original.Status); Assert.Equal(OperationStatus.ExecutionCompleted, copy.Status);
        Assert.Equal(original.History.Count + 2, copy.History.Count);
    }

    [Fact]
    public void CopyRetainsInitialVehicleFailureDeactivationAndTombstoneWithoutRevivingAnything()
    {
        var operation = Received(); operation.UpdateVehicle(Id(9), "s", At(1));
        var copy = operation.Copy(); copy.UpdateVehicle(Id(8), "s", At(2));
        Assert.Equal(Id(2), copy.InitialVehicleId); Assert.Equal(Id(9), operation.VehicleId);
        operation.Deactivate("Withdraw", "s", At(2));
        Assert.Equal("Withdraw", operation.Copy().DeactivationReason); Assert.False(operation.Copy().IsActive);
        var failed = Diagnosing(); failed.FailDiagnostic("Impossible", "m", At(2));
        Assert.Equal("Impossible", failed.Copy().DiagnosticFailureReason);
        var canceled = WorkOperation.CreateCanceled(Id(7), "Fence", "s", Time).Copy();
        Assert.True(canceled.IsTombstone); Assert.False(canceled.IsActive); Assert.Null(canceled.VehicleId);
        Assert.Equal("Fence", canceled.CancellationReason); Assert.Equal(OperationStatus.Canceled, canceled.Status);
    }
}
