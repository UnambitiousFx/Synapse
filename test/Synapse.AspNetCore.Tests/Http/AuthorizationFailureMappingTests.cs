using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UnambitiousFx.Functional.AspNetCore.Mappers;
using UnambitiousFx.Functional.Failures;

namespace UnambitiousFx.Synapse.AspNetCore.Tests.Http;

// Pins the statuses the Synapse.Authorization docs promise: the authorization failures are denials, never a 500.
// The default mapper sends UnauthorizedFailure to 401 and UnauthenticatedFailure to 403; the docs show how to swap them.
public sealed class AuthorizationFailureMappingTests
{
    [Fact]
    public void DefaultFailureHttpMapper_UnauthenticatedFailure_MapsTo403()
    {
        // Arrange (Given)
        var mapper = new DefaultFailureHttpMapper();

        // Act (When)
        var response = mapper.GetFailureResponse(new UnauthenticatedFailure("Authentication is required."));

        // Assert (Then)
        Assert.NotNull(response);
        Assert.Equal(StatusCodes.Status403Forbidden, response.StatusCode);
    }

    [Fact]
    public void DefaultFailureHttpMapper_UnauthorizedFailure_MapsTo401()
    {
        // Arrange (Given)
        var mapper = new DefaultFailureHttpMapper();

        // Act (When)
        var response = mapper.GetFailureResponse(new UnauthorizedFailure("Requires scope 'write'."));

        // Assert (Then)
        Assert.NotNull(response);
        Assert.Equal(StatusCodes.Status401Unauthorized, response.StatusCode);
    }

    [Fact]
    public void CustomMapper_FromTheAuthorizationDocs_GivesTheConventionalSplit()
    {
        // Arrange (Given)
        IFailureHttpMapper mapper = new AuthorizationStatusMapper();

        // Act (When)
        var unauthenticated = mapper.GetFailureResponse(new UnauthenticatedFailure("Authentication is required."));
        var unauthorized = mapper.GetFailureResponse(new UnauthorizedFailure("Requires scope 'write'."));
        var notFound = mapper.GetFailureResponse(new NotFoundFailure("Loan", "42"));

        // Assert (Then)
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthenticated?.StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, unauthorized?.StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, notFound?.StatusCode);
    }

    // Kept identical to the mapper shown in docs/docs/authorization.mdx.
    private sealed class AuthorizationStatusMapper : IFailureHttpMapper
    {
        private readonly DefaultFailureHttpMapper _default = new();

        public FailureHttpResponse? GetFailureResponse(IFailure failure)
        {
            return failure switch
            {
                UnauthenticatedFailure => Problem(StatusCodes.Status401Unauthorized, failure),
                UnauthorizedFailure => Problem(StatusCodes.Status403Forbidden, failure),
                _ => _default.GetFailureResponse(failure)
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
}
