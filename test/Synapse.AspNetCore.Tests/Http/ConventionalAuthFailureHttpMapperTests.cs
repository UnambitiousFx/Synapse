using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional.AspNetCore.Mappers;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.AspNetCore.Http;

namespace UnambitiousFx.Synapse.AspNetCore.Tests.Http;

[TestSubject(typeof(ConventionalAuthFailureHttpMapper))]
public sealed class ConventionalAuthFailureHttpMapperTests
{
    [Fact]
    public void GetFailureResponse_UnauthenticatedFailure_Returns401ProblemDetails()
    {
        // Arrange (Given)
        var mapper = new ConventionalAuthFailureHttpMapper();

        // Act (When)
        var response = mapper.GetFailureResponse(new UnauthenticatedFailure("Authentication is required."));

        // Assert (Then)
        Assert.NotNull(response);
        Assert.Equal(StatusCodes.Status401Unauthorized, response.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(response.Body);
        Assert.Equal(StatusCodes.Status401Unauthorized, problem.Status);
        Assert.Equal("Authentication is required.", problem.Detail);
    }

    [Fact]
    public void GetFailureResponse_UnauthorizedFailure_Returns403ProblemDetails()
    {
        // Arrange (Given)
        var mapper = new ConventionalAuthFailureHttpMapper();

        // Act (When)
        var response = mapper.GetFailureResponse(new UnauthorizedFailure("Requires scope 'write'."));

        // Assert (Then)
        Assert.NotNull(response);
        Assert.Equal(StatusCodes.Status403Forbidden, response.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(response.Body);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.Status);
        Assert.Equal("Requires scope 'write'.", problem.Detail);
    }

    [Fact]
    public void GetFailureResponse_OtherFailure_DelegatesToDefaultMapper()
    {
        // Arrange (Given)
        var mapper = new ConventionalAuthFailureHttpMapper();
        var failure = new NotFoundFailure("Loan", "42");

        // Act (When)
        var response = mapper.GetFailureResponse(failure);

        // Assert (Then)
        Assert.Equal(new DefaultFailureHttpMapper().GetFailureResponse(failure)?.StatusCode, response?.StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, response?.StatusCode);
    }

    [Fact]
    public void AddSynapseAspNetCore_WithMapperRegisteredFirst_KeepsIt()
    {
        // Arrange (Given)
        var services = new ServiceCollection();
        services.AddSingleton<IFailureHttpMapper, ConventionalAuthFailureHttpMapper>();

        // Act (When)
        services.AddSynapseAspNetCore();

        // Assert (Then)
        using var provider = services.BuildServiceProvider();
        Assert.IsType<ConventionalAuthFailureHttpMapper>(provider.GetRequiredService<IFailureHttpMapper>());
    }
}
