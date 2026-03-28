# ADR-0003: Async Runtime and Task Scheduler

**Status:** Proposed
**Date:** 2026-03-23
**Context:** Concurrency model for Furst — how async/await and parallel execution should work at the LLVM level

---

## Context

Furst compiles to native code via LLVM IR. Unlike .NET/JVM, there is no managed runtime providing a thread pool or task scheduler. LLVM is a code generator — it emits machine instructions but provides no concurrency runtime. Everything above "call a function on the current thread" must be built by us.

### The execution stack

```
Furst tasks/coroutines       (potentially millions, cheap)
        ↓ scheduled onto
OS threads                   (kernel-managed, ~1MB stack each)
        ↓ kernel timeslices onto
Hardware threads              (fixed: cores × hyperthreads, e.g. 8C/16T)
```

We control the first mapping. The OS controls the second. OS threads always outnumber hardware threads on any real system — the kernel timeslices thousands of threads (across all processes) onto a fixed number of cores. Creating more OS threads than cores just increases context-switch overhead without gaining parallelism.

### What LLVM provides

| Primitive | What it is |
|---|---|
| **Function calls** | `call @pthread_create(...)` — OS thread creation is just a C call |
| **Coroutine intrinsics** | `@llvm.coro.suspend`, `@llvm.coro.resume` — compiler splits a function into a state machine |
| **Atomics** | `atomicrmw`, `cmpxchg`, `fence` — hardware-level atomic operations for lock-free data structures |

No scheduler, no thread pool, no I/O multiplexing. We must ship a runtime library.

---

## Decision Options

### Option 1: LLVM Coroutine Intrinsics (Rust/C++20 style)

**Approach:** Compiler transforms `async` functions using LLVM's coro intrinsics. Each `await` becomes a suspend point; LLVM splits the function into a state machine. A small runtime provides the scheduler.

**Compiler changes:**
- New AST nodes: `AsyncDef`, `Await`, `Spawn`
- Lowering pass emits `@llvm.coro.begin`, `@llvm.coro.suspend`, `@llvm.coro.end`
- Coroutine frame allocation (heap or stack)

**Runtime:**
- Thread pool (N ≈ hardware thread count)
- Work queue + I/O poller (epoll/IOCP)
- `@llvm.coro.resume` called by worker threads when I/O completes

**Pros:**
- Zero-cost abstractions — suspended coroutine is just a struct on the heap
- State machine is optimized by LLVM's passes
- No hidden stack allocation per coroutine

**Cons:**
- Complex compiler transforms
- LLVM coro API is finicky, poorly documented
- Tight coupling to LLVM version
- Colored function problem (async infects call chains)

### Option 2: CPS (Continuation-Passing Style) Transform

**Approach:** Compiler rewrites `async` functions into chains of closures. Each `await` becomes "pass a callback to the I/O operation." No LLVM-specific intrinsics needed.

**Compiler changes:**
- Desugar `let! x = foo()` into `foo(fun x -> rest_of_function)`
- Reuses existing lambda lifting infrastructure

**Runtime:**
- Same thread pool + I/O poller as Option 1
- Callbacks are just function pointers

**Pros:**
- No dependency on LLVM coro intrinsics
- Reuses lambda lifting (already implemented)
- Portable to any backend

**Cons:**
- Heap allocation per continuation (closure + captured variables)
- Deep callback chains hard to debug
- Stack traces are meaningless
- Harder to optimize than state machines

### Option 3: Green Threads (Go style)

**Approach:** Each spawned task gets a small user-space stack (~4KB, growable). The runtime cooperatively (or preemptively) switches between green threads by swapping stack pointers. No function coloring — any function can be suspended.

**Compiler changes:**
- Minimal: `spawn` keyword lowers to a runtime call
- Compiler inserts yield points (e.g. at function calls or loop back-edges) for preemptive scheduling
- No async/await keywords needed — all code is implicitly suspendable

**Runtime:**
- Thread pool of N OS threads (N ≈ core count)
- M:N scheduler: M green threads multiplexed onto N OS threads
- Each green thread has its own small stack (mmap'd, guard page, growable)
- Context switch = save/restore registers + swap stack pointer (assembly, per-arch)
- I/O operations yield to scheduler, resume when ready (epoll/IOCP integration)
- Work-stealing between OS threads for load balancing

**Pros:**
- No colored function problem — every function is "async" for free
- Simpler mental model for users (looks like blocking code, runs concurrently)
- Minimal compiler changes (just insert yield points + `spawn` lowering)
- Proven at scale (Go, Erlang, early Java)

**Cons:**
- Runtime is more complex (stack management, context switching)
- Per-architecture assembly for stack switching (x86_64, aarch64 minimum)
- Small stacks can overflow if not growable (segmented/copyable stacks add complexity)
- FFI requires pinning to OS thread (C code doesn't expect stack to move)
- Harder to reason about memory layout than state machines

### Option 4: OS Threads Only (no runtime)

**Approach:** Just expose `pthread_create`/`CreateThread` as Furst primitives. No green threads, no coroutines. Users manage threads manually.

**Pros:**
- Zero runtime complexity
- Trivial to implement

**Cons:**
- 1 OS thread per concurrent task — doesn't scale past ~10K
- 1MB stack per thread — memory hungry
- No lightweight concurrency story
- Not competitive with any modern language

---

## Decision

**Option 3: Green Threads.**

### Rationale

1. **No colored functions.** F#'s `async { let! ... }` computation expression style works but splits the world into sync/async. Green threads let all code suspend transparently — closer to Go's model, simpler for users.

2. **Minimal compiler changes.** The compiler just needs to: emit `spawn` as a runtime call, and insert yield points at function prologues or loop back-edges. No CPS transform, no coro intrinsics. The heavy lifting is in the runtime, not the compiler.

3. **Proven model.** Go runs millions of goroutines on a handful of OS threads. The M:N scheduling model is well-understood and battle-tested.

4. **FFI story is manageable.** Pin green thread to OS thread during FFI calls. Go does this (runtime.LockOSThread).

### Architecture

```
┌─────────────────────────────────────┐
│          Furst Program              │
│  spawn taskA        spawn taskB     │
│    ↓                    ↓           │
│  [green thread]    [green thread]   │  ← user-space stacks (~4KB each)
└────────────┬──────────┬─────────────┘
             ↓          ↓
┌─────────────────────────────────────┐
│        Furst Runtime (C/C++)        │
│  ┌──────────┐  ┌──────────────────┐ │
│  │ Scheduler │  │ I/O Poller       │ │
│  │ (M:N)    │  │ (epoll/IOCP)     │ │
│  └──────────┘  └──────────────────┘ │
│  ┌──────────────────────────────────┐│
│  │ OS Thread Pool (N ≈ core count) ││
│  │  [worker 0] [worker 1] ... [N-1]││
│  └──────────────────────────────────┘│
└─────────────────────────────────────┘
             ↓
┌─────────────────────────────────────┐
│     Kernel Scheduler                │
│     Hardware Threads (fixed)        │
└─────────────────────────────────────┘
```

### Implementation plan (high level)

1. **Runtime library in C** — thread pool, work-stealing deques, context switch (asm), stack allocator
2. **Yield point insertion** — compiler pass adds `call @furst_runtime_yield()` at function entries / loop back-edges
3. **`spawn` keyword** — lowers to `call @furst_runtime_spawn(fn_ptr, args_ptr)`
4. **Channel primitives** — `chan<T>`, send/recv as runtime calls that yield when blocked
5. **I/O integration** — wrap syscalls to yield to scheduler instead of blocking OS thread

---

## Consequences

**Positive:**
- Users write straight-line code that "just works" concurrently
- Scales to millions of concurrent tasks on a handful of OS threads
- Compiler stays simple — runtime bears the complexity

**Negative:**
- Must write and maintain per-platform context switch assembly (x86_64, aarch64)
- Stack growth strategy needed (guard pages + mmap, or segmented stacks)
- FFI calls need OS thread pinning
- Debugging green threads is harder than OS threads (debuggers see N OS threads, not M green threads)
- Runtime must ship with every Furst binary (or be statically linked)
