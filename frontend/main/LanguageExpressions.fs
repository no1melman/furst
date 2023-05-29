module LanguageExpressions

type ValueDefinition =
  {
    Value: string
  }

type RightHandAssignment =
  | Value of ValueDefinition

