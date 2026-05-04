# Authorization And Handshake State Machines

Archived implementation note for the `v6.2.0` authorization and handshake work.

## Scope That Was Delivered

This branch implements the minimal feature set needed to validate the concept:

- optional server-side `AuthorizeConnectionAsync` admission callback
- optional framed pre-registration handshake/state-machine callbacks on both server and client
- explicit control-plane statuses, events, and exceptions for rejection and handshake failure
- pending-connection tracking so `ListClients()` only exposes fully admitted clients
- Touchstone-based shared automated coverage surfaced through CLI, xUnit, and NUnit hosts

## Validation Goal

The goal was not exhaustive authentication coverage.

The goal was to prove that WatsonTcp can:

1. accept a TCP connection
2. hold it in a pending pre-registration state
3. run application-defined authorization logic
4. run an application-defined framed state machine
5. either reject cleanly or promote the client to a normal connected WatsonTcp session

## Shared Automated Coverage

The shared `Test.Shared` suite validates:

- authorization allow
- authorization reject
- authorization timeout reject
- authorization exception reject
- static API key handshake success
- static API key handshake failure
- multi-step challenge/response handshake success
- multi-step challenge/response handshake failure
- missing client handshake callback
- server-side handshake timeout
- server-side handshake exception
- client-declared handshake failure
- cleanup of pending state after failures

These scenarios run through:

- `Test.Automated`
- `Test.XUnit`
- `Test.Nunit`

## What This Archive Means

This archive records that the concept works and is tested at a practical regression level.

It does **not** claim:

- an exhaustive authentication matrix
- every possible sample state machine
- certificate-policy or multi-tenant negotiation examples
- fuzzing or malformed-frame hardening coverage

Those would be follow-up work if needed, not part of the minimal concept validation target.

## Verification Commands

The feature and shared test hosts were verified with:

```bash
dotnet build src/WatsonTcp.sln -c Debug
dotnet run --project src/Test.Automated/Test.Automated.csproj --framework net8.0 -- --results cli-results.json
dotnet test src/Test.XUnit/Test.XUnit.csproj -c Debug --framework net8.0
dotnet test src/Test.Nunit/Test.Nunit.csproj -c Debug --framework net8.0
```

## Conclusion

The concept is validated:

- a connection can be authorized before activation
- a framed multi-step state machine can be used during that authorization window
- successful handshakes transition into normal WatsonTcp messaging
- failed handshakes are rejected without exposing the client through `ListClients()`
