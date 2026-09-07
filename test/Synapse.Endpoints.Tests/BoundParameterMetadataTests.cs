using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Internal;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed partial class BoundParameterMetadataTests
{
    [Fact]
    public void BoundParametersMetadata_WithParameters_ExposesThem()
    {
        // Arrange
        var page = new BoundParameterMetadata
        {
            Name = "page",
            Location = BoundParameterLocation.Query,
            Required = true,
            IsArray = false,
            ValueType = typeof(int)
        };

        // Act
        var metadata = new BoundParametersMetadata([page]);

        // Assert
        Assert.Single(metadata.Parameters);
        Assert.Equal("page", metadata.Parameters[0].Name);
        Assert.Equal(BoundParameterLocation.Query, metadata.Parameters[0].Location);
        Assert.True(metadata.Parameters[0].Required);
        Assert.False(metadata.Parameters[0].IsArray);
        Assert.Equal(typeof(int), metadata.Parameters[0].ValueType);
    }

    [Fact]
    public void BoundParametersMetadata_WithNullParameters_Throws()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new BoundParametersMetadata(null!));
    }

    [Fact]
    public void FormFieldMetadata_ForFilePart_CarriesFormFileAsValueType()
    {
        // Arrange & Act
        var field = new FormFieldMetadata
        {
            Name = "File",
            Required = true,
            IsArray = false,
            ValueType = typeof(Microsoft.AspNetCore.Http.IFormFile)
        };

        // Assert
        Assert.Equal(typeof(Microsoft.AspNetCore.Http.IFormFile), field.ValueType);
        Assert.True(field.Required);
    }

    private sealed class LegacyBinder : IEndpointBinder<string>
    {
        // Overrides neither new member — the shape of a hand-written binder written before they
        // existed. It must keep compiling and must declare no parameters.
        public ValueTask<BindResult<string>> BindAsync(HttpContext context) =>
            ValueTask.FromResult(BindResult<string>.Success("x"));
    }

    [Fact]
    public void IEndpointBinder_WithoutOverrides_DeclaresNoParametersOrFormFields()
    {
        // Arrange
        IEndpointBinder<string> binder = new LegacyBinder();

        // Act & Assert
        Assert.Empty(binder.Parameters);
        Assert.Empty(binder.FormFields);
    }

    private sealed class DeclaringBinder : IEndpointBinder<string>
    {
        private static readonly BoundParameterMetadata[] ParametersValue =
        [
            new()
            {
                Name = "page",
                Location = BoundParameterLocation.Query,
                Required = true,
                IsArray = false,
                ValueType = typeof(int)
            }
        ];

        public ValueTask<BindResult<string>> BindAsync(HttpContext context) =>
            ValueTask.FromResult(BindResult<string>.Success("x"));

        public IReadOnlyList<BoundParameterMetadata> Parameters => ParametersValue;
    }

    [Fact]
    public void IEndpointBinder_WithOverride_DeclaresItsParameters()
    {
        // Arrange
        IEndpointBinder<string> binder = new DeclaringBinder();

        // Act & Assert — reached through the interface reference, which is how every tier holds it.
        Assert.Single(binder.Parameters);
        Assert.Equal("page", binder.Parameters[0].Name);
    }

    private sealed record SearchTasksQuery : IRequest<string>;

    private sealed partial class SearchTasksEndpoint : Endpoint<SearchTasksQuery, string>;

    // A hand-written binder, not a generated one: this test's subject is the tier's attachment of
    // whatever the binder declares, not what the generator emits — that is the generator project's
    // own snapshot tests, and the full chain (generator through OpenApiDocument) is Task 7/8's. A
    // binder that declares something proves the attachment works now, rather than waiting on a skip
    // that Task 4 (which only touches the generator) could never actually lift.
    private sealed class SearchTasksBinder : IEndpointBinder<SearchTasksQuery>
    {
        private static readonly BoundParameterMetadata[] ParametersValue =
        [
            new()
            {
                Name = "page",
                Location = BoundParameterLocation.Query,
                Required = true,
                IsArray = false,
                ValueType = typeof(int)
            }
        ];

        public ValueTask<BindResult<SearchTasksQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<SearchTasksQuery>.Success(new SearchTasksQuery()));
        }

        public IReadOnlyList<BoundParameterMetadata> Parameters => ParametersValue;
    }

    [Fact]
    public void MappedEndpoint_WithQueryBoundMessage_CarriesBoundParametersMetadata()
    {
        // Arrange & Act — map through the real routing stack so the real routing stack applies the
        // metadata, the same way OpenApiMetadataTests does; EndpointHarness exposes no Endpoint
        // property to read route metadata off of.
        EndpointRegistry.RegisterBinder(new SearchTasksBinder());
        EndpointRegistry.RegisterMetadata<SearchTasksEndpoint>(new EndpointMetadata(["GET"], "/search-tasks"));
        var app = WebApplication.CreateSlimBuilder().Build();
        app.MapEndpoint<SearchTasksEndpoint>();
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();

        // Assert
        var metadata = endpoint.Metadata.GetMetadata<BoundParametersMetadata>();
        Assert.NotNull(metadata);
        Assert.Contains(metadata.Parameters, p => p.Name == "page" && p.Location == BoundParameterLocation.Query);
    }

    private sealed record NoParametersQuery : IRequest<string>;

    private sealed partial class NoParametersEndpoint : Endpoint<NoParametersQuery, string>;

    // Overrides neither Parameters nor FormFields, so the tier's { Count: > 0 } guard should skip
    // WithMetadata entirely rather than attaching an empty BoundParametersMetadata.
    private sealed class NoParametersBinder : IEndpointBinder<NoParametersQuery>
    {
        public ValueTask<BindResult<NoParametersQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<NoParametersQuery>.Success(new NoParametersQuery()));
        }
    }

    [Fact]
    public void MappedEndpoint_WithBinderDeclaringNoParameters_CarriesNoMetadata()
    {
        // Arrange & Act
        EndpointRegistry.RegisterBinder(new NoParametersBinder());
        EndpointRegistry.RegisterMetadata<NoParametersEndpoint>(new EndpointMetadata(["GET"], "/no-parameters"));
        var app = WebApplication.CreateSlimBuilder().Build();
        app.MapEndpoint<NoParametersEndpoint>();
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();

        // Assert
        Assert.Null(endpoint.Metadata.GetMetadata<BoundParametersMetadata>());
    }
}
