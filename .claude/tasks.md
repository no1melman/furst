# Furst Language Tasks

## Completed
- [x] Create CLAUDE.md with project purpose and guidelines
- [x] Fix F# frontend compilation errors (16 errors → 0)
- [x] Add TokenWithMetadata with position tracking (Line, Column, TokenLength)
- [x] Update all parsers to capture position metadata using FParsec
- [x] Create active patterns for testing (TokenAt, AnyToken, WithMeta)
- [x] Define AST types (Expression, LetBinding, FunctionDefinition, BinaryOperation, FunctionCall, etc.)
- [x] Implement Result-based error handling with CompileError (position-aware errors)
- [x] Build Row → Expression AST converters (rowToExpression, buildLetBinding, buildFunctionDefinition, parseExpression)
- [x] Fix function definition detection (handle Parameter tokens in IsParameterListExpression)
- [x] Implement binary operation parsing with literals and identifiers
- [x] Implement function call expression parsing
- [x] Create comprehensive AST builder tests (6 tests passing)
- [x] Test nested function definitions with function calls

## In Progress

## Todo
- [ ] Implement protobuf schema for AST serialization
- [ ] F# AST → protobuf serialization
- [ ] C++ protobuf → MLIR IR generation
- [ ] Build out FurstDialect in MLIR
- [ ] Implement struct definition AST building
- [ ] Add more expression types (if/then/else, pattern matching, etc.)
- [ ] Implement type inference
- [ ] Add integration tests for full pipeline
