module VariableDefinitionParser

open FParsec
open CommonParsers
open BasicTypes

type ValueDefinition =
  {
    Value: string
  }

type RightHandAssignment =
  | Value of ValueDefinition

//| UserDefined of string

type VariableDefinition =
  {
    Identifier: string
    Type: TypeDefinitions 
    RightHandAssignment: RightHandAssignment
  }

let typeChoices : Parser<TypeDefinitions> = choice [
    pstring TypeKeywords.i32Type >>. preturn I32
    pstring TypeKeywords.i64Type >>. preturn I64
    pstring TypeKeywords.doubleType >>. preturn Double
    pstring TypeKeywords.floatType >>. preturn Float
    pstring TypeKeywords.stringType >>. preturn String
]


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
