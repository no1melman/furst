# ADR-0006: Allocation Strategy

**Status:** Proposed
**Date:** 2026-03-24
**Context:** Stack vs heap placement, arena allocators, struct layout
**Depends on:** ADR-0004 (memory ownership)

---

## Context

Furst defaults to stack allocation with promotion to heap when needed. For specific domains (web services, file processing), arena allocators provide significant performance wins. Users need control over struct memory layout for SIMD and binary protocols.

## Decision

### Stack-first with promotion

See [Stack size limits](../discussions/memory.md#stack-size-limits) and [Can the compiler promote from stack to heap?](../discussions/memory.md#can-the-compiler-promote-from-stack-to-heap) for the full exploration.

1. **Known size at compile time** → stack, exact allocation
2. **Unknown size, no explicit tag** → start on stack with small buffer, promote to heap during construction if overflow
3. **Explicit `<heap>` tag** → heap from the start (future feature). See [Tagged bindings](../discussions/memory.md#tagged-bindings-for-explicit-placement-future-exploration).

The user's default mental model: don't think about it, the compiler handles it. Override with `new` or tagged bindings when needed.

### Allocator as ambient context

See [Allocator as ambient context](../discussions/memory.md#allocator-as-ambient-context) for the rationale against Zig-style parameter threading.

Allocators are **not** threaded as parameters. Instead, set via a compiler intrinsic attribute at function boundaries:

```fsharp
type AllocatorType =
    | Default       // mimalloc/jemalloc
    | Arena         // bump allocator, bulk free on scope exit

[<Allocator(Arena)>]
let handleRequest (req: Request) : Response =
    // all heap allocations in here use the arena
    // return value copied to caller's allocator
```

`AllocatorType` is a closed DU, compiler intrinsic. Functions called within an `[<Allocator>]` scope inherit the allocator — no parameter threading.

**Allocation metadata:**

```
┌──────────┬──────────┬────────────────┐
│ refcount │ alloc_id │ actual data... │
└──────────┴──────────┴────────────────┘
```

Refcount decrement calls `allocator.free(ptr)` — arena's free is a no-op, default's free is real.

### Arena constraints

- **No nesting.** One arena per domain boundary (per request, per file job). See [Key decisions: arenas don't nest](../discussions/memory.md#key-decisions-attributes-arenas-and-green-threads).
- **Short-lived only.** Arena refcount-free is a no-op, so dead values waste space until arena dies. Fine for request-response, wrong for long-lived connections. See [Arena and refcount interaction](../discussions/memory.md#arena-and-refcount-interaction).
- **Return copies out.** Values returned from an arena-scoped function are copied to the caller's allocator.

### Default allocator

Ship with `mimalloc` or `jemalloc` — better than system `malloc` for the small, short-lived allocation patterns functional languages produce.

### Struct layout

Compiler reorders struct fields by alignment for optimal packing by default. User never thinks about it. See [Memory layout](../discussions/memory.md#memory-layout).

Override via `Align` attribute (compiler intrinsic, closed DU). See [Layout control via Align attribute](../discussions/memory.md#layout-control-via-align-attribute).

```fsharp
type AlignMode =
    | Packed        // no padding — binary protocols
    | SIMD          // 16/32-byte alignment for vectorised ops
    | Preserve      // declaration order — FFI, matching C layouts
```

### v1 scope

- Default allocator + stack-first promotion: v1
- Arena allocator: v1 (important for web service use case)
- `Align` attribute: v1 for `Packed` and `Preserve`, `SIMD` can wait
- Tagged bindings (`<heap>`, `<stack>`): future

## Consequences

**Positive:**
- Users don't think about allocators unless they need to
- Arena per request = predictable latency, no GC pauses
- Compiler controls layout for optimal performance by default

**Negative:**
- Allocator metadata adds bytes to every heap value
- Arena wastes space for dead values (acceptable for short-lived scopes)
- Thread-local allocator swap has small overhead

## Related ADRs
- ADR-0004: Memory ownership (refcount interacts with allocator)
- ADR-0003: Green threads (arena per green thread for web requests)
