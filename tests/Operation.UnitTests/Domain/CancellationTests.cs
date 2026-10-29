using GearFlow.Operation.Domain;
using Xunit;
using static Operation.UnitTests.Support.DomainFixture;

namespace Operation.UnitTests.Domain;

public class CancellationTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DeactivationIsDifferentFromCancellationAndBlocksFurtherWork(bool startDiagnostic)
    {
        var operation = startDiagnostic ? Diagnosing() : Received();
        var status = operation.Status;
        Assert.True(operation.Deactivate("Retirada da fila", "system:os", At(2)));
        Assert.Equal(status, operation.Status); Assert.False(operation.IsActive);
        Assert.Null(operation.CancellationReason);
        Assert.Equal("Retirada da fila", operation.DeactivationReason);
        var version = operation.Version;
        Assert.False(operation.Deactivate("Repetida", "system:os", At(3)));
        Assert.Equal(version, operation.Version);
        Unchanged(operation, () => operation.StartDiagnostic("m", At(3)));
        Unchanged(operation, () => operation.UpdateVehicle(Id(9), "m", At(3)));
        if (startDiagnostic) Unchanged(operation, () => operation.CompleteDiagnostic(Snapshot(operation), [], "m", At(3)));
        Assert.True(operation.CancelOperation("Cancelamento definitivo", "system:saga", At(3)));
        Assert.Equal(OperationStatus.Canceled, operation.Status);
    }

    [Fact]
    public void DeactivationAfterDiagnosticAndInvalidReasonsAreRejected()
    {
        var operation = Diagnosed();
        Unchanged(operation, () => operation.Deactivate("Retirar", "s", At(4)));
        var received = Received();
        Unchanged<ArgumentException>(received, () => received.Deactivate(" ", "s", At(1)));
        Unchanged<ArgumentException>(received, () => received.CancelOperation(" ", "s", At(1)));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void CancellationIsIdempotentInEveryPreRepairState(int stage)
    {
        var operation = stage switch { 0 => Received(), 1 => Diagnosing(), 2 => Diagnosed(), 3 => Ready(), _ => Diagnosing() };
        if (stage == 4) operation.FailDiagnostic("Impossível", "m", At(3));
        Assert.True(operation.CancelOperation("Prazo expirado", "system:saga", At(6)));
        var version = operation.Version; var history = operation.History.Count;
        Assert.False(operation.CancelOperation("Prazo expirado", "system:saga", At(7)));
        Assert.Equal(version, operation.Version); Assert.Equal(history, operation.History.Count);
        Assert.False(operation.IsActive); Assert.Equal(OperationStatus.Canceled, operation.Status);
        Assert.Equal("Prazo expirado", operation.CancellationReason);
        Unchanged(operation, () => operation.StartDiagnostic("m", At(8)));
        Unchanged(operation, () => operation.StartExecution("m", At(8)));
        var validAuthorization = Authorization(Diagnosed());
        Unchanged(operation, () => operation.AuthorizeExecution(validAuthorization, "s", At(8)));
    }

    [Fact]
    public void TombstoneDoesNotInventVehicleOrRequestedItemsAndCannotAuthorizeLateWork()
    {
        var operation = WorkOperation.CreateCanceled(Id(1), "Cancelado antes de chegar", "system:saga", Time);
        Assert.True(operation.IsTombstone); Assert.False(operation.IsActive);
        Assert.Null(operation.VehicleId); Assert.Null(operation.DiagnosticId); Assert.Null(operation.ServiceOrderCode);
        Assert.Empty(operation.RequestedJobs); Assert.Empty(operation.RequestedParts);
        Assert.Equal(OperationAction.Canceled, Assert.Single(operation.History).Action);
        Assert.False(operation.CancelOperation("Repetir", "s", At(1)));
        Unchanged(operation, () => operation.StartDiagnostic("m", At(1)));
        Unchanged(operation, () => operation.AuthorizeExecution(Authorization(Diagnosed()), "s", At(4)));
        Assert.Throws<ArgumentException>(() => WorkOperation.CreateCanceled(Guid.Empty, "x", "s", Time));
        Assert.Throws<ArgumentException>(() => WorkOperation.CreateCanceled(Id(1), " ", "s", Time));
    }

    [Fact]
    public void CancellationAndPhysicalStartHaveMutuallyExclusiveSequentialOutcomes()
    {
        var cancelFirst = Ready();
        cancelFirst.CancelOperation("Cancelar", "s", At(5));
        Unchanged(cancelFirst, () => cancelFirst.StartExecution("m", At(6)));
        var startFirst = Ready();
        startFirst.StartExecution("m", At(5));
        Unchanged(startFirst, () => startFirst.CancelOperation("Cancelar", "s", At(6)));
        startFirst.FinalizeExecution("m", At(6));
        Unchanged(startFirst, () => startFirst.CancelOperation("Cancelar", "s", At(7)));
        // Não é teste de disputa entre transações/processos: isso exige adapter de persistência.
    }
}
