using GearFlow.Operation.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Operation.UnitTests.Infrastructure;

public class ModelContractTests
{
    [Theory]
    [InlineData("operation-model.txt")]
    [InlineData("operation-schema.sql")]
    [InlineData("migration-history.sql")]
    public void ModelAndDdlMatchTheBaselineCapturedBeforeConfigurationExtraction(string file)
    {
        using var db = Context();
        var actual = file switch
        {
            "operation-model.txt" => db.GetService<IDesignTimeModel>().Model.ToDebugString(MetadataDebugStringOptions.LongDefault),
            "operation-schema.sql" => db.Database.GenerateCreateScript(),
            "migration-history.sql" => db.GetService<IHistoryRepository>().GetCreateScript(),
            _ => throw new ArgumentOutOfRangeException(nameof(file))
        };
        var expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Infrastructure", "Fixtures", file));
        Assert.Equal(Normalize(expected), Normalize(actual));
    }

    [Fact]
    public void ConfigurationExtractionDoesNotRequireANewMigration()
    {
        using var db = Context();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(4, db.Database.GetMigrations().Count());
    }

    private static OperationDbContext Context() => new OperationContextFactory(
        "Host=127.0.0.1;Database=model_contract;Username=not_used").CreateDbContext();

    private static string Normalize(string value) => string.Join("\n", value.Replace("\r\n", "\n", StringComparison.Ordinal)
        .Split('\n').Select(line => line.TrimEnd())).TrimEnd() + "\n";
}
