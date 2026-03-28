# Furst Language Tasks

## Completed

### Epic 1-4: Infrastructure, Reader, Emitter, Pipeline
- [x] All tasks complete (build infra, .fso reader, LLVM IR emitter, compilation pipeline)

### Epic 5: Project System
- [x] All tasks complete (furst new/build/run, yaml, libraries, workspaces)

### Epic 5b: Module System
- [x] 5b.1-5b.15 complete (mod/lib/open/private, symbol table, qualified access, manifests, backend codegen)
- [ ] 5b.16 Tests — mod scoping, lib paths, open resolution, qualified access, shadowing errors, etc.

### Epic 5c: Monadic AST Parser
- [x] All tasks complete (combinators, expression/statement/row parsers, wired in, old AstBuilder deleted)

### Epic 6: Type System & Operators
- [x] 6.1 Hindley-Milner type inference
- [x] 6.2 Type unification
- [x] 6.3 Typed AST
- [x] 6.4 Operator definition (`let (+) a b = ...`, infix desugar to function call)
- [x] 6.5 Precedence and associativity
- [x] 6.6 Backend type-aware codegen (fadd/fsub/fmul for floats, typed debug info, typed externals)

## Todo

### Epic 6 (continued)
- [ ] 6.7 Type errors in backend — reject type mismatches with source locations from .fso
- [ ] 6.8 Struct type registration — register struct types in type environment
- [ ] 6.9 Struct construction expression — `Point { x = 1, y = 2 }` syntax
- [ ] 6.10 Field access expression — `p.x` syntax
- [ ] 6.11 Backend struct codegen — LLVM named struct types, stack alloca, GEP
- [ ] 6.12 Tests — type inference roundtrips, mixed-type errors, struct construction and field access

### Epic 7+
- [ ] Type contracts, memory model, resource management, strings/collections, algebraic types, async
