using System.Text.Json.Nodes;
using GearFlow.Operation.Infrastructure.Messaging;
using Xunit;
using W = GearFlow.Contracts.Operation.V1;

namespace Operation.IntegrationTests.Messaging;

[Collection("Postgres")]
public class EventTopologyTests(PostgresFixture postgres)
{
    [Fact]
    public async Task AllThirteenEventsUseAgreedExchangesAndPreserveBodyAndTransportIds()
    {
        var h = await postgres.Create(); await using var broker = await BrokerHarness.Start(h);
        foreach (var (type, exchange) in W.OperationEventNames.All)
        {
            var id = Guid.NewGuid(); var cause = Guid.NewGuid();
            var json = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Contracts", "Examples", type.Name + ".json")))!;
            json["messageId"] = id.ToString(); json["correlationId"] = h.OrderId.ToString(); json["causationId"] = cause.ToString();
            var message = OperationWire.Deserialize(type.Name, json.ToJsonString());
            await broker.PublishEvent(message, id, h.OrderId, cause);
            await BrokerHarness.Until(() => Task.FromResult(broker.Events.Any(value => value.Id == id)));
            var received = Assert.Single(broker.Events, value => value.Id == id);
            Assert.Equal(type, received.Body.GetType());
            Assert.Equal(exchange, received.Exchange);
            Assert.Equal(h.OrderId, received.Correlation); Assert.Equal(cause, received.Cause);
            Assert.Equal(OperationWire.Serialize(message), OperationWire.Serialize(received.Body));
        }
        Assert.Equal(13, broker.Events.Count);
        Assert.Equal((0, 0, 0, 0), await h.Counts());
    }
}
