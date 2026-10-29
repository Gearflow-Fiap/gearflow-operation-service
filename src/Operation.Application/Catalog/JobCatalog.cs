namespace GearFlow.Operation.Application;

public sealed record JobDefinition
{
    public string Name { get; }
    public string Description { get; }
    public int PriceCents { get; }
    public JobDefinition(string name, string description, int priceCents)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 128 || description is null || description.Length > 2000 || priceCents < 0)
            throw new ArgumentException("Nome, descrição ou preço de mão de obra inválidos.");
        Name = name.Trim(); Description = description.Trim(); PriceCents = priceCents;
    }
}
public sealed record CatalogJob(Guid Id, string Name, string Description, int PriceCents, bool IsActive, long Version,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string UpdatedBy);
public interface IJobCatalog
{
    Task<CatalogJob?> Find(Guid id, CancellationToken ct);
    Task<PageResult<CatalogJob>> List(PageRequest page, bool includeInactive, CancellationToken ct);
    Task<CatalogJob> Create(JobDefinition definition, string actor, CancellationToken ct);
    Task<CatalogJob> Update(Guid id, long version, JobDefinition definition, string actor, CancellationToken ct);
    Task<CatalogJob> Deactivate(Guid id, long version, string actor, CancellationToken ct);
    Task<IReadOnlyList<CatalogPrice>> ReadPrices(IReadOnlyList<Guid> ids, CancellationToken ct);
}
