using System.Runtime.CompilerServices;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Authorization;

/// <summary>
///     Authorizes an <see cref="IStreamRequest{TResponse}" /> with the same rules as the request behaviors, in the same
///     order: actor, role, scope (a stream counts as a query for the default scope), request policy, then resource.
///     The checks run once, before the handler starts. A denial is reported to every <see cref="IAuthorizationDenialRecorder" /> and
///     the stream yields a single failed item (<c>UnauthenticatedFailure</c> or <c>UnauthorizedFailure</c>) without
///     running the handler. Runs at <see cref="AuthorizationBehaviorOrder.Actor" />.
/// </summary>
/// <remarks>
///     The checks run in one behavior: unlike the request pipeline, an application behavior cannot sit between two
///     of them.
/// </remarks>
/// <typeparam name="TRequest">The stream request type.</typeparam>
/// <typeparam name="TItem">The type of the streamed items.</typeparam>
public sealed class StreamAuthorizationBehavior<TRequest, TItem> : IStreamRequestPipelineBehavior<TRequest, TItem>,
    IOrderedPipelineBehavior
    where TRequest : IStreamRequest<TItem>
    where TItem : notnull
{
    private readonly string? _defaultScope;
    private readonly Func<AuthorizationDenial, Failure?>? _denialFailure;
    private readonly IResourceAccessPolicy? _policy;
    private readonly ICurrentPrincipal _principal;
    private readonly IRequestAccessPolicy<TRequest>? _requestPolicy;
    private readonly IEnumerable<IAuthorizationDenialRecorder> _recorders;
    private readonly SystemBypass _systemBypass;

    /// <summary>
    ///     Creates the behavior.
    /// </summary>
    /// <param name="principal">The caller.</param>
    /// <param name="options">The default query scope, and the denial failure.</param>
    /// <param name="recorders">The recorders told about a denial.</param>
    /// <param name="policy">The application's resource policy, <see langword="null" /> when none is registered.</param>
    /// <param name="requestPolicy">The request type's policy, <see langword="null" /> when none is registered.</param>
    public StreamAuthorizationBehavior(ICurrentPrincipal principal,
        SynapseAuthorizationOptions options,
        IEnumerable<IAuthorizationDenialRecorder> recorders,
        IResourceAccessPolicy? policy = null,
        IRequestAccessPolicy<TRequest>? requestPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(recorders);
        _principal = principal;
        _recorders = recorders;
        _policy = policy;
        _requestPolicy = requestPolicy;
        _defaultScope = AuthorizationChecks.DefaultScope<TRequest>(options);
        _denialFailure = options.DenialFailure;
        _systemBypass = options.SystemBypass;
    }

    /// <inheritdoc />
    public uint Order => AuthorizationBehaviorOrder.Actor;

    /// <inheritdoc />
    public async IAsyncEnumerable<Result<TItem>> HandleAsync(TRequest request,
        StreamRequestHandlerDelegate<TItem> next,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var denied = AuthorizationChecks.SkipsIdentityChecks(_principal, _systemBypass)
            ? null
            : AuthorizationChecks.Actor(request, _principal)
              ?? AuthorizationChecks.Role(request, _principal)
              ?? AuthorizationChecks.Scope(request, _principal, _defaultScope);
        var skipsResourceChecks = AuthorizationChecks.SkipsResourceChecks(_principal, _systemBypass);
        if (denied is null && _requestPolicy is not null && !skipsResourceChecks)
        {
            denied = await AuthorizationChecks.RequestAsync(request, _principal, _requestPolicy, cancellationToken);
        }

        if (denied is null && !skipsResourceChecks && request is IResourceScoped scoped)
        {
            denied = await AuthorizationChecks.ResourceAsync<TRequest>(scoped, _principal, _policy, cancellationToken);
        }

        if (denied is { } d)
        {
            var failure = AuthorizationDenials.FailureFor(d, _denialFailure);
            await AuthorizationDenials.RecordAsync(_recorders, d.Denial, cancellationToken);
            yield return Result.Failure<TItem>(failure);
            yield break;
        }

        await foreach (var item in next())
        {
            yield return item;
        }
    }
}
