# ADR-0005: Resource Management (Drop + use)

**Status:** Proposed
**Date:** 2026-03-24
**Context:** Cleanup of non-memory resources (file handles, sockets, connections)
**Depends on:** ADR-0004 (memory ownership)

---

## Context

When a refcount hits zero, memory is freed. But resources beyond memory — file handles, sockets, database connections — need explicit cleanup logic. Freeing the struct's memory doesn't close the OS file descriptor.

Need a mechanism for:
1. Defining cleanup logic for a type
2. Controlling when cleanup happens
3. Not forcing cleanup on users who don't own the resource lifecycle (avoiding .NET's IDisposable warning problem)

## Decision

### `Drop` trait + opt-in `use` binding

See [Destructors / drop behaviour](../discussions/memory.md#destructors--drop-behaviour) for the full design exploration.

**`Drop` trait** defines *how* to clean up:

```fsharp
trait Drop =
    drop (self) : unit

impl Drop for FileHandle =
    drop (self) = OS.close self.fd
```

Compiler auto-generates `drop` for structs — calls `drop` on each field that implements `Drop`, then frees. User only writes `drop` for types holding external resources.

**`use` binding** controls *when* — deterministic cleanup at scope exit:

```fsharp
// bind with use — dropped at scope exit
use file = File.open "data.txt"
let contents = File.readAll file
// file dropped here, guaranteed

// standalone use — attach deferred cleanup to existing binding
let handle = File.open "hello.txt"
// ... do stuff ...
use handle    // drop at end of this scope
```

**`use` is opt-in, not enforced.** No compiler warnings for not using `use`. If you `let` bind a droppable resource and forget to clean it up, that's on you. This avoids the .NET problem where framework-owned disposable objects generate warnings in user code. See [`use` is opt-in, not enforced](../discussions/memory.md#use-is-opt-in-not-enforced) for the rationale.

### Loan pattern as idiomatic API

Standard library APIs should prefer the loan pattern — resource owner controls lifecycle, user passes a callback. See [Loan pattern over handle passing](../discussions/memory.md#loan-pattern-over-handle-passing).

```fsharp
let withReader (path: String) (f: Reader -> 'a) : 'a =
    use reader = Reader.create path
    f reader
    // reader dropped here

// user code — never touches the handle
let contents = File.withReader "data.txt" (fun reader ->
    reader.readAll ()
)
```

Library authors use `use` + `Drop`. Application developers use `with*` and never think about handles.

## Consequences

**Positive:**
- Deterministic cleanup when needed via `use`
- No forced cleanup — pragmatic, avoids IDisposable warning hell
- Loan pattern makes resource misuse structurally impossible at the API level
- `Drop` composes — structs with droppable fields auto-generate cleanup

**Negative:**
- Forgetting `use` on a raw handle = resource leak (user's responsibility)
- Two patterns to learn (loan + use), though loan is the default

## Related ADRs
- ADR-0004: Memory ownership (refcount triggers Drop on zero)
- ADR-0002: Type system (Drop as a trait)
