namespace GearFlow.Operation.Domain;

// BudgetJobId é a identidade da linha no Budget, não o ID do catálogo nem uma equivalência presumida.
public sealed record ApprovedJob
{
    public Guid BudgetJobId { get; }
    public Guid DiagnosticLineId { get; }

    public ApprovedJob(Guid budgetJobId, Guid diagnosticLineId)
    {
        BudgetJobId = DomainGuard.Id(budgetJobId, nameof(budgetJobId));
        DiagnosticLineId = DomainGuard.Id(diagnosticLineId, nameof(diagnosticLineId));
    }
}

public sealed class ExecutionAuthorization
{
    public Guid AuthorizationId { get; }
    public Guid BudgetId { get; }
    public int BudgetRevision { get; }
    public DiagnosticSnapshot Snapshot { get; }
    public IReadOnlyList<ApprovedJob> Jobs { get; }

    public ExecutionAuthorization(Guid authorizationId, Guid budgetId, int budgetRevision,
        DiagnosticSnapshot snapshot, IEnumerable<ApprovedJob> jobs)
    {
        AuthorizationId = DomainGuard.Id(authorizationId, nameof(authorizationId));
        BudgetId = DomainGuard.Id(budgetId, nameof(budgetId));
        if (budgetRevision != 1) throw new ArgumentOutOfRangeException(nameof(budgetRevision), "A baseline local contempla apenas a revisão 1.");
        BudgetRevision = budgetRevision;
        ArgumentNullException.ThrowIfNull(snapshot);
        Snapshot = snapshot;
        var copy = DomainGuard.CopyItems(jobs, nameof(jobs));
        var laborIds = snapshot.Lines.Where(line => line.ItemType == OperationItemType.Labor).Select(line => line.LineId).ToHashSet();
        if (copy.Select(job => job.BudgetJobId).Distinct().Count() != copy.Length ||
            copy.Select(job => job.DiagnosticLineId).Distinct().Count() != copy.Length ||
            !laborIds.SetEquals(copy.Select(job => job.DiagnosticLineId)))
            throw new ArgumentException("Cada linha de mão de obra precisa de um BudgetJobId único e de um mapeamento exato.", nameof(jobs));
        Jobs = Array.AsReadOnly(copy);
    }

    // Um novo envelope com os mesmos termos não cria nova autorização ou reinicia reparo.
    internal bool HasSameTerms(ExecutionAuthorization other) =>
        BudgetId == other.BudgetId && BudgetRevision == other.BudgetRevision && Snapshot.HasSameContent(other.Snapshot) &&
        Jobs.OrderBy(job => job.BudgetJobId).SequenceEqual(other.Jobs.OrderBy(job => job.BudgetJobId));
}

public sealed record JobProgress
{
    public Guid BudgetJobId { get; }
    public Guid DiagnosticLineId { get; }
    public Guid CatalogJobId { get; }
    public DateTimeOffset? ExecutedAt { get; }
    public string? ExecutedBy { get; }

    internal JobProgress(Guid budgetJobId, Guid diagnosticLineId, Guid catalogJobId,
        DateTimeOffset? executedAt = null, string? executedBy = null)
    {
        BudgetJobId = budgetJobId;
        DiagnosticLineId = diagnosticLineId;
        CatalogJobId = catalogJobId;
        ExecutedAt = executedAt;
        ExecutedBy = executedBy;
    }

    internal JobProgress Execute(string actor, DateTimeOffset now) =>
        new(BudgetJobId, DiagnosticLineId, CatalogJobId, now, actor);
}
