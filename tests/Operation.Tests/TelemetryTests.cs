using System.Diagnostics;
using System.Diagnostics.Metrics;
using GearFlow.Operation.Application;
using Xunit;

namespace Operation.Tests;

[CollectionDefinition("Telemetry", DisableParallelization = true)]
public sealed class TelemetryCollection;

[Collection("Telemetry")]
public class TelemetryTests
{
    [Fact]
    public async Task CommandsExposeBoundedOutcomesAndTraceCorrelationWithoutIdsInMetrics()
    {
        var activities = new List<Activity>();
        using var activityListener = new ActivityListener { ShouldListenTo = source => source.Name == OperationTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded, ActivityStopped = activities.Add };
        ActivitySource.AddActivityListener(activityListener);
        var measurements = new List<(string Name, object Value, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener { InstrumentPublished = (instrument, meter) => { if (instrument.Meter.Name == OperationTelemetry.Name) meter.EnableMeasurementEvents(instrument); } };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => measurements.Add((instrument.Name, value, tags.ToArray())));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => measurements.Add((instrument.Name, value, tags.ToArray()))); listener.Start();
        var fixture = new ApplicationFixture(); var command = fixture.Context();
        using var parent = new Activity("request").SetIdFormat(ActivityIdFormat.W3C).Start();
        await fixture.Receive(command); await fixture.Receive(command); await fixture.Receive();
        await Assert.ThrowsAsync<OperationConcurrencyException>(() => fixture.Service.StartDiagnostic(fixture.Context(expectedVersion: 99)));
        var commands = measurements.Where(item => item.Name == "gearflow.operation.commands").ToArray(); Assert.Equal(4, commands.Length);
        Assert.Contains(commands, item => item.Tags.Any(tag => tag.Key == "outcome" && Equals(tag.Value, "changed")));
        Assert.Contains(commands, item => item.Tags.Any(tag => tag.Key == "outcome" && Equals(tag.Value, "replay")));
        Assert.Contains(commands, item => item.Tags.Any(tag => tag.Key == "outcome" && Equals(tag.Value, "unchanged")));
        Assert.Contains(commands, item => item.Tags.Any(tag => tag.Key == "outcome" && Equals(tag.Value, "error")));
        Assert.All(commands, item => Assert.All(item.Tags, tag => Assert.Contains(tag.Key, new[] { "action", "outcome" })));
        Assert.All(activities, activity => Assert.Equal(parent.TraceId, activity.TraceId));
        Assert.Contains(activities, activity => activity.Status == ActivityStatusCode.Error);
        Assert.Contains(activities, activity => Equals(activity.GetTagItem("gearflow.service_order.id"), DomainFixture.Id(1).ToString("D")));
    }

    [Fact]
    public void DeliveryResumesSavedContextAndMarksUnsuccessfulSpanAsError()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener { ShouldListenTo = source => source.Name == OperationTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded, ActivityStopped = stopped.Add };
        ActivitySource.AddActivityListener(listener);
        string parent; ActivityTraceId trace;
        using (var root = new Activity("origin").SetIdFormat(ActivityIdFormat.W3C).Start()) { parent = root.Id!; trace = root.TraceId; }
        using (var success = OperationTelemetry.Delivery(parent, null)) success.Complete();
        using (OperationTelemetry.Delivery(parent, null)) { }
        Assert.All(stopped, activity => Assert.Equal(trace, activity.TraceId));
        Assert.Equal(ActivityStatusCode.Unset, stopped[0].Status); Assert.Equal(ActivityStatusCode.Error, stopped[1].Status);
        Assert.Equal(parent, stopped[0].ParentId);
        using var oversized = new Activity("state").SetIdFormat(ActivityIdFormat.W3C).Start(); oversized.TraceStateString = new string('x', 513);
        Assert.Null(OperationTelemetry.TraceState);
    }
}
