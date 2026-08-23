### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
SYNE001 | Synapse.Endpoints | Error | Route parameter has no matching property
SYNE005 | Synapse.Endpoints | Error | Streaming message used with a non-streaming endpoint
SYNE006 | Synapse.Endpoints | Error | InGroup type does not derive from EndpointGroup
SYNE009 | Synapse.Endpoints | Error | Route declared both by attribute and in Configure
SYNE010 | Synapse.Endpoints | Error | Endpoint has a shape that cannot be mapped
SYNE002 | Synapse.Endpoints | Error | Multiple properties bind the same input
SYNE007 | Synapse.Endpoints | Warning | Body-bound property on a bodyless verb
SYNE011 | Synapse.Endpoints | Error | Bound property cannot be assigned
SYNE012 | Synapse.Endpoints | Error | Bound property type cannot be parsed from a string
SYNE013 | Synapse.Endpoints | Warning | Message type bound by endpoints with conflicting binding shapes
SYNE003 | Synapse.Endpoints | Info | POST/PUT endpoint declares no explicit success mapping
SYNE004 | Synapse.Endpoints | Warning | OnSuccess override conflicts with a declarative success method
SYNE008 | Synapse.Endpoints | Warning | Type used by an endpoint is missing from every JsonSerializerContext
