module FunctionDefinitionParser

open FParsec
open CommonParsers
open BasicTypes
open CommonParsers
open LanguageExpressions

let functionString = """

let something a = 
  a


"""

type ParameterExpression =
  {
    Name: NameExpression
    Type: TypeDefinitions
  }


type FunctionDefinition =
  {
    Identifier: string
    Type: TypeDefinitions 
    RightHandAssignment: RightHandAssignment
    Parameters: ParameterExpression list
  }

let typedParameterParser =
  between (pchar '(') (pchar ')') (
    spaces >>. word 
    .>> spaces .>> pchar ':' 
    .>> spaces1 .>>. typeChoices |>> fun (n, t) -> { Name = NameExpression n; Type = t } )

let singleParameterParser = 
  word |>> fun name -> { Name = NameExpression name; Type = Inferred }

let parameterDefinitionParser =
  sepEndBy1 ((attempt singleParameterParser <|> typedParameterParser)) (pchar ' ') 

let functionDefinitionParser =
 (letWord <?> "Expecting let keyword") .>> spaces
 >>. (word <?> "Expecting variable identifier") .>> spaces
 .>>. opt ( pchar ':' >>. spaces1 >>. typeChoices .>> spaces1 )
 .>> pchar '=' <?> "Expected assignment operator"
 .>> spaces1
 .>>. word 
 |>> (fun ((a, b), c) -> 
   { Identifier = a
     RightHandAssignment = Value { Value = c }
     Parameters = []
     Type = b 
            |> function 
               | Some t -> t
               | None -> Inferred
   })
