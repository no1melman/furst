# Backend Epics

> **Rule:** Mark a feature `[x]` only after the user confirms they're happy with it. Never auto-check.

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

## Epic 6: Type System & Operators as Functions

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
- [ ] 6.11 Tests — type inference roundtrips, operator desugaring, mixed-type errors

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

## furst.yaml Schema

```yaml
name: myapi
version: 0.1.0
type: executable         # or library
entry: src/main.fu

targets:
  - arch: x86_64
    os: linux

dependencies:
  - path: ../libs/shared  # project reference (later)
```
