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

type Expressions =
  | Return of string

type FunctionDefinition =
  {
    Identifier: string
    Type: TypeDefinitions 
    RightHandAssignment: RightHandAssignment
    Parameters: string list
  }
let parameterDefinitionParser =
  sepEndBy1 word (pchar ' ') 

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
