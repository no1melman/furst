# ADR-0004: Memory Ownership Model

**Status:** Proposed
**Date:** 2026-03-24
**Context:** Memory management, ownership, allocation, immutability
**Depends on:** ADR-0002 (type system), ADR-0003 (green threads)

---

## Context

Furst needs a memory management strategy that gives users control over placement (stack vs heap) without requiring manual lifecycle management. The language is immutable-first — users never see mutation. The compiler should push as much ownership reasoning as possible into compilation, avoiding both garbage collection and verbose annotations.

Design goals:
- User controls *where* memory lives (stack vs heap)
- Compiler controls *when* memory is freed
- No GC pauses, no manual `free`, no lifetime annotations
- Immutability makes sharing safe — aliasing + mutation can't happen

## Decision

### Immutable-first, refcounted ownership with reuse analysis

**Core principles:**

1. **Everything is immutable** to the user. Mutation is an optimisation the compiler performs under the hood when safe. See [Immutability by default](../discussions/memory.md#immutability-by-default-mutable-optimizations-under-the-hood).
2. **Stack by default.** Values go on the stack unless size is unknown at compile time or the user explicitly requests heap. See [Stack vs Heap](../discussions/memory.md#stack-vs-heap-in-furst).
3. **Stack-to-heap promotion.** Unknown-size values start on stack with a small buffer and promote to heap during construction if they overflow. See [Can the compiler promote from stack to heap?](../discussions/memory.md#can-the-compiler-promote-from-stack-to-heap)
4. **Heap values are refcounted.** Sharing a heap value bumps the refcount. When refcount hits zero, memory is freed. See [Immutability-first ownership](../discussions/memory.md#immutability-first-ownership-refcounting--reuse-analysis).
5. **Non-atomic refcounting.** Green threads are ownership boundaries (see below), so refcounts never cross threads. No atomic operations needed. See [Refcounting never needs to be atomic](../discussions/memory.md#refcounting-never-needs-to-be-atomic).
6. **Reuse analysis.** When the compiler detects refcount == 1 at a transformation site, it mutates in place instead of copying. See [Reuse analysis](../discussions/memory.md#reuse-analysis).
7. **`copy` keyword.** Explicit deep clone when the user wants to force independent ownership. Always deep — no shallow copies of `Ptr<T>`. See [Explicit control with copy](../discussions/memory.md#explicit-control-with-copy).

**Type system additions:**

- `Ptr<T>` — explicit pointer type for heap values, with auto-deref at access sites. See [Ptr<T> as monadic, auto-deref](../discussions/memory.md#ptrt-as-monadic-auto-deref-and-ces).
- `Slice<T>` — view into a parent array/list via offset + length, refcounts the parent
- No implicit type coercion — `Slice<T>` is not `List<T>`, explicit conversion required

**Ownership rules:**

- `let b = a` on a heap value: shares via refcount (safe because immutable)
- Function arguments: compiler infers borrow vs move from function body. `own` keyword only at trait/FFI boundaries. See [Resolving ownership details §1](../discussions/memory.md#1-does-implicit-borrowing-for-function-args-need-annotation-or-is-it-fully-inferred).
- Closures: borrow if closure stays in scope, move if it escapes. See [Resolving ownership details §3](../discussions/memory.md#3-closures-that-capture-heap-values--move-or-borrow).
- No borrowed references in struct fields (avoids lifetime annotations). Struct `Ptr<T>` fields are owned. See [Resolving ownership details §2](../discussions/memory.md#2-storing-a-ptrt-in-a-struct-field--ownership-and-movability).

### Green threads as ownership boundaries

- `green fn arg` syntax — explicit function + arguments, no closure capture. See [Green threads as ownership boundaries](../discussions/memory.md#green-threads-as-ownership-boundaries).
- Everything moved in, return moves out. No shared state across threads.
- Enables non-atomic refcounting everywhere — significant performance win over Swift's atomic ARC.
- Inter-thread communication via channels (move semantics: send = move in, receive = move out). See [Inter-thread communication: channels](../discussions/memory.md#inter-thread-communication-channels).

### Future exploration (not v1)

- `<heap>` / `<stack>` tagged bindings for explicit placement control
- `SharedMem<T>` for explicit shared mutable state with locking
- Periodic arena reset for long-lived scopes

## Consequences

**Positive:**
- Users write pure functional code that performs like imperative C++
- No lifetime annotations, no borrow checker, no GC
- Refcounting is non-atomic (thread-local only) — cheaper than Swift/ObjC
- Reuse analysis eliminates allocations the user didn't ask for
- Immutability guarantees no cycles in refcounted data

**Negative:**
- Refcount overhead on every heap value (increment/decrement per share/drop)
- Reuse analysis is complex to implement (Koka/Lean4 level compiler work)
- "Value moved" errors will occasionally surprise users
- No borrowed references in struct fields is a real constraint (no self-referential structs)

**Trade-offs:**
- Ergonomics over maximum control — users can't do everything Rust allows, but they also don't fight a borrow checker
- Refcount overhead vs GC pause — steady small cost vs occasional large cost

## Related ADRs
- ADR-0002: Type system (Ptr<T>, Slice<T> extend the type algebra)
- ADR-0003: Green threads (ownership boundary, non-atomic refcount)
- ADR-0005: Resource management (Drop + use)
- ADR-0006: Allocation strategy (stack/heap, arenas)
