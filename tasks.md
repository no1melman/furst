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

## In Progress
- [ ] Epic 6: Type System & Operators as Functions
  - [x] 6.1 Hindley-Milner type inference — Algorithm W implemented, wired into lowering
  - [x] 6.2 Type unification — unify, occurs check, substitution composition
  - [x] 6.3 Typed AST — inferTypes builds type map, applyInferredTypes rewrites lowered defs
  - [ ] 6.4 Operators as infix functions — `+` desugars to function calls
  - [ ] 6.5 Builtin operator functions — type-aware overloads
  - [ ] 6.6 Operator definition — `let (+) a b = ...` syntax
  - [ ] 6.7 Operator resolution — `a + b` resolves to operator function
  - [ ] 6.8 Precedence and associativity
  - [ ] 6.9 Backend type-aware codegen — fadd/fsub for floats
  - [ ] 6.10 Type errors in backend with source locations
  - [ ] 6.11 Tests

## Next
- [ ] Epic 7: Type Contracts & Polymorphic Operators (trait/protocol system)
- [ ] Scoped partial record construction (design idea logged in memory)

## Known Issues
- [ ] Parser doesn't accept underscores in identifiers
- [ ] Non-exported functions use ExternalLinkage (should use InternalLinkage)
- [ ] Workspace builds dependencies redundantly (needs build caching)
- [ ] Typed parameter parsing `(a: i32)` still broken
- [ ] 2 pre-existing test failures (AstWalkerTests, GeneratorTests)
