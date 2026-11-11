using Microsoft.CodeAnalysis;

namespace UnambitiousFx.Synapse.Generator;

/// <summary>
///     The <c>Stage</c>, <c>Before</c> and <c>After</c> a behavior declares on <c>[PipelineBehavior]</c>, captured as
///     equatable strings so they flow through the incremental pipeline. Drives both the emitted
///     <c>builder.ConfigureBehaviorOrdering(...)</c> call and the MDG018/MDG019 diagnostics.
/// </summary>
public readonly record struct BehaviorOrderingDetail
{
    public BehaviorOrderingDetail(string behaviorKey,
        int stage,
        string? stageName,
        string? beforeKey,
        string? beforeTypeOf,
        string? afterKey,
        string? afterTypeOf,
        LocationInfo? location)
    {
        BehaviorKey = behaviorKey;
        Stage = stage;
        StageName = stageName;
        BeforeKey = beforeKey;
        BeforeTypeOf = beforeTypeOf;
        AfterKey = afterKey;
        AfterTypeOf = afterTypeOf;
        Location = location;
    }

    /// <summary>The behavior as a <c>typeof</c> operand, open generics unbound: <c>global::Ns.Audit&lt;,&gt;</c>.</summary>
    public string BehaviorKey { get; }

    /// <summary>The declared stage value, or <c>-1</c> when none is declared.</summary>
    public int Stage { get; }

    /// <summary>The declared stage's enum member name, or null when none is declared.</summary>
    public string? StageName { get; }

    /// <summary>The <c>Before</c> target normalized to its definition (for matching), or null.</summary>
    public string? BeforeKey { get; }

    /// <summary>
    ///     The <c>Before</c> target as written, ready for <c>typeof</c>, or null when absent or not accessible from
    ///     the generated register group.
    /// </summary>
    public string? BeforeTypeOf { get; }

    /// <summary>The <c>After</c> target normalized to its definition (for matching), or null.</summary>
    public string? AfterKey { get; }

    /// <summary>The <c>After</c> target as written, ready for <c>typeof</c>, or null; see <see cref="BeforeTypeOf" />.</summary>
    public string? AfterTypeOf { get; }

    /// <summary>Where to report diagnostics: the behavior class, or null for a behavior from a referenced assembly.</summary>
    public LocationInfo? Location { get; }

    public bool HasStage => Stage >= 0;

    /// <summary>Whether there is anything to emit once inaccessible targets are dropped.</summary>
    public bool HasEmittableDeclaration => HasStage || BeforeTypeOf is not null || AfterTypeOf is not null;

    private static readonly SymbolDisplayFormat NoGenericsFormat = new(
        SymbolDisplayGlobalNamespaceStyle.Included,
        SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        SymbolDisplayGenericsOptions.None);

    /// <summary>
    ///     Reads the ordering named arguments of a <c>[PipelineBehavior]</c> application, or returns null when it sets
    ///     none of <c>Stage</c>, <c>Before</c>, <c>After</c>.
    /// </summary>
    public static BehaviorOrderingDetail? From(AttributeData attribute,
        INamedTypeSymbol behavior,
        Compilation compilation,
        LocationInfo? location)
    {
        var stage = -1;
        string? stageName = null;
        INamedTypeSymbol? before = null;
        INamedTypeSymbol? after = null;

        foreach (var argument in attribute.NamedArguments)
        {
            var value = argument.Value;
            switch (argument.Key)
            {
                case "Stage" when value.Kind == TypedConstantKind.Enum && value.Value is int stageValue:
                    stage = stageValue;
                    stageName = value.Type?.GetMembers().OfType<IFieldSymbol>()
                        .FirstOrDefault(field => field.HasConstantValue && Equals(field.ConstantValue, stageValue))
                        ?.Name;
                    break;
                case "Before" when value.Value is INamedTypeSymbol { TypeKind: not TypeKind.Error } type:
                    before = type;
                    break;
                case "After" when value.Value is INamedTypeSymbol { TypeKind: not TypeKind.Error } type:
                    after = type;
                    break;
            }
        }

        if (stage < 0 && before is null && after is null)
        {
            return null;
        }

        return new BehaviorOrderingDetail(KeyOf(behavior), stage, stageName,
            before is null ? null : KeyOf(before), TypeOfOperand(before, compilation),
            after is null ? null : KeyOf(after), TypeOfOperand(after, compilation),
            location);
    }

    /// <summary>The definition of <paramref name="type" /> as a <c>typeof</c> operand, generics unbound.</summary>
    public static string KeyOf(INamedTypeSymbol type)
    {
        var definition = type.OriginalDefinition;
        return definition.IsGenericType
            ? $"{definition.ToDisplayString(NoGenericsFormat)}<{new string(',', definition.Arity - 1)}>"
            : definition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    /// <summary>Strips <c>global::</c> and namespaces for messages: <c>global::Ns.Audit&lt;,&gt;</c> becomes <c>Audit&lt;,&gt;</c>.</summary>
    public static string DisplayName(string key)
    {
        var generic = key.IndexOf('<');
        var head = generic < 0 ? key : key.Substring(0, generic);
        var dot = head.LastIndexOf('.');
        var colon = head.LastIndexOf(':');
        var start = Math.Max(dot, colon) + 1;
        return key.Substring(start);
    }

    private static string? TypeOfOperand(INamedTypeSymbol? type, Compilation compilation)
    {
        if (type is null || !compilation.IsSymbolAccessibleWithin(type.OriginalDefinition, compilation.Assembly))
        {
            return null;
        }

        return type.IsUnboundGenericType
            ? KeyOf(type)
            : type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }
}
