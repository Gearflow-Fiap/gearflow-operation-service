using GearFlow.Operation.Application;
using GearFlow.Operation.Domain;
using static Operation.UnitTests.Support.DomainFixture;

namespace Operation.UnitTests.Support;

internal sealed class ApplicationClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = Time;
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(int minutes = 1) => Now = Now.AddMinutes(minutes);
}

internal sealed class TestPriceCatalog(ApplicationClock clock) : IPriceCatalog
{
    public int Calls { get; private set; }
    public IReadOnlyList<PriceRequest>? LastRequest { get; private set; }
    public Func<IReadOnlyList<PriceRequest>, CatalogPriceQuote>? Quote { get; set; }
    public Exception? Failure { get; set; }
    public Action? OnRequest { get; set; }

    public Task<CatalogPriceQuote> GetPrices(IReadOnlyList<PriceRequest> items, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Calls++; LastRequest = items;
        OnRequest?.Invoke();
        if (Failure is not null) throw Failure;
        return Task.FromResult(Quote is null ? DefaultQuote(items) : Quote(items));
    }

    public CatalogPriceQuote DefaultQuote(IReadOnlyList<PriceRequest> items) => new(clock.Now, "BRL",
        Array.AsReadOnly(items.Select(item => new CatalogPrice(item.ItemType, item.ItemId, item.ItemType.ToString(),
            item.ItemType switch { OperationItemType.Labor => 1000, OperationItemType.Part => 300, _ => 200 })).ToArray()));
}

/// <summary>
/// Modelo de teste do contrato de storage: cópias destacadas e publicação atômica em memória.
/// Não é adapter de produção nem prova de transações/constraints do banco real.
/// </summary>
internal sealed class IsolatedStore : IOperationUnitOfWork, IOperationReadStore
{
    private readonly object sync = new();
    private readonly Dictionary<Guid, WorkOperation> operations = [];
    private readonly Dictionary<Guid, CommandReceipt> receipts = [];
    private readonly List<OperationEvent> events = [];
    public Func<OperationWrite, Task>? BeforeCommit { get; set; }
    public Exception? CommitFailure { get; set; }
    public int Commits { get; private set; }
    public OperationSearch? LastSearch { get; private set; }
    public PageRequest? LastPage { get; private set; }
    public IReadOnlyList<OperationEvent> Events { get { lock (sync) return Array.AsReadOnly(events.ToArray()); } }
    public int ReceiptCount { get { lock (sync) return receipts.Count; } }

    public void Seed(WorkOperation operation) { lock (sync) operations[operation.ServiceOrderId] = operation.Copy(); }
    public WorkOperation? Peek(Guid id) { lock (sync) return operations.GetValueOrDefault(id)?.Copy(); }
    public Task<WorkOperation?> Load(Guid serviceOrderId, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(Peek(serviceOrderId)); }
    public Task<WorkOperation?> Find(Guid serviceOrderId, CancellationToken ct) => Load(serviceOrderId, ct);
    public Task<CommandReceipt?> FindReceipt(Guid intentId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (sync) return Task.FromResult(receipts.GetValueOrDefault(intentId));
    }

    public async Task<CommandResult> Commit(OperationWrite write, CancellationToken ct)
    {
        if (BeforeCommit is not null) await BeforeCommit(write);
        ct.ThrowIfCancellationRequested();
        var prepared = write.Operation.Copy();
        var pending = write.PendingEvents.ToArray();
        lock (sync)
        {
            if (receipts.TryGetValue(write.Receipt.IntentId, out var recorded))
            {
                if (recorded.Fingerprint != write.Receipt.Fingerprint) throw new IntentConflictException("Intenção concorrente divergente.");
                return recorded.Result;
            }
            var current = operations.GetValueOrDefault(prepared.ServiceOrderId);
            if (current?.Version != write.ExpectedStoredVersion)
                throw new OperationConcurrencyException("Snapshot lido ficou desatualizado.");
            // Injetar falha depois de preparar candidato/eventos, antes de tornar qualquer coisa visível.
            if (CommitFailure is not null) throw CommitFailure;
            operations[prepared.ServiceOrderId] = prepared;
            receipts.Add(write.Receipt.IntentId, write.Receipt);
            events.AddRange(pending);
            Commits++;
            return write.Receipt.Result;
        }
    }

    public Task<PageResult<OperationListItem>> GetPage(OperationSearch search, PageRequest page, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); LastSearch = search; LastPage = page;
        WorkOperation[] all;
        lock (sync) all = operations.Values.Select(item => item.Copy()).ToArray();
        var selected = all.Where(item => search.IncludeInactive || item.IsActive)
            .Where(item => !search.Status.HasValue || item.Status == search.Status.Value);
        var ordered = search.Scope == OperationListScope.ActionableQueue ? ExecutionQueue.Order(selected).ToArray()
            : selected.OrderByDescending(item => item.CreatedAt).ThenBy(item => item.ServiceOrderId).ToArray();
        return Task.FromResult(Page(ordered.Select(OperationListItem.From).ToArray(), page));
    }

    public Task<PageResult<OperationHistory>?> GetHistory(Guid id, PageRequest page, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); LastPage = page;
        var operation = Peek(id);
        return Task.FromResult(operation is null ? null : Page(operation.History.OrderBy(item => item.Version).ToArray(), page));
    }

    public Task<DurationSummary> GetDurationSummary(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (sync) return Task.FromResult(DurationCalculator.Summarize(operations.Values));
    }

    private static PageResult<T> Page<T>(T[] values, PageRequest page)
    {
        var items = page.Offset >= values.Length ? [] : values.Skip((int)page.Offset).Take(page.PageSize).ToArray();
        return new(Array.AsReadOnly(items), page.Page, page.PageSize, values.Length);
    }
}

internal sealed class ApplicationFixture
{
    private int counter = 1000;
    public ApplicationClock Clock { get; } = new();
    public IsolatedStore Store { get; } = new();
    public TestPriceCatalog Prices { get; }
    public OperationService Service { get; }
    public OperationQueries Queries { get; }

    public ApplicationFixture(ConsumablePricingMode mode = ConsumablePricingMode.PerUnitV1)
    {
        Prices = new(Clock);
        Service = new(Store, new DiagnosticPricer(Prices, mode, Clock), Clock);
        Queries = new(Store);
    }

    public CommandContext Context(Guid? intentId = null, Guid? orderId = null, long? expectedVersion = null, string actor = "staff:1") =>
        new(intentId ?? Id(Interlocked.Increment(ref counter)), orderId ?? Id(1), actor, expectedVersion);

    public Task<CommandResult> Receive(CommandContext? context = null, int jobCount = 1) => Service.Receive(context ?? Context(), Id(2),
        Enumerable.Range(0, jobCount).Select(i => new RequestedJob(Id(10 + i), Id(100))), [new(Id(20), Id(200), 2)], 45);

    public async Task Diagnosing(int jobCount = 1)
    {
        await Receive(jobCount: jobCount); Clock.Advance();
        await Service.StartDiagnostic(Context());
    }

    public async Task Diagnosed(int jobCount = 1)
    {
        await Diagnosing(jobCount); Clock.Advance();
        await Service.CompleteDiagnostic(Context(), [Consumable()]);
    }

    public async Task Ready(int jobCount = 1)
    {
        await Diagnosed(jobCount); Clock.Advance();
        var context = Context(); var operation = Store.Peek(Id(1))!;
        await Service.AuthorizeExecution(context, Authorization(operation, authorizationId: context.IntentId));
    }

    public async Task Executing(int jobCount = 1)
    {
        await Ready(jobCount); Clock.Advance();
        await Service.StartExecution(Context());
    }
}
