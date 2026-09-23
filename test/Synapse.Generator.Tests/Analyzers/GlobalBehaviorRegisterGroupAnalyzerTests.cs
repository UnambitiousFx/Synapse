using UnambitiousFx.Synapse.Generator.Analyzers;

namespace UnambitiousFx.Synapse.Generator.Tests.Analyzers;

public sealed class GlobalBehaviorRegisterGroupAnalyzerTests
{
    // SomeBehavior (the [assembly: SynapseGlobalBehavior] target) and the default-named RegisterGroup class,
    // both in the default (TestAssembly) namespace — reused by every test that doesn't need a custom name.
    private const string GroupsFile = """
        using UnambitiousFx.Synapse.Abstractions;

        namespace TestAssembly
        {
            public sealed class SomeBehavior<TRequest, TResponse> { }

            public sealed class RegisterGroup : IRegisterGroup
            {
                public void Register(IDependencyInjectionBuilder builder) { }
            }
        }
        """;

    [Fact]
    public async Task Analyze_GlobalBehaviorWithOwnGroupRegistered_ReportsNoDiagnostic()
    {
        // Arrange (Given) — the correctly-wired case: AddSynapse is called, and the compilation's own
        // default-named group (TestAssembly.RegisterGroup, since no build_property.RootNamespace is
        // available in this hand-rolled harness and the assembly is named "TestAssembly") is registered.
        const string wiringFile = """
            using Microsoft.Extensions.DependencyInjection;
            using UnambitiousFx.Synapse;
            using UnambitiousFx.Synapse.Abstractions;

            [assembly: SynapseGlobalBehavior(typeof(TestAssembly.SomeBehavior<,>))]

            namespace TestAssembly
            {
                public static class Wiring
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSynapse(cfg =>
                        {
                            cfg.AddRegisterGroup(new RegisterGroup());
                        });
                    }
                }
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunFilesAsync<GlobalBehaviorRegisterGroupAnalyzer>(
            ("Groups.cs", GroupsFile), ("Wiring.cs", wiringFile));

        // Assert (Then)
        Assert.Empty(diagnostics.Where(d => d.Id == GlobalBehaviorRegisterGroupAnalyzer.DiagnosticId));
    }

    [Fact]
    public async Task Analyze_OtherGroupsRegisteredButNotOwnGroup_ReportsSyn105()
    {
        // Arrange (Given) — the exact shape from issue #91: another group (simulating ModuleA/ModuleB) is
        // registered, but this assembly's own group never is.
        const string otherGroupFile = """
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestAssembly
            {
                public sealed class OtherGroup : IRegisterGroup
                {
                    public void Register(IDependencyInjectionBuilder builder) { }
                }
            }
            """;
        const string wiringFile = """
            using Microsoft.Extensions.DependencyInjection;
            using UnambitiousFx.Synapse;
            using UnambitiousFx.Synapse.Abstractions;

            [assembly: SynapseGlobalBehavior(typeof(TestAssembly.SomeBehavior<,>))]

            namespace TestAssembly
            {
                public static class Wiring
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSynapse(cfg =>
                        {
                            cfg.AddRegisterGroup(new OtherGroup());
                        });
                    }
                }
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunFilesAsync<GlobalBehaviorRegisterGroupAnalyzer>(
            ("Groups.cs", GroupsFile), ("OtherGroup.cs", otherGroupFile), ("Wiring.cs", wiringFile));

        // Assert (Then)
        var syn105 = Assert.Single(diagnostics, d => d.Id == GlobalBehaviorRegisterGroupAnalyzer.DiagnosticId);
        Assert.Contains("SynapseGlobalBehavior", syn105.GetMessage());
    }

    [Fact]
    public async Task Analyze_NoAddRegisterGroupCallAtAll_ReportsSyn105()
    {
        // Arrange (Given) — AddSynapse is called (composition root), but no AddRegisterGroup call exists.
        const string wiringFile = """
            using Microsoft.Extensions.DependencyInjection;
            using UnambitiousFx.Synapse;
            using UnambitiousFx.Synapse.Abstractions;

            [assembly: SynapseGlobalBehavior(typeof(TestAssembly.SomeBehavior<,>))]

            namespace TestAssembly
            {
                public static class Wiring
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSynapse(cfg => { });
                    }
                }
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunFilesAsync<GlobalBehaviorRegisterGroupAnalyzer>(
            ("Groups.cs", GroupsFile), ("Wiring.cs", wiringFile));

        // Assert (Then)
        Assert.Single(diagnostics, d => d.Id == GlobalBehaviorRegisterGroupAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task Analyze_NoAddSynapseCallAnywhere_ReportsNoDiagnostic()
    {
        // Arrange (Given) — the "shared library, not a host" case: the attribute is declared, but this
        // compilation never calls AddSynapse at all, so it is not acting as a composition root. Silent by
        // design (see the spec's resolved scope fork).
        const string attributeOnlyFile = """
            using UnambitiousFx.Synapse.Abstractions;

            [assembly: SynapseGlobalBehavior(typeof(TestAssembly.SomeBehavior<,>))]
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunFilesAsync<GlobalBehaviorRegisterGroupAnalyzer>(
            ("Groups.cs", GroupsFile), ("Attribute.cs", attributeOnlyFile));

        // Assert (Then)
        Assert.Empty(diagnostics.Where(d => d.Id == GlobalBehaviorRegisterGroupAnalyzer.DiagnosticId));
    }

    [Fact]
    public async Task Analyze_NoGlobalBehaviorAttribute_ReportsNoDiagnosticRegardlessOfRegistration()
    {
        // Arrange (Given) — no [assembly: SynapseGlobalBehavior] anywhere; nothing for SYN105 to check.
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using UnambitiousFx.Synapse;
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestAssembly
            {
                public static class Wiring
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSynapse(cfg => { });
                    }
                }
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<GlobalBehaviorRegisterGroupAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics.Where(d => d.Id == GlobalBehaviorRegisterGroupAnalyzer.DiagnosticId));
    }

    [Fact]
    public async Task Analyze_CustomRegisterGroupNameRegisteredCorrectly_ReportsNoDiagnostic()
    {
        // Arrange (Given) — a [RegisterGroup]-attributed class with a non-default name/namespace, correctly
        // registered under that same name.
        const string customGroupsFile = """
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestAssembly
            {
                public sealed class SomeBehavior<TRequest, TResponse> { }
            }

            namespace TestAssembly.Wiring
            {
                [RegisterGroup]
                public sealed partial class MyGroup : IRegisterGroup
                {
                    public void Register(IDependencyInjectionBuilder builder) { }
                }
            }
            """;
        const string wiringFile = """
            using Microsoft.Extensions.DependencyInjection;
            using UnambitiousFx.Synapse;
            using UnambitiousFx.Synapse.Abstractions;

            [assembly: SynapseGlobalBehavior(typeof(TestAssembly.SomeBehavior<,>))]

            namespace TestAssembly.Wiring
            {
                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSynapse(cfg =>
                        {
                            cfg.AddRegisterGroup(new MyGroup());
                        });
                    }
                }
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunFilesAsync<GlobalBehaviorRegisterGroupAnalyzer>(
            ("Groups.cs", customGroupsFile), ("Wiring.cs", wiringFile));

        // Assert (Then)
        Assert.Empty(diagnostics.Where(d => d.Id == GlobalBehaviorRegisterGroupAnalyzer.DiagnosticId));
    }

    [Fact]
    public async Task Analyze_CustomRegisterGroupNameRegisteredUnderDefaultGuessedName_ReportsSyn105()
    {
        // Arrange (Given) — a [RegisterGroup]-attributed class (MyGroup) exists, but the code registers a
        // plain "RegisterGroup" instead — the default name SYN105 must NOT fall back to once a valid custom
        // target exists.
        const string customGroupsFile = """
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestAssembly
            {
                public sealed class SomeBehavior<TRequest, TResponse> { }

                public sealed class RegisterGroup : IRegisterGroup
                {
                    public void Register(IDependencyInjectionBuilder builder) { }
                }
            }

            namespace TestAssembly.Wiring
            {
                [RegisterGroup]
                public sealed partial class MyGroup : IRegisterGroup
                {
                    public void Register(IDependencyInjectionBuilder builder) { }
                }
            }
            """;
        const string wrongWiringFile = """
            using Microsoft.Extensions.DependencyInjection;
            using UnambitiousFx.Synapse;
            using UnambitiousFx.Synapse.Abstractions;

            [assembly: SynapseGlobalBehavior(typeof(TestAssembly.SomeBehavior<,>))]

            namespace TestAssembly
            {
                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSynapse(cfg =>
                        {
                            cfg.AddRegisterGroup(new RegisterGroup());
                        });
                    }
                }
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunFilesAsync<GlobalBehaviorRegisterGroupAnalyzer>(
            ("Groups.cs", customGroupsFile), ("Wiring.cs", wrongWiringFile));

        // Assert (Then)
        Assert.Single(diagnostics, d => d.Id == GlobalBehaviorRegisterGroupAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task Analyze_MultipleGlobalBehaviorAttributesCorrectlyRegistered_ReportsNoDiagnostics()
    {
        // Arrange (Given) — two [assembly: SynapseGlobalBehavior] entries, group correctly registered:
        // zero diagnostics, not one skipped and one reported.
        const string wiringFile = """
            using Microsoft.Extensions.DependencyInjection;
            using UnambitiousFx.Synapse;
            using UnambitiousFx.Synapse.Abstractions;

            [assembly: SynapseGlobalBehavior(typeof(TestAssembly.SomeBehavior<,>))]
            [assembly: SynapseGlobalBehavior(typeof(TestAssembly.SomeBehavior<,>))]

            namespace TestAssembly
            {
                public static class Wiring
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSynapse(cfg =>
                        {
                            cfg.AddRegisterGroup(new RegisterGroup());
                        });
                    }
                }
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunFilesAsync<GlobalBehaviorRegisterGroupAnalyzer>(
            ("Groups.cs", GroupsFile), ("Wiring.cs", wiringFile));

        // Assert (Then)
        Assert.Empty(diagnostics.Where(d => d.Id == GlobalBehaviorRegisterGroupAnalyzer.DiagnosticId));
    }

    [Fact]
    public async Task Analyze_AttributeInGeneratedFile_IsNotReported()
    {
        // Arrange (Given) — mirrors the existing rules' coverage: an [assembly: SynapseGlobalBehavior] that
        // sits in a file the analyzer treats as generated (by filename convention) is never reported, even
        // though its group is genuinely unregistered.
        const string wiringFile = """
            using Microsoft.Extensions.DependencyInjection;
            using UnambitiousFx.Synapse;
            using UnambitiousFx.Synapse.Abstractions;

            [assembly: SynapseGlobalBehavior(typeof(TestAssembly.SomeBehavior<,>))]

            namespace TestAssembly
            {
                public static class Wiring
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSynapse(cfg => { });
                    }
                }
            }
            """;

        // Act (When) — the ".g.cs" suffix on the wiring file's path is what marks it generated.
        var diagnostics = await AnalyzerTestHelper.RunFilesAsync<GlobalBehaviorRegisterGroupAnalyzer>(
            ("Groups.cs", GroupsFile), ("Wiring.g.cs", wiringFile));

        // Assert (Then)
        Assert.Empty(diagnostics.Where(d => d.Id == GlobalBehaviorRegisterGroupAnalyzer.DiagnosticId));
    }
}
