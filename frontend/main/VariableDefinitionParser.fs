module VariableDefinitionParser

open FParsec
open CommonParsers
open BasicTypes
open LanguageExpressions

type VariableDefinition =
  {
    Identifier: string
    Type: TypeDefinitions 
    RightHandAssignment: RightHandAssignment
  }

// let variableDefinitionParser =
//  (letWord <?> "Expecting let keyword") .>> spaces
//  >>. (word <?> "Expecting variable identifier") .>> spaces
//  .>>. couldExpect (
//     spaces1 >>. typeChoices .>> spaces1 
//     ) (':','=') "Expected assignment operator"
//  .>> spaces1
//  .>>. word 
//  |>> (fun ((a, b), c) -> 
//    { Identifier = a
//      RightHandAssignment = Value { Value = c }
//      Type = b 
//             |> function 
//                | Some t -> t
//                | None -> Inferred
//    })
let variableDefinitionParser =
 (letWord <?> "Expecting let keyword") .>> spaces
 >>. (word <?> "Expecting variable identifier") .>> spaces
 .>>. opt ( pchar ':' >>. spaces1 >>. typeChoices .>> spaces1 )
 .>> pchar '=' <?> "Expected assignment operator"
 .>> spaces1
 .>>. word 
 |>> (fun ((a, b), c) -> 
   { Identifier = a
     RightHandAssignment = Value { Value = c }
     Type = b 
            |> function 
               | Some t -> t
               | None -> Inferred
   })
