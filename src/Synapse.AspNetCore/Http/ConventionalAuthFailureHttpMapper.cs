using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UnambitiousFx.Functional.AspNetCore.Mappers;
using UnambitiousFx.Functional.Failures;

namespace UnambitiousFx.Synapse.AspNetCore.Http;

/// <summary>
///     A <see cref="DefaultFailureHttpMapper" /> that follows the usual HTTP convention for authorization failures:
///     <see cref="UnauthenticatedFailure" /> (no caller) becomes <c>401 Unauthorized</c> and
///     <see cref="UnauthorizedFailure" /> (caller without access) becomes <c>403 Forbidden</c>, both as
///     <see cref="ProblemDetails" />. Every other failure is mapped by <see cref="DefaultFailureHttpMapper" />.
/// </summary>
/// <remarks>
///     <see cref="DefaultFailureHttpMapper" /> maps these two failures the other way round, so many HTTP clients treat a
///     permission denial as an expired token and retry. Register this mapper before
///     <see cref="ServiceCollectionExtensions.AddSynapseAspNetCore" />, which then keeps it:
///     <code>
///     services.AddSingleton&lt;IFailureHttpMapper, ConventionalAuthFailureHttpMapper&gt;();
///     services.AddSynapseAspNetCore();
///     </code>
/// </remarks>
public class ConventionalAuthFailureHttpMapper : DefaultFailureHttpMapper
{
    /// <inheritdoc />
    public override FailureHttpResponse GetFailureResponse(IFailure failure)
    {
        return failure switch
        {
            UnauthenticatedFailure => Problem(StatusCodes.Status401Unauthorized, failure),
            UnauthorizedFailure => Problem(StatusCodes.Status403Forbidden, failure),
            _ => base.GetFailureResponse(failure)
        };
    }

    private static FailureHttpResponse Problem(int status, IFailure failure)
    {
        return new FailureHttpResponse
        {
            StatusCode = status,
            Body = new ProblemDetails { Status = status, Detail = failure.Message }
        };
    }
}
