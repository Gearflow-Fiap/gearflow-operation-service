using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace GearFlow.Operation.Application;

public static class OperationTelemetry
{
    public const string Name = "GearFlow.Operation";
    public static readonly ActivitySource Source = new(Name);
    public static readonly Meter Meter = new(Name);
    private static readonly Counter<long> Commands = Meter.CreateCounter<long>("gearflow.operation.commands", "{command}");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("gearflow.operation.command.duration", "s");
    private static readonly Counter<long> Deliveries = Meter.CreateCounter<long>("gearflow.operation.outbox.deliveries", "{message}");
    public static CommandSpan Start(string action, Guid order, Guid intent) => new(action, order, intent);
    public static string? TraceParent => Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity ? activity.Id : null;
    public static string? TraceState => Activity.Current?.TraceStateString is { Length: <= 512 } state ? state : null;
    public static DeliverySpan Delivery(string? parent, string? state) => new(parent, state);
    public sealed class DeliverySpan : IDisposable
    {
        private readonly Activity? activity;
        private bool completed;
        internal DeliverySpan(string? parent, string? state)
        {
            ActivityContext.TryParse(parent, state, out var context);
            activity = Source.StartActivity("operation.outbox.publish", ActivityKind.Producer, context);
        }
        public void Complete() => completed = true;
        public void Dispose() { if (!completed) activity?.SetStatus(ActivityStatusCode.Error); activity?.Dispose(); }
    }
    public static void Delivered(string kind) => Deliveries.Add(1, new KeyValuePair<string, object?>("kind", kind));
    public sealed class CommandSpan : IDisposable
    {
        private readonly string action;
        private readonly Activity? activity;
        private readonly long start = Stopwatch.GetTimestamp();
        private string outcome = "error";
        internal CommandSpan(string action, Guid order, Guid intent)
        {
            this.action = action; activity = Source.StartActivity("operation." + action, ActivityKind.Internal);
            activity?.SetTag("operation.action", action);
            activity?.SetTag("gearflow.service_order.id", order.ToString("D"));
            activity?.SetTag("gearflow.intent.id", intent.ToString("D"));
        }
        public void Complete(string result) { outcome = result; activity?.SetTag("operation.outcome", result); }
        public void Dispose()
        {
            if (outcome == "error") activity?.SetStatus(ActivityStatusCode.Error);
            var tags = new TagList { { "action", action }, { "outcome", outcome } };
            Commands.Add(1, tags); Duration.Record(Stopwatch.GetElapsedTime(start).TotalSeconds, tags); activity?.Dispose();
        }
    }
}

