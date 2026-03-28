module Ast

open Types

type CompileError = {
  Message: string
  Line: Line
  Column: Column
  Length: TokenLength
}
with
  static member Empty(message: string) = {
    Message = message
    Line = Line 0L
    Column = Column 0L
    Length = TokenLength 0
  }

type SourceLocation = {
  StartLine: Line
  StartCol: Column
  EndLine: Line
  EndCol: Column
}

type ParameterExpression = { Name: WordToken; Type: TypeDefinitions }

type Expressions =
  | ParameterExpression

let (|TypedParameterExpressionMatch|ParameterExpressionMatch|Incorrect|) (tokens: TokenWithMetadata list) =
  match tokens |> List.map _.Token with
  | [ OpenParen; Name name; TypeIdentifier; TypeDefinition typeDefinition; ClosedParen ] ->
    TypedParameterExpressionMatch { Name = name; Type = typeDefinition }
  | [ OpenParen; Parameter p; TypeIdentifier; TypeDefinition typeDefinition; ClosedParen ] ->
    TypedParameterExpressionMatch { Name = Word p; Type = typeDefinition }
  | [ Name name ] -> ParameterExpressionMatch { Name = name; Type = Inferred }
  | _ -> Incorrect

type Operator =
  | Add
  | Subtract
  | Multiply

type LiteralValue =
  | IntLiteral of int
  | FloatLiteral of float
  | StringLiteral of string

type BodyExpression = BodyExpression of Expression list
  and LetBinding =
    {
      Name: string
      Type: TypeDefinitions
      Value: Expression
    }
  and FunctionCall =
    {
      FunctionName: string
      Arguments: Expression list
    }
  and Operation =
    {
      Left: Expression
      Operator: Operator
      Right: Expression
    }
  and StructDefinition =
    {
      Name: string
      Fields: (string * TypeDefinitions) list
    }
  and FunctionDetails =
    {
      Identifier: string
      Type: TypeDefinitions
      Parameters: ParameterExpression list
      Body: BodyExpression
      Visibility: Visibility
    }
  and FunctionDefinition = FunctionDefinition of FunctionDetails

  and Expression =
    | LetBindingExpression of LetBinding
    | FunctionDefinitionExpression of FunctionDefinition
    | FunctionCallExpression of FunctionCall
    | OperatorExpression of Operation
    | IdentifierExpression of string
    | LiteralExpression of LiteralValue
    | StructExpression of StructDefinition
    | ModuleDeclaration of string list * Expression list
    | OpenDeclaration of string list

type ExpressionNode = {
  Expr: Expression
  Location: SourceLocation
}

let isParameterListExpression (tokens: TokenWithMetadata list) =
    let isParameterExpression tokens =
      match tokens with
      | TypedParameterExpressionMatch expr -> Some expr
      | ParameterExpressionMatch expr -> Some expr
      | Incorrect -> None

    let rec walkList list =
      match list |> List.map _.Token with
      | OpenParen :: _ ->
        let maybeExpr = isParameterExpression list.[0..4]
        maybeExpr |> Option.bind (fun expr ->
            let remaining = list.[5..]
            match remaining with
            | [] -> Some [ expr ]
            | _ -> walkList list.[5..]
          )
      | Name _ :: _ -> walkList (List.tail list)
      | Parameter _ :: _ -> walkList (List.tail list)
      | [] -> Some []
      | _ -> None

    match tokens with
    | [] -> false
    | _ -> walkList tokens |> Option.isSome

let isFunctionDefinition (row: Row) =
    let tokens = row.Expressions |> List.map _.Token
    match tokens with
    | [ Let; Name _; Assignment ] ->
        not row.Body.IsEmpty
    | Let :: Name _ :: _ ->
        let paramsBeforeAssignment = tokens |> List.skip 2 |> List.takeWhile ((<>) Assignment)
        if paramsBeforeAssignment.IsEmpty then
            not row.Body.IsEmpty
        else
            let paramTokens = row.Expressions |> List.skip 2 |> List.takeWhile (fun t -> t.Token <> Assignment)
            isParameterListExpression paramTokens
    | _ -> false
