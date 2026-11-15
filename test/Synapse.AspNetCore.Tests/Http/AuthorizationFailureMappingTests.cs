using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional.AspNetCore;
using UnambitiousFx.Functional.AspNetCore.Mappers;
using UnambitiousFx.Functional.Failures;

namespace UnambitiousFx.Synapse.AspNetCore.Tests.Http;

// Pins the statuses the Synapse.Authorization and validation docs promise for the mapper AddSynapseAspNetCore registers.
public sealed class AuthorizationFailureMappingTests
{
    [Fact]
    public void DefaultFailureHttpMapper_UnauthenticatedFailure_MapsTo401()
    {
        // Arrange (Given)
        var mapper = new DefaultFailureHttpMapper();

        // Act (When)
        var response = mapper.GetFailureResponse(new UnauthenticatedFailure("Authentication is required."));

        // Assert (Then)
        Assert.Equal(StatusCodes.Status401Unauthorized, response.StatusCode);
    }

    [Fact]
    public void DefaultFailureHttpMapper_UnauthorizedFailure_MapsTo403()
    {
        // Arrange (Given)
        var mapper = new DefaultFailureHttpMapper();

        // Act (When)
        var response = mapper.GetFailureResponse(new UnauthorizedFailure("Requires scope 'write'."));

        // Assert (Then)
        Assert.Equal(StatusCodes.Status403Forbidden, response.StatusCode);
    }

    [Fact]
    public void DefaultFailureHttpMapper_ValidationFailure_MapsTo422()
    {
        // Arrange (Given)
        var mapper = new DefaultFailureHttpMapper();

        // Act (When)
        var response = mapper.GetFailureResponse(new ValidationFailure([new ValidationError("Name", "Required")]));

        // Assert (Then)
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public void AddSynapseAspNetCore_AfterAddResultHttp_KeepsTheConfiguredStatuses()
    {
        // Arrange (Given) — the documented way to change a status: configure Functional's mapper first
        var services = new ServiceCollection();
        services.AddResultHttp(o => o.ValidationStatus(StatusCodes.Status400BadRequest));

        // Act (When)
        services.AddSynapseAspNetCore();
        using var provider = services.BuildServiceProvider();
        var response = provider.GetRequiredService<IFailureHttpMapper>()
            .GetFailureResponse(new ValidationFailure([new ValidationError("Name", "Required")]));

        // Assert (Then)
        Assert.Equal(StatusCodes.Status400BadRequest, response?.StatusCode);
    }
}
