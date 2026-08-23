using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UnambitiousFx.Synapse.Endpoints.Generator.Emit;
using UnambitiousFx.Synapse.Endpoints.Generator.Model;

namespace UnambitiousFx.Synapse.Endpoints.Generator;

/// <summary>
///     Emits endpoint registration and route metadata for every endpoint declared in the compilation.
/// </summary>
/// <remarks>
///     Discovery walks the base-type chain of every class declaration (<see cref="CreateSyntaxProvider" />)
///     rather than matching an attribute. An endpoint may declare its route inside <c>Configure</c> and
///     carry no attribute at all, so attribute-based discovery (which caches better) would miss it.
/// </remarks>
[Generator]
public sealed class EndpointsGenerator : IIncrementalGenerator
{
    private const string EndpointVoid = "UnambitiousFx.Synapse.Endpoints.Endpoint`1";
    private const string EndpointValue = "UnambitiousFx.Synapse.Endpoints.Endpoint`2";
    private const string EndpointMapped = "UnambitiousFx.Synapse.Endpoints.MappedEndpoint`4";
    private const string EndpointStream = "UnambitiousFx.Synapse.Endpoints.StreamEndpoint`2";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var endpoints = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                static (ctx, _) => Analyze(ctx))
            .Where(static target => target is not null)
            .Select(static (target, _) => target!.Value)
            .Collect();

        var rootNamespace = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
        {
            provider.GlobalOptions.TryGetValue("build_property.RootNamespace", out var value);
            return value;
        });

        context.RegisterSourceOutput(
            endpoints.Combine(rootNamespace),
            static (spc, pair) => Emit(spc, pair.Left, pair.Right));
    }

    private static EndpointTarget? Analyze(GeneratorSyntaxContext context)
    {
        if (context.SemanticModel.GetDeclaredSymbol(context.Node) is not INamedTypeSymbol symbol ||
            symbol.IsAbstract)
        {
            return null;
        }

        for (var baseType = symbol.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            var metadataName =
                $"{baseType.OriginalDefinition.ContainingNamespace}.{baseType.OriginalDefinition.MetadataName}";

            var kind = metadataName switch
            {
                EndpointVoid => EndpointKind.Void,
                EndpointValue => EndpointKind.Value,
                EndpointMapped => EndpointKind.Mapped,
                EndpointStream => EndpointKind.Stream,
                _ => (EndpointKind?)null
            };

            if (kind is null)
            {
                continue;
            }

            var bound = baseType.TypeArguments[0];
            var (method, route) = ReadRouteAttribute(symbol);

            return new EndpointTarget(
                symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                bound.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                kind.Value,
                method,
                route,
                ReadGroupAttribute(symbol),
                LocationInfo.CreateFrom(symbol.Locations.FirstOrDefault()));
        }

        return null;
    }

    private static (string Method, string Route) ReadRouteAttribute(INamedTypeSymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            for (var type = attribute.AttributeClass; type is not null; type = type.BaseType)
            {
                if (type.ToDisplayString() != "UnambitiousFx.Synapse.Endpoints.HttpEndpointAttribute")
                {
                    continue;
                }

                // Verb attributes pass the method to the base constructor, so the derived
                // attribute's single argument is the route.
                var arguments = attribute.ConstructorArguments;
                if (arguments.Length == 1)
                {
                    var verb = attribute.AttributeClass!.Name switch
                    {
                        "GetAttribute" => "GET",
                        "PostAttribute" => "POST",
                        "PutAttribute" => "PUT",
                        "PatchAttribute" => "PATCH",
                        "DeleteAttribute" => "DELETE",
                        _ => string.Empty
                    };
                    return (verb, arguments[0].Value as string ?? string.Empty);
                }

                if (arguments.Length == 2)
                {
                    var method = (arguments[0].Value as string ?? string.Empty).ToUpperInvariant();
                    return (method, arguments[1].Value as string ?? string.Empty);
                }
            }
        }

        // No attribute: the route is declared in Configure.
        return (string.Empty, string.Empty);
    }

    private static string? ReadGroupAttribute(INamedTypeSymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass is { IsGenericType: true } generic &&
                $"{generic.ContainingNamespace}.{generic.OriginalDefinition.MetadataName}" ==
                "UnambitiousFx.Synapse.Endpoints.InGroupAttribute`1")
            {
                return generic.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
        }

        return null;
    }

    private static void Emit(SourceProductionContext context,
        ImmutableArray<EndpointTarget> endpoints,
        string? rootNamespace)
    {
        if (endpoints.IsDefaultOrEmpty)
        {
            return;
        }

        var ns = rootNamespace ?? "UnambitiousFx.Synapse.Endpoints.Generated";
        var ordered = endpoints.OrderBy(e => e.EndpointFullName, StringComparer.Ordinal).ToArray();

        context.AddSource("EndpointGroup.g.cs", EndpointGroupEmitter.EmitGroup(ns, ordered));
        context.AddSource("SynapseEndpointRegistrations.g.cs", EndpointGroupEmitter.EmitRegistrations(ns, ordered));
    }
}
