# Furst Language Tasks

## Completed

### Phase 1: Basic Parsing & AST
- [x] Create CLAUDE.md with project purpose and guidelines
- [x] Fix F# frontend compilation errors (16 errors → 0)
- [x] Add TokenWithMetadata with position tracking (Line, Column, TokenLength)
- [x] Update all parsers to capture position metadata using FParsec
- [x] Create active patterns for testing (TokenAt, AnyToken, WithMeta)
- [x] Define AST types (Expression, LetBinding, FunctionDefinition, Operation, FunctionCall, etc.)
- [x] Implement Result-based error handling with CompileError (position-aware errors)
- [x] Build Row → Expression AST converters (rowToExpression, buildLetBinding, buildFunctionDefinition, parseExpression)
- [x] Fix function definition detection (handle Parameter tokens in IsParameterListExpression)
- [x] Implement binary operation parsing with literals and identifiers
- [x] Implement function call expression parsing
- [x] Create comprehensive AST builder tests (7 tests passing)
- [x] Test nested function definitions with function calls

### Phase 2: IDE Integration Prep
- [x] Rename BinaryOp → Operator (cleaner naming)
- [x] Add SourceLocation type (StartLine, StartCol, EndLine, EndCol)
- [x] Add ExpressionNode wrapper (contains Expression + Location)
- [x] Implement location tracking (tokenLocation, tokensLocation, rowLocation)
- [x] Refactor parseExpression (handles chained operations: a + b + c)
- [x] Add location tracking test

### Phase 3: Architecture Documentation
- [x] Create ADR-0001: Symbol tracking and function call validation
- [x] Create ADR-0002: Algebraic type system and type inference

## In Progress

## Todo

### Phase 4: Symbol Tracking (ADR-0001)
- [ ] Define SymbolTable type (Functions, Variables maps)
- [ ] Implement collectSymbols (walk AST, build symbol table)
- [ ] Implement validateExpression (check function calls exist)
- [ ] Add scope tracking (parameters, local let bindings)
- [ ] Add symbol validation tests
- [ ] Integrate validation into compilation pipeline

### Phase 5: Type System Foundation (ADR-0002)
- [ ] Expand Type ADT (TPrim, TFunc, TTuple, TRecord, TVar, TSum)
- [ ] Define TypeEnv and TypeScheme
- [ ] Implement fresh type variable generation
- [ ] Implement unification algorithm
- [ ] Add occurs check (prevent infinite types)
- [ ] Write unification tests

### Phase 6: Type Inference (ADR-0002)
- [ ] Implement Algorithm W (type inference)
- [ ] Add constraint generation
- [ ] Implement constraint solving
- [ ] Add generalization (∀ quantification)
- [ ] Add instantiation (type scheme → type)
- [ ] Write type inference tests
- [ ] Integrate with AST building

### Phase 7: Algebraic Data Types
- [ ] Implement sum type definitions (type Option<T> = Some T | None)
- [ ] Implement record type definitions (type Point = { x: Float, y: Float })
- [ ] Add type alias support
- [ ] Implement pattern matching on sum types
- [ ] Add exhaustiveness checking for patterns
- [ ] Write ADT tests

### Phase 8: Expression Types
- [ ] Add if/then/else expressions
- [ ] Add let bindings in expression position
- [ ] Add tuple expressions
- [ ] Add record construction/access
- [ ] Add list literals and operations
- [ ] Implement struct definition AST building

### Phase 9: Backend Integration
- [ ] Implement protobuf schema for typed AST serialization
- [ ] F# AST → protobuf serialization (with type info)
- [ ] C++ protobuf → MLIR IR generation
- [ ] Build out FurstDialect in MLIR
- [ ] Add LLVM codegen for algebraic types

### Phase 10: Integration & Testing
- [ ] Add end-to-end compilation tests
- [ ] Add integration tests for full pipeline
- [ ] Add error message quality tests
- [ ] Performance benchmarks (type inference)
- [ ] LSP integration for IDE support
