module VariableDefinitionParser

open FParsec
open CommonParsers

type ValueDefinition =
  {
    Value: string
  }

type RightHandAssignment =
  | Value of ValueDefinition

type TypeDefinitions =
  | I32
  | I64
  | Float
  | Double
  | String
  | Inferred
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

let couldExpect (pleft: Parser<'a,_>) (charsEitherOr: char * char) l : Parser<'a option,_> = 
    (fun stream ->
        let leftchar = fst charsEitherOr
        let rightchar = snd charsEitherOr
        let firstChar = attempt (pchar leftchar) stream

        if firstChar.Status = Ok then 
            let leftStr = (pleft .>> pchar rightchar) stream
            if leftStr.Status = Ok then
                Reply(Some leftStr.Result)
            else 
                Reply(FatalError, leftStr.Error)
            
        else 
            let secondChar = attempt (pchar rightchar) stream 
            if secondChar.Status = Ok then 
                Reply(None) 
            else 
                Reply(FatalError, messageError l)
    )

let variableDefinitionParser =
 (letWord <?> "Expecting let keyword") .>> spaces
 >>. (word <?> "Expecting variable identifier") .>> spaces
 .>>. couldExpect (
    spaces1 >>. typeChoices .>> spaces1 
    ) (':','=') "Expected assignment operator"
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
