using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace UnambitiousFx.Synapse.Endpoints.Binding;

/// <summary>
///     Reads route, query and header values while collecting every problem it finds, so a request with
///     several bad inputs produces one <c>400</c> naming all of them rather than one naming the first.
/// </summary>
/// <remarks>
///     <para>
///         A mutable <see langword="struct" /> whose error store is allocated lazily on the first
///         error, so a valid request allocates nothing at all. That matters because generated binders
///         use this type on every request to every high-level endpoint;
///         <see cref="BindResult{T}" /> is a struct for the same reason.
///     </para>
///     <para>
///         Being a mutable struct, it has copy semantics: <b>use it as a local and do not pass it to
///         another method</b>, or errors added by the callee will be added to a copy and silently lost.
///         It is a plain struct rather than a <see langword="ref struct" /> because a handler needs to
///         <see langword="await" /> a body read between calls, which a <see langword="ref struct" />
///         local forbids.
///     </para>
///     <para>
///         The <c>…Optional</c> readers come as a <see langword="struct" />-constrained and a
///         <see langword="class" />-constrained overload pair. That is a legal overload rather than a
///         duplicate signature because the <see langword="out" /> parameter itself differs —
///         <c>Nullable&lt;T&gt;</c> against <c>T</c> — not merely the constraint. The enum readers get
///         a <c>…Enum</c> name instead precisely because theirs would not: their parameter is <c>T</c>
///         either way.
///     </para>
///     <para>
///         Its job is presence and parseability, plus <see cref="Check" /> for anything else. Business
///         rules belong in <c>IRequestValidator</c> and the Synapse pipeline, not here.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// var v = context.Validate();
/// v.Route&lt;Guid&gt;("taskId", out var taskId);
/// v.QueryOptional&lt;int&gt;("size", out var size);
/// v.QueryOptional&lt;string&gt;("sort", out var sort);
///
/// // Guarded on the read, so a request that sent no page is not also told it is too small.
/// if (v.Query&lt;int&gt;("page", out var page))
/// {
///     v.Check(page >= 1, "page", "must be at least 1");
/// }
///
/// if (!v.IsValid)
/// {
///     return v.Problem();
/// }
///     </code>
/// </example>
public struct BindingValidator
{
    private readonly HttpContext _context;
    private Dictionary<string, List<string>>? _errors;

    /// <summary>Initializes a new instance of the <see cref="BindingValidator" /> struct.</summary>
    /// <param name="context">The HTTP context whose values are read.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context" /> is <see langword="null" />.</exception>
    public BindingValidator(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _errors = null;
    }

    /// <summary>Gets a value indicating whether nothing has been reported.</summary>
    public bool IsValid => _errors is null;

    /// <summary>Gets the collected errors, keyed by field name, or <see langword="null" /> when valid.</summary>
    public IReadOnlyDictionary<string, string[]>? Errors => Materialize(_errors);

    /// <summary>Reads a required route value.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="name">The route parameter name.</param>
    /// <param name="value">The parsed value, or <see langword="default" /> when absent or unparsable.</param>
    /// <returns><see langword="true" /> when the value was present and parsed.</returns>
    public bool Route<T>(string name,
        out T value)
        where T : IParsable<T>
    {
        return Required(BindingSourceKind.Route, name, out value);
    }

    /// <summary>Reads an optional route value, reporting nothing when it is absent.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="name">The route parameter name.</param>
    /// <param name="value">The parsed value, or <see langword="null" /> when absent.</param>
    /// <returns><see langword="true" /> when the value was absent or present and parsed.</returns>
    public bool RouteOptional<T>(string name,
        out T? value)
        where T : struct, IParsable<T>
    {
        return Optional(BindingSourceKind.Route, name, out value);
    }

    /// <summary>
    ///     Reads an optional route value of a reference type, reporting nothing when it is absent.
    /// </summary>
    /// <typeparam name="T">The reference type to parse into.</typeparam>
    /// <param name="name">The route parameter name.</param>
    /// <param name="value">The parsed value, or <see langword="null" /> when absent.</param>
    /// <returns><see langword="true" /> when the value was absent or present and parsed.</returns>
    public bool RouteOptional<T>(string name,
        out T? value)
        where T : class, IParsable<T>
    {
        return Optional(BindingSourceKind.Route, name, out value);
    }

    /// <summary>Reads a required query value.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="name">The query key.</param>
    /// <param name="value">The parsed value, or <see langword="default" /> when absent or unparsable.</param>
    /// <returns><see langword="true" /> when the value was present and parsed.</returns>
    public bool Query<T>(string name,
        out T value)
        where T : IParsable<T>
    {
        return Required(BindingSourceKind.Query, name, out value);
    }

    /// <summary>Reads an optional query value, reporting nothing when it is absent.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="name">The query key.</param>
    /// <param name="value">The parsed value, or <see langword="null" /> when absent.</param>
    /// <returns><see langword="true" /> when the value was absent or present and parsed.</returns>
    public bool QueryOptional<T>(string name,
        out T? value)
        where T : struct, IParsable<T>
    {
        return Optional(BindingSourceKind.Query, name, out value);
    }

    /// <summary>
    ///     Reads an optional query value of a reference type, reporting nothing when it is absent.
    /// </summary>
    /// <typeparam name="T">The reference type to parse into.</typeparam>
    /// <param name="name">The query key.</param>
    /// <param name="value">The parsed value, or <see langword="null" /> when absent.</param>
    /// <returns><see langword="true" /> when the value was absent or present and parsed.</returns>
    /// <example>
    ///     <code>
    /// v.QueryOptional&lt;string&gt;("search", out var search);           // absent → null, never an error
    /// v.QueryOptional&lt;CallbackUrl&gt;("callback", out var callback);  // absent → null, bad → reported
    ///     </code>
    /// </example>
    public bool QueryOptional<T>(string name,
        out T? value)
        where T : class, IParsable<T>
    {
        return Optional(BindingSourceKind.Query, name, out value);
    }

    /// <summary>Reads a required header.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="name">The header name.</param>
    /// <param name="value">The parsed value, or <see langword="default" /> when absent or unparsable.</param>
    /// <returns><see langword="true" /> when the value was present and parsed.</returns>
    public bool Header<T>(string name,
        out T value)
        where T : IParsable<T>
    {
        return Required(BindingSourceKind.Header, name, out value);
    }

    /// <summary>Reads an optional header, reporting nothing when it is absent.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="name">The header name.</param>
    /// <param name="value">The parsed value, or <see langword="null" /> when absent.</param>
    /// <returns><see langword="true" /> when the value was absent or present and parsed.</returns>
    public bool HeaderOptional<T>(string name,
        out T? value)
        where T : struct, IParsable<T>
    {
        return Optional(BindingSourceKind.Header, name, out value);
    }

    /// <summary>Reads an optional header of a reference type, reporting nothing when it is absent.</summary>
    /// <typeparam name="T">The reference type to parse into.</typeparam>
    /// <param name="name">The header name.</param>
    /// <param name="value">The parsed value, or <see langword="null" /> when absent.</param>
    /// <returns><see langword="true" /> when the value was absent or present and parsed.</returns>
    public bool HeaderOptional<T>(string name,
        out T? value)
        where T : class, IParsable<T>
    {
        return Optional(BindingSourceKind.Header, name, out value);
    }

    /// <summary>Reads a required route value as an enum, accepting its name or its numeric value.</summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="name">The route parameter name.</param>
    /// <param name="value">The parsed value.</param>
    /// <returns><see langword="true" /> when the value was present and parsed.</returns>
    public bool RouteEnum<TEnum>(string name,
        out TEnum value)
        where TEnum : struct, Enum
    {
        return RequiredEnum(BindingSourceKind.Route, name, out value);
    }

    /// <summary>Reads a required query value as an enum, accepting its name or its numeric value.</summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="name">The query key.</param>
    /// <param name="value">The parsed value.</param>
    /// <returns><see langword="true" /> when the value was present and parsed.</returns>
    public bool QueryEnum<TEnum>(string name,
        out TEnum value)
        where TEnum : struct, Enum
    {
        return RequiredEnum(BindingSourceKind.Query, name, out value);
    }

    /// <summary>Reads a required header as an enum, accepting its name or its numeric value.</summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="name">The header name.</param>
    /// <param name="value">The parsed value.</param>
    /// <returns><see langword="true" /> when the value was present and parsed.</returns>
    public bool HeaderEnum<TEnum>(string name,
        out TEnum value)
        where TEnum : struct, Enum
    {
        return RequiredEnum(BindingSourceKind.Header, name, out value);
    }

    /// <summary>Reads every value of a repeated query key, parsing each one.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="name">The query key.</param>
    /// <param name="values">Every element that parsed, in request order.</param>
    /// <returns><see langword="true" /> when every present element parsed.</returns>
    /// <remarks>
    ///     An absent key is an empty array and reports nothing: HTTP has no way to send zero values
    ///     under a key, so treating absence as a failure would demand something unsendable. A caller
    ///     that genuinely requires one writes <c>v.Check(values.Length > 0, name, "…")</c>.
    /// </remarks>
    public bool QueryValues<T>(string name,
        out T[] values)
        where T : IParsable<T>
    {
        return ReadValues(BindingSourceKind.Query, name, out values);
    }

    /// <summary>Reads every value of a repeated header, parsing each one.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="name">The header name.</param>
    /// <param name="values">Every element that parsed, in request order.</param>
    /// <returns><see langword="true" /> when every present element parsed.</returns>
    public bool HeaderValues<T>(string name,
        out T[] values)
        where T : IParsable<T>
    {
        return ReadValues(BindingSourceKind.Header, name, out values);
    }

    /// <summary>Reads every value of a repeated query key as an enum, by name or numeric value.</summary>
    /// <typeparam name="TEnum">The enum element type.</typeparam>
    /// <param name="name">The query key.</param>
    /// <param name="values">Every element that parsed, in request order.</param>
    /// <returns><see langword="true" /> when every present element parsed.</returns>
    /// <remarks>
    ///     Named apart from <see cref="QueryValues{T}" /> for the same reason
    ///     <see cref="QueryEnum{TEnum}" /> is named apart from <see cref="Query{T}" />: an enum does not
    ///     implement <see cref="IParsable{TSelf}" />, so it cannot satisfy that constraint.
    /// </remarks>
    public bool QueryValuesEnum<TEnum>(string name,
        out TEnum[] values)
        where TEnum : struct, Enum
    {
        return ReadEnumValues(BindingSourceKind.Query, name, out values);
    }

    /// <summary>Reads every value of a repeated header as an enum, by name or numeric value.</summary>
    /// <typeparam name="TEnum">The enum element type.</typeparam>
    /// <param name="name">The header name.</param>
    /// <param name="values">Every element that parsed, in request order.</param>
    /// <returns><see langword="true" /> when every present element parsed.</returns>
    public bool HeaderValuesEnum<TEnum>(string name,
        out TEnum[] values)
        where TEnum : struct, Enum
    {
        return ReadEnumValues(BindingSourceKind.Header, name, out values);
    }

    /// <summary>Reports <paramref name="message" /> against <paramref name="field" /> when the condition is false.</summary>
    /// <param name="condition">The condition that must hold.</param>
    /// <param name="field">The field the message is about.</param>
    /// <param name="message">The message.</param>
    /// <returns>The value of <paramref name="condition" />, so calls can be chained in an <c>if</c>.</returns>
    /// <remarks>
    ///     Guard a check on the read that produced the value it tests. A failed read reports its own
    ///     error and leaves the value at <see langword="default" />, so an unguarded check on it adds a
    ///     second, false error — a request that omitted <c>page</c> entirely would be told both that it
    ///     is required and that it "must be at least 1". The readers return
    ///     <see langword="bool" /> for exactly this:
    ///     <c>if (v.Query&lt;int&gt;("page", out var page)) { v.Check(page >= 1, …); }</c>.
    /// </remarks>
    public bool Check(bool condition,
        string field,
        string message)
    {
        if (!condition)
        {
            AddError(field, message);
        }

        return condition;
    }

    /// <summary>Reports a message against a field.</summary>
    /// <param name="field">The field the message is about.</param>
    /// <param name="message">The message.</param>
    /// <exception cref="ArgumentException"><paramref name="field" /> is null or whitespace.</exception>
    public void AddError(string field,
        string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        _errors ??= new Dictionary<string, List<string>>(StringComparer.Ordinal);

        if (!_errors.TryGetValue(field, out var messages))
        {
            messages = [];
            _errors[field] = messages;
        }

        messages.Add(message);
    }

    /// <summary>Builds the <c>400</c> response describing every collected error.</summary>
    /// <returns>A <c>400 Bad Request</c> carrying <c>HttpValidationProblemDetails</c>.</returns>
    /// <exception cref="InvalidOperationException">Nothing was reported, so there is no problem to describe.</exception>
    public IResult Problem()
    {
        var errors = Materialize(_errors)
                     ?? throw new InvalidOperationException(
                         "No validation errors were collected, so there is nothing to report. Check " +
                         "IsValid before calling Problem.");

        return TypedResults.ValidationProblem(errors.ToDictionary(pair => pair.Key, pair => pair.Value));
    }

    private static Dictionary<string, string[]>? Materialize(Dictionary<string, List<string>>? errors)
    {
        return errors?.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
    }

    private bool TryRead(BindingSourceKind source,
        string name,
        out string? raw)
    {
        return source switch
        {
            BindingSourceKind.Route => BindingHelpers.TryGetRoute(_context, name, out raw),
            BindingSourceKind.Query => BindingHelpers.TryGetQuery(_context, name, out raw),
            _ => BindingHelpers.TryGetHeader(_context, name, out raw)
        };
    }

    private bool TryReadValues(BindingSourceKind source,
        string name,
        out StringValues values)
    {
        return source switch
        {
            BindingSourceKind.Query => BindingHelpers.TryGetQueryValues(_context, name, out values),
            _ => BindingHelpers.TryGetHeaderValues(_context, name, out values)
        };
    }

    private bool ReadValues<T>(BindingSourceKind source,
        string name,
        out T[] values)
        where T : IParsable<T>
    {
        if (!TryReadValues(source, name, out var raw) || raw.Count == 0)
        {
            values = [];
            return true;
        }

        var parsed = new List<T>(raw.Count);
        var complete = true;

        for (var index = 0; index < raw.Count; index++)
        {
            if (T.TryParse(raw[index], CultureInfo.InvariantCulture, out var value))
            {
                parsed.Add(value);
                continue;
            }

            // Reported and skipped rather than returned on: a request with several bad elements
            // should answer with all of them, which is the whole point of this collector.
            AddError(name, $"The {Describe(source)} at index {index} is not a valid {typeof(T)}.");
            complete = false;
        }

        values = parsed.ToArray();
        return complete;
    }

    private bool ReadEnumValues<TEnum>(BindingSourceKind source,
        string name,
        out TEnum[] values)
        where TEnum : struct, Enum
    {
        if (!TryReadValues(source, name, out var raw) || raw.Count == 0)
        {
            values = [];
            return true;
        }

        var parsed = new List<TEnum>(raw.Count);
        var complete = true;

        for (var index = 0; index < raw.Count; index++)
        {
            if (Enum.TryParse<TEnum>(raw[index], out var value))
            {
                parsed.Add(value);
                continue;
            }

            AddError(name, $"The {Describe(source)} at index {index} is not a valid {typeof(TEnum)}.");
            complete = false;
        }

        values = parsed.ToArray();
        return complete;
    }

    private bool Required<T>(BindingSourceKind source,
        string name,
        out T value)
        where T : IParsable<T>
    {
        value = default!;

        if (!TryRead(source, name, out var raw))
        {
            AddError(name, $"The {Describe(source)} is required.");
            return false;
        }

        // Invariant culture, matching ASP.NET Core's own parameter binding. A wire format must not
        // depend on the server's locale: with the current culture a "1.5" or an ISO date would parse
        // differently on a de-DE host than on the en-US host it was developed on.
        if (!T.TryParse(raw, CultureInfo.InvariantCulture, out var parsed))
        {
            AddError(name, $"The {Describe(source)} is not a valid {typeof(T)}.");
            return false;
        }

        value = parsed;
        return true;
    }

    private bool Optional<T>(BindingSourceKind source,
        string name,
        out T? value)
        where T : struct, IParsable<T>
    {
        value = null;

        if (!TryRead(source, name, out var raw))
        {
            return true;
        }

        if (!T.TryParse(raw, CultureInfo.InvariantCulture, out var parsed))
        {
            AddError(name, $"The {Describe(source)} is not a valid {typeof(T)}.");
            return false;
        }

        value = parsed;
        return true;
    }

    private bool Optional<T>(BindingSourceKind source,
        string name,
        out T? value)
        where T : class, IParsable<T>
    {
        value = null;

        if (!TryRead(source, name, out var raw))
        {
            return true;
        }

        if (!T.TryParse(raw, CultureInfo.InvariantCulture, out var parsed))
        {
            AddError(name, $"The {Describe(source)} is not a valid {typeof(T)}.");
            return false;
        }

        value = parsed;
        return true;
    }

    private bool RequiredEnum<TEnum>(BindingSourceKind source,
        string name,
        out TEnum value)
        where TEnum : struct, Enum
    {
        value = default;

        if (!TryRead(source, name, out var raw))
        {
            AddError(name, $"The {Describe(source)} is required.");
            return false;
        }

        if (!Enum.TryParse(raw, out value))
        {
            AddError(name, $"The {Describe(source)} is not a valid {typeof(TEnum)}.");
            return false;
        }

        return true;
    }

    private static string Describe(BindingSourceKind source)
    {
        return source switch
        {
            BindingSourceKind.Route => "route value",
            BindingSourceKind.Query => "query value",
            _ => "header"
        };
    }
}

/// <summary>Which part of the request a value is read from.</summary>
internal enum BindingSourceKind
{
    Route,
    Query,
    Header
}
