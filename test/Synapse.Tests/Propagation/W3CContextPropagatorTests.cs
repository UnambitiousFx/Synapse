using System.Diagnostics;
using NSubstitute;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Contexts;
using UnambitiousFx.Synapse.Propagation;

namespace UnambitiousFx.Synapse.Tests.Propagation;

public sealed class W3CContextPropagatorTests
{
    [Fact]
    public void InjectThenExtract_RoundTripsBaggage()
    {
        // Arrange (Given)
        var propagator = new W3CContextPropagator();
        var context = NewContext();
        context.SetBaggage("tenant.id", "contoso");
        context.SetBaggage("user.id", "u-42");
        var carrier = new DictionaryPropagationCarrier();

        // Act (When)
        propagator.Inject(context, carrier);
        var extracted = propagator.Extract(carrier);

        // Assert (Then)
        Assert.NotNull(extracted.Baggage);
        Assert.Equal("contoso", extracted.Baggage!["tenant.id"]);
        Assert.Equal("u-42", extracted.Baggage["user.id"]);
    }

    [Fact]
    public void Inject_PutsNoSynapseIdentityEntriesInBaggage()
    {
        // Arrange (Given) — identity rides traceparent; baggage carries business values only, so a second
        // identifier scheme must not reappear on the wire
        var propagator = new W3CContextPropagator();
        var context = NewContext();
        context.SetBaggage("tenant.id", "contoso");
        var carrier = new DictionaryPropagationCarrier();

        // Act (When)
        propagator.Inject(context, carrier);

        // Assert (Then)
        Assert.True(carrier.Headers.TryGetValue(PropagationKeys.Baggage, out var baggage));
        Assert.DoesNotContain("synapse.", baggage!, StringComparison.Ordinal);
        Assert.Equal("tenant.id=contoso", baggage);
    }

    [Theory]
    [InlineData("value with spaces")]
    [InlineData("value=with=equals")]
    [InlineData("value;with;semicolons")]
    [InlineData("100% sure")]
    [InlineData("café — accented")]
    public void InjectThenExtract_RoundTripsValuesNeedingEncoding(string value)
    {
        // Arrange (Given)
        var propagator = new W3CContextPropagator();
        var context = NewContext();
        Assert.True(context.SetBaggage("k", value));
        var carrier = new DictionaryPropagationCarrier();

        // Act (When)
        propagator.Inject(context, carrier);
        var extracted = propagator.Extract(carrier);

        // Assert (Then)
        Assert.Equal(value, extracted.Baggage!["k"]);
    }

    [Fact]
    public void InjectThenExtract_WithNoActivityListener_CarriesBaggageAndTheContextsTraceId()
    {
        // Arrange (Given) — Activity.Current is null in a host that never wired OpenTelemetry, so baggage
        // stored only on the Activity would silently vanish. It must live on the context instead — and so must
        // the trace id: writing no traceparent at all left the receiver to start a brand new trace (known
        // issue 040).
        Assert.Null(Activity.Current);
        var propagator = new W3CContextPropagator();
        var context = NewContext();
        context.SetBaggage("tenant.id", "contoso");
        var carrier = new DictionaryPropagationCarrier();

        // Act (When)
        propagator.Inject(context, carrier);
        var extracted = propagator.Extract(carrier);

        // Assert (Then)
        Assert.Equal("contoso", extracted.Baggage!["tenant.id"]);
        Assert.Equal(context.TraceId, extracted.Trace.TraceId.ToHexString());
    }

    [Fact]
    public void Inject_WithNoActivity_WritesATraceParentDerivedFromTheContext()
    {
        // Arrange (Given) — there is no span to name, so the parent id is derived from the trace id and the
        // sampled flag is off: this is what a peer that is not recording reports
        Assert.Null(Activity.Current);
        var propagator = new W3CContextPropagator();
        var context = NewContext();
        var carrier = new DictionaryPropagationCarrier();

        // Act (When)
        propagator.Inject(context, carrier);

        // Assert (Then)
        var traceParent = carrier.Headers[PropagationKeys.TraceParent];
        Assert.Equal($"00-{context.TraceId}-{context.TraceId[..16]}-00", traceParent);
        Assert.True(ActivityContext.TryParse(traceParent, null, out var parsed));
        Assert.Equal(context.TraceId, parsed.TraceId.ToHexString());
        Assert.Equal(ActivityTraceFlags.None, parsed.TraceFlags);
    }

    [Fact]
    public void Inject_TwiceWithNoActivity_NamesTheSameSyntheticSpan()
    {
        // Arrange (Given) — derived rather than random, so two hops of one flow do not claim two different
        // parents in the same trace
        var propagator = new W3CContextPropagator();
        var context = NewContext();
        var first = new DictionaryPropagationCarrier();
        var second = new DictionaryPropagationCarrier();

        // Act (When)
        propagator.Inject(context, first);
        propagator.Inject(context, second);

        // Assert (Then)
        Assert.Equal(first.Headers[PropagationKeys.TraceParent],
            second.Headers[PropagationKeys.TraceParent]);
    }

    [Fact]
    public void Inject_WithARecordingActivity_PrefersTheRealSpanOverTheDerivedOne()
    {
        // Arrange (Given) — the derived parent is a fallback, never a replacement: a real span id is the one a
        // backend can actually resolve
        using var listener = NewRecordingListener(out var sourceName);
        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity("outbound");
        Assert.NotNull(activity);

        var propagator = new W3CContextPropagator();
        var carrier = new DictionaryPropagationCarrier();

        // Act (When)
        propagator.Inject(NewContext(), carrier);

        // Assert (Then)
        Assert.Contains(activity!.SpanId.ToHexString(), carrier.Headers[PropagationKeys.TraceParent],
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-a-trace-id")]
    [InlineData("00000000000000000000000000000000")]
    // 32 characters, so the length check passes, but not a trace id a conformant receiver can parse
    [InlineData("0AF7651916CD43DD8448EB211C80319C")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void Inject_WithAnUnusableContextTraceId_WritesNoTraceParent(string traceId)
    {
        // Arrange (Given) — IContext.TraceId is contractually 32 lowercase hex characters, but a custom
        // IContextFactory could return anything, and an unparseable traceparent is worse than an absent one
        Assert.Null(Activity.Current);
        var propagator = new W3CContextPropagator();
        var context = Substitute.For<IContext>();
        context.TraceId.Returns(traceId);
        context.Baggage.Returns(new Dictionary<string, string>());
        var carrier = new DictionaryPropagationCarrier();

        // Act (When)
        propagator.Inject(context, carrier);

        // Assert (Then)
        Assert.False(carrier.Headers.ContainsKey(PropagationKeys.TraceParent));
    }

    [Fact]
    public void Inject_WhenAnActivityIsRecording_WritesTraceParentThatExtractsBack()
    {
        // Arrange (Given) — the ordinary case: the context took its trace id from the ambient activity, so the
        // two agree and the platform propagator writes the header
        using var listener = NewRecordingListener(out var sourceName);
        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity("outbound");
        Assert.NotNull(activity);

        var propagator = new W3CContextPropagator();
        var carrier = new DictionaryPropagationCarrier();

        // Act (When)
        propagator.Inject(NewContextOn(activity!), carrier);
        var extracted = propagator.Extract(carrier);

        // Assert (Then)
        Assert.True(carrier.Headers.ContainsKey(PropagationKeys.TraceParent));
        Assert.Equal(activity!.TraceId, extracted.Trace.TraceId);
        Assert.Equal(activity.SpanId, extracted.Trace.SpanId);
    }

    [Fact]
    public void Inject_WhenTheContextAndTheActivityDisagree_PropagatesTheContextsTraceId()
    {
        // Arrange (Given) — an untrusted boundary refused the caller's traceparent, so the context carries a
        // server-minted trace id while the host's request instrumentation already parented the ambient activity
        // to the caller. Injecting the activity's id put the caller-chosen trace id back on the wire as this
        // flow's identity — the value the boundary rejected, forwarded onward (known issue 032).
        using var listener = NewRecordingListener(out var sourceName);
        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity("outbound");
        Assert.NotNull(activity);

        var propagator = new W3CContextPropagator();
        var context = NewContext();
        Assert.NotEqual(activity!.TraceId.ToHexString(), context.TraceId);
        var carrier = new DictionaryPropagationCarrier();

        // Act (When)
        propagator.Inject(context, carrier);
        var extracted = propagator.Extract(carrier);

        // Assert (Then) — the trace is the context's, the span is still the one making the call
        Assert.Equal(context.TraceId, extracted.Trace.TraceId.ToHexString());
        Assert.Equal(activity.SpanId, extracted.Trace.SpanId);
        Assert.DoesNotContain(activity.TraceId.ToHexString(),
            carrier.Headers[PropagationKeys.TraceParent], StringComparison.Ordinal);
    }

    [Fact]
    public void Inject_WhenTheContextAndTheActivityDisagree_DropsTheActivitysTraceState()
    {
        // Arrange (Given) — tracestate is vendor state scoped to the caller's trace, and the header being
        // written names a different one
        using var listener = NewRecordingListener(out var sourceName);
        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity("outbound");
        Assert.NotNull(activity);
        activity!.TraceStateString = "vendor=opaque";

        var propagator = new W3CContextPropagator();
        var carrier = new DictionaryPropagationCarrier();

        // Act (When)
        propagator.Inject(NewContext(), carrier);

        // Assert (Then)
        Assert.False(carrier.Headers.ContainsKey(PropagationKeys.TraceState));
        Assert.DoesNotContain("vendor=opaque", string.Join(';', carrier.Headers.Values),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Inject_WhenTheContextsTraceIdIsUnusableAndAnActivityExists_WritesNoTraceParent()
    {
        // Arrange (Given) — falling back to the activity's id here would readmit the very value the divergence
        // exists to refuse, so an absent header is the only safe answer
        using var listener = NewRecordingListener(out var sourceName);
        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity("outbound");
        Assert.NotNull(activity);

        var propagator = new W3CContextPropagator();
        var context = Substitute.For<IContext>();
        context.TraceId.Returns("not-a-trace-id");
        context.Baggage.Returns(new Dictionary<string, string>());
        var carrier = new DictionaryPropagationCarrier();

        // Act (When)
        propagator.Inject(context, carrier);

        // Assert (Then)
        Assert.False(carrier.Headers.ContainsKey(PropagationKeys.TraceParent));
    }

    [Fact]
    public void Extract_FromEmptyCarrier_ReturnsNone()
    {
        // Arrange (Given)
        var propagator = new W3CContextPropagator();

        // Act (When)
        var extracted = propagator.Extract(new DictionaryPropagationCarrier());

        // Assert (Then)
        Assert.True(extracted.IsEmpty);
    }

    [Fact]
    public void Extract_WithMalformedTraceParent_LeavesTraceUnset()
    {
        // Arrange (Given) — a bad peer must not fail the request
        var propagator = new W3CContextPropagator();
        var carrier = new DictionaryPropagationCarrier();
        carrier.Set(PropagationKeys.TraceParent, "not-a-traceparent");

        // Act (When)
        var extracted = propagator.Extract(carrier);

        // Assert (Then)
        Assert.Equal(default, extracted.Trace);
    }

    [Fact]
    public void Extract_WithMalformedBaggage_KeepsTheUsableEntries()
    {
        // Arrange (Given) — a bad peer must degrade the request, not fail it
        var propagator = new W3CContextPropagator();
        var carrier = new DictionaryPropagationCarrier();
        carrier.Set(PropagationKeys.Baggage, "no-separator,good=yes,=novalue,also.good=1");

        // Act (When)
        var extracted = propagator.Extract(carrier);

        // Assert (Then)
        Assert.Equal("yes", extracted.Baggage!["good"]);
        Assert.Equal("1", extracted.Baggage["also.good"]);
        Assert.Equal(2, extracted.Baggage.Count);
    }

    [Fact]
    public void Extract_WithOversizedBaggage_CapsItInsteadOfThrowing()
    {
        // Arrange (Given)
        var propagator = new W3CContextPropagator();
        var oversized = string.Join(',',
            Enumerable.Range(0, 40).Select(i => $"k{i}={new string('x', 1000)}"));
        var carrier = new DictionaryPropagationCarrier();
        carrier.Set(PropagationKeys.Baggage, oversized);

        // Act (When)
        var extracted = propagator.Extract(carrier);

        // Assert (Then)
        Assert.NotNull(extracted.Baggage);
        Assert.True(extracted.Baggage!.Count < 40);
        Assert.InRange(
            extracted.Baggage.Sum(e => BaggageLimits.MeasureEntry(e.Key, e.Value)),
            1, BaggageLimits.MaxTotalBytes);
    }

    [Fact]
    public void Extract_WithOnlyTheLegacyBaggageHeader_ReadsIt()
    {
        // Arrange (Given) — an older ASP.NET Core peer sends Correlation-Context rather than baggage
        var propagator = new W3CContextPropagator();
        var carrier = new DictionaryPropagationCarrier();
        carrier.Set(PropagationKeys.LegacyBaggage, "tenant.id=acme");

        // Act (When)
        var extracted = propagator.Extract(carrier);

        // Assert (Then)
        Assert.Equal("acme", extracted.Baggage!["tenant.id"]);
    }

    [Fact]
    public void Extract_WithBothBaggageHeaders_PrefersTheW3COne()
    {
        // Arrange (Given) — the W3C name is what a current peer sets deliberately
        var propagator = new W3CContextPropagator();
        var carrier = new DictionaryPropagationCarrier();
        carrier.Set(PropagationKeys.Baggage, "tenant.id=w3c");
        carrier.Set(PropagationKeys.LegacyBaggage, "tenant.id=legacy");

        // Act (When)
        var extracted = propagator.Extract(carrier);

        // Assert (Then)
        Assert.Equal("w3c", extracted.Baggage!["tenant.id"]);
    }

    [Fact]
    public void Extract_WithEmptyW3CBaggageAndALegacyHeader_FallsBackToLegacy()
    {
        // Arrange (Given) — an empty W3C header must not mask a usable legacy one
        var propagator = new W3CContextPropagator();
        var carrier = new DictionaryPropagationCarrier();
        carrier.Set(PropagationKeys.Baggage, "   ");
        carrier.Set(PropagationKeys.LegacyBaggage, "tenant.id=legacy");

        // Act (When)
        var extracted = propagator.Extract(carrier);

        // Assert (Then)
        Assert.Equal("legacy", extracted.Baggage!["tenant.id"]);
    }

    [Fact]
    public void Inject_WritesOnlyTheW3CBaggageHeader()
    {
        // Arrange (Given) — the legacy name is read for compatibility, never written
        var propagator = new W3CContextPropagator();
        var context = NewContext();
        context.SetBaggage("tenant.id", "acme");
        var carrier = new DictionaryPropagationCarrier();

        // Act (When)
        propagator.Inject(context, carrier);

        // Assert (Then)
        Assert.True(carrier.Headers.ContainsKey(PropagationKeys.Baggage));
        Assert.False(carrier.Headers.ContainsKey(PropagationKeys.LegacyBaggage));
    }

    [Fact]
    public void Inject_WithBaggageOnTheAmbientActivity_DoesNotForwardIt()
    {
        // Arrange (Given) — the platform propagator serializes Activity.Baggage alongside the trace headers, so
        // an entry that lives only on the activity — including inbound baggage an untrusted boundary dropped from
        // the context on purpose — used to leave the process anyway (known issue 037)
        using var listener = NewRecordingListener(out var sourceName);
        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity("outbound");
        Assert.NotNull(activity);
        activity!.AddBaggage("caller.supplied", "spoofed");

        var propagator = new W3CContextPropagator();
        var context = NewContext();
        context.SetBaggage("tenant.id", "contoso");
        var carrier = new DictionaryPropagationCarrier();

        // Act (When)
        propagator.Inject(context, carrier);

        // Assert (Then) — trace context still written, baggage is the context's alone
        Assert.True(carrier.Headers.ContainsKey(PropagationKeys.TraceParent));
        Assert.Equal("tenant.id=contoso", carrier.Headers[PropagationKeys.Baggage]);
        Assert.False(carrier.Headers.ContainsKey(PropagationKeys.LegacyBaggage));
        Assert.DoesNotContain("spoofed", string.Join(';', carrier.Headers.Values), StringComparison.Ordinal);
    }

    [Fact]
    public void Inject_WithEmptyContextBaggageAndBaggageOnTheActivity_WritesNoBaggageHeader()
    {
        // Arrange (Given) — the case the old code could not express: nothing to say, and the platform's copy of
        // the activity's baggage saying it anyway
        using var listener = NewRecordingListener(out var sourceName);
        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity("outbound");
        Assert.NotNull(activity);
        activity!.AddBaggage("caller.supplied", "spoofed");

        var propagator = new W3CContextPropagator();
        var carrier = new DictionaryPropagationCarrier();

        // Act (When)
        propagator.Inject(NewContext(), carrier);

        // Assert (Then)
        Assert.True(carrier.Headers.ContainsKey(PropagationKeys.TraceParent));
        Assert.False(carrier.Headers.ContainsKey(PropagationKeys.Baggage));
        Assert.False(carrier.Headers.ContainsKey(PropagationKeys.LegacyBaggage));
    }

    [Fact]
    public void InjectThenExtract_RoundTripsAValueContainingACommaAndSpaces()
    {
        // Arrange (Given) — the value a conformant peer escapes as "Acme%2C%20Inc". Refusing commas in decoded
        // values rejected it on the way in and on the way out (known issue 038).
        var propagator = new W3CContextPropagator();
        var context = NewContext();
        Assert.True(context.SetBaggage("company.name", "Acme, Inc"));
        var carrier = new DictionaryPropagationCarrier();

        // Act (When)
        propagator.Inject(context, carrier);
        var extracted = propagator.Extract(carrier);

        // Assert (Then) — escaped on the wire, so the comma cannot be read as an entry separator
        Assert.Equal("company.name=Acme%2C%20Inc", carrier.Headers[PropagationKeys.Baggage]);
        Assert.Single(extracted.Baggage!);
        Assert.Equal("Acme, Inc", extracted.Baggage!["company.name"]);
    }

    [Fact]
    public void Extract_WithAnEscapedCommaFromAConformantPeer_KeepsTheEntry()
    {
        // Arrange (Given) — the inbound half of the same bug: the entry was unescaped, then rejected for holding
        // the comma the peer had correctly escaped, and silently counted as dropped
        var propagator = new W3CContextPropagator();
        var carrier = new DictionaryPropagationCarrier();
        carrier.Set(PropagationKeys.Baggage, "tenant.name=Acme%2C%20Inc");

        // Act (When)
        var extracted = propagator.Extract(carrier);

        // Assert (Then)
        Assert.Equal("Acme, Inc", extracted.Baggage!["tenant.name"]);
    }

    [Fact]
    public void Extract_IgnoresBaggageEntryProperties()
    {
        // Arrange (Given) — the spec allows ";key=value" metadata after an entry value
        var propagator = new W3CContextPropagator();
        var carrier = new DictionaryPropagationCarrier();
        carrier.Set(PropagationKeys.Baggage, "tenant.id=contoso;prop=ignored");

        // Act (When)
        var extracted = propagator.Extract(carrier);

        // Assert (Then)
        Assert.Equal("contoso", extracted.Baggage!["tenant.id"]);
    }

    [Fact]
    public void ExtractedState_FedToTheFactory_ContinuesTheSendersTrace()
    {
        // Arrange (Given) — the full hop: sender injects, receiver extracts, receiver builds its context
        using var listener = NewRecordingListener(out var sourceName);
        using var source = new ActivitySource(sourceName);
        using var sendingActivity = source.StartActivity("outbound");
        Assert.NotNull(sendingActivity);

        var propagator = new W3CContextPropagator();
        var sender = NewContextOn(sendingActivity!);
        sender.SetBaggage("tenant.id", "contoso");
        var carrier = new DictionaryPropagationCarrier();
        propagator.Inject(sender, carrier);

        var factory = new DefaultContextFactory();

        // Act (When)
        var receiver = factory.Create(propagator.Extract(carrier));

        // Assert (Then) — one trace id across the hop, and the sender's span id as the cause
        Assert.Equal(sendingActivity!.TraceId.ToHexString(), receiver.TraceId);
        Assert.Equal(sendingActivity.SpanId.ToHexString(), receiver.CausationId);
        Assert.Equal("contoso", receiver.GetBaggage("tenant.id"));
    }

    private static ActivityListener NewRecordingListener(out string sourceName)
    {
        sourceName = $"synapse-propagator-test-{Guid.NewGuid():N}";
        var name = sourceName;

        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };

        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static Context NewContext()
    {
        var identity = new ContextIdentity(ActivityTraceId.CreateRandom().ToHexString(), null,
            DateTimeOffset.UtcNow);
        return new Context(identity);
    }

    /// <summary>
    ///     A context whose trace id is the activity's, which is what <see cref="ContextIdentity.ForUnitOfWork" />
    ///     produces whenever an activity exists and the boundary did not refuse its trace context.
    /// </summary>
    private static Context NewContextOn(Activity activity)
    {
        return new Context(new ContextIdentity(activity.TraceId.ToHexString(), null, DateTimeOffset.UtcNow));
    }
}
