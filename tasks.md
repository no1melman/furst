# Furst Tasks

## Done — Frontend (prior sessions)
- [x] Basic token types and parser (BasicTypes, CommonParsers)
- [x] Two-phase row parser with indentation nesting (TestTwoPhase)
- [x] Position tracking on all tokens (Line, Column, TokenLength)
- [x] AST expression types (LanguageExpressions)
- [x] Row → ExpressionNode builders (rowToExpression, buildFunctionDefinition, etc.)
- [x] Result-based error handling with source positions
- [x] AstBuilderTests: variable bindings, functions, binary ops, function calls, errors
- [x] CLI (`Cli.fs`) with `help`, `lex`, `ast`, `check`, `build`, `run`, `new` commands
- [x] Pretty-printed AST tree output (indented, human-readable)
- [x] Lowering pass with lambda lifting and name mangling
- [x] .fso protobuf serialization (FsoWriter)
- [x] Frontend call-name rewriting for lambda-lifted functions

## Done — Backend (this session)
- [x] Epic 1: Build infrastructure (CMake, flake.nix, clang-format, clang-tidy, skeleton)
- [x] Epic 2: .fso reader (error types, internal C++ types, protobuf deserialization, fixtures)
- [x] Epic 3: LLVM IR emitter (literals, arithmetic, let bindings, functions, calls, lambda lifting)
- [x] Epic 4: Compilation pipeline (.ll/.o/exe output, optimization O0-O3, debug info, linker, furstc-backend CLI)
- [x] Epic 5: Project system (furst new, furst.yaml, furst build/run, library .a + .fsi manifest, export keyword, dependencies, multi-file, workspaces)

## Done — Type System (started this session)
- [x] TypeInference.fs — Algorithm W implementation (type vars, unification, occurs check, inference)
- [x] Wired into lowering pipeline — inferred types flow through to .fso

## Done — Epic 6 (this session)
- [x] Epic 6: Type System & Operators
  - [x] 6.1 Hindley-Milner type inference — Algorithm W implemented, wired into lowering
  - [x] 6.2 Type unification — unify, occurs check, substitution composition
  - [x] 6.3 Typed AST — inferTypes builds type map, applyInferredTypes rewrites lowered defs
  - [x] 6.4 Operator definition — operators stay as AST nodes, not desugared to function calls
  - [x] 6.5 Precedence and associativity
  - [x] 6.6 Backend type-aware codegen — fadd/fsub/fmul for floats, typed manifests, typed debug info
  - [x] 6.7 Backend type ICEs with source locations — binary op, call arity, call arg type, return type
  - [x] 6.8 End-to-end type system tests — typed/inferred/mixed params, let bindings, double arithmetic, entry point i32 constraint

## Next
- [ ] Epic 7: Type Contracts & Polymorphic Operators (trait/protocol system)
  - Needed before Epic 8 (Drop trait) and Epic 9 (Copyable trait)
- [ ] Epic 8: Memory Ownership & Refcounting (ADR-0004)
  - [ ] 8.1 `Ptr<T>` type in type system — explicit pointer type with auto-deref
  - [ ] 8.2 Refcount infrastructure — refcount field on heap values, increment/decrement, free at zero
  - [ ] 8.3 Move semantics — `let b = a` on `Ptr<T>` shares via refcount, compiler tracks liveness
  - [ ] 8.4 Ownership analysis pass — infer borrow vs move for function args from body analysis
  - [ ] 8.5 `copy` keyword — explicit deep clone, auto-derived for structs with all-copyable fields
  - [ ] 8.6 Stack-to-heap promotion — small buffer optimisation for unknown-size values
  - [ ] 8.7 Reuse analysis — detect refcount==1 at transformation sites, mutate in place
- [ ] Epic 9: Resource Management (ADR-0005)
  - [ ] 9.1 `Drop` trait — user-defined cleanup logic, compiler auto-generates for structs
  - [ ] 9.2 `use` binding — deterministic cleanup at scope exit
  - [ ] 9.3 Standalone `use x` — attach deferred cleanup to existing binding
  - [ ] 9.4 Loan pattern standard library APIs (`File.withReader`, etc.)
- [ ] Epic 10: String & Collection Types (ADR-0007)
  - [ ] 10.1 `String` type — UTF-8, SSO (≤23 bytes inline), refcounted for large
  - [ ] 10.2 String interpolation — `"Hello ${name}"` with `${expr:formatter}` syntax
  - [ ] 10.3 Fixed-size arrays — `i32[N]`, stack-allocated, value semantics
  - [ ] 10.4 `List<T>` — dynamic, refcounted, growable buffer
  - [ ] 10.5 `Slice<T>` — view type, refcounts parent, slice-of-slice tightens on original
  - [ ] 10.6 `Align` attribute — `Packed | SIMD | Preserve` for struct layout control
- [ ] Epic 11: Green Thread Ownership (ADR-0003 + ADR-0004 + ADR-0006)
  - [ ] 11.1 `green fn arg` syntax — ownership boundary, everything moved in/out
  - [ ] 11.2 Non-atomic refcounting — thread-local refcount ops only
  - [ ] 11.3 Channels — `Channel.create<T>`, send/receive as moves
  - [ ] 11.4 `[<Allocator(Arena)>]` attribute — ambient allocator, thread-local swap
  - [ ] 11.5 Arena allocator implementation — bump pointer, bulk free
- [ ] Scoped partial record construction (design idea logged in memory)

## Known Issues
- [ ] Parser doesn't accept underscores in identifiers
- [ ] Non-exported functions use ExternalLinkage (should use InternalLinkage)
- [ ] Workspace builds dependencies redundantly (needs build caching)
- [ ] Parser doesn't accept `_` as parameter name (wildcard pattern)
- [ ] Forward-ref checker doesn't check let binding values (visibility test skipped)
- [ ] 2 pre-existing test failures (AstWalkerTests, GeneratorTests)
