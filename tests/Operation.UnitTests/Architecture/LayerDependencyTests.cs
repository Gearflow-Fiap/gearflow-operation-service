using GearFlow.Operation.Application;
using GearFlow.Operation.Domain;
using Xunit;
using W = GearFlow.Contracts.Operation.V1;

namespace Operation.UnitTests.Architecture;

public class LayerDependencyTests
{
    [Fact]
    public void DomainDoesNotDependOnOuterLayersOrIntegrationContracts()
    {
        var names = typeof(WorkOperation).Assembly.GetReferencedAssemblies().Select(name => name.Name).ToArray();
        Assert.DoesNotContain("Operation.Application", names);
        Assert.DoesNotContain("Operation.Infrastructure", names);
        Assert.DoesNotContain("Operation.Api", names);
        Assert.DoesNotContain("GearFlow.Contracts.Operation", names);
    }

    [Fact]
    public void ApplicationDoesNotDependOnTransportPersistenceOrHost()
    {
        var names = typeof(OperationService).Assembly.GetReferencedAssemblies().Select(name => name.Name).ToArray();
        Assert.DoesNotContain("Operation.Infrastructure", names);
        Assert.DoesNotContain("Operation.Api", names);
        Assert.DoesNotContain("MassTransit", names);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", names);
        Assert.DoesNotContain("GearFlow.Contracts.Operation", names);
        Assert.DoesNotContain(names, name => name is not null &&
            (name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||
             name.StartsWith("Microsoft.Extensions.Configuration", StringComparison.Ordinal) ||
             name.StartsWith("Microsoft.Extensions.Options", StringComparison.Ordinal)));
    }

    [Fact]
    public void WireContractsDoNotDependOnBusinessImplementation()
    {
        var names = typeof(W.StartDiagnostic).Assembly.GetReferencedAssemblies().Select(name => name.Name).ToArray();
        Assert.DoesNotContain("Operation.Domain", names);
        Assert.DoesNotContain("Operation.Application", names);
        Assert.DoesNotContain("Operation.Infrastructure", names);
        Assert.DoesNotContain("Operation.Api", names);
    }
}
