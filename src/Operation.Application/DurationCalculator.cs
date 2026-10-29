using GearFlow.Operation.Domain;

namespace GearFlow.Operation.Application;

public sealed record PhaseDurations(double? DiagnosticQueueMinutes, double? DiagnosticMinutes,
    double? ExecutionQueueMinutes, double? ExecutionMinutes);
public sealed record PhaseAverage(double? Minutes, int Samples);
public sealed record DurationSummary(PhaseAverage DiagnosticQueue, PhaseAverage Diagnostic,
    PhaseAverage ExecutionQueue, PhaseAverage Execution);

public static class DurationCalculator
{
    public static PhaseDurations For(WorkOperation operation) => new(
        Between(operation.CreatedAt, operation.DiagnosticStartedAt),
        Between(operation.DiagnosticStartedAt, operation.DiagnosticCompletedAt),
        Between(operation.ExecutionAuthorizedAt, operation.ExecutionStartedAt),
        Between(operation.ExecutionStartedAt, operation.ExecutionCompletedAt));

    // Referência para adapters/read models. O adapter de banco deve agregar sem carregar o histórico inteiro.
    public static DurationSummary Summarize(IEnumerable<WorkOperation> operations)
    {
        var values = operations.Where(operation => operation.IsActive).Select(For).ToArray();
        return new(Average(values.Select(item => item.DiagnosticQueueMinutes)), Average(values.Select(item => item.DiagnosticMinutes)),
            Average(values.Select(item => item.ExecutionQueueMinutes)), Average(values.Select(item => item.ExecutionMinutes)));
    }

    private static double? Between(DateTimeOffset? start, DateTimeOffset? end) =>
        start.HasValue && end.HasValue ? (end.Value - start.Value).TotalMinutes : null;

    private static PhaseAverage Average(IEnumerable<double?> values)
    {
        var samples = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return new(samples.Length == 0 ? null : samples.Average(), samples.Length);
    }
}
