using System.Text.Json.Serialization;
using UnambitiousFx.Synapse.Endpoints.Internal;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, ProbeJsonContext.Default);
});

var app = builder.Build();

// Probe: a descriptor captured by the lambda, exactly as the real design does it.
var descriptor = new EndpointDescriptor
{
    Route = "/probe/{id:int}",
    HttpMethods = ["GET"],
    ApplyMetadata = _ => { },
    InvokeAsync = async context =>
    {
        var id = int.Parse(context.Request.RouteValues["id"]!.ToString()!);
        await Results.Ok(new ProbeResponse(id, "ok")).ExecuteAsync(context);
    }
};

EndpointMapper.Map(app, descriptor);

app.Run();

internal sealed record ProbeResponse(int Id, string Status);

[JsonSerializable(typeof(ProbeResponse))]
internal sealed partial class ProbeJsonContext : JsonSerializerContext;
