# ADR-0001: Symbol Tracking and Function Call Validation

**Status:** Proposed
**Date:** 2026-02-27
**Context:** IDE integration, type checking, function call validation

---

## Context

During AST construction, need to validate function calls reference existing functions. Challenge: F#-style languages allow forward references and mutual recursion, so linear "has this been defined yet?" checks insufficient.

Example problematic code:
```fsharp
let f = g 5        // calls g before it's defined
let g x = x + 1    // g defined after f
```

## Decision Options

### Option 1: Separate Validation Pass (Recommended)
**Approach:**
1. Parse entire file to AST
2. Walk AST collecting all symbols (functions, variables) into symbol table
3. Second walk validates all references against symbol table

**Implementation:**
```fsharp
type SymbolTable = {
    Functions: Map<string, FunctionDefinition>
    Variables: Map<string, LetBinding>
}

let rec collectSymbols (exprs: ExpressionNode list) : SymbolTable
let rec validateCalls (symTable: SymbolTable) (expr: Expression)
    : Result<unit, CompileError>
```

**Pros:**
- Clean separation: parsing vs validation
- Handles forward references naturally
- Easy to add scope tracking later
- Matches typical compiler architecture

**Cons:**
- Two passes over AST
- Errors reported after parsing complete

### Option 2: Thread Symbol Table Through Parsing
**Approach:**
Change parser signatures to thread symbol table:
```fsharp
let rec rowToExpression (symTable: SymbolTable) (row: Row)
    : Result<ExpressionNode * SymbolTable, CompileError>
```

**Pros:**
- Single pass
- Immediate error reporting

**Cons:**
- Still needs two passes for forward refs (pre-scan definitions)
- Complicates parsing logic
- Harder to extend

### Option 3: Scope-Aware Parsing (Future Extension)
**Approach:**
Track nested scopes during parsing:
```fsharp
type Scope = {
    Parent: Scope option
    Symbols: Map<string, Expression>
}
```

**Pros:**
- Proper lexical scoping
- Catches shadowing errors

**Cons:**
- Complex implementation
- Overkill for top-level only validation

## Decision

**Choose Option 1: Separate Validation Pass**

Reasons:
- Simplest to implement correctly
- Separates concerns (parse vs validate)
- Foundation for future type checking pass
- Handles forward references without special cases

## Implementation Plan

1. **Phase 1:** Symbol collection
   - Walk ExpressionNode list
   - Extract FunctionDefinition and LetBinding names
   - Build SymbolTable

2. **Phase 2:** Reference validation
   - Walk Expression tree recursively
   - Check FunctionCallExpression against symbol table
   - Check IdentifierExpression against variables + parameters

3. **Phase 3:** Scope tracking (future)
   - Add nested scope support
   - Track function parameters as local symbols
   - Handle let bindings inside function bodies

## Consequences

**Positive:**
- Clear error messages: "Function 'foo' not found at line X"
- Easy to extend with type info later
- Supports full F# semantics (forward refs, mutual recursion)
- Symbol table reusable for IDE features (autocomplete, go-to-def)

**Negative:**
- Extra AST traversal (acceptable cost)
- Errors reported after full parse (not incremental)

**Future Work:**
- Add scope hierarchy for nested let bindings
- Track symbol kinds (function vs variable vs parameter)
- Store type information in symbol table
- Export symbol table for IDE Language Server Protocol (LSP)

## Related ADRs
- (Future) ADR-0002: Type inference and checking
- (Future) ADR-0003: LSP integration for IDE support
