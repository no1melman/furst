# ADR-0009: Monadic AST Parsing

**Status:** Accepted
**Date:** 2026-03-28
**Context:** AST construction from token stream, operator precedence, extensibility
**Discussion:** [monadic-ast-parsing](../discussions/monadic-ast-parsing.md)

---

## Context

`AstBuilder.fs` was a monolithic pattern-match over token lists that was hard to extend, had flat operator precedence, and couldn't handle self-referential structs or `and`-chains. Adding new constructs required threading cases into the right position in a large match expression.

## Decision

Replace `AstBuilder` with a custom FParsec-style parser combinator library operating over the lexer's existing `Row` tree output. The two-phase architecture (lex → Row trees → AST) is preserved; only the second phase changes.

### Architecture

**Option chosen: Custom monadic parser over Row trees (Option C from discussion)**

Rejected alternatives:
- **Option A** (FParsec over token stream): FParsec is designed for `CharStream`, not arbitrary token streams — requires adapter.
- **Option B** (single-pass FParsec): loses clean separation between tokenisation and parsing; indentation-sensitive parsing in FParsec is fragile.
- **Option D** (Pratt parser for expressions only): too surgical — doesn't address extensibility of statement/declaration parsing.

### Core abstraction

```fsharp
type TParser<'a> = TokenWithMetadata list -> ParseState -> ParseResult<'a>
```

Composition via FParsec-style operators (`>>=`, `|>>`, `>>.`, `.>>`, `.>>.`, `<|>`) rather than computation expressions. Expression parsing uses precedence layers via `chainl1`: atom → application → unary → multiply → add.

### Symbol table integration

`ParseState` threads a symbol table through parsing. Symbols registered on define, checked on use. Strict definition order (F#-style) — no forward references except within `struct ... and ...` chains, which use a `PendingTypeRefs` map for deferred resolution.

### Folder structure

Tokenisation (`Tokenise/`) and AST parsing (`Parse/`) are sibling directories under `compiler/`, making the phase boundary explicit.

### NegateExpression

Added `NegateExpression of Expression` to the AST union. Unary negation folds into literal values when possible (e.g., `-42` → `IntLiteral -42`), falls back to `NegateExpression` for non-literal operands. Handled in all downstream passes (type inference, lambda lifting, emitter).

## Consequences

- New language constructs are added by writing a small composable parser and plugging it into `choice` at the row dispatch level.
- Operator precedence is now correct (`*` binds tighter than `+`/`-`).
- `AstBuilder.fs` deleted; dead code in `Ast.fs` (`isParameterListExpression`, `isFunctionDefinition`, active patterns) removed.
- `and`-chain support for recursive/mutual struct types is designed into the state model but not yet exercised (no `and` keyword in lexer yet).

## Supersedes

- ADR-0001's "Option 1: Separate Validation Pass" is partially superseded — symbol tracking now happens during parsing rather than as a separate walk. The existing `SymbolTable.fs` and `Pipeline.checkForwardReferences` remain for cross-file resolution.
