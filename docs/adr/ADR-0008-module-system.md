# ADR-0008: Module System (lib + mod)

**Status:** Accepted
**Date:** 2026-03-27
**Context:** Module/namespace system for code organisation, scoping, and BCL development
**Discussion:** [modules-namespaces-bcl](../discussions/modules-namespaces-bcl.md)

---

## Context

The language supports libraries (`.a` + `.fsi` manifests) and multi-file compilation, but all exported functions live in a single flat namespace. Building a BCL requires scoped module paths like `Furst.Collections.List.map`.

## Decision

### Two keywords: `lib` and `mod`

- **`lib`** — library scoping, only used in library projects (`type: library` in yaml). Declares which lib path the subsequent mods belong to. Relative to the yaml root name.
- **`mod`** — organisational unit for functions and types. Used in both libraries and executables.

### Key rules

1. **`lib` is library-only.** Executables use `mod` for internal organisation.
2. **`mod` is universal.** Both libs and executables can declare mods.
3. **Implicit mods from filesystem.** If a file doesn't declare a `mod`, the filename becomes the mod name. Directories extend the lib path. E.g. with yaml root `Furst`, `src/collections/list.fu` → lib `Furst.Collections`, mod `List`.
4. **`lib` name from yaml + filesystem, with override.** The yaml `library: name:` gives the root. Directory structure extends the lib path. Explicit `lib` in source overrides the filesystem-derived lib path. Filename always gives the implicit mod name (unless overridden by explicit `mod`).
5. **Libs are flat in files.** All `lib` declarations are relative to the yaml root, independently. No nesting of libs within a file.
6. **Mods are flat.** No nested mod declarations. Dotted mod names (e.g. `mod Api.Types`) are structural — the compiler builds a hierarchy behind the scenes.
7. **Libs roll up.** `lib Collections.Generic` doesn't require `lib Collections` to exist. They're independent paths that share a prefix.
8. **Everything is additive.** Multiple files can contribute to the same mod or lib path. Even across compiled library boundaries.
9. **No shadowing.** Duplicate symbol at the same path is a compile-time error. Language-wide rule.
10. **Declaration order matters.** F#-style: a symbol must be declared before use. File order in `sources:` matters.
11. **`open` is shallow.** `open Furst.Collections` brings in direct mods under that path, not sub-paths like `Collections.Generic`.

### Visibility

- **Public by default.** Everything in a mod is visible to consumers.
- **`private` scoped to mod.** `private let helper = ...` is not visible outside the mod.

### Types

- Types can live in mods, same as functions, same visibility rules.
- No special hoisting or lib-level type scope.

### Entry point

- Every file gets an implicit mod, including `main.fu` → `mod Main`.
- `main` is just a regular function. The compiler looks for a function named `main` in the last file of the source ordering.
- Entry point projects must have exactly one `main` function in their last source file.

### Module extensions (deferred)

- `extend mod Furst.Collections.List` allows cross-package augmentation.
- Extensions only visible when the extending library is `open`ed.
- Detail design deferred to a future ADR.

## Examples

```furst
// Library: furst.yaml has library: name: Furst.Collections
// src/list.fu → implicit: lib Furst.Collections, mod List

let map f xs = ...
let filter f xs = ...
private let partition_impl xs = ...

type Node<'a> =
    | Cons of 'a * Node<'a>
    | Nil
```

```furst
// Consumer
open Furst.Collections

let result = List.map f xs
let node = List.Node.Cons (1, List.Node.Nil)
```

```furst
// Executable: src/main.fu → implicit mod Main
let main = 0
```

## Consequences

**Positive:**
- Clean two-level scoping without namespace keyword
- Filesystem convention minimises boilerplate
- Additive merging supports large codebases and BCL organisation
- No-shadowing rule prevents subtle bugs
- Consistent rules — types, functions, visibility all work the same way

**Negative:**
- Implicit mod from filesystem adds a convention to learn
- Additive merging across projects requires careful manifest design
- `lib` override in source can diverge from filesystem — potential confusion

**Future work:**
- Module extensions (`extend mod`) — cross-package augmentation
- Nested mods via `=` syntax (if needed)
- `internal` visibility level (package-private)
