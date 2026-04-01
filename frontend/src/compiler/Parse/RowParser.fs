module RowParser

open Types
open Ast
open TokenCombinators
open ExpressionParser
open StatementParser

// --- Helpers ---

let private tokenLocation (token: TokenWithMetadata) : SourceLocation =
    let (Column startCol) = token.Column
    let (TokenLength len) = token.Length
    { StartLine = token.Line; StartCol = token.Column
      EndLine = token.Line; EndCol = Column (startCol + int64 len) }

let private tokensLocation (tokens: TokenWithMetadata list) : SourceLocation =
    match tokens with
    | [] -> { StartLine = Line 0L; StartCol = Column 0L; EndLine = Line 0L; EndCol = Column 0L }
    | _ ->
        let first = tokens.Head
        let last = tokens |> List.last
        let (Column lastCol) = last.Column
        let (TokenLength lastLen) = last.Length
        { StartLine = first.Line; StartCol = first.Column
          EndLine = last.Line; EndCol = Column (lastCol + int64 lastLen) }

let rec private rowLocation (row: Row) : SourceLocation =
    match row.Body with
    | [] -> tokensLocation row.Expressions
    | bodyRows ->
        let firstToken = row.Expressions |> List.tryHead
        let lastBodyRow = bodyRows |> List.last
        let endLoc = rowLocation lastBodyRow
        match firstToken with
        | Some first ->
            { StartLine = first.Line; StartCol = first.Column
              EndLine = endLoc.EndLine; EndCol = endLoc.EndCol }
        | None -> endLoc

/// Run a TParser against a row's token list
let runOnRow (parser: TParser<'a>) (row: Row) (state: ParseState) : ParseResult<'a> =
    parser row.Expressions state

// --- Row-level dispatch ---

let rec parseRow (row: Row) (state: ParseState) : Result<ExpressionNode * ParseState, CompileError> =
    let tokens = row.Expressions |> List.map _.Token
    let loc = rowLocation row
    match tokens with
    | Lib :: _ ->
        match runOnRow pLibDecl row state with
        | POk (parts, _, state') -> Ok ({ Expr = LibDeclaration parts; Location = loc }, state')
        | PError e -> Error e
    | Mod :: _ ->
        parseMod row state
    | Open :: _ ->
        match runOnRow pOpenDecl row state with
        | POk (parts, _, state') -> Ok ({ Expr = OpenDeclaration parts; Location = loc }, state')
        | PError e -> Error e
    | Tokens.Private :: Let :: _ ->
        parseLetOrFunc row state Visibility.Private
    | Let :: _ ->
        parseLetOrFunc row state Visibility.Public
    | Struct :: _ ->
        parseStructChain row state
    | _ ->
        // expression row
        match runOnRow pExpr row state with
        | POk (expr, _, state') -> Ok ({ Expr = expr; Location = loc }, state')
        | PError e -> Error e

and parseLetOrFunc (row: Row) (state: ParseState) (visibility: Visibility) : Result<ExpressionNode * ParseState, CompileError> =
    let loc = rowLocation row
    // strip Private token if present
    let exprTokens =
        match row.Expressions |> List.map _.Token with
        | Tokens.Private :: _ -> row.Expressions |> List.tail
        | _ -> row.Expressions
    // skip Let
    let afterLet = exprTokens |> List.tail
    // get name (either regular Name or OperatorName from lexer)
    match afterLet with
    | nameToken :: rest ->
        let name =
            match nameToken.Token with
            | Name (Word n) -> Some n
            | OperatorName n -> Some n
            | _ -> None
        match name with
        | None ->
            Error { Message = $"Expected name after let, got {nameToken.Token}"
                    Line = nameToken.Line; Column = nameToken.Column; Length = nameToken.Length }
        | Some funcName ->
            // let binding vs function: if `=` is immediately after name, it's a let binding.
            // if there are params before `=`, it's a function definition.
            let isLetBinding =
                match rest with
                | t :: _ when t.Token = Assignment -> true
                | _ -> false
            if isLetBinding then
                let valueTokens = rest |> List.tail  // skip the `=`
                if valueTokens.IsEmpty && row.Body.IsEmpty then
                    let assignTok = rest.Head
                    Error { Message = $"Expected value after '=' in let binding for '{funcName}'"
                            Line = assignTok.Line; Column = assignTok.Column; Length = assignTok.Length }
                else
                    // value is either inline tokens, body rows, or both
                    let inlineExpr =
                        if valueTokens.IsEmpty then None
                        else
                            match pExpr valueTokens state with
                            | POk (expr, _, _) -> Some expr
                            | PError _ -> None
                    let bodyExpr =
                        if row.Body.IsEmpty then None
                        else
                            match parseBody row.Body state with
                            | Ok (exprs, _) -> exprs |> List.tryLast
                            | Error _ -> None
                    match inlineExpr, bodyExpr with
                    | None, None ->
                        let assignTok = rest.Head
                        Error { Message = $"Expected value in let binding for '{funcName}'"
                                Line = assignTok.Line; Column = assignTok.Column; Length = assignTok.Length }
                    | _ ->
                        let valExpr = match inlineExpr with Some e -> e | None -> bodyExpr.Value
                        let state' =
                            match registerSymbol funcName 0 [] state with
                            | POk ((), _, s) -> s
                            | _ -> state
                        Ok ({ Expr = LetBindingExpression { Name = funcName; Type = Inferred; Value = valExpr }
                              Location = loc }, state')
            else
                // function definition — tokens before `=` are parameters
                parseFunctionDef funcName rest row.Body visibility loc state
    | [] ->
        Error (CompileError.Empty "Expected name after let")

and parseFunctionDef (name: string) (afterName: TokenWithMetadata list) (bodyRows: Row list)
                     (visibility: Visibility) (loc: SourceLocation) (state: ParseState)
                     : Result<ExpressionNode * ParseState, CompileError> =
    // tokens before `=` are parameters
    let beforeAssign = afterName |> List.takeWhile (fun t -> t.Token <> Assignment)
    match pParams beforeAssign state with
    | PError e -> Error e
    | POk (parameters, _, state') ->
        match parseBody bodyRows state' with
        | Error e -> Error e
        | Ok (bodyExprs, state'') ->
            let paramCount = parameters.Length
            let state''' =
                match registerSymbol name paramCount [] state'' with
                | POk ((), _, s) -> s
                | _ -> state''
            let details = {
                Identifier = name
                Type = Inferred
                Parameters = parameters
                Body = BodyExpression bodyExprs
                Visibility = visibility
            }
            Ok ({ Expr = FunctionDefinitionExpression (FunctionDefinition details); Location = loc }, state''')

and parseMod (row: Row) (state: ParseState) : Result<ExpressionNode * ParseState, CompileError> =
    let loc = rowLocation row
    match runOnRow (expect Mod >>. expectNameOrQualifiedParts) row state with
    | PError e -> Error e
    | POk (parts, _, state') ->
        match row.Body with
        | [] -> Ok ({ Expr = ModuleDeclaration (parts, []); Location = loc }, state')
        | body ->
            match parseModBody body state' with
            | Error e -> Error e
            | Ok (bodyExprs, state'') ->
                Ok ({ Expr = ModuleDeclaration (parts, bodyExprs); Location = loc }, state'')

and parseModBody (bodyRows: Row list) (state: ParseState) : Result<Expression list * ParseState, CompileError> =
    let rec loop acc rows st =
        match rows with
        | [] -> Ok (List.rev acc, st)
        | r :: rest ->
            match parseRow r st with
            | Error e -> Error e
            | Ok (node, st') ->
                match node.Expr with
                | ModuleDeclaration _ ->
                    Error { Message = "Nested mod declarations are not allowed"
                            Line = node.Location.StartLine; Column = node.Location.StartCol; Length = TokenLength 0 }
                | LibDeclaration _ ->
                    Error { Message = "lib declarations are not allowed inside mod"
                            Line = node.Location.StartLine; Column = node.Location.StartCol; Length = TokenLength 0 }
                | _ -> loop (node.Expr :: acc) rest st'
    loop [] bodyRows state

and parseStructChain (row: Row) (state: ParseState) : Result<ExpressionNode * ParseState, CompileError> =
    let loc = rowLocation row
    // parse: struct Name { fields }
    let tokens = row.Expressions
    match runOnRow (expect Struct >>. expectName) row state with
    | PError e -> Error e
    | POk (name, afterName, state') ->
        // register type before parsing body (allows self-referential structs)
        let state' =
            match registerType name [] state' with
            | POk ((), _, s) -> s
            | _ -> state'
        match pStructBody afterName state' with
        | PError e -> Error e
        | POk (fields, _, state'') ->
            Ok ({ Expr = StructExpression { Name = name; Fields = fields }; Location = loc }, state'')

and parseBody (bodyRows: Row list) (state: ParseState) : Result<Expression list * ParseState, CompileError> =
    let rec loop acc rows st =
        match rows with
        | [] -> Ok (List.rev acc, st)
        | r :: rest ->
            match parseRow r st with
            | Error e -> Error e
            | Ok (node, st') -> loop (node.Expr :: acc) rest st'
    loop [] bodyRows state

// --- Entry point ---

/// Parse a list of top-level rows into expression nodes, threading state
let parseFile (rows: Row list) (state: ParseState) : Result<ExpressionNode list * ParseState, CompileError> =
    let rec loop acc rows st =
        match rows with
        | [] -> Ok (List.rev acc, st)
        | r :: rest ->
            match parseRow r st with
            | Error e -> Error e
            | Ok (node, st') -> loop (node :: acc) rest st'
    loop [] rows state
