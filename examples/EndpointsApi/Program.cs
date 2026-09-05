using UnambitiousFx.Examples.EndpointsApi;
using UnambitiousFx.Examples.EndpointsApi.Features.Tasks;
using UnambitiousFx.Examples.EndpointsApi.Infrastructure;
using UnambitiousFx.Synapse;
using UnambitiousFx.Synapse.AspNetCore;
using UnambitiousFx.Synapse.Endpoints;
using UnambitiousFx.Synapse.Endpoints.OpenApi;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

builder.Services.AddSingleton<TaskRepository>();

// StampPatchBehavior takes a clock, so the pipeline can stamp a [NotBound] property.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSynapseAspNetCore();
builder.Services.AddSynapse(cfg =>
    cfg.AddRegisterGroup(new global::UnambitiousFx.Examples.EndpointsApi.RegisterGroup()));

builder.Services.AddSynapseEndpointsOpenApi();
builder.Services.AddOpenApi(options => options.AddSynapseEndpoints());

var app = builder.Build();

app.UseSynapsePropagation();
app.MapOpenApi();

// One line per assembly. The generated SynapseEndpointGroup is a list of MapEndpoint<T>() calls.
app.MapSynapseEndpoints(new global::UnambitiousFx.Examples.EndpointsApi.SynapseEndpointGroup());

app.Run();

namespace UnambitiousFx.Examples.EndpointsApi
{
    // Makes Program accessible to WebApplicationFactory<Program> in the integration test project.
    public class Program;
}
