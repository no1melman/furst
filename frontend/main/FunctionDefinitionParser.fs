module FunctionDefinitionParser

open FParsec
open CommonParsers

let functionString = """

let something a = 
  a


"""




type Expressions =
  | Return of string
  | 
and VariableDefinition =
  {
    Name: string
    Rhs: Expressions
  }
and FunctionDefinition =
  {
    Name: string
    Parameters: string list
    Body: BodyExpression
  }

type BodyExpression =
  {
    Stuff: string
  }

type FunctionDefinition =
  {
    Name: string
    Parameters: string list
    Body: BodyExpression
  }

