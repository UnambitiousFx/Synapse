using System.Diagnostics;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Tests.Definitions;

[DebuggerDisplay("{Name}")] public sealed record EventExample(string Name) : IEvent;