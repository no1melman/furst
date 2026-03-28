# Epics

> **Rule:** Mark a feature `[x]` only after the user confirms they're happy with it. Never auto-check.
> **Rule:** Ensure after a task is checked off the relevant ADR coverage table is updated as well.

## ADR Coverage

| ADR | Title | Status | Covered by | % Done |
|-----|-------|--------|-----------|--------|
| 0001 | Symbol tracking | Proposed | 5b.7, 5b.8, 5b.10 | 100% |
| 0002 | Type system & inference | Proposed | 6.1–6.3, 6.11–6.13, 11.1 | 0% |
| 0003 | Async/green threads | Proposed | 12.1 | 0% |
| 0004 | Memory ownership | Proposed | 8.1 | 0% |
| 0005 | Resource management | Proposed | 9.1 | 0% |
| 0006 | Allocation strategy | Proposed | 8.1 | 0% |
| 0007 | String/collection types | Proposed | 10.1 | 0% |
| 0008 | Module system | Accepted | 5b.1–5b.16 | 88% |

## Epic 1: Build Infrastructure

- [x] 1.1 CMakeLists.txt — C++23, find_package, format/lint targets, GTest discovery
- [x] 1.2 flake.nix — multi-shell (frontend, backend, default)
- [x] 1.3 .clang-format config
- [x] 1.4 .clang-tidy config
- [x] 1.5 Skeleton src/ and tests/ — backend.h/.cpp stub, smoke GTest, prove full build works

## Epic 2: .fso Reader

- [x] 2.1 Error types — structured error enum used across reader and emitter
- [x] 2.2 Internal C++ types — structs mirroring proto (FunctionDef, Expression, etc.)
- [x] 2.3 FsoReader — read .fso, validate 8-byte header, deserialize, map to internal types
- [x] 2.4 Fixture .fso files — generated from real Furst source via frontend tests
- [x] 2.5 Tests — roundtrip .fso → internal types, invalid file/header/proto errors

## Epic 3: LLVM IR Emitter

- [x] 3.1 Module + function scaffolding — emit empty `main` returning 0, verify valid IR
- [x] 3.2 Integer literals + arithmetic — i32 constants, add/subtract/multiply, identifier lookup
- [x] 3.3 Variable bindings — `let x = expr` as alloca/store/load
- [x] 3.4 Function definitions — typed params, return type, body
- [x] 3.5 Function calls — direct calls with args
- [x] 3.6 Lambda-lifted functions — `outer$inner` naming, captured params as extra args (frontend call rewrite fixed)
- [x] 3.7 Tests — 28 tests, fixture .fso → emitted IR, tested as each feature landed

## Epic 4: Compilation Pipeline

- [x] 4.1 Wire `compile()` entrypoint — .fso → LLVM module → output .ll
- [x] 4.2 Object code emission — TargetMachine::emit, in-process, configurable target triple
- [x] 4.3 Linking — .o → executable via system linker (cc)
- [x] 4.4 CompileOptions — output format (.ll/.o/exe), optimization level (O0-O3)
- [x] 4.5 Integration test — .fso → executable → run → check exit code (42!)
- [x] 4.6 Debug info — DICompileUnit, DISubprogram, DILocation on instructions for gdb/lldb source mapping
- [x] 4.7 Hook into frontend `build` command — `furstc` CLI + `furstc-backend` in build/

## Epic 5: Project System

- [x] 5.1 `furst new -n MyApi -o ./myapi` — scaffold project dir with `furst.yaml`, `src/main.fu`, `.gitignore`
- [x] 5.2 `furst.yaml` schema — name, version, type (executable/library), entry, targets
- [x] 5.3 YAML parser in frontend — add YamlDotNet, parse `furst.yaml` into project config
- [x] 5.4 `furst build` without args — find nearest `furst.yaml`, build the project
- [x] 5.5 Target triple construction — yaml `arch`+`os` → triple string, pass to backend
- [x] 5.6 Output conventions — `bin/` for final output, `build/` for intermediates (.fso, .o, .ll)
- [x] 5.7 `furst run` — build + execute, pass through command-line args
- [x] 5.8 Library projects — `type: library` produces `.a` via `ar`, `.fsi` manifest, `export` keyword
- [x] 5.9 Project references — `dependencies:` in yaml, manifest-based `declare`, linked against `.a`
- [x] 5.10 Multi-file compilation — `sources:` list in yaml, merged in order, single compilation unit
- [x] 5.11 Workspace support — `furst-workspace.yaml` listing projects, builds all in order

## Epic 5b: Module System

> ADR: [ADR-0008](docs/adr/ADR-0008-module-system.md), [ADR-0001](docs/adr/ADR-0001-symbol-tracking.md) | Discussion: [modules-namespaces-bcl](docs/discussions/modules-namespaces-bcl.md)
>
> ADR-0001 (symbol tracking) is implemented by the scoped symbol table (5b.7–5b.10) — two-pass collection + validation approach.
> ADR-0008 (module system) is the primary driver for all tasks in this epic.

- [x] 5b.1 `mod` keyword parsing — parse `mod Name` blocks with indentation-scoped body, dotted names (e.g. `mod Api.Types`) build hierarchy structurally
- [x] 5b.2 Implicit mod from filesystem — filename becomes mod name, directories extend lib path (e.g. `src/collections/list.fu` with yaml root `Furst` → lib `Furst.Collections`, mod `List`)
- [x] 5b.3 `lib` keyword parsing — parse `lib Path` in library projects only (compile error in executables), relative to yaml root name, overrides filesystem-derived lib path
- [x] 5b.4 Yaml schema update — add `library: name:` field for root lib name (library projects only). Top-level `name:` remains as project name for all project types
- [x] 5b.5 Visibility flip — remove `export` keyword, switch to public-by-default. Remove `ExportedFuncDef`/`TopExportedFunction` from AST and lowering, update parser and all tests
- [x] 5b.6 `private` keyword — private-to-mod scoping for functions and types, replaces `export` as the visibility modifier
- [x] 5b.7 Scoped symbol table — symbols keyed by fully qualified path (`lib.mod.name` for libraries, `mod.name` for executables), enforced declaration order (F#-style)
- [x] 5b.8 No-shadowing enforcement — compile error on duplicate symbol at same fully qualified path, language-wide
- [x] 5b.9 `open` keyword — brings direct mods under specified lib path into scope, shallow only (not sub-paths)
- [x] 5b.10 Qualified access — resolve `List.map`, `Furst.Collections.List.map` etc. through scoped symbol table
- [x] 5b.11 Additive mod merging — multiple files contribute to same mod; second file must use explicit `mod` (filesystem convention would give wrong name)
- [x] 5b.12 Proto/fso update — add module path and `is_private` visibility flag to .fso format (replaces lost export status)
- [x] 5b.13 Manifest update — `.fsi` carries fully qualified `lib.mod.function` paths, not just flat function names
- [x] 5b.14 Backend module-aware codegen — namespaced symbol names in LLVM IR
- [ ] 5b.15 Entry point convention — compiler finds function named `main` in last source file's implicit mod, executable projects only
- [ ] 5b.16 Tests — mod scoping, lib paths, open resolution, qualified access, shadowing errors, implicit mods, private visibility, additive merging, entry point

Deferred:
- `extend mod` for cross-package module augmentation
- Nested mods via `=` syntax
- `internal` visibility (package-private)

## Epic 6: Type System & Operators as Functions

> ADR: [ADR-0002](docs/adr/ADR-0002-type-system.md) (phases 1–4: primitives, inference, unification, typed AST)
>
> ADR-0002 phases 5–7 (sum types, pattern matching, generics) deferred to Epic 11.

- [ ] 6.1 Hindley-Milner type inference — Algorithm W in the frontend, infer types for all expressions
- [ ] 6.2 Type unification — unify type variables, detect mismatches, produce clear errors with source locations
- [ ] 6.3 Typed AST — replace `Inferred` with concrete types after inference, flow through to .fso
- [ ] 6.4 Operators as infix functions — `+` desugars to `add(a, b)`, `-` to `subtract(a, b)`, `*` to `multiply(a, b)`
- [ ] 6.5 Builtin operator functions — compiler-provided `add`, `subtract`, `multiply` with type-aware overloads (i32, i64, float, double)
- [ ] 6.6 Operator definition — `let (+) a b = ...` syntax for user-defined operators
- [ ] 6.7 Operator resolution — `a + b` in source resolves to matching operator function definition
- [ ] 6.8 Precedence and associativity — builtin precedence table, user operators default to lowest precedence
- [ ] 6.9 Backend type-aware codegen — emit `fadd`/`fsub` for floats, `add`/`sub` for ints based on inferred types
- [ ] 6.10 Type errors in backend — reject type mismatches with source locations from .fso
- [ ] 6.11 Struct type registration — register struct types in type environment, fields as typed records
- [ ] 6.12 Struct construction expression — `Point { x = 1, y = 2 }` syntax, type-checked against definition
- [ ] 6.13 Field access expression — `p.x` syntax, resolves to field index + type
- [ ] 6.14 Backend struct codegen — bare minimum: LLVM named struct types, stack alloca, GEP for construction and field access. No heap/arena — just enough to prove structs work before memory model ADR
- [ ] 6.15 Tests — type inference roundtrips, operator desugaring, mixed-type errors, struct construction and field access

Unresolved questions:
- prelude module for builtins or hardcoded in compiler?
- type classes / traits for overloading, or function overloading by type?
- user-declarable precedence (`infixl 6 +`) or fixed table?
- what operator symbols allowed? just math, or custom like `<|>`, `>>=`?

## Epic 7: Type Contracts & Polymorphic Operators

- [ ] 7.1 Design syntax for type contracts (not "class" — explore: trait, protocol, contract, shape)
- [ ] 7.2 Design syntax for implementing contracts on types (explore: impl, extend, for, instance)
- [ ] 7.3 Standard contracts — Functor (<$>/map), Applicative (<*>), Monad (>>=)
- [ ] 7.4 Compiler resolves contract implementations at compile time (zero runtime cost)
- [ ] 7.5 Allow adding contracts to existing/third-party types after the fact
- [ ] 7.6 Operator symbols — any combination of symbols allowed, standard set predefined
- [ ] 7.7 Tests — contract resolution, operator dispatch, type errors

Unresolved questions:
- keyword for defining a contract? (trait, protocol, contract, shape, ...)
- keyword for implementing? (impl, extend, for, ...)
- should contracts support default implementations?
- associated types?

## Epic 8: Memory Model & Allocation

> ADR: [ADR-0004](docs/adr/ADR-0004-memory-ownership.md), [ADR-0006](docs/adr/ADR-0006-allocation-strategy.md)
>
> ADR-0004 defines immutable-first ownership, refcounting, reuse analysis, Ptr<T>, Slice<T>.
> ADR-0006 defines stack-first placement, heap promotion, arena allocators, struct layout.
> Both depend on a working type system (Epic 6) — the compiler needs concrete types to decide placement.
> Epic 6.14 (struct codegen) deliberately stops at stack alloca; this epic picks up from there.

- [ ] 8.1 Refine tasks from ADR-0004 and ADR-0006 — break down ownership semantics, allocation strategy, reuse analysis, Ptr<T>/Slice<T> into implementable tasks

## Epic 9: Resource Management

> ADR: [ADR-0005](docs/adr/ADR-0005-resource-management.md)
>
> ADR-0005 defines Drop trait, `use` binding, auto-generated drop for structs, loan pattern.
> Depends on Epic 8 (memory model) — Drop triggers on refcount zero, needs ownership semantics in place.
> Depends on Epic 7 (type contracts) — Drop is itself a contract/trait.

- [ ] 9.1 Refine tasks from ADR-0005 — break down Drop trait, `use` binding, auto-drop generation, loan pattern into implementable tasks

## Epic 10: Strings & Collection Types

> ADR: [ADR-0007](docs/adr/ADR-0007-string-collection-types.md)
>
> ADR-0007 defines String (UTF-8, SSO), fixed arrays, List<T>, Slice<T>, no implicit coercion.
> Depends on Epic 8 (memory model) — SSO and growable buffers need allocation strategy.
> Depends on Epic 6 (type system) — generic types like List<T> need type parameters.

- [ ] 10.1 Refine tasks from ADR-0007 — break down String type, SSO, fixed arrays, List<T>, Slice<T>, explicit conversions into implementable tasks

## Epic 11: Algebraic Types & Pattern Matching

> ADR: [ADR-0002](docs/adr/ADR-0002-type-system.md) (phases 5–7: sum types, discriminated unions, pattern matching)
>
> ADR-0002 phases 5–7 cover sum types (`type Option<T> = Some T | None`), tuples, and pattern matching.
> Deferred from Epic 6 because sum types need the memory model (Epic 8) to decide tag+payload layout,
> and pattern matching benefits from having strings and collections (Epic 10) to match against.

- [ ] 11.1 Refine tasks from ADR-0002 phases 5–7 — break down sum types, tuples, discriminated unions, pattern matching, exhaustiveness checking into implementable tasks

## Epic 12: Async Runtime & Green Threads

> ADR: [ADR-0003](docs/adr/ADR-0003-async-task-scheduler.md)
>
> ADR-0003 defines M:N green thread scheduler, spawn keyword, yield points, user-space stacks, I/O integration.
> Most independent epic — no hard dependency on type system or memory model, but benefits from both.
> Placed last because the language is usable without async; the runtime is a large standalone effort.

- [ ] 12.1 Refine tasks from ADR-0003 — break down runtime library, scheduler, spawn keyword, yield insertion, context switching, I/O integration into implementable tasks

## Future Features (to discuss)

- **User-defined infix operators** — `let (|>) = pipeForward` pattern, pipe-forward as stdlib not syntax
- **Match expressions** — pattern matching on values, destructuring
- **String literals** — parsing, type inference, emit

## Project Structure Convention

```
myproject/
├── furst.yaml
├── src/
│   └── main.fu          # entry point (executables)
├── bin/                  # final output (executable or .a)
└── build/                # intermediates (.fso, .o, .ll)
```

## Workspace Structure

```
mycompany/
├── furst-workspace.yaml
├── services/
│   ├── api/
│   │   ├── furst.yaml   # type: executable
│   │   └── src/main.fu
│   └── worker/
│       ├── furst.yaml   # type: executable
│       └── src/main.fu
└── libs/
    └── shared/
        ├── furst.yaml   # type: library
        └── src/lib.fu
```

## furst.yaml Schema

```yaml
# Executable project
name: myapi              # project name (all project types)
version: 0.1.0
type: executable
entry: src/main.fu

targets:
  - arch: x86_64
    os: linux

dependencies:
  - path: ../libs/shared  # project reference
```

```yaml
# Library project
name: furst-collections  # project name (used locally, e.g. output filenames)
version: 0.1.0
type: library

library:
  name: Furst.Collections  # fully qualified lib root (what consumers see/open)

sources:
  - src/list.fu           # → Furst.Collections.List (implicit)
  - src/map.fu            # → Furst.Collections.Map (implicit)

targets:
  - arch: x86_64
    os: linux
```
