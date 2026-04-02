using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.AspNetCore;

/// <summary>
///     Extension methods for configuring Synapse middleware in the ASP.NET Core request pipeline.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    ///     Adds middleware that appends the Synapse correlation ID to responses as the
    ///     <c>X-Correlation-Id</c> header. The header is written when the response starts,
    ///     after the mediator handler has run, so the correlation ID is always available.
    /// </summary>
    /// <remarks>
    ///     When no <see cref="IContextAccessor" /> is registered, or when the context has not
    ///     been initialized (e.g., for non-mediator routes), the header is silently omitted.
    /// </remarks>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
    {
        return app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Headers.TryGetValue("X-Correlation-Id", out var incomingCorrelationId)
                && Guid.TryParse(incomingCorrelationId, out var correlationId))
            {
                var setter = ctx.RequestServices.GetService<IContextSetter>();
                if (setter is not null)
                {
                    setter.Context = setter.Context.WithCorrelationId(correlationId);
                }
            }

            ctx.Response.OnStarting(() => OnStartingResponse(ctx));
            await next();
        });
    }

    private static Task OnStartingResponse(HttpContext httpContext)
    {
        try
        {
            var accessor = httpContext.RequestServices.GetService<IContextAccessor>();
            if (accessor is not null)
            {
                httpContext.Response.Headers.TryAdd("X-Correlation-Id", accessor.Context.CorrelationId.ToString());
            }
        }
        catch
        {
            // Context not initialized for non-mediator routes — skip silently.
        }

        return Task.CompletedTask;
    }
}