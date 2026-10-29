using GearFlow.Operation.Application;
using GearFlow.Operation.Domain;
using Microsoft.EntityFrameworkCore;

namespace GearFlow.Operation.Infrastructure.Persistence;

internal sealed class JobRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int PriceCents { get; set; }
    public bool IsActive { get; set; } = true;
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
}

public sealed class PostgresJobCatalog(IDbContextFactory<OperationDbContext> factory, TimeProvider clock) : IJobCatalog
{
    public async Task<CatalogJob?> Find(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Set<JobRow>().AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, ct);
        return row is null ? null : Dto(row);
    }
    public async Task<PageResult<CatalogJob>> List(PageRequest page, bool includeInactive, CancellationToken ct)
    {
        if (page.Page < 1 || page.PageSize is < 1 or > 100) throw new ArgumentException("Página inválida.");
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var query = db.Set<JobRow>().AsNoTracking().Where(item => includeInactive || item.IsActive);
        var count = await query.CountAsync(ct);
        var rows = page.Offset >= count ? [] : await query.OrderBy(item => item.Name).ThenBy(item => item.Id).Skip((int)page.Offset).Take(page.PageSize).ToArrayAsync(ct);
        return new(Array.AsReadOnly(rows.Select(Dto).ToArray()), page.Page, page.PageSize, count);
    }
    public async Task<CatalogJob> Create(JobDefinition definition, string actor, CancellationToken ct)
    {
        Actor(actor);
        var row = new JobRow { Id = Guid.NewGuid(), Name = definition.Name, Description = definition.Description,
            PriceCents = definition.PriceCents, CreatedAt = clock.GetUtcNow(), UpdatedAt = clock.GetUtcNow(), UpdatedBy = actor };
        await using var db = await factory.CreateDbContextAsync(ct); db.Add(row); await db.SaveChangesAsync(ct); return Dto(row);
    }
    public Task<CatalogJob> Update(Guid id, long version, JobDefinition definition, string actor, CancellationToken ct) =>
        Change(id, version, actor, row =>
        {
            if (!row.IsActive) throw new OperationConflictException("Serviço de catálogo desativado.");
            row.Name = definition.Name; row.Description = definition.Description; row.PriceCents = definition.PriceCents;
        }, ct);
    public Task<CatalogJob> Deactivate(Guid id, long version, string actor, CancellationToken ct) => Change(id, version, actor, row => row.IsActive = false, ct);
    private async Task<CatalogJob> Change(Guid id, long version, string actor, Action<JobRow> change, CancellationToken ct)
    {
        Actor(actor);
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Set<JobRow>().SingleOrDefaultAsync(item => item.Id == id, ct) ?? throw new KeyNotFoundException("Serviço de catálogo não encontrado.");
        if (row.Version != version) throw new OperationConcurrencyException("Versão do catálogo divergente.");
        change(row); row.Version++; row.UpdatedAt = clock.GetUtcNow(); row.UpdatedBy = actor;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new OperationConcurrencyException("Catálogo atualizado simultaneamente."); }
        return Dto(row);
    }
    public async Task<IReadOnlyList<CatalogPrice>> ReadPrices(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var wanted = ids.Distinct().ToArray();
        var rows = await db.Set<JobRow>().AsNoTracking().Where(item => wanted.Contains(item.Id) && item.IsActive).ToArrayAsync(ct);
        if (rows.Length != wanted.Length) throw new InvalidPriceQuoteException("Mão de obra ausente ou inativa.");
        return Array.AsReadOnly(rows.Select(item => new CatalogPrice(OperationItemType.Labor, item.Id, item.Name, item.PriceCents)).ToArray());
    }
    private static void Actor(string actor) { if (string.IsNullOrWhiteSpace(actor) || actor.Length > 200) throw new ArgumentException("Ator inválido."); }
    private static CatalogJob Dto(JobRow row) => new(row.Id, row.Name, row.Description, row.PriceCents, row.IsActive, row.Version, row.CreatedAt, row.UpdatedAt, row.UpdatedBy);
}
