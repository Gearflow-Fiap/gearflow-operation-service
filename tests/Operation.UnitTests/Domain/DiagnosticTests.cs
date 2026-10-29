using GearFlow.Operation.Domain;
using Xunit;
using static Operation.UnitTests.Support.DomainFixture;

namespace Operation.UnitTests.Domain;

public class DiagnosticTests
{
    [Fact]
    public void CompleteDiagnosticPreservesRequestedItemsAndFractionalConsumables()
    {
        var operation = Diagnosing(2);
        var consumables = new List<DiagnosticConsumable> { Consumable() };
        var snapshot = Snapshot(operation, consumables);
        operation.CompleteDiagnostic(snapshot, consumables, "staff:2", At(3));
        consumables.Clear();
        Assert.Equal(OperationStatus.AwaitingExecutionAuthorization, operation.Status);
        Assert.Same(snapshot, operation.Diagnostic);
        Assert.Equal(1.5m, Assert.Single(operation.Consumables).Quantity);
        Assert.Equal(4, snapshot.Lines.Count);
        Assert.Equal(At(1), operation.DiagnosticStartedAt);
        Assert.Equal(At(3), operation.DiagnosticCompletedAt);
        Assert.Equal(3, operation.Version);
        Assert.Equal(operation.DiagnosticId, operation.History[^1].SubjectId);
        Assert.Throws<NotSupportedException>(() => ((IList<DiagnosticConsumable>)operation.Consumables).Clear());
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void SnapshotCannotOmitReplaceAddOrChangeRequestedItems(int change)
    {
        var operation = Diagnosing();
        var lines = Snapshot(operation).Lines.ToList();
        switch (change)
        {
            case 0: lines.RemoveAt(1); break;
            case 1: lines[0] = new(lines[0].LineId, OperationItemType.Labor, Id(999), "Outro", 1, 1000, 1000); break;
            case 2: lines.Add(new(Id(99), OperationItemType.Labor, Id(101), "Extra", 1, 10, 10)); break;
            case 3: lines[1] = new(lines[1].LineId, OperationItemType.Part, lines[1].ItemId, "Peça", 3, 300, 900); break;
            case 4: lines[0] = new(lines[0].LineId, OperationItemType.Part, lines[0].ItemId, "Tipo trocado", 1, 1000, 1000); break;
            case 5: lines[0] = new(Id(99), lines[0].ItemType, lines[0].ItemId, lines[0].Name, 1, 1000, 1000); break;
        }
        var snapshot = new DiagnosticSnapshot(operation.DiagnosticId!.Value, lines, At(2), "fixture-supplied");
        Unchanged(operation, () => operation.CompleteDiagnostic(snapshot, [], "m", At(3)));
    }

    [Fact]
    public void SnapshotMustMatchAllConsumablesWithoutReusingAnInitialLine()
    {
        var operation = Diagnosing();
        var consumable = Consumable();
        Unchanged(operation, () => operation.CompleteDiagnostic(Snapshot(operation), [consumable], "m", At(3)));
        Unchanged(operation, () => operation.CompleteDiagnostic(Snapshot(operation, [consumable]), [], "m", At(3)));
        var reused = new DiagnosticConsumable(operation.RequestedParts[0].LineId, Id(300), 1);
        Unchanged(operation, () => operation.CompleteDiagnostic(Snapshot(operation), [reused], "m", At(3)));
        Unchanged<ArgumentNullException>(operation, () => operation.CompleteDiagnostic(Snapshot(operation), null!, "m", At(3)));
        Unchanged<ArgumentException>(operation, () => operation.CompleteDiagnostic(Snapshot(operation), [null!], "m", At(3)));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void DiagnosticIdentityAndPricingWindowAreCheckedBeforeMutation(int change)
    {
        var operation = Diagnosing();
        var snapshot = new DiagnosticSnapshot(change == 0 ? Id(999) : operation.DiagnosticId!.Value,
            Snapshot(operation).Lines, change == 1 ? At(4) : change == 2 ? Time : At(2), "fixture");
        Unchanged(operation, () => operation.CompleteDiagnostic(snapshot, [], "m", At(3)));
    }

    [Fact]
    public void InvalidActorNullSnapshotAndWrongStateDoNotConcludeDiagnostic()
    {
        var operation = Diagnosing();
        Unchanged<ArgumentException>(operation, () => operation.CompleteDiagnostic(Snapshot(operation), [], " ", At(3)));
        Unchanged<ArgumentNullException>(operation, () => operation.CompleteDiagnostic(null!, [], "m", At(3)));
        var received = Received();
        Unchanged(received, () => received.CompleteDiagnostic(Snapshot(received), [], "m", At(3)));
        operation.CompleteDiagnostic(Snapshot(operation), [], "m", At(3));
        Unchanged(operation, () => operation.CompleteDiagnostic(Snapshot(operation), [], "m", At(4)));
    }

    [Fact]
    public void ExplicitFailureBlocksSuccessAndAllowsCancellation()
    {
        var operation = Diagnosing();
        Unchanged<ArgumentException>(operation, () => operation.FailDiagnostic(" ", "m", At(2)));
        operation.FailDiagnostic("  Falha técnica identificada  ", "staff:1", At(2));
        Assert.Equal(OperationStatus.DiagnosticFailed, operation.Status);
        Assert.Equal("Falha técnica identificada", operation.DiagnosticFailureReason);
        Assert.Null(operation.Diagnostic); Assert.Null(operation.DiagnosticCompletedAt);
        Unchanged(operation, () => operation.CompleteDiagnostic(Snapshot(operation), [], "m", At(3)));
        Unchanged(operation, () => operation.StartDiagnostic("m", At(3)));
        Assert.True(operation.CancelOperation("Cancelar", "system:saga", At(3)));
        Assert.Equal(OperationStatus.Canceled, operation.Status);
    }
}
