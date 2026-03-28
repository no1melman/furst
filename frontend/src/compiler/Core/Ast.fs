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
    | NegateExpression of Expression
    | ModuleDeclaration of string list * Expression list
    | LibDeclaration of string list
    | OpenDeclaration of string list

type ExpressionNode = {
  Expr: Expression
  Location: SourceLocation
}

