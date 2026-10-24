namespace GearFlow.Operation.Domain;

/// <summary>Estado operacional local. Não é a OS nem o orçamento dos outros serviços.</summary>
public sealed class WorkOperation
{
    private readonly List<OperationHistory> history = [];
    private readonly List<JobProgress> jobs = [];
    public Guid ServiceOrderId { get; }
    public OperationStatus Status { get; private set; } = OperationStatus.Received;
    public DateTimeOffset CreatedAt { get; }
    public IReadOnlyList<OperationHistory> History => history.AsReadOnly();
    public IReadOnlyList<JobProgress> Jobs => jobs.AsReadOnly();

    public WorkOperation(Guid serviceOrderId, string actor, DateTimeOffset now)
    {
        if (serviceOrderId == Guid.Empty) throw new ArgumentException("OS obrigatória.", nameof(serviceOrderId));
        ValidateActor(actor);
        ServiceOrderId = serviceOrderId;
        CreatedAt = now;
        history.Add(new(Status, actor, now));
    }

    public void StartDiagnostic(string actor, DateTimeOffset now) =>
        Move(OperationStatus.Received, OperationStatus.InDiagnostic, actor, now);
    public void CompleteDiagnostic(string actor, DateTimeOffset now) =>
        Move(OperationStatus.InDiagnostic, OperationStatus.AwaitingApproval, actor, now);

    // Chamado somente depois da aprovação e do resultado da reserva, confirmados externamente.
    public void AcceptExecution(IEnumerable<Guid> approvedJobIds, bool stockReserved, string actor, DateTimeOffset now)
    {
        Require(OperationStatus.AwaitingApproval);
        ValidateActor(actor);
        var ids = approvedJobIds.ToArray();
        if (ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length)
            throw new ArgumentException("Serviços devem ter IDs válidos e únicos.", nameof(approvedJobIds));
        jobs.AddRange(ids.Select(id => new JobProgress(id, null)));
        Move(OperationStatus.AwaitingApproval, stockReserved ? OperationStatus.InExecution :
            OperationStatus.AwaitingPartsOrConsumables, actor, now);
    }

    // O adaptador deve confirmar a reserva antes de chamar este método.
    public void ResumeExecution(string actor, DateTimeOffset now) =>
        Move(OperationStatus.AwaitingPartsOrConsumables, OperationStatus.InExecution, actor, now);

    public void ExecuteJob(Guid jobId, string actor, DateTimeOffset now)
    {
        Require(OperationStatus.InExecution);
        ValidateActor(actor);
        var index = jobs.FindIndex(j => j.JobId == jobId);
        if (index < 0) throw new OperationConflictException("Serviço não pertence à execução aprovada.");
        if (jobs[index].ExecutedAt is not null) throw new OperationConflictException("Serviço já executado.");
        jobs[index] = jobs[index] with { ExecutedAt = now };
        history.Add(new(Status, actor, now));
    }

    // Preserva a regra do monólito: exige InExecution, sem novo bloqueio por jobs pendentes.
    // Finaliza apenas a execução LOCAL. A Saga ainda deve comandar ConsumeStock e aguardar StockConsumed.
    public void FinalizeExecution(string actor, DateTimeOffset now) =>
        Move(OperationStatus.InExecution, OperationStatus.Finalized, actor, now);

    public void RejectBudget(string actor, DateTimeOffset now) =>
        Move(OperationStatus.AwaitingApproval, OperationStatus.Canceled, actor, now);

    // Compensação local pré-execução; repetir confirma sucesso sem duplicar histórico.
    // Não desfaz trabalho físico já iniciado nem movimenta estoque ou pagamento.
    public void CancelDiagnostic(string actor, DateTimeOffset now)
    {
        ValidateActor(actor);
        if (Status == OperationStatus.Canceled) return;
        if (Status is OperationStatus.InExecution or OperationStatus.Finalized)
            throw new OperationConflictException("Execução iniciada exige recuperação, não rollback físico.");
        Move(Status, OperationStatus.Canceled, actor, now);
    }

    private void Move(OperationStatus expected, OperationStatus target, string actor, DateTimeOffset now)
    {
        Require(expected);
        ValidateActor(actor);
        Status = target;
        history.Add(new(target, actor, now));
    }
    private void Require(OperationStatus expected)
    {
        if (Status != expected) throw new OperationConflictException($"Esperado {expected}; estado atual {Status}.");
    }
    private static void ValidateActor(string actor)
    {
        if (string.IsNullOrWhiteSpace(actor)) throw new ArgumentException("Autor obrigatório.", nameof(actor));
    }
}
