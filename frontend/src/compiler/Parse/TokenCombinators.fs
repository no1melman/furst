module TokenCombinators

open Types
open Ast

// --- State ---

type ParseState = {
    Symbols: Map<string, int>            // name -> param count
    PendingTypeRefs: Map<string, SourceLocation list>  // unresolved type names
    ScopeDepth: int
}

let emptyState : ParseState = {
    Symbols = Map.empty
    PendingTypeRefs = Map.empty
    ScopeDepth = 0
}

// --- Result & Parser type ---

type ParseResult<'a> =
    | POk of value: 'a * remaining: TokenWithMetadata list * state: ParseState
    | PError of CompileError

type TParser<'a> = TokenWithMetadata list -> ParseState -> ParseResult<'a>

// --- Core combinators ---

let preturn (x: 'a) : TParser<'a> =
    fun tokens state -> POk (x, tokens, state)

let pfail (msg: string) : TParser<'a> =
    fun tokens _state ->
        let pos =
            match tokens with
            | t :: _ -> t
            | [] -> { Line = Line 0L; Column = Column 0L; Length = TokenLength 0; Token = NoToken }
        PError { Message = msg; Line = pos.Line; Column = pos.Column; Length = pos.Length }

let pfailAt (msg: string) (tok: TokenWithMetadata) : TParser<'a> =
    fun _tokens _state ->
        PError { Message = msg; Line = tok.Line; Column = tok.Column; Length = tok.Length }

/// Bind
let (>>=) (p: TParser<'a>) (f: 'a -> TParser<'b>) : TParser<'b> =
    fun tokens state ->
        match p tokens state with
        | POk (a, rest, state') -> (f a) rest state'
        | PError e -> PError e

/// Map
let (|>>) (p: TParser<'a>) (f: 'a -> 'b) : TParser<'b> =
    p >>= (fun a -> preturn (f a))

/// Sequence, keep right
let (>>.) (p1: TParser<'a>) (p2: TParser<'b>) : TParser<'b> =
    p1 >>= fun _ -> p2

/// Sequence, keep left
let (.>>) (p1: TParser<'a>) (p2: TParser<'b>) : TParser<'a> =
    p1 >>= fun a -> p2 |>> fun _ -> a

/// Sequence, keep both
let (.>>.) (p1: TParser<'a>) (p2: TParser<'b>) : TParser<'a * 'b> =
    p1 >>= fun a -> p2 |>> fun b -> (a, b)

/// Ordered choice
let (<|>) (p1: TParser<'a>) (p2: TParser<'a>) : TParser<'a> =
    fun tokens state ->
        match p1 tokens state with
        | POk _ as ok -> ok
        | PError _ -> p2 tokens state

// --- Helpers ---

let optional (p: TParser<'a>) : TParser<'a option> =
    (p |>> Some) <|> (preturn None)

let rec many (p: TParser<'a>) : TParser<'a list> =
    fun tokens state ->
        match p tokens state with
        | PError _ -> POk ([], tokens, state)
        | POk (x, rest, state') ->
            match many p rest state' with
            | POk (xs, rest', state'') -> POk (x :: xs, rest', state'')
            | PError e -> PError e

let many1 (p: TParser<'a>) : TParser<'a list> =
    p >>= fun x -> many p |>> fun xs -> x :: xs

let choice (parsers: TParser<'a> list) : TParser<'a> =
    List.reduce (<|>) parsers

/// Left-associative chain: parse one or more `p` separated by `op`
let chainl1 (p: TParser<'a>) (op: TParser<'a -> 'a -> 'a>) : TParser<'a> =
    let rec loop acc tokens state =
        match op tokens state with
        | PError _ -> POk (acc, tokens, state)
        | POk (f, rest, state') ->
            match p rest state' with
            | PError e -> PError e
            | POk (b, rest', state'') -> loop (f acc b) rest' state''
    p >>= fun first -> fun tokens state -> loop first tokens state

// --- Primitives ---

/// Expect a specific token
let expect (expected: Tokens) : TParser<TokenWithMetadata> =
    fun tokens state ->
        match tokens with
        | t :: rest when t.Token = expected -> POk (t, rest, state)
        | t :: _ -> PError { Message = $"Expected {expected}, got {t.Token}"; Line = t.Line; Column = t.Column; Length = t.Length }
        | [] -> PError (CompileError.Empty $"Expected {expected}, got end of input")

/// Expect a Name token, return the string
let expectName : TParser<string> =
    fun tokens state ->
        match tokens with
        | t :: rest ->
            match t.Token with
            | Name (Word n) -> POk (n, rest, state)
            | _ -> PError { Message = $"Expected name, got {t.Token}"; Line = t.Line; Column = t.Column; Length = t.Length }
        | [] -> PError (CompileError.Empty "Expected name, got end of input")

/// Expect a Name or QualifiedName, return as dotted string
let expectNameOrQualified : TParser<string> =
    fun tokens state ->
        match tokens with
        | t :: rest ->
            match t.Token with
            | Name (Word n) -> POk (n, rest, state)
            | QualifiedName parts -> POk (System.String.Join(".", parts), rest, state)
            | _ -> PError { Message = $"Expected name, got {t.Token}"; Line = t.Line; Column = t.Column; Length = t.Length }
        | [] -> PError (CompileError.Empty "Expected name, got end of input")

/// Expect a Name or QualifiedName, return as string list
let expectNameOrQualifiedParts : TParser<string list> =
    fun tokens state ->
        match tokens with
        | t :: rest ->
            match t.Token with
            | Name (Word n) -> POk ([n], rest, state)
            | QualifiedName parts -> POk (parts, rest, state)
            | _ -> PError { Message = $"Expected name, got {t.Token}"; Line = t.Line; Column = t.Column; Length = t.Length }
        | [] -> PError (CompileError.Empty "Expected name, got end of input")

/// Expect a NumberLiteral token
let expectNumber : TParser<NumberValue> =
    fun tokens state ->
        match tokens with
        | t :: rest ->
            match t.Token with
            | NumberLiteral n -> POk (n, rest, state)
            | _ -> PError { Message = $"Expected number, got {t.Token}"; Line = t.Line; Column = t.Column; Length = t.Length }
        | [] -> PError (CompileError.Empty "Expected number, got end of input")

/// Expect a TypeDefinition token
let expectType : TParser<TypeDefinitions> =
    fun tokens state ->
        match tokens with
        | t :: rest ->
            match t.Token with
            | TypeDefinition td -> POk (td, rest, state)
            | _ -> PError { Message = $"Expected type, got {t.Token}"; Line = t.Line; Column = t.Column; Length = t.Length }
        | [] -> PError (CompileError.Empty "Expected type, got end of input")

/// Peek at the next token without consuming
let peek : TParser<Tokens option> =
    fun tokens state ->
        match tokens with
        | t :: _ -> POk (Some t.Token, tokens, state)
        | [] -> POk (None, tokens, state)

/// Check if at end of token stream
let atEnd : TParser<bool> =
    fun tokens state -> POk (tokens.IsEmpty, tokens, state)

/// Expect a Parameter token (lexer-produced untyped param like `a` in param position)
let expectParameter : TParser<string> =
    fun tokens state ->
        match tokens with
        | t :: rest ->
            match t.Token with
            | Parameter p -> POk (p, rest, state)
            | _ -> PError { Message = $"Expected parameter, got {t.Token}"; Line = t.Line; Column = t.Column; Length = t.Length }
        | [] -> PError (CompileError.Empty "Expected parameter, got end of input")

// --- State combinators ---

let registerSymbol (name: string) (paramCount: int) : TParser<unit> =
    fun tokens state ->
        let state' = { state with Symbols = Map.add name paramCount state.Symbols }
        POk ((), tokens, state')

let registerType (name: string) : TParser<unit> =
    fun tokens state ->
        // remove from pending if present
        let pending' = Map.remove name state.PendingTypeRefs
        let state' = { state with PendingTypeRefs = pending' }
        POk ((), tokens, state')

let getState : TParser<ParseState> =
    fun tokens state -> POk (state, tokens, state)

let setState (state: ParseState) : TParser<unit> =
    fun tokens _ -> POk ((), tokens, state)
