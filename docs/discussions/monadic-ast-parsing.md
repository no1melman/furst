# Monadic AST Parsing After Tokenisation

## Current Architecture & What's On The Table

Looking at the codebase, the current pipeline is a two-phase approach:

1. **Lexer phase** (`Lexer.fs`): FParsec parses raw source text into `TokenWithMetadata` values, then `nestRows()` organises them into an indentation-based `Row` tree (each `Row` has `Expressions: TokenWithMetadata list` and `Body: Row list`).

2. **AST Builder phase** (`AstBuilder.fs`): Hand-rolled pattern matching over `Row` token lists produces `ExpressionNode` values. This is essentially a big `match tokensFromRow row with` dispatch that looks at leading tokens (`Lib`, `Mod`, `Let`, `Struct`, etc.) and delegates to specialised builder functions.

There are also some leftover combinators in `Parsers.fs` — `pBranch`, `pMatch`, `<!>` — that manipulate FParsec streams directly and include debug `printfn` calls. These aren't currently used by the main AST path (which goes through `AstBuilder`), but they show an earlier attempt at a more combinator-based approach that was abandoned in favour of the pattern-matching style.

**What works well today:**
- The lexer's indentation-to-nesting logic is clean and handles scope correctly.
- `StructParser.fs` demonstrates idiomatic FParsec combinator style — it's the gold standard in this codebase for how parsers *should* look.
- Source location tracking is thorough.

**What's painful:**
- `AstBuilder.rowToExpression` is a monolithic match expression that's hard to extend. Adding a new construct means threading another case into the right position in the match, and getting the pattern priority right.
- Expression parsing (`parseExpression`) has grown organically — it handles negative literal folding, binary operations, and function calls in one function with nested `match` expressions. Operator precedence is flat (no precedence climbing or Pratt parsing).
- The `splitArgumentGroups` function is doing ad-hoc parenthesis balancing that a proper combinator would handle naturally.
- Error messages are manually constructed rather than leveraging FParsec's `<?>` label system.

So the question is: **what should the new approach look like?** The branch is named `feat/monadic-ast-parsing`, which suggests moving toward parser combinators over the token stream. But there are a few distinct design directions this could take, and I'd like to understand what you're thinking before diving in.

Here are the main options I see:

### Option A: FParsec over a token stream (two-pass, combinator AST)
Keep the existing lexer producing tokens, but instead of pattern-matching in `AstBuilder`, write FParsec-style combinators that consume a `Token list` (or a custom stream). This preserves the two-pass architecture but makes the second pass composable.

**Challenge:** FParsec is designed to parse `CharStream`, not arbitrary token streams. You'd need either a custom `TokenStream` adapter or a different combinator library.

### Option B: Single-pass FParsec (lex + parse in one)
Collapse the lexer and parser into a single FParsec grammar that goes directly from source text to AST. `StructParser.fs` already works this way. The indentation handling would move into the parser via FParsec's `IndentationParser` or custom indentation-aware combinators.

**Advantage:** Simpler pipeline, no intermediate token type. **Risk:** Indentation-sensitive parsing in FParsec can be tricky, and you lose the clean separation of concerns.

### Option C: Custom monadic parser over `Row` trees
Keep the lexer + `nestRows()` producing `Row` trees, but replace the pattern-matching in `AstBuilder` with a purpose-built computation expression (monad) that provides combinator-like composition over `Row` data. Something like:

```fsharp
let letBinding = parser {
    let! _ = expect Let
    let! name = expectName
    let! _ = expect Assignment
    let! value = parseExpr
    return LetBindingExpression { Name = name; Type = Inferred; Value = value }
}
```

**Advantage:** Works with the existing lexer output, feels idiomatic F#, easy to extend. **Trade-off:** You're building a mini parser combinator library from scratch, but it can be very targeted to your needs.

### Option D: Pratt parser / precedence climbing for expressions only
Keep the current structure mostly intact but replace the expression parsing with a proper Pratt parser (or precedence climbing algorithm) for handling operator precedence, unary operators, and function application. This is a more surgical fix that addresses the biggest pain point without a full rewrite.

### Unresolved
- Which option (or combination) are you leaning toward?
- Is the two-pass architecture (lex → Row tree → AST) something you want to preserve, or are you open to collapsing it?
- How important is operator precedence right now vs. later? (Currently it's flat, left-associative.)
- Are there new language constructs coming soon that would stress-test whichever approach we pick?

## Custom Monadic Parser with Stateful Symbol Tracking

**User direction:** Build a custom FParsec-style parser combinator library that operates over the token stream, threading a symbol table through parser state. This would unify parsing and symbol collection into a single pass, and could handle forward references / recursive types via deferred resolution.

This is very achievable. Here's how the pieces fit together:

### The Core Abstraction

The parser monad would thread two things: a **position in the token stream** and a **parse state** (containing the symbol table). In F# computation expression form:

```fsharp
type ParseState = {
    Symbols: SymbolTable
    Unresolved: Map<string, SourceLocation list>  // uses before declaration
    ScopeDepth: int
}

type ParseResult<'a> =
    | Success of value: 'a * remaining: TokenWithMetadata list * state: ParseState
    | Failure of error: CompileError

type TokenParser<'a> = TokenWithMetadata list -> ParseState -> ParseResult<'a>
```

The computation expression builder would give you `let!` (bind), `return`, and you'd build combinators on top: `expect`, `choice`, `many`, `optional`, etc. — all operating over `TokenWithMetadata list` instead of FParsec's `CharStream`.

### Symbol Table Integration

This is where it gets interesting. Instead of ADR-0001's two-pass approach (parse everything, then collect symbols, then validate), you'd **build the symbol table as you parse**:

```fsharp
let letBinding = parser {
    let! _ = expect Let
    let! name = expectName
    let! _ = expect Assignment
    let! value = parseExpr
    let binding = { Name = name; Type = Inferred; Value = value }
    do! registerSymbol name (Variable binding)  // adds to state
    return LetBindingExpression binding
}
```

Every time you parse a `let` or a `function` definition, `registerSymbol` pushes it into the threaded state. Every time you encounter an identifier *usage*, you either resolve it against the symbol table or mark it as **unresolved**.

### Deferred Resolution for Forward References

This is the clever part you're describing. When you hit a usage of `g` before `g` is defined:

```fsharp
let f = g 5      // g not in symbol table yet → add to Unresolved
let g x = x + 1  // g defined → remove from Unresolved, add to Symbols
```

The rule would be: **at each top-level binding boundary, check if any unresolved symbols from the previous binding have now been resolved.** If they haven't been resolved by the time you finish parsing the next same-level binding, emit an error.

In practice this means:

```fsharp
let topLevelBinding = parser {
    let! expr = choice [letBinding; functionDef; structDef; ...]
    do! resolveDeferred  // check: did previous unresolved symbols get satisfied?
    return expr
}
```

`resolveDeferred` walks the `Unresolved` map, checks each symbol against the now-updated `Symbols` table, and either clears them or produces an error.

### Why This Works for Recursive Types Too

For something like:

```fsharp
type Tree =
    | Leaf of i32
    | Node of Tree * Tree   // self-referential
```

Or mutual recursion:

```fsharp
type Expr = ... | Call of Func
type Func = ... | Body of Expr
```

The same deferred resolution mechanism applies. When you parse `Node of Tree * Tree`, `Tree` isn't fully defined yet, but it's *being* defined — you know the name exists. You could handle this by registering a **placeholder symbol** at the start of a type definition (before parsing its body), then filling in the details as you go. The `and` keyword (for mutual recursion) would register all names in the group as placeholders before parsing any of their bodies.

### What You'd Need to Build

The combinator library itself is small — the core is maybe 150–200 lines:

1. **`TokenParser<'a>` type** + computation expression builder (~40 lines)
2. **Primitive combinators**: `expect`, `expectName`, `expectNumber`, `peek`, `satisfy` (~30 lines)
3. **Composition combinators**: `choice`, `many`, `many1`, `optional`, `sepBy` (~40 lines)
4. **State combinators**: `registerSymbol`, `lookupSymbol`, `markUnresolved`, `resolveDeferred` (~40 lines)
5. **Error handling**: position tracking, labels, contextual messages (~30 lines)

Then the actual grammar definitions (let bindings, functions, structs, expressions, etc.) would each be small, composable parser values — much like `StructParser.fs` looks today, but over tokens instead of characters.

### Relationship to ADR-0001

This approach is essentially ADR-0001's "Option 2: Thread Symbol Table Through Parsing" — which was rejected as too complex at the time. But a monadic parser makes it *not* complex, because the state threading is hidden inside the monad. The `registerSymbol` / `resolveDeferred` calls are explicit and composable rather than being manually plumbed through every function signature.

The deferred resolution strategy also addresses the main con listed in ADR-0001 for Option 2: "Still needs two passes for forward refs." With your approach, you don't need two passes — you just defer and resolve at scope boundaries.

### Unresolved
- Should the parser consume `Row` trees (preserving the lexer's indentation nesting) or a flat `TokenWithMetadata list` (re-handling indentation in the parser)?
- What's the resolution boundary for forward references — next same-level binding, end of module, or end of file?
- Should `and` bindings (mutual recursion) be an early consideration in the design, or can they be added later without breaking the model?
- Does the deferred resolution need to distinguish between "used before declared in same scope" (OK in F#) vs. "used but never declared" (error)?

## Revised: Operator-Based Combinators with Strict Definition Order

**User clarification:** No computation expressions — use FParsec-style operators (`>>.`, `.>>`, `.>>.`, `|>>`, etc.) to compose parsers. Also, Furst enforces **strict definition order** like F#: everything must be declared before use. The only exception is `struct ... and ...` (recursive/mutual type definitions), where the `and`-bound names are forward-visible to each other.

This simplifies things considerably. The deferred resolution machinery from the previous section mostly goes away. The symbol table becomes a straightforward "add on define, check on use" mechanism with one special case for `and`-bound structs.

### Revised Core Types

```fsharp
type ParseState = {
    Symbols: SymbolTable
    ScopeDepth: int
}

type ParseResult<'a> =
    | POk of value: 'a * remaining: TokenWithMetadata list * state: ParseState
    | PError of error: CompileError

/// A parser that consumes tokens and threads state
type TParser<'a> = TokenWithMetadata list -> ParseState -> ParseResult<'a>
```

### Operator Combinators

These mirror FParsec's operators but over `TokenWithMetadata list` instead of `CharStream`:

```fsharp
/// Bind: run p1, feed result + remaining tokens into f
let (>>=) (p: TParser<'a>) (f: 'a -> TParser<'b>) : TParser<'b> =
    fun tokens state ->
        match p tokens state with
        | POk (v, rest, s') -> f v rest s'
        | PError e -> PError e

/// Map: transform the result of a parser
let (|>>) (p: TParser<'a>) (f: 'a -> 'b) : TParser<'b> =
    fun tokens state ->
        match p tokens state with
        | POk (v, rest, s') -> POk (f v, rest, s')
        | PError e -> PError e

/// Sequence, keep right: run p1, discard result, run p2
let (>>.) (p1: TParser<'a>) (p2: TParser<'b>) : TParser<'b> =
    p1 >>= fun _ -> p2

/// Sequence, keep left: run p1, keep result, run p2, discard
let (.>>) (p1: TParser<'a>) (p2: TParser<'b>) : TParser<'a> =
    p1 >>= fun a -> p2 |>> fun _ -> a

/// Sequence, keep both
let (.>>.) (p1: TParser<'a>) (p2: TParser<'b>) : TParser<'a * 'b> =
    p1 >>= fun a -> p2 |>> fun b -> (a, b)

/// Choice: try p1, if it fails (without consuming), try p2
let (<|>) (p1: TParser<'a>) (p2: TParser<'a>) : TParser<'a> =
    fun tokens state ->
        match p1 tokens state with
        | POk _ as ok -> ok
        | PError _ -> p2 tokens state  // backtrack to same position
```

### Primitive Parsers

```fsharp
/// Match a specific token
let expect (tok: Tokens) : TParser<TokenWithMetadata> =
    fun tokens state ->
        match tokens with
        | t :: rest when t.Token = tok -> POk (t, rest, state)
        | t :: _ -> PError (tokenError $"Expected {tok}, got {t.Token}" t)
        | [] -> PError (CompileError.Empty $"Expected {tok}, got end of input")

/// Match a Name token, return the string
let expectName : TParser<string> =
    fun tokens state ->
        match tokens with
        | t :: rest ->
            match t.Token with
            | Name (Word n) -> POk (n, rest, state)
            | _ -> PError (tokenError $"Expected identifier, got {t.Token}" t)
        | [] -> PError (CompileError.Empty "Expected identifier")

/// Peek without consuming
let peek : TParser<TokenWithMetadata option> =
    fun tokens state ->
        match tokens with
        | t :: _ -> POk (Some t, tokens, state)
        | [] -> POk (None, tokens, state)
```

### Symbol Table — Simple "Define Before Use"

Since definition order is strict, the symbol table logic is clean:

```fsharp
/// Register a symbol in the current scope
let registerSymbol name entry : TParser<unit> =
    fun tokens state ->
        let symbols' = state.Symbols |> SymbolTable.add name entry
        POk ((), tokens, { state with Symbols = symbols' })

/// Assert a symbol exists (for identifier usage)
let requireSymbol name location : TParser<SymbolEntry> =
    fun tokens state ->
        match state.Symbols |> SymbolTable.tryFind name with
        | Some entry -> POk (entry, tokens, state)
        | None -> PError { Message = $"'{name}' is not defined"; ... }
```

### The `and` Exception for Recursive Structs

For `struct Foo { ... } and struct Bar { ... }`, you'd handle this with a two-step parse:

1. **Pre-register** all names in the `and` group as placeholder type symbols
2. **Parse** the bodies (now all names are visible to each other)
3. **Fill in** the placeholder entries with full definitions

```fsharp
let andStructGroup : TParser<Expression list> =
    // First pass: collect all struct names from "struct Name { ... } and struct Name { ... }"
    // Register placeholders
    // Second pass: parse each struct body with all names visible
```

This is the *only* place where you need anything beyond simple "define then use." Everything else — let bindings, function definitions, module declarations — follows strict order.

### What a Grammar Rule Looks Like

Putting the operators together, here's what a let binding parser would look like:

```fsharp
let pLetBinding : TParser<Expression> =
    expect Let >>. expectName .>>. (expect Assignment >>. pExpr)
    |>> fun (name, value) ->
        LetBindingExpression { Name = name; Type = Inferred; Value = value }
```

And a function definition:

```fsharp
let pFunctionDef : TParser<Expression> =
    expect Let >>. expectName .>>. pParams .>> expect Assignment .>>. pBody
    |>> fun ((name, params), body) ->
        FunctionDefinitionExpression (FunctionDefinition {
            Identifier = name
            Type = Inferred
            Parameters = params
            Body = BodyExpression body
            Visibility = Public
        })
```

These are concise, composable, and read like the grammar itself. Adding a new language construct means writing a new parser value and plugging it into the top-level `choice`.

### Unresolved
- Should `registerSymbol` happen inside the individual parsers (e.g., `pLetBinding` also registers) or as a wrapper at the top-level dispatch?
- How does `Row` body nesting feed into this? Does `pBody` receive the `Row.Body` children's tokens, or does the parser itself handle indentation?
- For error recovery: should the parser stop at first error, or try to continue and collect multiple errors?

## Revised `and` Strategy: Deferred Resolution Without Lookahead

**User clarification:** No two-phase pre-registration needed. Instead, the parser shifts into a "pending resolution" mode when it enters a struct definition:

1. **Parsing a struct body** — encounter an unknown type reference → don't error. Stash it in a temporary pending set on the state.
2. **Hit `and`** → keep parsing the next struct, still accumulating pending type references. Each completed struct gets registered into the symbol table normally.
3. **`and` chain ends** (next token isn't `and`) → now check all pending references against what was defined across the entire `and` group.
4. **Anything still unresolved** → error.

This is simpler than pre-registration because there's no lookahead and no placeholder symbols. The parser just defers judgement on unknown type references while inside a struct/and group, then settles up at the end.

### State Shape

```fsharp
type ParseState = {
    Symbols: SymbolTable
    PendingTypeRefs: Map<string, SourceLocation list> option
    // None = normal mode (unknown type → immediate error)
    // Some = inside struct/and group (unknown type → stash here)
    ScopeDepth: int
}
```

The `PendingTypeRefs` field acts as a mode flag and accumulator in one. When it's `None`, `requireType` behaves normally — unknown type is an error. When it's `Some`, unknown types get added to the map instead of erroring.

### Flow

```fsharp
let pStructAndGroup : TParser<Expression list> =
    enterPendingMode           // set PendingTypeRefs = Some Map.empty
    >>. sepBy1 pStructDef (expect And)
    >>= fun structs ->
        resolvePendingRefs     // check pending against symbols, error if any remain
        |>> fun () -> structs

let pStructDef : TParser<Expression> =
    expect Struct >>. expectName .>>. pStructBody
    >>= fun (name, fields) ->
        let def = StructExpression { Name = name; Fields = fields }
        registerSymbol name (TypeSymbol def)   // available to subsequent `and` structs
        |>> fun () -> def
```

The key insight is that each struct in the `and` chain registers itself *as it's parsed*. So `struct Foo { x: Bar } and struct Bar { y: Foo }` works because:

- Parse `Foo`: encounter `Bar` (unknown) → stash in pending. Register `Foo`.
- Parse `Bar`: encounter `Foo` → it's in the symbol table already (just registered). Register `Bar`.
- End of `and` chain: check pending — `Bar` is now registered → all clear.

Order within the `and` group doesn't matter for resolution, only that everything is resolved by the time the group closes.

### What `requireType` Looks Like in Both Modes

```fsharp
let requireType name location : TParser<TypeDefinition> =
    fun tokens state ->
        match state.Symbols |> SymbolTable.tryFindType name with
        | Some typeDef -> POk (typeDef, tokens, state)
        | None ->
            match state.PendingTypeRefs with
            | Some pending ->
                // In struct/and mode: stash for later
                let locs = pending |> Map.tryFind name |> Option.defaultValue []
                let pending' = pending |> Map.add name (location :: locs)
                POk (Placeholder name, tokens, { state with PendingTypeRefs = Some pending' })
            | None ->
                // Normal mode: immediate error
                PError { Message = $"Type '{name}' is not defined"; ... }
```

When in pending mode, it returns a `Placeholder` type definition that gets resolved later. This means the AST might temporarily contain placeholders, but they're guaranteed to be resolved (or errored) before `pStructAndGroup` completes.

### Why This Is Nice

- **No lookahead** — you don't need to scan ahead to find all `and`-bound names before parsing.
- **No placeholders in the main symbol table** — the pending set is separate and temporary.
- **Naturally ordered** — earlier structs in the `and` chain are visible to later ones for free, you only need the pending set for backward references within the group.
- **Clear error semantics** — if the `and` chain ends and pending refs remain, you have exact source locations for every unresolved reference.

### Unresolved
- Does the AST need a `Placeholder` type variant, or can we avoid it by doing a fixup pass over just the `and` group's AST nodes before returning them?
- Should standalone `struct Foo { ... }` (no `and`) also go through pending mode, or should it use immediate resolution? (Going through pending mode uniformly would be simpler code, but standalone structs can't have forward refs so immediate errors would be more helpful.)
- Can `and` chains eventually apply to things beyond structs (e.g., mutual recursive functions like F#'s `let rec ... and ...`)?

## Refined: Reactive Pending Refs — No Mode Switching

**User clarification:** There's no explicit "enter pending mode." Pending type refs are purely reactive — they only come into existence when you're inside a struct body and encounter an unknown type. Then:

- Finish the struct → check next token
- Next is `and` → keep going, the pending refs carry forward
- Next is *not* `and` → pending refs exist but there's no `and` to resolve them → error immediately

So a standalone struct with an unknown type is an instant error. The pending mechanism only *matters* when `and` exists, but you don't need to know about `and` upfront.

### Revised State

```fsharp
type ParseState = {
    Symbols: SymbolTable
    PendingTypeRefs: Map<string, SourceLocation list>  // always present, usually empty
    ScopeDepth: int
}
```

No `option` wrapper. The map is just there — empty most of the time. Unknown types during struct parsing add to it. Everything else ignores it.

### Revised `requireType`

```fsharp
let requireType name location : TParser<TypeDefinition> =
    fun tokens state ->
        match state.Symbols |> SymbolTable.tryFindType name with
        | Some typeDef -> POk (typeDef, tokens, state)
        | None ->
            // Only struct field types are allowed to be pending
            // The caller (pStructField) is responsible for calling this
            // rather than the normal requireSymbol
            let locs = state.PendingTypeRefs |> Map.tryFind name |> Option.defaultValue []
            let pending' = state.PendingTypeRefs |> Map.add name (location :: locs)
            POk (Deferred name, tokens, { state with PendingTypeRefs = pending' })
```

This is *only* called from struct field type parsing. Normal identifier usage in let bindings, function calls, etc. uses `requireSymbol` which errors immediately on unknown names. The distinction is structural — it's not a mode, it's a different parser used in a different context.

### Revised Flow

```fsharp
let pStructDef : TParser<Expression> =
    expect Struct >>. expectName .>>. pStructBody
    >>= fun (name, fields) ->
        registerSymbol name (TypeSymbol { Name = name; Fields = fields })
        |>> fun () -> StructExpression { Name = name; Fields = fields }

let pStructOrAndChain : TParser<Expression list> =
    pStructDef >>= fun first ->
        let rec andLoop acc =
            peek >>= fun next ->
                match next with
                | Some t when t.Token = And ->
                    expect And >>. pStructDef >>= fun s -> andLoop (s :: acc)
                | _ ->
                    // Chain is over. Any pending refs still unresolved?
                    checkPendingRefs  // errors if map is non-empty
                    |>> fun () -> List.rev acc
        andLoop [first]
```

Walk through `struct Foo { x: Bar } and struct Bar { y: Foo }`:

1. Parse `Foo`'s body: hit field `x: Bar`. `Bar` not in symbols → `requireType` adds `"Bar"` to pending. Return `Deferred "Bar"` as the type.
2. `Foo` finishes → register `Foo` in symbols.
3. Peek next token → it's `and` → consume it, keep going.
4. Parse `Bar`'s body: hit field `y: Foo`. `Foo` *is* in symbols (registered in step 2) → resolves normally.
5. `Bar` finishes → register `Bar` in symbols.
6. Peek next token → not `and` → chain over.
7. `checkPendingRefs`: `"Bar"` is pending. Check symbols — `Bar` was registered in step 5. Remove from pending.
8. Pending map is now empty → success.

Now the standalone case: `struct Foo { x: Nope }`:

1. Parse `Foo`'s body: hit field `x: Nope`. Not in symbols → pending gets `"Nope"`.
2. `Foo` finishes → register `Foo`.
3. Peek next token → not `and` → chain over.
4. `checkPendingRefs`: `"Nope"` is pending. Check symbols — not there. **Error: "Type 'Nope' is not defined" at the exact source location where it was used.**

### What `checkPendingRefs` Does

```fsharp
let checkPendingRefs : TParser<unit> =
    fun tokens state ->
        let unresolved =
            state.PendingTypeRefs
            |> Map.filter (fun name _ -> state.Symbols |> SymbolTable.tryFindType name |> Option.isNone)
        match unresolved |> Map.toList with
        | [] ->
            // All resolved — clear the pending map
            POk ((), tokens, { state with PendingTypeRefs = Map.empty })
        | (name, locs) :: _ ->
            // Report first unresolved, with its usage location
            let loc = locs |> List.head
            PError { Message = $"Type '{name}' is not defined"; ... }
```

### Why This Is Better

- **No mode switching** — no `option`, no `enterPendingMode`. The pending map is always there, just empty.
- **Reactive** — pending refs appear as a natural consequence of parsing struct fields with unknown types. No upfront decision.
- **Immediate error for non-`and` cases** — a standalone struct with a bad type errors right away because `checkPendingRefs` runs immediately after.
- **`requireType` vs `requireSymbol`** — the distinction is which parser you use, not which mode you're in. Struct fields use `requireType` (allows deferral). Everything else uses `requireSymbol` (immediate error).

### Remaining Question: Deferred Type in the AST

The `Deferred "Bar"` value that `requireType` returns needs to live somewhere temporarily. Two options:

1. **`TypeDefinition` gets a `Deferred of string` case** — `checkPendingRefs` could also do a fixup pass over the just-parsed structs to replace `Deferred` with the real type. This keeps it contained to the `and` group.
2. **Struct fields store type names as strings** — resolution to `TypeDefinition` happens entirely in `checkPendingRefs` after the group is complete. The AST never sees `Deferred`.

### Unresolved
- Option 1 vs 2 above for handling the deferred type in the AST?
- Should `checkPendingRefs` also do the fixup (replacing `Deferred` → real type), or should that be a separate step?
- ~~Self-referential structs (`struct Tree { left: Tree }`) — does the struct's own name get registered *before* parsing its body, or does `Tree` referencing itself also go through pending?~~

## Self-Referential Structs: Register Name Before Body

**Settled:** F# has no special self-reference keyword — a type is simply visible within its own definition. Furst follows the same rule. So: **register the struct name into the symbol table before parsing its body.**

This means `pStructDef` becomes:

```fsharp
let pStructDef : TParser<Expression> =
    expect Struct >>. expectName
    >>= fun name ->
        registerSymbol name (TypePlaceholder name)   // visible during body parsing
        >>. pStructBody
        >>= fun fields ->
            let def = { Name = name; Fields = fields }
            updateSymbol name (TypeSymbol def)        // replace placeholder with full def
            |>> fun () -> StructExpression def
```

Two-step registration:
1. **Before body:** register name as a `TypePlaceholder` — enough for `requireType` to resolve self-references without deferral.
2. **After body:** update the entry with the full `TypeSymbol` containing field info.

This means self-references like `struct Tree { left: Tree }` resolve immediately against the placeholder — they never hit the pending map. The pending map is now *exclusively* for cross-references between `and`-bound structs where the referenced struct hasn't been reached yet.

### Revised `and` chain walkthrough

`struct Foo { x: Bar } and struct Bar { y: Foo; z: Bar }`:

1. Register `Foo` as placeholder.
2. Parse `Foo` body: `x: Bar` → `Bar` not in symbols → pending.
3. Update `Foo` placeholder → full definition.
4. Peek → `and` → continue.
5. Register `Bar` as placeholder.
6. Parse `Bar` body: `y: Foo` → `Foo` in symbols → resolves. `z: Bar` → `Bar` in symbols (placeholder) → resolves.
7. Update `Bar` placeholder → full definition.
8. Peek → no `and` → `checkPendingRefs`: `Bar` now in symbols → clear. Done.

### Unresolved
- ~~Does the symbol table need distinct `TypePlaceholder` vs `TypeSymbol` entries, or can the placeholder just be a `TypeSymbol` with empty fields that gets overwritten?~~
- ~~Option 1 vs 2 for `Deferred` in the AST still open (see previous section).~~

## Placeholder vs TypeSymbol — Walkthrough & Resolution

Walking through `struct Tree { left: Tree; value: i32 }` step by step to see if we need distinct `TypePlaceholder` / `TypeSymbol` entries or a `Deferred` AST variant.

**With a `TypePlaceholder` approach:**

```
Step 1: Parse "struct Tree"
         Symbol table: { Tree → TypePlaceholder }
         (name is known, but no field info)

Step 2: Parse field "left: Tree"
         requireType "Tree" → finds TypePlaceholder → OK
         But what TypeDefinition do we return? The placeholder has no structure.
         We'd need Deferred "Tree" or some indirection in the AST.

Step 3: Parse field "value: i32"
         Built-in → I32, fine.

Step 4: Body done. Fields = [("left", Deferred "Tree"), ("value", I32)]
         Update symbol table: { Tree → TypeSymbol { Name="Tree"; Fields=[...] } }
         Then fixup: replace Deferred "Tree" → UserDefined "Tree" (or resolved type)
```

The awkward part is step 2 — you need a temporary AST representation that gets patched up later.

**But looking at the actual AST:**

```fsharp
// Struct fields are (string * TypeDefinition) pairs
// TypeDefinition = I32 | I64 | Float | Double | String | UserDefined of string | Inferred
```

`UserDefined of string` is already just a name. The field `left: Tree` is stored as `("left", UserDefined "Tree")`. The symbol table lookup doesn't need to *return* a resolved type — it just needs to **confirm the name exists.**

**Revised — no placeholder, no deferred, just existence checking:**

```
Step 1: Parse "struct Tree"
         Symbol table: { Tree → TypeRegistered }
         (just a flag: "this name is a known type")

Step 2: Parse field "left: Tree"
         assertTypeExists "Tree" → "Tree" in symbol table? YES → OK
         Field stored as ("left", UserDefined "Tree") — already just a string

Step 3: Parse field "value: i32"
         Built-in → ("value", I32)

Step 4: Body done.
         Update: { Tree → TypeDefined { Name="Tree"; Fields=[...] } }
```

No placeholder variant. No deferred variant. No fixup pass. The AST already stores user-defined types as name strings via `UserDefined`. The symbol table during parsing is just a gatekeeper answering "is this name known?" — a boolean check, not a structural lookup.

**This also resolves the `Deferred` question from the previous section — we don't need it.**

### Revised `requireType` — Just an Existence Check

```fsharp
let assertTypeExists name location : TParser<unit> =
    fun tokens state ->
        if SymbolTable.typeExists name state.Symbols then
            POk ((), tokens, state)
        else
            // Not found — add to pending (for and-chain resolution)
            let locs = state.PendingTypeRefs |> Map.tryFind name |> Option.defaultValue []
            let pending' = state.PendingTypeRefs |> Map.add name (location :: locs)
            POk ((), tokens, { state with PendingTypeRefs = pending' })
```

And `pStructDef`:

```fsharp
let pStructDef : TParser<Expression> =
    expect Struct >>. expectName
    >>= fun name ->
        registerType name            // "Tree" is now a known type name
        >>. pStructBody name         // fields use assertTypeExists, store UserDefined strings
        >>= fun fields ->
            let def = { Name = name; Fields = fields }
            updateTypeDefinition name def   // fill in full struct info
            |>> fun () -> StructExpression def
```

### And-chain walkthrough with this model

`struct Foo { x: Bar } and struct Bar { y: Foo }`:

```
1. registerType "Foo"           → symbols: { Foo → TypeRegistered }
2. Parse Foo body: "x: Bar"    → assertTypeExists "Bar" → NOT found → pending: { Bar → [loc] }
3. Field stored as ("x", UserDefined "Bar") — no Deferred needed
4. updateTypeDefinition "Foo"   → symbols: { Foo → TypeDefined {...} }
5. Peek → and → continue
6. registerType "Bar"           → symbols: { Foo → TypeDefined, Bar → TypeRegistered }
7. Parse Bar body: "y: Foo"    → assertTypeExists "Foo" → found → OK
8. Field stored as ("y", UserDefined "Foo")
9. updateTypeDefinition "Bar"   → symbols: { Foo → TypeDefined, Bar → TypeDefined }
10. Peek → no and → checkPendingRefs
11. "Bar" in pending → check symbols → Bar exists → remove from pending
12. Pending empty → success
```

Clean. No temporary AST variants. No fixup pass.

### Unresolved
- Should `registerType` and `updateTypeDefinition` be two distinct operations, or can `registerType` just store a partial entry that gets mutated/replaced?
- The symbol table entries: do we need `TypeRegistered` vs `TypeDefined` as distinct states, or is a single entry with optional fields sufficient?

## Does the Backend Need More Than Existence Checks at Parse Time?

Explored the full pipeline: frontend AST → protobuf .fso → C++ backend → LLVM IR.

### What happens today

**Protobuf contract** (`furst_ast.proto`):
```protobuf
message StructDef {
  string name = 1;
  repeated Parameter fields = 2;    // each field = name + TypeRef
  SourceLocation location = 3;
  repeated string module_path = 4;
}
```

`TypeRef` is a oneof: either a `BuiltinType` enum (I32, I64, Float, Double, String, Void) or a `user_defined` string. So the .fso format already carries user-defined types **as name strings only** — no resolved struct layout, no field offsets, no size info.

**Backend type resolution** (`emitter.cpp:43-62`):
```cpp
static llvm::Type* resolve_type(llvm::LLVMContext& ctx, const ast::TypeRef& type_ref)
```
Maps builtins to LLVM types. For user-defined types: currently **stubbed** — returns i32 as a fallback with a comment "User-defined types not yet supported."

**Backend struct reading** (`fso_reader.cpp:172-191`): Deserializes `StructDef` into `ast::StructDef` (name + vector of fields with TypeRef). No LLVM struct type creation happens yet.

### What the backend *will* need

To actually emit LLVM struct types, the backend needs to:

1. **Build a type registry** — map struct names to `llvm::StructType*`. LLVM supports opaque struct types (`StructType::create(ctx, "Foo")`) that get their body set later, which naturally handles recursive/mutual references.
2. **Resolve field types** — for each field, look up the `TypeRef` in the registry. Builtins map directly; user-defined names resolve to the registered `llvm::StructType*`.
3. **Compute layout** — LLVM does this automatically once field types are set.

All of this happens **in the backend**, using only the information already in the .fso: struct name, field names, and field type names. The frontend's symbol table existence check is sufficient — the backend does its own type resolution independently.

### Future: ref counting and stack-to-heap

Per ADR-0004 (memory ownership) and ADR-0006 (allocation strategy):

- **Refcounting**: Each heap value gets a header `[refcount | alloc_id | data...]`. This is a backend/codegen concern — the frontend doesn't need to know field sizes for this. The backend wraps heap-allocated structs in the header.
- **Stack-to-heap promotion**: Decided at compile time based on whether size is known. For structs, size *is* known (all fields have known types), so structs are stack-allocated by default. Promotion would be triggered by escape analysis (does a reference outlive its scope?), which is also a backend/lowering concern.
- **`Ptr<T>`**: When this lands, the frontend type system needs to understand `Ptr<T>` as a generic wrapper, but that's a type-checking concern, not a parsing concern. The parser just needs to recognise the syntax.

### Bottom line

**The parser's existence check is sufficient.** The frontend doesn't need to resolve struct layouts, compute sizes, or understand field types structurally at parse time. It just needs to confirm "this type name has been declared." All structural type work happens either in the frontend's type-checking pass (post-parse) or in the backend's LLVM emission.

The only thing that *might* change this is if we add **type-directed parsing** in the future (where the parser needs to know a type's structure to decide how to parse something). But Furst's syntax doesn't require that — the grammar is context-free with respect to types.

### Decisions
- **Serialize the symbol table** in the .fso, just in case the backend needs it. Doesn't hurt to have it.
- **Leave out generic types** (`Ptr<T>` etc.) for now — the tokeniser doesn't encode them yet.

## Coverage Gap Analysis: Current AstBuilder vs New Parser Design

Mapping everything `AstBuilder.fs` currently handles against what we've designed for the new `TParser` combinator approach.

### Covered in discussion (have a design)
| Feature | Current location | New parser sketch |
|---------|-----------------|-------------------|
| Let bindings | `rowToExpression` lines 136-143, `buildLetBinding` | `pLetBinding` — `expect Let >>. expectName ...` |
| Function definitions | `rowToExpression` lines 139-146, `buildFunctionDefinition` | `pFunctionDef` — sketched with params + body |
| Struct definitions | `rowToExpression` line 148, `buildStructDefinition` | `pStructDef` with `registerType` before body |
| Struct `and` chains | Not implemented yet | `pStructOrAndChain` with pending refs |
| Symbol registration | Not in parser (separate pass per ADR-0001) | `registerSymbol` / `registerType` / `assertTypeExists` inline |

### NOT yet covered — need design for new parser

**1. `lib` declarations** (lines 117-122)
Simple: `expect Lib >>. expectNameOrQualified |>> LibDeclaration`. No body, no symbol registration needed.

**2. `mod` declarations** (lines 124-127, `buildModDeclaration` lines 404-411)
More interesting — `mod` has an optional body containing nested definitions. Currently `buildModBody` recursively calls `rowToExpression` on `Row.Body` children and validates that no nested `mod` or `lib` appears inside.

Key question: **how does the new parser handle `Row.Body` nesting?** This is the big architectural question we haven't settled. The current `AstBuilder` receives `Row` trees from the lexer and accesses `.Body` directly. The new `TParser` operates on flat `TokenWithMetadata list`. Two options:
- (a) `TParser` also receives `Row` trees, not flat token lists — `pBody` accesses `Row.Body` children
- (b) Flatten rows into a token stream with synthetic indent/dedent tokens, handle nesting in the parser

**3. `open` declarations** (lines 129-134)
Same as `lib` — trivial. `expect Open >>. expectNameOrQualified |>> OpenDeclaration`.

**4. `private` visibility modifier** (lines 136-140)
Currently handled by pattern matching `Private :: Let :: ...`. In the new parser: `optional (expect Private)` composed before `pLetBinding` or `pFunctionDef`, threaded into visibility field.

**5. Expression parsing** (`parseExpression` lines 175-293)
The biggest chunk. Currently handles:
- **Single operands**: identifier, qualified name, number literal
- **Binary operations**: left-associative fold over `op operand` pairs, no precedence
- **Negative literal folding**: `-5` at start or after operator becomes `IntValue -5`
- **Function calls**: `f a b`, `f a (2 + 3)` — identifier followed by arguments with `splitArgumentGroups` for paren handling
- **Parenthesised sub-expressions**: via `splitArgumentGroups` depth tracking

This is where the new parser would benefit most from combinators. Currently it's a single 120-line function. With `TParser`:

```fsharp
let pAtom = pIdentifier <|> pNumber <|> pParenExpr
let pFuncCall = pIdentifier .>>. many1 pAtom |>> FunctionCallExpression
let pBinOp = chainl1 pAtom pOperator  // left-associative chain
let pExpr = pBinOp <|> pFuncCall <|> pAtom
```

But there's an ambiguity: `f a + b` — is this `f(a) + b` or `f(a + b)`? Currently the parser tries binary ops first (if any top-level operator exists), then function calls. This priority needs to be preserved.

**6. Parameter extraction** (`extractParameters` lines 336-349)
Handles both typed `(x: i32)` and untyped `x` parameters. Straightforward with combinators:

```fsharp
let pTypedParam =
    expect OpenParen >>. expectName .>> expect TypeIdentifier .>>. expectType .>> expect ClosedParen
let pUntypedParam = expectName |>> fun n -> (n, Inferred)
let pParam = pTypedParam <|> pUntypedParam
let pParams = many1 pParam
```

**7. Function vs let-binding disambiguation** (`isFunctionDefinition` in Ast.fs lines 126-138)
Currently a separate predicate that checks: does `let name ... =` have parameters before the `=` and a non-empty body? In the new parser this could be handled by **trying** `pFunctionDef` first (which expects params), and falling back to `pLetBinding` on failure. Or: parse `let name`, then check what follows — if parameters before `=`, it's a function; if `=` immediately, it's a binding.

**8. Mod body validation** (`buildModBody` lines 378-402)
Checks that `mod` bodies don't contain nested `mod` or `lib` declarations. This is a semantic validation. Could stay as a post-parse check, or be encoded into the parser by having `pModBody` use a restricted `choice` that excludes `pMod` and `pLib`.

**9. Source location tracking** (lines 30-78)
`tokenLocation`, `tokensLocation`, `rowLocation` — compute `SourceLocation` spanning tokens/rows. In the new parser, this would be a combinator like `withLoc` that wraps any parser and captures start/end positions:

```fsharp
let withLoc (p: TParser<'a>) : TParser<'a * SourceLocation> =
    fun tokens state ->
        let startTok = tokens |> List.tryHead
        match p tokens state with
        | POk (v, rest, s') ->
            let loc = computeSpan startTok rest
            POk ((v, loc), rest, s')
        | PError e -> PError e
```

### The Big Open Question: Row Trees vs Flat Tokens

Almost everything above has a clean combinator equivalent *except* body nesting. The current parser relies on `Row.Body` to know which tokens belong to a function/module body. The new `TParser<'a>` type signature is `TokenWithMetadata list -> ParseState -> ParseResult<'a>` — a flat list.

**Option A: TParser operates on Row trees**
Change the type to `Row list -> ParseState -> ParseResult<'a>`. Each row's `.Expressions` is the token list for that line. `pBody` naturally maps to processing `Row.Body`. But then combinator operators work on rows, not tokens — you'd need a way to parse *within* a row's expressions too.

**Option B: Flatten with indent/dedent tokens**
Before parsing, convert `Row` trees to a flat token stream with synthetic `Indent` / `Dedent` tokens (like Python's tokeniser does). Then `pBody` is just `expect Indent >>. many pStatement .>> expect Dedent`. The `TParser` stays cleanly over a flat token list.

**Option C: Two-level parser**
Top level operates on `Row list` (dispatches per-row). Within each row, a `TParser` operates on `TokenWithMetadata list` (the row's expressions). Body nesting is handled at the `Row` level. This is closest to what `AstBuilder` does today, just with combinators instead of pattern matching.

### Unresolved
- ~~Row trees vs flat tokens vs two-level? This is the key remaining architectural decision.~~
- Expression ambiguity: `f a + b` — how should the combinator parser handle function call vs binop priority?
- Should mod body validation (no nested mod/lib) be in the parser or a post-parse check?

## Decision: Parser Operates on Row Trees (Option C)

**The AST's main job** is to turn unstructured tokens into a typed semantic tree — "this is a function with these params and this body" rather than "here's some tokens at indent level 4." Downstream passes (type inference, lowering, codegen) consume the AST, not tokens or rows.

The Row tree already gives us structural nesting — the lexer's `nestRows()` has done the hard work of turning indentation into parent/child relationships. The parser just needs to:

1. Look at a row's tokens → decide what it is
2. If it has a body → recurse into `Row.Body`

There's no reason to flatten what the lexer already structured. So the parser naturally recurses into `Row.Body` for bodies.

### Revised TParser Type

The parser is two-level:
- **Row level**: dispatches on what kind of statement a row is, recurses into `.Body`
- **Token level**: combinators operate on `TokenWithMetadata list` within a single row's `.Expressions`

```fsharp
/// Token-level parser: operates within a single row's expressions
type TParser<'a> = TokenWithMetadata list -> ParseState -> ParseResult<'a>

/// Row-level parser: dispatches on rows, accesses .Body for nesting
let parseRow (row: Row) (state: ParseState) : ParseResult<ExpressionNode> =
    // run token-level combinators on row.Expressions
    // if the matched construct has a body, recurse into row.Body

let parseBody (rows: Row list) (state: ParseState) : ParseResult<Expression list> =
    // map parseRow over each row in the body
```

### How nesting works

```fsharp
let pFunctionDef : Row -> ParseState -> ParseResult<ExpressionNode> =
    fun row state ->
        // Parse row.Expressions with token-level combinators:
        //   expect Let >>. expectName .>>. pParams .>> expect Assignment
        // Then recurse into row.Body:
        //   let bodyResult = parseBody row.Body state'
        // Combine into FunctionDefinitionExpression
```

For `mod`:
```fsharp
let pModDecl : Row -> ParseState -> ParseResult<ExpressionNode> =
    fun row state ->
        // Parse "mod Name" from row.Expressions
        // Recurse into row.Body with parseBody
        // Validate no nested mod/lib in the body results
```

The token-level `TParser` combinators (`>>.`, `.>>`, `|>>`, etc.) stay exactly as designed — they just operate within a single row's expression list. The row-level dispatch is a thin layer on top that handles "which row is which construct" and "recurse into body."

### What changes from earlier sketches

Not much. The combinator operators, `expect`, `expectName`, `choice`, etc. all work the same — they consume from `TokenWithMetadata list`. The only new thing is that `pBody` isn't a token-level combinator; it's a row-level recursion that calls back into the row dispatcher.

This means constructs that span a single row (let bindings, lib, open, struct, standalone expressions) are pure token-level combinator parsing. Constructs with bodies (functions, modules) use token-level parsing for the header row, then row-level recursion for the body.

## Expression Parsing: Precedence and Associativity

### What F# does

F# function application binds tighter than any operator. So `f a + b` is always `(f a) + b`. The precedence order (high to low) is roughly:

1. **Function application** — `f x` (tightest)
2. **Unary operators** — `-x`
3. **Multiplicative** — `*`, `/`, `%`
4. **Additive** — `+`, `-`
5. **Comparison** — `<`, `>`, `=`, etc.
6. **Boolean** — `&&`, `||`
7. **Pipe** — `|>`, `<|` (loosest)

All arithmetic operators are left-associative: `a + b + c` = `(a + b) + c`.

### What Furst currently does (broken)

In `parseExpression` (AstBuilder.fs:232), the parser scans the entire token list for any top-level operator. If one exists *anywhere*, it enters binary op mode and tries to parse `operand op operand op ...` pairs. This means for `f a + b`:

1. Scan finds `+` at top level → enter binary op mode
2. Try to parse `f` as first operand → OK
3. Expect operator next → but `a` is not an operator → **error or misparsing**

The current parser doesn't implement function application having higher precedence than operators. It works for simple cases (`1 + 2`, `f a`) in isolation, but compound expressions like `f a + g b` would fail.

### How the new parser should handle this

With combinators, this falls out naturally from **precedence climbing**. You define layers from tightest to loosest binding:

```fsharp
// Atoms: literals, identifiers, parenthesised expressions
let pAtom = pNumber <|> pIdentifier <|> pParenExpr

// Function application: an atom followed by zero or more atom arguments
// f a b = ((f a) b) — left-associative, tightest after atoms
let pApp =
    pAtom .>>. many pAtom
    |>> fun (head, args) ->
        match args with
        | [] -> head  // just an atom, not a call
        | _ -> FunctionCallExpression { FunctionName = ...; Arguments = args }

// Multiplicative: app (* app)*
let pMul = chainl1 pApp (pOp Multiply)

// Additive: mul (+ mul)* or mul (- mul)*
let pAdd = chainl1 pMul (pOp Add <|> pOp Subtract)

// Top-level expression
let pExpr = pAdd
```

Now `f a + b` parses correctly:
1. `pAdd` calls `pMul` for left side
2. `pMul` calls `pApp` → tries `pAtom` → gets `f`, then `many pAtom` → gets `a` → result: `f(a)`
3. `pMul` has no `*` → returns `f(a)`
4. `pAdd` sees `+` → calls `pMul` for right side
5. `pMul` calls `pApp` → `pAtom` ��� gets `b`, no more atoms → result: `b`
6. Final: `(f a) + b` ✓

And `f a * g b + c`:
1. `pAdd.left` → `pMul.left` → `pApp` → `f(a)`
2. `pMul` sees `*` → `pMul.right` → `pApp` → `g(b)`
3. `pMul` result: `f(a) * g(b)`
4. `pAdd` sees `+` → `pAdd.right` → `pMul` → `pApp` → `c`
5. Final: `(f(a) * g(b)) + c` ✓

### `chainl1` combinator

This is a standard combinator we'd need to add:

```fsharp
/// Left-associative binary operator chain
/// chainl1 pOperand pOp parses: operand (op operand)*
let chainl1 (pOperand: TParser<Expression>) (pOp: TParser<Operator>) : TParser<Expression> =
    pOperand >>= fun first ->
        let rec loop acc =
            (pOp .>>. pOperand >>= fun (op, right) ->
                loop (OperatorExpression { Left = acc; Operator = op; Right = right })
            ) <|> preturn acc
        loop first
```

### Negative literals

Currently handled by a pre-pass `foldNegativeLiterals` that rewrites `-5` into `IntValue -5`. With proper precedence, this could instead be a unary minus parser between `pAtom` and `pApp`:

```fsharp
let pUnary =
    (expect Subtraction >>. pAtom |>> fun e -> negate e) <|> pAtom
```

But this changes `-5` from a literal to a unary op applied to `5`. Depends whether we want negative literals in the AST or unary negation expressions. The current approach (fold into literal) is simpler for codegen.

### Decision: Follow F#'s Precedence and Associativity

Furst follows F#'s operator precedence and associativity rules. Full table (high to low):

| Level | Operators | Associativity | Tokeniser support |
|-------|-----------|---------------|-------------------|
| 1 | Function application `f x` | Left | ✓ |
| 2 | Unary minus `-x` | Prefix | ✓ (Subtraction token) |
| 3 | Multiplicative `* / %` | Left | `*` only |
| 4 | Additive `+ -` | Left | ✓ |
| 5 | Comparison `< > <= >=` | Left | `<` `>` only |
| 6 | Equality `= <>` | Left | Not yet |
| 7 | Boolean AND `&&` | Left | Not yet |
| 8 | Boolean OR `\|\|` | Left | Not yet |
| 9 | Pipe `\|> <\|` | Left / Right | `\|` exists but not `\|>` |

The combinator chain implements all currently-tokenised levels. Adding future levels is just inserting one `chainl1` between existing layers:

```fsharp
let pAtom    = pNumber <|> pIdentifier <|> pParenExpr
let pApp     = pAtom .>>. many pAtom |>> resolveApp      // tightest
let pUnary   = (expect Subtraction >>. pApp |>> negate) <|> pApp
let pMul     = chainl1 pUnary pMulOp
let pAdd     = chainl1 pMul pAddOp
let pCompare = chainl1 pAdd pCompareOp                   // future: just uncomment
let pExpr    = pAdd                                       // or pCompare when ready
```

### Decision: Mod Validation in the Parser

Mod body validation belongs in the parser, not a post-parse check. If you're inside `pModBody`, the `choice` simply doesn't include `pMod` or `pLib`:

```fsharp
let pModBody = many1 (parseRowWith pModBodyConstruct)

let pModBodyConstruct =
    choice [
        pFunctionDef    // allowed
        pLetBinding     // allowed
        pStructDef      // allowed
        pOpenDecl       // allowed
        pExpr           // allowed
        // pModDecl     — NOT included → parse error if encountered
        // pLibDecl     — NOT included → parse error if encountered
    ]
```

Invalid constructs never parse rather than parsing then being rejected. Error message comes naturally: "Unexpected token 'mod'" at the right source location.

### Decision: Negative Literals — Fold When Possible, Negate Otherwise

Both approaches, depending on context. The unary minus parser checks what it's negating:

- `-5` → fold into `IntLiteral -5` (it's a literal, keep it simple)
- `-x` → `NegateExpression (Identifier "x")` (can't fold)
- `-f a` → `NegateExpression (FunctionCall ...)` (negating a call result)

```fsharp
let pUnary =
    (expect Subtraction >>. pApp
     |>> fun expr ->
         match expr with
         | LiteralExpression (IntLiteral i) -> LiteralExpression (IntLiteral -i)
         | LiteralExpression (FloatLiteral f) -> LiteralExpression (FloatLiteral -f)
         | other -> NegateExpression other)
    <|> pApp
```

This means the AST needs a new `NegateExpression of Expression` variant. But the common case (`-5`) stays as a plain literal — no overhead for codegen. The backend only needs to handle `NegateExpression` for the dynamic cases.

The current `foldNegativeLiterals` pre-pass goes away entirely — the precedence layer handles it naturally.

### All Unresolved Questions Settled

The design is complete. Summary of key decisions:
1. **Custom FParsec-style operator combinators** over token lists (no CEs)
2. **Strict definition order** — define before use, symbol table built during parsing
3. **Struct `and` chains** — reactive pending refs, existence-check only, no deferred AST types
4. **Self-referential structs** — register name before body parsing
5. **Parser operates on Row trees** — two-level (row dispatch + token-level combinators)
6. **F# precedence/associativity** — layered `chainl1` combinators
7. **Mod validation in parser** — restricted `choice` in mod body
8. **Negative literals** — fold into literal when possible, `NegateExpression` otherwise
9. **Serialize symbol table** in .fso
10. **No generic types** for now

## Full Sketch

Pseudo-ish F# showing how all the pieces fit together. Not compilable as-is but close enough to be a blueprint.

### Core Types & Plumbing

```fsharp
module TokenParsers

open Types
open Ast

// --- Symbol table ---

type SymbolEntry =
    | TypeRegistered                          // name known, body not yet parsed
    | TypeDefined of StructDefinition         // fully parsed struct
    | FuncDefined of FunctionDetails
    | VarDefined of LetBinding

type SymbolTable = Map<string, SymbolEntry>

// --- Parser state ---

type ParseState = {
    Symbols: SymbolTable
    PendingTypeRefs: Map<string, SourceLocation list>   // empty unless mid-struct/and
    ScopeDepth: int
}

module ParseState =
    let empty = { Symbols = Map.empty; PendingTypeRefs = Map.empty; ScopeDepth = 0 }

// --- Result type ---

type ParseResult<'a> =
    | POk of value: 'a * remaining: TokenWithMetadata list * state: ParseState
    | PError of CompileError

// --- The parser function type ---

type TParser<'a> = TokenWithMetadata list -> ParseState -> ParseResult<'a>
```

### Combinator Operators

```fsharp
// --- Core bind/map ---

let (>>=) (p: TParser<'a>) (f: 'a -> TParser<'b>) : TParser<'b> =
    fun toks st ->
        match p toks st with
        | POk (v, rest, st') -> f v rest st'
        | PError e -> PError e

let (|>>) (p: TParser<'a>) (f: 'a -> 'b) : TParser<'b> =
    fun toks st ->
        match p toks st with
        | POk (v, rest, st') -> POk (f v, rest, st')
        | PError e -> PError e

// --- Sequencing ---

let (>>.)  p1 p2 = p1 >>= fun _ -> p2                           // keep right
let (.>>)  p1 p2 = p1 >>= fun a -> p2 |>> fun _ -> a            // keep left
let (.>>.) p1 p2 = p1 >>= fun a -> p2 |>> fun b -> (a, b)       // keep both

// --- Choice ---

let (<|>) (p1: TParser<'a>) (p2: TParser<'a>) : TParser<'a> =
    fun toks st ->
        match p1 toks st with
        | POk _ as ok -> ok
        | PError _ -> p2 toks st

// --- Helpers ---

let preturn (v: 'a) : TParser<'a> =
    fun toks st -> POk (v, toks, st)

let pfail (msg: string) : TParser<'a> =
    fun toks _ ->
        match toks with
        | t :: _ -> PError { Message = msg; Line = t.Line; Column = t.Column; Length = t.Length }
        | [] -> PError (CompileError.Empty msg)
```

### Composition Combinators

```fsharp
let optional (p: TParser<'a>) : TParser<'a option> =
    (p |>> Some) <|> preturn None

let rec many (p: TParser<'a>) : TParser<'a list> =
    fun toks st ->
        match p toks st with
        | PError _ -> POk ([], toks, st)
        | POk (v, rest, st') ->
            match many p rest st' with
            | POk (vs, rest', st'') -> POk (v :: vs, rest', st'')
            | PError e -> PError e

let many1 (p: TParser<'a>) : TParser<'a list> =
    p >>= fun first ->
    many p |>> fun rest ->
    first :: rest

let choice (parsers: TParser<'a> list) : TParser<'a> =
    parsers |> List.reduce (<|>)

/// Left-associative operator chain: operand (op operand)*
let chainl1 (pOperand: TParser<Expression>) (pOp: TParser<Operator>) : TParser<Expression> =
    pOperand >>= fun first ->
        let rec loop acc =
            (pOp .>>. pOperand >>= fun (op, right) ->
                loop (OperatorExpression { Left = acc; Operator = op; Right = right })
            ) <|> preturn acc
        loop first
```

### Primitive Token Parsers

```fsharp
let expect (tok: Tokens) : TParser<TokenWithMetadata> =
    fun toks st ->
        match toks with
        | t :: rest when t.Token = tok -> POk (t, rest, st)
        | t :: _ -> PError { Message = $"Expected {tok}, got {t.Token}"; Line = t.Line; Column = t.Column; Length = t.Length }
        | [] -> PError (CompileError.Empty $"Expected {tok}")

let expectName : TParser<string> =
    fun toks st ->
        match toks with
        | t :: rest ->
            match t.Token with
            | Name (Word n) -> POk (n, rest, st)
            | _ -> PError { Message = $"Expected identifier, got {t.Token}"; Line = t.Line; Column = t.Column; Length = t.Length }
        | [] -> PError (CompileError.Empty "Expected identifier")

let expectNameOrQualified : TParser<string list> =
    fun toks st ->
        match toks with
        | t :: rest ->
            match t.Token with
            | Name (Word n) -> POk ([n], rest, st)
            | QualifiedName parts -> POk (parts, rest, st)
            | _ -> PError { Message = $"Expected name"; Line = t.Line; Column = t.Column; Length = t.Length }
        | [] -> PError (CompileError.Empty "Expected name")

let expectNumber : TParser<LiteralValue> =
    fun toks st ->
        match toks with
        | t :: rest ->
            match t.Token with
            | NumberLiteral (IntValue i) -> POk (IntLiteral i, rest, st)
            | NumberLiteral (FloatValue f) -> POk (FloatLiteral f, rest, st)
            | _ -> PError { Message = "Expected number"; Line = t.Line; Column = t.Column; Length = t.Length }
        | [] -> PError (CompileError.Empty "Expected number")

let expectType : TParser<TypeDefinitions> =
    fun toks st ->
        match toks with
        | t :: rest ->
            match t.Token with
            | TypeDefinition td -> POk (td, rest, st)
            | _ -> PError { Message = "Expected type"; Line = t.Line; Column = t.Column; Length = t.Length }
        | [] -> PError (CompileError.Empty "Expected type")

let isAtEnd : TParser<bool> =
    fun toks st -> POk (toks.IsEmpty, toks, st)

let peek : TParser<Tokens option> =
    fun toks st ->
        match toks with
        | t :: _ -> POk (Some t.Token, toks, st)
        | [] -> POk (None, toks, st)
```

### State Combinators

```fsharp
let registerSymbol name entry : TParser<unit> =
    fun toks st ->
        POk ((), toks, { st with Symbols = st.Symbols |> Map.add name entry })

let registerType name : TParser<unit> =
    registerSymbol name TypeRegistered

let updateType name def : TParser<unit> =
    registerSymbol name (TypeDefined def)

/// For identifiers in let/function/call context — immediate error if unknown
let requireSymbol name : TParser<SymbolEntry> =
    fun toks st ->
        match st.Symbols |> Map.tryFind name with
        | Some entry -> POk (entry, toks, st)
        | None -> PError (CompileError.Empty $"'{name}' is not defined")

/// For type references in struct fields — defers if unknown
let assertTypeExists name (loc: SourceLocation) : TParser<unit> =
    fun toks st ->
        // built-in types always exist
        match name with
        | "i32" | "i64" | "float" | "double" | "string" -> POk ((), toks, st)
        | _ ->
            match st.Symbols |> Map.tryFind name with
            | Some _ -> POk ((), toks, st)
            | None ->
                let locs = st.PendingTypeRefs |> Map.tryFind name |> Option.defaultValue []
                let pending' = st.PendingTypeRefs |> Map.add name (loc :: locs)
                POk ((), toks, { st with PendingTypeRefs = pending' })

let checkPendingRefs : TParser<unit> =
    fun toks st ->
        let unresolved =
            st.PendingTypeRefs
            |> Map.filter (fun name _ -> st.Symbols |> Map.containsKey name |> not)
        match unresolved |> Map.toList with
        | [] -> POk ((), toks, { st with PendingTypeRefs = Map.empty })
        | (name, loc :: _) :: _ ->
            PError { Message = $"Type '{name}' is not defined"
                     Line = loc.StartLine; Column = loc.StartCol; Length = TokenLength 0 }
        | _ -> POk ((), toks, st)
```

### Expression Parsers (Precedence Layers)

```fsharp
// --- Atoms ---

let pIdentifier : TParser<Expression> =
    expectName |>> IdentifierExpression

let pNumber : TParser<Expression> =
    expectNumber |>> LiteralExpression

let rec pParenExpr : TParser<Expression> =
    expect OpenParen >>. pExpr .>> expect ClosedParen

// --- Atoms combined ---

and pAtom : TParser<Expression> =
    choice [ pNumber; pParenExpr; pIdentifier ]
    // pIdentifier last — it's the most general, acts as fallback

// --- Function application (tightest binding) ---
// f a b  →  FunctionCall("f", [a; b])
// f      →  just an identifier (no call)

and pApp : TParser<Expression> =
    pAtom >>= fun head ->
        many pAtom |>> fun args ->
            match head, args with
            | _, [] -> head                                     // just an atom
            | IdentifierExpression name, args ->
                FunctionCallExpression { FunctionName = name; Arguments = args }
            | _ -> head                                         // non-identifier in call position, just return it

// --- Unary minus ---

and pUnary : TParser<Expression> =
    (expect Subtraction >>. pApp |>> fun expr ->
        match expr with
        | LiteralExpression (IntLiteral i)   -> LiteralExpression (IntLiteral -i)
        | LiteralExpression (FloatLiteral f) -> LiteralExpression (FloatLiteral -f)
        | other -> NegateExpression other)
    <|> pApp

// --- Operators ---

and pMulOp = expect Multiply |>> fun _ -> Operator.Multiply
and pAddOp =
    (expect Addition |>> fun _ -> Operator.Add)
    <|> (expect Subtraction |>> fun _ -> Operator.Subtract)

// --- Precedence chain ---

and pMul  = chainl1 pUnary pMulOp          // * binds tighter
and pAdd  = chainl1 pMul   pAddOp          // + - bind looser

and pExpr = pAdd                            // top-level expression entry point
```

### Statement / Declaration Parsers

```fsharp
// --- Parameters ---

let pTypedParam : TParser<ParameterExpression> =
    expect OpenParen >>. expectName .>> expect TypeIdentifier .>>. expectType .>> expect ClosedParen
    |>> fun (name, typ) -> { Name = Word name; Type = typ }

let pUntypedParam : TParser<ParameterExpression> =
    expectName |>> fun name -> { Name = Word name; Type = Inferred }

let pParam = pTypedParam <|> pUntypedParam
let pParams = many1 pParam

// --- Let binding: let x = expr ---

let pLetBinding : TParser<Expression> =
    expect Let >>. expectName .>> expect Assignment .>>. pExpr
    |>> fun (name, value) ->
        LetBindingExpression { Name = name; Type = Inferred; Value = value }

// --- Lib / Open ---

let pLibDecl : TParser<Expression> =
    expect Lib >>. expectNameOrQualified |>> LibDeclaration

let pOpenDecl : TParser<Expression> =
    expect Open >>. expectNameOrQualified |>> OpenDeclaration

// --- Struct field ---

let pStructField : TParser<string * TypeDefinitions> =
    expectName .>> expect TypeIdentifier .>>. expectType
    // after parsing the type, assert it exists if UserDefined
    >>= fun (name, typ) ->
        match typ with
        | UserDefined typeName ->
            let loc = ... // source location of the type token
            assertTypeExists typeName loc |>> fun () -> (name, typ)
        | _ -> preturn (name, typ)

let pStructBody : TParser<(string * TypeDefinitions) list> =
    expect OpenBrace >>. many1 pStructField .>> expect ClosedBrace
```

### Row-Level Parser (The Dispatch Layer)

```fsharp
/// Run a token-level parser on a row's expressions
let runOnRow (p: TParser<'a>) (row: Row) (state: ParseState) : Result<'a * ParseState, CompileError> =
    match p row.Expressions state with
    | POk (v, remaining, st') ->
        match remaining with
        | [] -> Ok (v, st')
        | t :: _ -> Error { Message = $"Unexpected token: {t.Token}"; Line = t.Line; Column = t.Column; Length = t.Length }
    | PError e -> Error e

/// Parse a single row into an ExpressionNode
let rec parseRow (row: Row) (state: ParseState) : Result<ExpressionNode * ParseState, CompileError> =
    let loc = rowLocation row
    let tokens = row.Expressions |> List.map _.Token

    match tokens with
    // --- lib ---
    | Lib :: _ ->
        runOnRow pLibDecl row state
        |> Result.map (fun (expr, st) -> { Expr = expr; Location = loc }, st)

    // --- open ---
    | Open :: _ ->
        runOnRow pOpenDecl row state
        |> Result.map (fun (expr, st) -> { Expr = expr; Location = loc }, st)

    // --- mod (with optional body) ---
    | Mod :: _ ->
        parseMod row state

    // --- struct (with optional and chain) ---
    | Struct :: _ ->
        parseStructChain row state

    // --- private let ... ---
    | Private :: Let :: _ ->
        parseLetOrFunc Visibility.Private row state

    // --- let ... ---
    | Let :: _ ->
        parseLetOrFunc Visibility.Public row state

    // --- bare expression ---
    | _ ->
        runOnRow pExpr row state
        |> Result.map (fun (expr, st) -> { Expr = expr; Location = loc }, st)

/// Decide: is this a function def or a let binding?
and parseLetOrFunc (vis: Visibility) (row: Row) (state: ParseState) : Result<ExpressionNode * ParseState, CompileError> =
    let loc = rowLocation row
    if row.Body.IsEmpty then
        // no body → let binding
        // strip optional Private token before running
        let p = (if vis = Private then expect Private >>. pLetBinding else pLetBinding)
        runOnRow p row state
        |> Result.map (fun (expr, st) ->
            let st' = // register the binding
                match expr with
                | LetBindingExpression lb -> { st with Symbols = st.Symbols |> Map.add lb.Name (VarDefined lb) }
                | _ -> st
            { Expr = expr; Location = loc }, st')
    else
        // has body → function definition
        parseFunctionDef vis row state

/// Parse function: header from row.Expressions, body from row.Body
and parseFunctionDef (vis: Visibility) (row: Row) (state: ParseState) : Result<ExpressionNode * ParseState, CompileError> =
    let loc = rowLocation row

    // parse header tokens: [private] let name params =
    let headerParser =
        (if vis = Private then expect Private >>. expect Let else expect Let)
        >>. expectName .>>. pParams .>> expect Assignment

    match runOnRow headerParser row state with
    | Error e -> Error e
    | Ok ((name, parameters), headerState) ->
        // recurse into body rows
        match parseBody row.Body headerState with
        | Error e -> Error e
        | Ok (bodyExprs, bodyState) ->
            let details = {
                Identifier = name
                Type = Inferred
                Parameters = parameters
                Body = BodyExpression bodyExprs
                Visibility = vis
            }
            let st' = { bodyState with Symbols = bodyState.Symbols |> Map.add name (FuncDefined details) }
            Ok ({ Expr = FunctionDefinitionExpression (FunctionDefinition details); Location = loc }, st')

/// Parse mod: header from row.Expressions, body from row.Body
and parseMod (row: Row) (state: ParseState) : Result<ExpressionNode * ParseState, CompileError> =
    let loc = rowLocation row
    match runOnRow (expect Mod >>. expectNameOrQualified) row state with
    | Error e -> Error e
    | Ok (parts, st) ->
        match row.Body with
        | [] -> Ok ({ Expr = ModuleDeclaration (parts, []); Location = loc }, st)
        | body ->
            // parse body with restricted construct set (no mod, no lib)
            match parseModBody body st with
            | Error e -> Error e
            | Ok (bodyExprs, st') ->
                Ok ({ Expr = ModuleDeclaration (parts, bodyExprs); Location = loc }, st')

/// Parse body rows — used by functions and top-level
and parseBody (rows: Row list) (state: ParseState) : Result<Expression list * ParseState, CompileError> =
    let rec loop acc st remaining =
        match remaining with
        | [] -> Ok (List.rev acc, st)
        | row :: rest ->
            match parseRow row st with
            | Error e -> Error e
            | Ok (node, st') -> loop (node.Expr :: acc) st' rest
    loop [] state rows

/// Parse mod body — restricted: no mod or lib allowed
and parseModBody (rows: Row list) (state: ParseState) : Result<Expression list * ParseState, CompileError> =
    let rec loop acc st remaining =
        match remaining with
        | [] -> Ok (List.rev acc, st)
        | row :: rest ->
            let tokens = row.Expressions |> List.map _.Token
            match tokens with
            | Mod :: _ ->
                let (line, col, len) = getFirstTokenPos row
                Error { Message = "Nested mod declarations are not allowed"; Line = line; Column = col; Length = len }
            | Lib :: _ ->
                let (line, col, len) = getFirstTokenPos row
                Error { Message = "lib declarations are not allowed inside mod"; Line = line; Column = col; Length = len }
            | _ ->
                match parseRow row st with
                | Error e -> Error e
                | Ok (node, st') -> loop (node.Expr :: acc) st' rest
    loop [] state rows

/// Parse struct, then check for and-chain
and parseStructChain (row: Row) (state: ParseState) : Result<ExpressionNode * ParseState, CompileError> =
    let loc = rowLocation row

    // parse: struct Name { fields }
    let structParser =
        expect Struct >>. expectName
        >>= fun name ->
            registerType name
            >>. pStructBody
            >>= fun fields ->
                let def = { Name = name; Fields = fields }
                updateType name def
                |>> fun () -> StructExpression def

    match runOnRow structParser row state with
    | Error e -> Error e
    | Ok (expr, st) ->
        // TODO: check for `and` on the next row in the parent's row list
        // for now, just check pending refs
        match checkPendingRefs row.Expressions st with   // reuse tokens for position
        | POk ((), _, st') -> Ok ({ Expr = expr; Location = loc }, st')
        | PError e -> Error e
```

### Entry Point

```fsharp
/// Parse a full file: Row list → ExpressionNode list
let parseFile (rows: Row list) : Result<ExpressionNode list * ParseState, CompileError> =
    let rec loop acc state remaining =
        match remaining with
        | [] -> Ok (List.rev acc, state)
        | row :: rest ->
            match parseRow row state with
            | Error e -> Error e
            | Ok (node, state') -> loop (node :: acc) state' rest
    loop [] ParseState.empty rows
```

## Full Concrete Sketch

Showing what the whole parser looks like end-to-end. Not compilable as-is but close enough to be a blueprint.

### Core Types & Plumbing

```fsharp
module TokenParsers

open Types
open Ast

// --- Symbol table entries ---

type SymbolEntry =
    | TypeRegistered                          // name known, body not parsed yet
    | TypeDefined of StructDefinition         // fully parsed struct
    | FuncDefined of FunctionDetails
    | VarDefined of LetBinding

type SymbolTable = Map<string, SymbolEntry>

// --- Parser state threaded through everything ---

type ParseState = {
    Symbols: SymbolTable
    PendingTypeRefs: Map<string, SourceLocation list>   // empty unless mid-struct/and
    ScopeDepth: int
}

module ParseState =
    let empty = { Symbols = Map.empty; PendingTypeRefs = Map.empty; ScopeDepth = 0 }

// --- Result ---

type ParseResult<'a> =
    | POk of value: 'a * remaining: TokenWithMetadata list * state: ParseState
    | PError of CompileError

// --- The parser function type: tokens in, result out, state threaded ---

type TParser<'a> = TokenWithMetadata list -> ParseState -> ParseResult<'a>
```

### Combinator Operators

```fsharp
// --- Bind and map: the two building blocks everything else is made from ---

let (>>=) (p: TParser<'a>) (f: 'a -> TParser<'b>) : TParser<'b> =
    fun toks st ->
        match p toks st with
        | POk (v, rest, st') -> f v rest st'
        | PError e -> PError e

let (|>>) (p: TParser<'a>) (f: 'a -> 'b) : TParser<'b> =
    fun toks st ->
        match p toks st with
        | POk (v, rest, st') -> POk (f v, rest, st')
        | PError e -> PError e

// --- Sequencing ---

let (>>.)  p1 p2 = p1 >>= fun _ -> p2                           // keep right
let (.>>)  p1 p2 = p1 >>= fun a -> p2 |>> fun _ -> a            // keep left
let (.>>.) p1 p2 = p1 >>= fun a -> p2 |>> fun b -> (a, b)       // keep both

// --- Choice: try p1, if it fails try p2 ---

let (<|>) (p1: TParser<'a>) (p2: TParser<'a>) : TParser<'a> =
    fun toks st ->
        match p1 toks st with
        | POk _ as ok -> ok
        | PError _ -> p2 toks st

// --- Helpers ---

let preturn (v: 'a) : TParser<'a> =
    fun toks st -> POk (v, toks, st)

let pfail (msg: string) : TParser<'a> =
    fun toks _ ->
        match toks with
        | t :: _ -> PError { Message = msg; Line = t.Line; Column = t.Column; Length = t.Length }
        | [] -> PError (CompileError.Empty msg)
```

### Composition Combinators

```fsharp
let optional (p: TParser<'a>) : TParser<'a option> =
    (p |>> Some) <|> preturn None

let rec many (p: TParser<'a>) : TParser<'a list> =
    fun toks st ->
        match p toks st with
        | PError _ -> POk ([], toks, st)
        | POk (v, rest, st') ->
            match many p rest st' with
            | POk (vs, rest', st'') -> POk (v :: vs, rest', st'')
            | PError e -> PError e

let many1 (p: TParser<'a>) : TParser<'a list> =
    p >>= fun first ->
    many p |>> fun rest ->
    first :: rest

let choice (parsers: TParser<'a> list) : TParser<'a> =
    parsers |> List.reduce (<|>)

/// Left-associative operator chain: operand (op operand)*
let chainl1 (pOperand: TParser<Expression>) (pOp: TParser<Operator>) : TParser<Expression> =
    pOperand >>= fun first ->
        let rec loop acc =
            (pOp .>>. pOperand >>= fun (op, right) ->
                loop (OperatorExpression { Left = acc; Operator = op; Right = right })
            ) <|> preturn acc
        loop first
```

### Primitive Token Parsers

```fsharp
let expect (tok: Tokens) : TParser<TokenWithMetadata> =
    fun toks st ->
        match toks with
        | t :: rest when t.Token = tok -> POk (t, rest, st)
        | t :: _ -> PError { Message = $"Expected {tok}, got {t.Token}"
                             Line = t.Line; Column = t.Column; Length = t.Length }
        | [] -> PError (CompileError.Empty $"Expected {tok}")

let expectName : TParser<string> =
    fun toks st ->
        match toks with
        | t :: rest ->
            match t.Token with
            | Name (Word n) -> POk (n, rest, st)
            | _ -> PError { Message = $"Expected identifier, got {t.Token}"
                            Line = t.Line; Column = t.Column; Length = t.Length }
        | [] -> PError (CompileError.Empty "Expected identifier")

let expectNameOrQualified : TParser<string list> =
    fun toks st ->
        match toks with
        | t :: rest ->
            match t.Token with
            | Name (Word n) -> POk ([n], rest, st)
            | QualifiedName parts -> POk (parts, rest, st)
            | _ -> PError { Message = "Expected name"
                            Line = t.Line; Column = t.Column; Length = t.Length }
        | [] -> PError (CompileError.Empty "Expected name")

let expectNumber : TParser<LiteralValue> =
    fun toks st ->
        match toks with
        | t :: rest ->
            match t.Token with
            | NumberLiteral (IntValue i) -> POk (IntLiteral i, rest, st)
            | NumberLiteral (FloatValue f) -> POk (FloatLiteral f, rest, st)
            | _ -> PError { Message = "Expected number"
                            Line = t.Line; Column = t.Column; Length = t.Length }
        | [] -> PError (CompileError.Empty "Expected number")

let expectType : TParser<TypeDefinitions> =
    fun toks st ->
        match toks with
        | t :: rest ->
            match t.Token with
            | TypeDefinition td -> POk (td, rest, st)
            | _ -> PError { Message = "Expected type"
                            Line = t.Line; Column = t.Column; Length = t.Length }
        | [] -> PError (CompileError.Empty "Expected type")

let peek : TParser<Tokens option> =
    fun toks st ->
        match toks with
        | t :: _ -> POk (Some t.Token, toks, st)
        | [] -> POk (None, toks, st)
```

### State Combinators

```fsharp
let registerSymbol name entry : TParser<unit> =
    fun toks st ->
        POk ((), toks, { st with Symbols = st.Symbols |> Map.add name entry })

let registerType name : TParser<unit> =
    registerSymbol name TypeRegistered

let updateType name def : TParser<unit> =
    registerSymbol name (TypeDefined def)

/// Identifiers in let/function/call context — immediate error if unknown
let requireSymbol name : TParser<SymbolEntry> =
    fun toks st ->
        match st.Symbols |> Map.tryFind name with
        | Some entry -> POk (entry, toks, st)
        | None -> PError (CompileError.Empty $"'{name}' is not defined")

/// Type references in struct fields — defers if unknown (for and-chains)
let assertTypeExists name (loc: SourceLocation) : TParser<unit> =
    fun toks st ->
        match st.Symbols |> Map.tryFind name with
        | Some _ -> POk ((), toks, st)
        | None ->
            let locs = st.PendingTypeRefs |> Map.tryFind name |> Option.defaultValue []
            let pending' = st.PendingTypeRefs |> Map.add name (loc :: locs)
            POk ((), toks, { st with PendingTypeRefs = pending' })

let checkPendingRefs : TParser<unit> =
    fun toks st ->
        let unresolved =
            st.PendingTypeRefs
            |> Map.filter (fun name _ -> st.Symbols |> Map.containsKey name |> not)
        match unresolved |> Map.toList with
        | [] -> POk ((), toks, { st with PendingTypeRefs = Map.empty })
        | (name, loc :: _) :: _ ->
            PError { Message = $"Type '{name}' is not defined"
                     Line = loc.StartLine; Column = loc.StartCol; Length = TokenLength 0 }
        | _ -> POk ((), toks, st)
```

### Expression Parsers (Precedence Layers)

```fsharp
let pIdentifier : TParser<Expression> =
    expectName |>> IdentifierExpression

let pNumber : TParser<Expression> =
    expectNumber |>> LiteralExpression

let rec pParenExpr : TParser<Expression> =
    expect OpenParen >>. pExpr .>> expect ClosedParen

and pAtom : TParser<Expression> =
    choice [ pNumber; pParenExpr; pIdentifier ]

// function application: f a b → FunctionCall("f", [a, b])
// just f with no args → IdentifierExpression "f"
and pApp : TParser<Expression> =
    pAtom >>= fun head ->
        many pAtom |>> fun args ->
            match head, args with
            | _, [] -> head
            | IdentifierExpression name, args ->
                FunctionCallExpression { FunctionName = name; Arguments = args }
            | _ -> head

// unary minus: -5 folds to literal, -x becomes NegateExpression
and pUnary : TParser<Expression> =
    (expect Subtraction >>. pApp |>> fun expr ->
        match expr with
        | LiteralExpression (IntLiteral i)   -> LiteralExpression (IntLiteral -i)
        | LiteralExpression (FloatLiteral f) -> LiteralExpression (FloatLiteral -f)
        | other -> NegateExpression other)
    <|> pApp

and pMulOp = expect Multiply |>> fun _ -> Operator.Multiply
and pAddOp =
    (expect Addition    |>> fun _ -> Operator.Add)
    <|> (expect Subtraction |>> fun _ -> Operator.Subtract)

// precedence chain: mul binds tighter than add
and pMul  = chainl1 pUnary pMulOp
and pAdd  = chainl1 pMul   pAddOp

// top-level expression entry point
and pExpr = pAdd
```

### Statement Parsers

```fsharp
// --- Parameters ---

let pTypedParam : TParser<ParameterExpression> =
    expect OpenParen >>. expectName .>> expect TypeIdentifier .>>. expectType .>> expect ClosedParen
    |>> fun (name, typ) -> { Name = Word name; Type = typ }

let pUntypedParam : TParser<ParameterExpression> =
    expectName |>> fun name -> { Name = Word name; Type = Inferred }

let pParam = pTypedParam <|> pUntypedParam
let pParams = many1 pParam

// --- Let binding: let x = expr ---

let pLetBinding : TParser<Expression> =
    expect Let >>. expectName .>> expect Assignment .>>. pExpr
    |>> fun (name, value) ->
        LetBindingExpression { Name = name; Type = Inferred; Value = value }

// --- Simple declarations ---

let pLibDecl : TParser<Expression> =
    expect Lib >>. expectNameOrQualified |>> LibDeclaration

let pOpenDecl : TParser<Expression> =
    expect Open >>. expectNameOrQualified |>> OpenDeclaration

// --- Struct fields with type existence checking ---

let pStructField : TParser<string * TypeDefinitions> =
    expectName .>> expect TypeIdentifier .>>. expectType
    >>= fun (name, typ) ->
        match typ with
        | UserDefined typeName ->
            assertTypeExists typeName (*loc*) |>> fun () -> (name, typ)
        | _ -> preturn (name, typ)

let pStructBody : TParser<(string * TypeDefinitions) list> =
    expect OpenBrace >>. many1 pStructField .>> expect ClosedBrace
```

### Row-Level Parser (The Dispatch Layer)

```fsharp
/// Run a token-level parser against a row's expression tokens
let runOnRow (p: TParser<'a>) (row: Row) (state: ParseState) : Result<'a * ParseState, CompileError> =
    match p row.Expressions state with
    | POk (v, [], st') -> Ok (v, st')
    | POk (_, t :: _, _) ->
        Error { Message = $"Unexpected token: {t.Token}"
                Line = t.Line; Column = t.Column; Length = t.Length }
    | PError e -> Error e

/// Parse a single row into an ExpressionNode
let rec parseRow (row: Row) (state: ParseState) : Result<ExpressionNode * ParseState, CompileError> =
    let loc = rowLocation row
    let tokens = row.Expressions |> List.map _.Token

    match tokens with
    | Lib :: _ ->
        runOnRow pLibDecl row state
        |> Result.map (fun (expr, st) -> { Expr = expr; Location = loc }, st)

    | Open :: _ ->
        runOnRow pOpenDecl row state
        |> Result.map (fun (expr, st) -> { Expr = expr; Location = loc }, st)

    | Mod :: _ ->
        parseMod row state

    | Struct :: _ ->
        parseStructChain row state

    | Private :: Let :: _ ->
        parseLetOrFunc Visibility.Private row state

    | Let :: _ ->
        parseLetOrFunc Visibility.Public row state

    | _ ->
        runOnRow pExpr row state
        |> Result.map (fun (expr, st) -> { Expr = expr; Location = loc }, st)

/// Function vs let-binding: has body → function, no body → let binding
and parseLetOrFunc (vis: Visibility) (row: Row) (state: ParseState) : Result<ExpressionNode * ParseState, CompileError> =
    let loc = rowLocation row
    if row.Body.IsEmpty then
        let p =
            if vis = Private then expect Private >>. pLetBinding
            else pLetBinding
        runOnRow p row state
        |> Result.map (fun (expr, st) ->
            match expr with
            | LetBindingExpression lb ->
                let st' = { st with Symbols = st.Symbols |> Map.add lb.Name (VarDefined lb) }
                { Expr = expr; Location = loc }, st'
            | _ ->
                { Expr = expr; Location = loc }, st)
    else
        parseFunctionDef vis row state

/// Parse function: header from row.Expressions, body by recursing into row.Body
and parseFunctionDef (vis: Visibility) (row: Row) (state: ParseState) : Result<ExpressionNode * ParseState, CompileError> =
    let loc = rowLocation row

    let headerParser =
        (if vis = Private then expect Private >>. expect Let else expect Let)
        >>. expectName .>>. pParams .>> expect Assignment

    match runOnRow headerParser row state with
    | Error e -> Error e
    | Ok ((name, parameters), headerState) ->
        match parseBody row.Body headerState with
        | Error e -> Error e
        | Ok (bodyExprs, bodyState) ->
            let details = {
                Identifier = name; Type = Inferred
                Parameters = parameters
                Body = BodyExpression bodyExprs
                Visibility = vis
            }
            let st' = { bodyState with
                            Symbols = bodyState.Symbols |> Map.add name (FuncDefined details) }
            Ok ({ Expr = FunctionDefinitionExpression (FunctionDefinition details)
                  Location = loc }, st')

/// Parse mod: header from tokens, body by recursing into row.Body
and parseMod (row: Row) (state: ParseState) : Result<ExpressionNode * ParseState, CompileError> =
    let loc = rowLocation row
    match runOnRow (expect Mod >>. expectNameOrQualified) row state with
    | Error e -> Error e
    | Ok (parts, st) ->
        match row.Body with
        | [] -> Ok ({ Expr = ModuleDeclaration (parts, []); Location = loc }, st)
        | body ->
            match parseModBody body st with
            | Error e -> Error e
            | Ok (bodyExprs, st') ->
                Ok ({ Expr = ModuleDeclaration (parts, bodyExprs); Location = loc }, st')

/// Parse body rows — threading state through each
and parseBody (rows: Row list) (state: ParseState) : Result<Expression list * ParseState, CompileError> =
    let rec loop acc st = function
        | [] -> Ok (List.rev acc, st)
        | row :: rest ->
            match parseRow row st with
            | Error e -> Error e
            | Ok (node, st') -> loop (node.Expr :: acc) st' rest
    loop [] state rows

/// Mod body — same as parseBody but rejects mod/lib before parsing
and parseModBody (rows: Row list) (state: ParseState) : Result<Expression list * ParseState, CompileError> =
    let rec loop acc st = function
        | [] -> Ok (List.rev acc, st)
        | row :: rest ->
            match row.Expressions |> List.map _.Token with
            | Mod :: _ ->
                let (line, col, len) = getFirstTokenPos row
                Error { Message = "Nested mod not allowed"; Line = line; Column = col; Length = len }
            | Lib :: _ ->
                let (line, col, len) = getFirstTokenPos row
                Error { Message = "lib not allowed inside mod"; Line = line; Column = col; Length = len }
            | _ ->
                match parseRow row st with
                | Error e -> Error e
                | Ok (node, st') -> loop (node.Expr :: acc) st' rest
    loop [] state rows

/// Struct with and-chain handling
and parseStructChain (row: Row) (state: ParseState) : Result<ExpressionNode * ParseState, CompileError> =
    let loc = rowLocation row

    let structParser =
        expect Struct >>. expectName
        >>= fun name ->
            registerType name
            >>. pStructBody
            >>= fun fields ->
                let def = { Name = name; Fields = fields }
                updateType name def
                |>> fun () -> StructExpression def

    match runOnRow structParser row state with
    | Error e -> Error e
    | Ok (expr, st) ->
        // TODO: and-chain — check sibling rows for `and` token
        // for now just check pending refs
        match checkPendingRefs [] st with
        | POk ((), _, st') -> Ok ({ Expr = expr; Location = loc }, st')
        | PError e -> Error e
```

### Entry Point

```fsharp
/// Parse a full file: Row list from lexer → ExpressionNode list + final state
let parseFile (rows: Row list) : Result<ExpressionNode list * ParseState, CompileError> =
    let rec loop acc state = function
        | [] -> Ok (List.rev acc, state)
        | row :: rest ->
            match parseRow row state with
            | Error e -> Error e
            | Ok (node, state') -> loop (node :: acc) state' rest
    loop [] ParseState.empty rows
```

### What Changed vs Current AstBuilder

| Aspect | Current `AstBuilder` | New parser |
|--------|---------------------|------------|
| Dispatch | One giant `match` on token list | `parseRow` peeks leading tokens, delegates |
| Let vs function | `isFunctionDefinition` predicate | `row.Body.IsEmpty` — no body = let, body = function |
| Expressions | 120-line `parseExpression` with flat fold | `pAtom` → `pApp` → `pUnary` → `pMul` → `pAdd` |
| Precedence | Flat, no precedence | F#-style layers, function app tightest |
| Function call args | `splitArgumentGroups` manual paren depth | `many pAtom` — parens via `pParenExpr` |
| Negative literals | Pre-pass `foldNegativeLiterals` | `pUnary` folds literals, `NegateExpression` for rest |
| Mod validation | Post-parse check in `buildModBody` | `parseModBody` rejects before parsing |
| Symbol tracking | Separate pass (ADR-0001) | Built into state, registered as you go |
| Struct self-ref | Not handled | `registerType` before body |
| Struct `and` | Not implemented | Pending refs + `checkPendingRefs` |

The `and`-chain is the one bit marked TODO — it needs access to sibling rows (the next row after the current struct), which means the dispatch layer needs to be aware of "current row + remaining rows" rather than just "current row." That's a small refactor to `parseBody`/`parseRow` to pass the row list instead of a single row.

## Cross-File Symbol State

The symbol state threads forward naturally — each row's parse produces an updated `ParseState` that feeds into the next. `parseBody` already does this via the `st'` threading in its loop. So when parsing multiple files, file B starts with the `ParseState` that file A ended with. The symbol table accumulates across files in compilation order.

This has an implication for `Compiler.fs`: currently `compileFiles` uses `Parallel.ForEach` to parse files concurrently, then does symbol checking after in a separate pass. With the new parser building the symbol table during parsing, **files must be parsed sequentially** (or at least in dependency order) so that symbols from earlier files are visible to later ones.

This is actually more correct — it matches F#'s behaviour where file order in the `.fsproj` matters and earlier files can't reference later ones. The current parallel approach only works because symbol checking is deferred to a post-parse pass. With symbols integrated into parsing, the sequential model is the natural fit.

```fsharp
/// Compile files in order, threading state across files
let compileFilesSequential (files: string list) : Result<ExpressionNode list * ParseState, CompileError> =
    files |> List.fold (fun acc filePath ->
        match acc with
        | Error e -> Error e
        | Ok (allNodes, state) ->
            match readAndLex filePath with
            | Error e -> Error e
            | Ok rows ->
                match parseFile rows state with    // state from previous file carries in
                | Error e -> Error e
                | Ok (nodes, state') -> Ok (allNodes @ nodes, state')
    ) (Ok ([], ParseState.empty))
```

The `parseFile` function would need a small change — accept an initial `ParseState` instead of always starting from `ParseState.empty`:

```fsharp
let parseFile (rows: Row list) (initialState: ParseState) : Result<ExpressionNode list * ParseState, CompileError> =
    let rec loop acc state = function
        | [] -> Ok (List.rev acc, state)
        | row :: rest ->
            match parseRow row state with
            | Error e -> Error e
            | Ok (node, state') -> loop (node :: acc) state' rest
    loop [] initialState rows
```

### Revised: Parallel Tokenisation, Sequential AST Parsing

**User clarification:** Tokenisation is pure — no state, no cross-file dependencies — so it stays parallel. Only the AST parsing needs to be sequential, threading state across files.

```
Files → [Parallel] → Row lists → [Sequential, threaded state] → AST + SymbolTable
         tokenise                   parse
```

```fsharp
let compileFiles (files: string list) : Result<ExpressionNode list * ParseState, CompileError> =
    // Phase 1: tokenise all files in parallel (no state, no dependencies)
    let tokenResults = ConcurrentDictionary<int, Result<Row list, string>>()
    Parallel.ForEach(files |> List.mapi (fun i f -> (i, f)), fun (i, file) ->
        match readFile file with
        | Error e -> tokenResults.[i] <- Error e
        | Ok source -> tokenResults.[i] <- Lexer.createAST file source
    ) |> ignore

    // Collect in order, bail on first error
    let orderedRows =
        [0 .. files.Length - 1]
        |> List.map (fun i -> tokenResults.[i])
        |> List.fold (fun acc r ->
            match acc, r with
            | Error e, _ -> Error e
            | _, Error e -> Error e
            | Ok rows, Ok fileRows -> Ok (rows @ [fileRows])
        ) (Ok [])

    // Phase 2: parse sequentially, threading symbol state across files
    match orderedRows with
    | Error e -> Error e
    | Ok allFileRows ->
        allFileRows |> List.fold (fun acc rows ->
            match acc with
            | Error e -> Error e
            | Ok (allNodes, state) ->
                match parseFile rows state with
                | Error e -> Error e
                | Ok (nodes, state') -> Ok (allNodes @ nodes, state')
        ) (Ok ([], ParseState.empty))
```

Tokenisation is embarrassingly parallel — each file's lexer is independent. The sequential part is just the AST fold, which is the cheap part (tokens are already produced, it's just pattern matching and state threading).
