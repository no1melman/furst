module VariableDefinitionParser

open FParsec
open CommonParsers
open BasicTypes
open LanguageExpressions

type VariableDefinition =
  {
    Identifier: string
    Type: TypeDefinitions 
    Value: ValueExpression
  }

let variableDefinitionParser =
 (letWord <?> "Expecting let keyword") .>> spaces
 >>. (word <?> "Expecting variable identifier") .>> spaces
 .>>. opt ( pchar ':' >>. spaces1 >>. typeChoices .>> spaces1 )
 .>> pchar '=' <?> "Expected assignment operator"
 .>> spaces1
 .>>. word 
 |>> (fun ((a, b), c) -> 
   { Identifier = a
     Value = ValueExpression c
     Type = b 
            |> function 
               | Some t -> t
               | None -> Inferred
   })
