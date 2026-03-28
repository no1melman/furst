# ADR-0007: String and Collection Types

**Status:** Proposed
**Date:** 2026-03-24
**Context:** String representation, array/list/slice types, type coercion rules
**Depends on:** ADR-0004 (memory ownership), ADR-0002 (type system)

---

## Context

Furst needs string and collection types that fit the immutable-first, refcounted ownership model. Rust's proliferation of string types (`String`, `&str`, `&'static str`, `CString`, `OsString`, `Box<str>`) is a direct consequence of explicit ownership in the type system. Furst's refcounting eliminates the owned/borrowed split.

Core principle: **no implicit type coercion.** Program correctness is paramount. Types are what they say they are.

## Decision

### String

**One type: `String`.** UTF-8 always. No separate borrowed/owned variants. See [String representation](../discussions/memory.md#string-representation) for the comparison with Rust's six string types.

- **String literals** are baked into the binary (static data), zero allocation, but still type `String`
- **Small string optimisation (SSO):** strings ≤ 23 bytes stored inline, no heap allocation. Invisible to user.
- **Long strings** heap-allocate, follow normal refcount rules
- **String interpolation** is a language feature: `"Hello ${name}, count is ${count:toString}"`
  - `:` pipes to a formatter for non-string types (`${dob:toIso}`, `${count:toString}`)
- **UTF-16** available as standard library if needed, not a language type

### Fixed-size arrays

Stack-allocated, size is part of the type. Value semantics — copy on assignment.

```fsharp
let rgb = [255, 128, 0]    // i32[3]
```

`i32[3]` is a different type from `i32[4]`. Same as C arrays.

### Dynamic lists

`List<T>` — runtime-sized, refcounted, follows all ownership rules. Growable buffer (pointer + length + capacity).

### Slices

`Slice<T>` — its own type, not interchangeable with `List<T>`. View into a parent array/list. See [Array and slice types](../discussions/memory.md#array-and-slice-types) for the design and the .NET `Span<T>` inspiration:

- Refcounts the **original parent**, not intermediate slices
- Slice of a slice tightens offset/length on the same parent reference
- Converting to a list is always an explicit copy: `List.ofSlice slice`

```fsharp
let items = [1, 2, 3, 4, 5]
let middle = items[1..3]            // Slice<i32>
let inner = middle[0..1]            // Slice<i32>, still refcounts items
let owned = List.ofSlice inner      // explicit copy → List<i32>
```

Inspired by .NET `Span<T>`.

### No implicit coercion

This applies to the whole language, not just collections:

```fsharp
let a: String = 2                   // compile error
let b: i32 = "1"                    // compile error
let c: List<i32> = items[1..3]      // compile error — Slice is not List

let a: String = String.ofInt 2      // explicit
let b: i32 = Int.parse "1"          // explicit
let c: List<i32> = List.ofSlice s   // explicit
```

## Consequences

**Positive:**
- One string type — simple mental model, no Rust-style proliferation
- SSO eliminates heap allocation for most identifiers and short strings
- Slices enable zero-copy views with safe refcounted lifetime
- No implicit coercion catches type bugs at compile time

**Negative:**
- No implicit int-to-string etc. means more verbose formatting
- `Slice<T>` is a new concept to learn (though familiar from .NET Span)
- SSO adds complexity to the string implementation (but invisible to users)

## Related ADRs
- ADR-0004: Memory ownership (refcounting for strings, slices)
- ADR-0002: Type system (String, List<T>, Slice<T> as types)
