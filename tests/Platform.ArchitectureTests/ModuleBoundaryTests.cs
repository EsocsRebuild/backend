using System.Reflection;
using NetArchTest.Rules;
using Platform.Infrastructure.Modules;

namespace Platform.ArchitectureTests;

/// <summary>
/// Guards the modular-monolith rules so the codebase cannot silently degrade into a big ball of mud:
/// modules talk to each other only through *.Contracts, and the domain stays persistence-ignorant.
/// </summary>
public class ModuleBoundaryTests
{
    private static readonly Assembly[] ModuleAssemblies = typeof(Program).Assembly.GetReferencedAssemblies()
        .Where(a => a.Name!.StartsWith("Platform.Modules.", StringComparison.Ordinal) && !a.Name.EndsWith(".Contracts", StringComparison.Ordinal))
        .Select(Assembly.Load)
        .ToArray();

    public static TheoryData<string> Modules() => new(ModuleAssemblies.Select(a => a.GetName().Name!));

    [Fact]
    public void All_eight_modules_are_registered() => Assert.Equal(8, ModuleAssemblies.Length);

    [Theory]
    [MemberData(nameof(Modules))]
    public void Modules_only_reference_other_modules_through_contracts(string module)
    {
        var assembly = ModuleAssemblies.Single(a => a.GetName().Name == module);
        var illegal = assembly.GetReferencedAssemblies()
            .Select(r => r.Name!)
            .Where(n => n.StartsWith("Platform.Modules.", StringComparison.Ordinal) && !n.EndsWith(".Contracts", StringComparison.Ordinal) && n != module)
            .ToList();

        Assert.True(illegal.Count == 0, $"{module} references {string.Join(", ", illegal)} directly. Use its Contracts assembly.");
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Domain_does_not_depend_on_infrastructure_or_web(string module)
    {
        var assembly = ModuleAssemblies.Single(a => a.GetName().Name == module);
        var result = Types.InAssembly(assembly)
            .That().ResideInNamespace($"{module}.Domain")
            .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", $"{module}.Infrastructure", $"{module}.Features",
                "Platform.Infrastructure", "Platform.Web")
            .GetResult();

        Assert.True(result.IsSuccessful, $"Offending types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Each_module_exposes_exactly_one_IModule(string module)
    {
        var assembly = ModuleAssemblies.Single(a => a.GetName().Name == module);
        Assert.Single(assembly.GetTypes(), t => typeof(IModule).IsAssignableFrom(t) && !t.IsAbstract);
    }

    [Fact]
    public void Contracts_depend_only_on_the_shared_kernel()
    {
        var contracts = AppDomain.CurrentDomain.GetAssemblies()
            .Concat(ModuleAssemblies.SelectMany(a => a.GetReferencedAssemblies()).Select(Assembly.Load))
            .Where(a => a.GetName().Name!.EndsWith(".Contracts", StringComparison.Ordinal))
            .DistinctBy(a => a.GetName().Name)
            .ToList();

        Assert.NotEmpty(contracts);
        foreach (var assembly in contracts)
        {
            var platformRefs = assembly.GetReferencedAssemblies().Select(r => r.Name!).Where(n => n.StartsWith("Platform.", StringComparison.Ordinal));
            Assert.All(platformRefs, r => Assert.Equal("Platform.SharedKernel", r));
        }
    }

    [Fact]
    public void Shared_kernel_has_no_framework_dependencies()
    {
        var result = Types.InAssembly(typeof(Platform.SharedKernel.Domain.Entity).Assembly)
            .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Platform.Application", "Platform.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful);
    }
}
