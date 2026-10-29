namespace GearFlow.Operation.Domain;

/// <summary>Agregado operacional local; não abre a OS, aprova orçamento ou movimenta estoque.</summary>
public sealed class WorkOperation
{
    private readonly List<OperationHistory> history = [];
    private readonly List<JobProgress> jobs = [];
    public Guid ServiceOrderId { get; }
    public int? ServiceOrderCode { get; }
    public Guid? VehicleId { get; private set; }
    public Guid? InitialVehicleId { get; }
    public Guid? DiagnosticId { get; }
    public OperationStatus Status { get; private set; } = OperationStatus.Received;
    public bool IsActive { get; private set; } = true;
    public bool IsTombstone { get; }
    public long Version { get; private set; } = 1;
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DiagnosticStartedAt { get; private set; }
    public DateTimeOffset? DiagnosticCompletedAt { get; private set; }
    public DateTimeOffset? ExecutionAuthorizedAt { get; private set; }
    public DateTimeOffset? ExecutionStartedAt { get; private set; }
    public DateTimeOffset? ExecutionCompletedAt { get; private set; }
    public string? DiagnosticFailureReason { get; private set; }
    public string? DeactivationReason { get; private set; }
    public string? CancellationReason { get; private set; }
    public DiagnosticSnapshot? Diagnostic { get; private set; }
    public ExecutionAuthorization? Authorization { get; private set; }
    public IReadOnlyList<RequestedJob> RequestedJobs { get; } = Array.AsReadOnly(Array.Empty<RequestedJob>());
    public IReadOnlyList<RequestedPart> RequestedParts { get; } = Array.AsReadOnly(Array.Empty<RequestedPart>());
    public IReadOnlyList<DiagnosticConsumable> Consumables { get; private set; } = Array.AsReadOnly(Array.Empty<DiagnosticConsumable>());
    public IReadOnlyList<OperationHistory> History => history.AsReadOnly();
    public IReadOnlyList<JobProgress> Jobs => jobs.AsReadOnly();

    public WorkOperation(Guid serviceOrderId, Guid vehicleId, IEnumerable<RequestedJob> requestedJobs,
        IEnumerable<RequestedPart> requestedParts, string actor, DateTimeOffset now, int? serviceOrderCode = null, Guid? diagnosticId = null)
    {
        ServiceOrderId = DomainGuard.Id(serviceOrderId, nameof(serviceOrderId));
        InitialVehicleId = VehicleId = DomainGuard.Id(vehicleId, nameof(vehicleId));
        DiagnosticId = DomainGuard.Id(diagnosticId ?? Guid.NewGuid(), nameof(diagnosticId));
        if (serviceOrderCode is <= 0) throw new ArgumentOutOfRangeException(nameof(serviceOrderCode));
        ServiceOrderCode = serviceOrderCode;
        var by = DomainGuard.Text(actor, nameof(actor));
        var jobCopy = DomainGuard.CopyItems(requestedJobs, nameof(requestedJobs));
        var partCopy = DomainGuard.CopyItems(requestedParts, nameof(requestedParts));
        if (jobCopy.Length == 0 || jobCopy.Length + partCopy.Length > DiagnosticSnapshot.MaximumLines)
            throw new ArgumentException("Ao menos um serviço e no máximo 200 linhas são necessários.", nameof(requestedJobs));
        var ids = jobCopy.Select(job => job.LineId).Concat(partCopy.Select(part => part.LineId)).ToArray();
        if (ids.Distinct().Count() != ids.Length)
            throw new ArgumentException("LineId deve ser único entre serviços e peças.", nameof(requestedJobs));
        RequestedJobs = Array.AsReadOnly(jobCopy);
        RequestedParts = Array.AsReadOnly(partCopy);
        CreatedAt = UpdatedAt = now.ToUniversalTime();
        history.Add(new(Version, Status, OperationAction.Received, by, CreatedAt, "Operação recebida da OS."));
    }

    private WorkOperation(Guid serviceOrderId, string reason, string actor, DateTimeOffset now)
    {
        ServiceOrderId = DomainGuard.Id(serviceOrderId, nameof(serviceOrderId));
        CancellationReason = DomainGuard.Text(reason, nameof(reason));
        var by = DomainGuard.Text(actor, nameof(actor));
        IsTombstone = true;
        IsActive = false;
        Status = OperationStatus.Canceled;
        CreatedAt = UpdatedAt = now.ToUniversalTime();
        history.Add(new(Version, Status, OperationAction.Canceled, by, CreatedAt, CancellationReason));
    }

    // Cópia destacada para unidades de trabalho e leituras isoladas; não é serialização wire/EF.
    public WorkOperation Copy() => new(this);

    private WorkOperation(WorkOperation source)
    {
        ServiceOrderId = source.ServiceOrderId;
        ServiceOrderCode = source.ServiceOrderCode;
        VehicleId = source.VehicleId;
        InitialVehicleId = source.InitialVehicleId;
        DiagnosticId = source.DiagnosticId;
        Status = source.Status;
        IsActive = source.IsActive;
        IsTombstone = source.IsTombstone;
        Version = source.Version;
        CreatedAt = source.CreatedAt;
        UpdatedAt = source.UpdatedAt;
        DiagnosticStartedAt = source.DiagnosticStartedAt;
        DiagnosticCompletedAt = source.DiagnosticCompletedAt;
        ExecutionAuthorizedAt = source.ExecutionAuthorizedAt;
        ExecutionStartedAt = source.ExecutionStartedAt;
        ExecutionCompletedAt = source.ExecutionCompletedAt;
        DiagnosticFailureReason = source.DiagnosticFailureReason;
        DeactivationReason = source.DeactivationReason;
        CancellationReason = source.CancellationReason;
        // Valores e coleções abaixo são imutáveis; somente as listas mutáveis internas são copiadas.
        RequestedJobs = source.RequestedJobs;
        RequestedParts = source.RequestedParts;
        Consumables = source.Consumables;
        Diagnostic = source.Diagnostic;
        Authorization = source.Authorization;
        history.AddRange(source.history);
        jobs.AddRange(source.jobs);
    }

    internal OperationState CaptureState() => new(1, ServiceOrderId, ServiceOrderCode, InitialVehicleId, DiagnosticId,
        RequestedJobs.ToArray(), RequestedParts.ToArray(), Consumables.ToArray(),
        Diagnostic is null ? null : new(Diagnostic.DiagnosticId, Diagnostic.Lines.ToArray(), Diagnostic.PricedAt, Diagnostic.PricingPolicyId),
        Authorization is null ? null : new(Authorization.AuthorizationId, Authorization.BudgetId, Authorization.BudgetRevision, Authorization.Jobs.ToArray()));

    // Reconstrói por ações auditadas, aplicando os MESMOS guards; dados inválidos não viram agregado por reflexão.
    internal static WorkOperation Restore(OperationState state, IReadOnlyList<OperationHistory> history)
    {
        if (state.FormatVersion != 1) throw new NotSupportedException("Formato de operação não suportado.");
        if (history.Count == 0 || history.Where((item, i) => item.Version != i + 1).Any())
            throw new InvalidDataException("Histórico ausente ou com versões descontínuas.");
        var first = history[0];
        var operation = first.Action == OperationAction.Canceled
            ? CreateCanceled(state.ServiceOrderId, first.Description, first.Actor, first.At)
            : new WorkOperation(state.ServiceOrderId, state.InitialVehicleId ?? throw new InvalidDataException("Veículo inicial ausente."),
                state.RequestedJobs, state.RequestedParts, first.Actor, first.At, state.ServiceOrderCode,
                state.DiagnosticId ?? throw new InvalidDataException("Diagnóstico ausente."));
        var snapshot = state.Diagnostic is null ? null : new DiagnosticSnapshot(state.Diagnostic.DiagnosticId,
            state.Diagnostic.Lines, state.Diagnostic.PricedAt, state.Diagnostic.PricingPolicyId);
        foreach (var item in history.Skip(1))
        {
            switch (item.Action)
            {
                case OperationAction.VehicleUpdated: operation.UpdateVehicle(Subject(item), item.Actor, item.At); break;
                case OperationAction.DiagnosticStarted: operation.StartDiagnostic(item.Actor, item.At); break;
                case OperationAction.DiagnosticCompleted: operation.CompleteDiagnostic(snapshot ?? throw new InvalidDataException("Snapshot ausente."), state.Consumables, item.Actor, item.At); break;
                case OperationAction.DiagnosticFailed: operation.FailDiagnostic(item.Description, item.Actor, item.At); break;
                case OperationAction.ExecutionAuthorized:
                    var authorization = state.Authorization ?? throw new InvalidDataException("Autorização ausente.");
                    operation.AuthorizeExecution(new(authorization.AuthorizationId, authorization.BudgetId, authorization.BudgetRevision,
                        snapshot ?? throw new InvalidDataException("Snapshot aprovado ausente."), authorization.Jobs), item.Actor, item.At); break;
                case OperationAction.ExecutionStarted: operation.StartExecution(item.Actor, item.At); break;
                case OperationAction.JobExecuted: operation.ExecuteJob(Subject(item), item.Actor, item.At); break;
                case OperationAction.ExecutionCompleted: operation.FinalizeExecution(item.Actor, item.At); break;
                case OperationAction.Deactivated: operation.Deactivate(item.Description, item.Actor, item.At); break;
                case OperationAction.Canceled: operation.CancelOperation(item.Description, item.Actor, item.At); break;
                default: throw new InvalidDataException("Ação persistida inválida.");
            }
        }
        if (!operation.History.SequenceEqual(history) || !operation.RequestedJobs.SequenceEqual(state.RequestedJobs) ||
            !operation.RequestedParts.SequenceEqual(state.RequestedParts) || !operation.Consumables.SequenceEqual(state.Consumables) ||
            operation.InitialVehicleId != state.InitialVehicleId || operation.ServiceOrderCode != state.ServiceOrderCode ||
            operation.DiagnosticId != state.DiagnosticId || (operation.Diagnostic is null) != (state.Diagnostic is null) ||
            (operation.Authorization is null) != (state.Authorization is null))
            throw new InvalidDataException("Estado persistido diverge das ações auditadas.");
        return operation;
    }

    private static Guid Subject(OperationHistory item) => item.SubjectId ?? throw new InvalidDataException("Objeto da ação ausente.");

    // Fence sem inventar veículo/serviços; persistência e unicidade por OS virão em outro incremento.
    public static WorkOperation CreateCanceled(Guid serviceOrderId, string reason, string actor, DateTimeOffset now) =>
        new(serviceOrderId, reason, actor, now);

    public bool UpdateVehicle(Guid vehicleId, string actor, DateTimeOffset now)
    {
        RequireActive();
        if (Status is not (OperationStatus.Received or OperationStatus.InDiagnostic))
            throw new OperationConflictException("Veículo bloqueado após a conclusão do diagnóstico.");
        DomainGuard.Id(vehicleId, nameof(vehicleId));
        var by = PrepareChange(actor, now);
        if (VehicleId == vehicleId) return false;
        VehicleId = vehicleId;
        Record(OperationAction.VehicleUpdated, by, now, "Veículo atualizado pela OS.", vehicleId);
        return true;
    }

    public void StartDiagnostic(string actor, DateTimeOffset now)
    {
        RequireActive();
        Require(OperationStatus.Received);
        var by = PrepareChange(actor, now);
        Status = OperationStatus.InDiagnostic;
        DiagnosticStartedAt = now.ToUniversalTime();
        Record(OperationAction.DiagnosticStarted, by, now, "Diagnóstico iniciado.");
    }

    public void CompleteDiagnostic(DiagnosticSnapshot snapshot, IEnumerable<DiagnosticConsumable> consumables,
        string actor, DateTimeOffset now)
    {
        RequireActive();
        Require(OperationStatus.InDiagnostic);
        ArgumentNullException.ThrowIfNull(snapshot);
        var copy = DomainGuard.CopyItems(consumables, nameof(consumables));
        var expected = RequestedJobs.Select(job => (job.LineId, Type: OperationItemType.Labor, Id: job.CatalogJobId, Quantity: 1m))
            .Concat(RequestedParts.Select(part => (part.LineId, Type: OperationItemType.Part, Id: part.PartId, Quantity: (decimal)part.Quantity)))
            .Concat(copy.Select(item => (item.LineId, Type: OperationItemType.Consumable, Id: item.ConsumableId, item.Quantity))).ToArray();
        if (expected.Select(item => item.LineId).Distinct().Count() != expected.Length || expected.Length != snapshot.Lines.Count ||
            expected.Any(item => !snapshot.Lines.Any(line => line.LineId == item.LineId && line.ItemType == item.Type &&
                line.ItemId == item.Id && line.Quantity == item.Quantity)))
            throw new OperationConflictException("Snapshot deve preservar todos os serviços/peças solicitados e consumíveis informados.");
        var by = PrepareChange(actor, now);
        if (snapshot.DiagnosticId != DiagnosticId) throw new OperationConflictException("Snapshot pertence a outro diagnóstico.");
        if (snapshot.PricedAt > now.ToUniversalTime() || snapshot.PricedAt < DiagnosticStartedAt)
            throw new OperationConflictException("Preço precisa estar na janela do diagnóstico.");
        Consumables = Array.AsReadOnly(copy);
        Diagnostic = snapshot;
        Status = OperationStatus.AwaitingExecutionAuthorization;
        DiagnosticCompletedAt = now.ToUniversalTime();
        Record(OperationAction.DiagnosticCompleted, by, now, "Diagnóstico concluído; aguardando autorização.", snapshot.DiagnosticId);
    }

    public void FailDiagnostic(string reason, string actor, DateTimeOffset now)
    {
        RequireActive();
        Require(OperationStatus.InDiagnostic);
        var detail = DomainGuard.Text(reason, nameof(reason));
        var by = PrepareChange(actor, now);
        DiagnosticFailureReason = detail;
        Status = OperationStatus.DiagnosticFailed;
        Record(OperationAction.DiagnosticFailed, by, now, detail);
    }

    // Autorização comercial/reserva é confirmada pela coordenação externa, não por bool livre do operador.
    public bool AuthorizeExecution(ExecutionAuthorization authorization, string actor, DateTimeOffset now)
    {
        RequireActive();
        ArgumentNullException.ThrowIfNull(authorization);
        var by = PrepareChange(actor, now);
        if (Authorization is not null)
        {
            if (!Authorization.HasSameTerms(authorization)) throw new OperationConflictException("Autorização conflitante com a já aceita.");
            return false;
        }
        Require(OperationStatus.AwaitingExecutionAuthorization);
        if (Diagnostic is null || !Diagnostic.HasSameContent(authorization.Snapshot))
            throw new OperationConflictException("Orçamento autorizado diverge do diagnóstico local.");
        // Construir tudo antes de alterar o agregado para não deixar progresso parcial em caso de erro.
        var progress = authorization.Jobs.Select(job => new JobProgress(job.BudgetJobId, job.DiagnosticLineId,
            Diagnostic.Lines.Single(line => line.LineId == job.DiagnosticLineId).ItemId)).ToArray();
        Authorization = authorization;
        jobs.AddRange(progress);
        Status = OperationStatus.ReadyForExecution;
        ExecutionAuthorizedAt = now.ToUniversalTime();
        Record(OperationAction.ExecutionAuthorized, by, now, "Execução autorizada; aguardando início físico.", authorization.AuthorizationId);
        return true;
    }

    public void StartExecution(string actor, DateTimeOffset now)
    {
        RequireActive();
        Require(OperationStatus.ReadyForExecution);
        var by = PrepareChange(actor, now);
        Status = OperationStatus.InExecution;
        ExecutionStartedAt = now.ToUniversalTime();
        Record(OperationAction.ExecutionStarted, by, now, "Reparo físico iniciado.");
    }

    public void ExecuteJob(Guid budgetJobId, string actor, DateTimeOffset now)
    {
        RequireActive();
        Require(OperationStatus.InExecution);
        DomainGuard.Id(budgetJobId, nameof(budgetJobId));
        var by = PrepareChange(actor, now);
        var index = jobs.FindIndex(job => job.BudgetJobId == budgetJobId);
        if (index < 0) throw new OperationConflictException("Linha não pertence ao orçamento autorizado.");
        if (jobs[index].ExecutedAt is not null) throw new OperationConflictException("Linha já executada.");
        jobs[index] = jobs[index].Execute(by, now.ToUniversalTime());
        Record(OperationAction.JobExecuted, by, now, "Serviço executado.", budgetJobId);
    }

    // Preserva o legado: não introduzir bloqueio por jobs pendentes. Não declara estoque consumido/OS entregue.
    public void FinalizeExecution(string actor, DateTimeOffset now)
    {
        RequireActive();
        Require(OperationStatus.InExecution);
        var by = PrepareChange(actor, now);
        Status = OperationStatus.ExecutionCompleted;
        ExecutionCompletedAt = now.ToUniversalTime();
        Record(OperationAction.ExecutionCompleted, by, now, "Execução local concluída.");
    }

    public bool Deactivate(string reason, string actor, DateTimeOffset now)
    {
        if (Status is not (OperationStatus.Received or OperationStatus.InDiagnostic))
            throw new OperationConflictException("Desativação permitida somente antes da conclusão do diagnóstico.");
        var detail = DomainGuard.Text(reason, nameof(reason));
        var by = PrepareChange(actor, now);
        if (!IsActive) return false;
        IsActive = false;
        DeactivationReason = detail;
        Record(OperationAction.Deactivated, by, now, detail);
        return true;
    }

    public bool CancelOperation(string reason, string actor, DateTimeOffset now)
    {
        var detail = DomainGuard.Text(reason, nameof(reason));
        var by = PrepareChange(actor, now);
        if (Status == OperationStatus.Canceled) return false;
        if (Status is OperationStatus.InExecution or OperationStatus.ExecutionCompleted)
            throw new OperationConflictException("Reparo já iniciado/concluído exige recuperação, não rollback físico.");
        IsActive = false;
        Status = OperationStatus.Canceled;
        CancellationReason = detail;
        Record(OperationAction.Canceled, by, now, detail);
        return true;
    }

    private string PrepareChange(string actor, DateTimeOffset now)
    {
        var by = DomainGuard.Text(actor, nameof(actor));
        if (now.ToUniversalTime() < UpdatedAt) throw new OperationConflictException("Data de alteração anterior ao último registro.");
        _ = checked(Version + 1);
        return by;
    }

    private void Record(OperationAction action, string actor, DateTimeOffset now, string description, Guid? subjectId = null)
    {
        Version++;
        UpdatedAt = now.ToUniversalTime();
        history.Add(new(Version, Status, action, actor, UpdatedAt, description, subjectId));
    }

    private void RequireActive()
    {
        if (!IsActive) throw new OperationConflictException("Operação inativa/cancelada não pode executar trabalho.");
    }

    private void Require(OperationStatus expected)
    {
        if (Status != expected) throw new OperationConflictException($"Esperado {expected}; estado atual {Status}.");
    }
}
