using GearFlow.Operation.Application;
using GearFlow.Operation.Domain;
using GearFlow.Operation.Infrastructure.Persistence;
using Xunit;

namespace Operation.UnitTests.Infrastructure;

public class PersistenceMappingTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void StorageRoundTripPreservesEveryAuditAndState(int scenario)
    {
        var operation = scenario switch
        {
            0 => DomainFixture.Received(),
            1 => DomainFixture.Diagnosing(),
            2 => DomainFixture.Diagnosed(),
            3 => DomainFixture.Ready(),
            4 => DomainFixture.Executing(),
            5 => WorkOperation.CreateCanceled(DomainFixture.Id(1), "Cancelado", "staff:1", DomainFixture.Time),
            _ => DomainFixture.Received()
        };
        if (scenario == 6) operation.Deactivate("Duplicada", "staff:1", DomainFixture.At(1));
        if (scenario == 7) operation.UpdateVehicle(DomainFixture.Id(99), "staff:1", DomainFixture.At(1));
        var restored = OperationMapper.Restore(OperationMapper.ToRow(operation), operation.History.Select(x => OperationMapper.ToRow(operation.ServiceOrderId, x)).ToArray());
        Assert.Equal(StoredJson.Write(OperationDetails.From(operation)), StoredJson.Write(OperationDetails.From(restored)));
        Assert.NotSame(operation, restored);
    }

    [Fact]
    public void UnsupportedOrCorruptedStorageIsRejected()
    {
        var operation = DomainFixture.Received();
        var history = operation.History.Select(x => OperationMapper.ToRow(operation.ServiceOrderId, x)).ToArray();
        var row = OperationMapper.ToRow(operation);
        row.StateFormat = 99;
        Assert.Throws<NotSupportedException>(() => OperationMapper.Restore(row, history));
        row.StateFormat = 1;
        row.Version++;
        Assert.Throws<InvalidDataException>(() => OperationMapper.Restore(row, history));
        Assert.Throws<System.Text.Json.JsonException>(() => StoredJson.Read<OperationState>("{}"));
        Assert.Throws<InvalidDataException>(() => StoredJson.Read<OperationState>("null"));
    }

    [Fact]
    public void RehydrationRejectsUnknownFormatAndMissingOrAlteredAudit()
    {
        var operation = DomainFixture.Diagnosed();
        var state = operation.CaptureState();
        Assert.Throws<NotSupportedException>(() => WorkOperation.Restore(state with { FormatVersion = 99 }, operation.History));
        Assert.Throws<InvalidDataException>(() => WorkOperation.Restore(state, []));
        var audit = operation.History.ToArray();
        audit[1] = audit[1] with { Version = 99 };
        Assert.Throws<InvalidDataException>(() => WorkOperation.Restore(state, audit));
    }
}
