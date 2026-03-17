# Furst Frontend Tasks

## Done
- [x] Basic token types and parser (BasicTypes, CommonParsers)
- [x] Two-phase row parser with indentation nesting (TestTwoPhase)
- [x] Position tracking on all tokens (Line, Column, TokenLength)
- [x] AST expression types (LanguageExpressions)
- [x] Row → ExpressionNode builders (rowToExpression, buildFunctionDefinition, etc.)
- [x] Result-based error handling with source positions
- [x] AstBuilderTests: variable bindings, functions, binary ops, function calls, errors
- [x] LanguageExpressions tests: subtract, multiply, chained ops, float literal, multi-body, standalone call, struct error
- [x] CLI (`Cli.fs`) with `help`, `lex`, `ast`, `check`, `build` (stub) commands
- [x] Program.fs rewritten as 3-line entrypoint
- [x] Pretty-printed AST tree output (indented, human-readable)
- [x] Removed `<!>` debug noise from parser output

## In Progress
- [ ] Typed parameter parsing — `TypedParameterExpressionMatch` expects `Name` but parser emits `Parameter` token; needs fix in active pattern or parser

## Next
- [ ] Design frontend→backend interchange format (protobuf? binary serialization TBD)
- [ ] Generate test AST fixture files for backend independent dev/testing
- [ ] Wire `build` command to invoke backend (frontend serializes AST → backend consumes → LLVM IR)
- [ ] Fix typed parameter parsing (`(a: i32)` style params)
- [ ] LLVM IR generation (GeneratorTests currently failing)
