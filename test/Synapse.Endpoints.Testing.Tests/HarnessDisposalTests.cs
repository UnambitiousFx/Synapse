using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace UnambitiousFx.Synapse.Endpoints.Testing.Tests;

public sealed partial class HarnessDisposalTests
{
    [Fact]
    public void Dispose_RemovesTheDiagnosticListenerFromAllListeners()
    {
        // Arrange: EndpointRoutingMiddleware cannot be activated without a DiagnosticListener, so
        // Create registers one as a pre-built singleton - DI never disposes those. Before the fix,
        // Dispose() disposed only the provider, so this instance stayed subscribed to the
        // process-wide DiagnosticListener.AllListeners forever.
        var harness = EndpointHarness.Create<PingEndpoint>();
        var listener = harness.DiagnosticListener;

        // Act
        harness.Dispose();

        // Assert: AllListeners.Subscribe replays every still-live listener to a new observer, so a
        // listener the harness failed to dispose would show up here by reference.
        var stillPresent = false;
        using (DiagnosticListener.AllListeners.Subscribe(new CapturingObserver(candidate =>
               {
                   if (ReferenceEquals(candidate, listener))
                   {
                       stillPresent = true;
                   }
               })))
        {
        }

        Assert.False(stillPresent);
    }

    private sealed class CapturingObserver(Action<DiagnosticListener> onNext) : IObserver<DiagnosticListener>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(DiagnosticListener value)
        {
            onNext(value);
        }
    }

    [Get("/ping")]
    internal sealed partial class PingEndpoint : RawEndpoint
    {
        public override ValueTask<IResult> HandleAsync(HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<IResult>(TypedResults.Ok("pong"));
        }
    }
}
