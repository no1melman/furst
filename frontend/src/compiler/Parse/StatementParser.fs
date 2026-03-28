module StatementParser

open Types
open Ast
open TokenCombinators
open ExpressionParser

/// Typed parameter: (name: type)
let pTypedParam : TParser<ParameterExpression> =
    expect OpenParen >>. (
        fun tokens state ->
            // accept either Name or Parameter token for the param name
            match tokens with
            | t :: rest ->
                let nameStr =
                    match t.Token with
                    | Name (Word n) -> Some n
                    | Parameter p -> Some p
                    | _ -> None
                match nameStr with
                | Some n ->
                    (expect TypeIdentifier >>. expectType .>> expect ClosedParen
                     |>> fun td -> { Name = Word n; Type = td }) rest state
                | None ->
                    PError { Message = $"Expected parameter name, got {t.Token}"; Line = t.Line; Column = t.Column; Length = t.Length }
            | [] -> PError (CompileError.Empty "Expected parameter name")
    )

/// Untyped parameter: bare name
let pUntypedParam : TParser<ParameterExpression> =
    fun tokens state ->
        match tokens with
        | t :: rest ->
            match t.Token with
            | Name (Word n) -> POk ({ Name = Word n; Type = Inferred }, rest, state)
            | Parameter p -> POk ({ Name = Word p; Type = Inferred }, rest, state)
            | _ -> PError { Message = $"Expected parameter, got {t.Token}"; Line = t.Line; Column = t.Column; Length = t.Length }
        | [] -> PError (CompileError.Empty "Expected parameter")

/// Parse parameters before `=`: mixed typed/untyped
let pParams : TParser<ParameterExpression list> =
    let pParam = pTypedParam <|> pUntypedParam
    many pParam

/// Parse a let binding value (tokens after `=`)
let pLetBinding (name: string) : TParser<Expression> =
    expect Assignment >>. pExpr

/// lib declaration: lib Name or lib Qualified.Name
let pLibDecl : TParser<string list> =
    expect Lib >>. expectNameOrQualifiedParts

/// open declaration: open Name or open Qualified.Name
let pOpenDecl : TParser<string list> =
    expect Open >>. expectNameOrQualifiedParts

/// Struct field: name: type
let pStructField : TParser<string * TypeDefinitions> =
    fun tokens state ->
        match tokens with
        | t :: rest ->
            match t.Token with
            | Name (Word n) ->
                (expect TypeIdentifier >>. expectType |>> fun td -> (n, td)) rest state
            | _ -> PError { Message = $"Expected field name, got {t.Token}"; Line = t.Line; Column = t.Column; Length = t.Length }
        | [] -> PError (CompileError.Empty "Expected field name")

/// Struct body: { field1: type1  field2: type2 ... }
let pStructBody : TParser<(string * TypeDefinitions) list> =
    expect OpenBrace >>. many pStructField .>> expect ClosedBrace
