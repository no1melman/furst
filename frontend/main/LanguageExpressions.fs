module LanguageExpressions

open BasicTypes


type ParameterExpression =
  {
    Name: Tokens
    Type: Tokens
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
    | ReturnExpression of Tokens
