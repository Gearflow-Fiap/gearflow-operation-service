using GearFlow.Operation.Domain;

namespace GearFlow.Operation.Application;

public sealed class OperationQueries(IOperationReadStore store)
{
    public async Task<OperationDetails> Get(Guid serviceOrderId, CancellationToken ct = default)
    {
        ValidateId(serviceOrderId);
        var operation = await store.Find(serviceOrderId, ct) ?? throw new KeyNotFoundException("Operação não encontrada.");
        return OperationDetails.From(operation);
    }

    public async Task<PageResult<OperationListItem>> List(int page = 1, int pageSize = 20,
        OperationStatus? status = null, bool includeInactive = false, CancellationToken ct = default) =>
        await ReadPage(new(OperationListScope.All, ValidateStatus(status), includeInactive), page, pageSize, ct);

    public async Task<PageResult<OperationListItem>> Queue(int page = 1, int pageSize = 20,
        OperationStatus? status = null, CancellationToken ct = default) =>
        await ReadPage(new(OperationListScope.ActionableQueue, ValidateStatus(status)), page, pageSize, ct);

    public async Task<PageResult<HistoryDto>> History(Guid serviceOrderId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        ValidateId(serviceOrderId);
        var result = await store.GetHistory(serviceOrderId, PageRequest.Normalize(page, pageSize), ct)
            ?? throw new KeyNotFoundException("Operação não encontrada.");
        return new(Array.AsReadOnly(result.Items.Select(HistoryDto.From).ToArray()), result.Page, result.PageSize, result.TotalCount);
    }

    public Task<DurationSummary> Durations(CancellationToken ct = default) => store.GetDurationSummary(ct);

    private async Task<PageResult<OperationListItem>> ReadPage(OperationSearch search, int page, int pageSize, CancellationToken ct)
    {
        var result = await store.GetPage(search, PageRequest.Normalize(page, pageSize), ct);
        return new(Array.AsReadOnly(result.Items.Select(OperationListItem.From).ToArray()), result.Page, result.PageSize, result.TotalCount);
    }

    private static void ValidateId(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("OS obrigatória.", nameof(id));
    }

    private static OperationStatus? ValidateStatus(OperationStatus? status)
    {
        if (status.HasValue && !Enum.IsDefined(status.Value)) throw new ArgumentOutOfRangeException(nameof(status));
        return status;
    }
}
