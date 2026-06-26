// This library intentionally does NOT opt into CQRS boundary enforcement. Enforcement is opted in once at the
// composition root (examples/MinimalApi, via [assembly: EnableSynapseCqrsBoundaryEnforcement]); that assembly's
// generator discovers this library's request handlers across the assembly reference and emits the closed CQRS
// registrations for them. Re-adding [assembly: EnableSynapseCqrsBoundaryEnforcement] here would still be safe —
// duplicate CQRS registrations are deduplicated at the service-collection level — but is unnecessary.
