module LanguageExpressions

open BasicTypes

type ValueExpression = ValueExpression of string

type NameExpression = NameExpression of string 

type ParameterExpression =
  {
    Name: NameExpression
    Type: TypeDefinitions
  }

type BodyExpression = BodyExpression of Expression list
  and FunctionDefinition =
    {
      Identifier: string
      Type: TypeDefinitions 
      Parameters: ParameterExpression list
      Body: BodyExpression
    }
  and Expression =
    | FunctionExpression of FunctionDefinition
    | ReturnExpression of ValueExpression
