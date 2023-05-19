module StructParser

open System
open FParsec
open CommonParsers

type ParsedField =
  {
    FieldName: string
    FieldValue: string
  }

type ParsedStruct =
  {
    Type: string
    Fields: ParsedField list
  }

let fieldParser = 
  spaces >>. (word .>> spaces <?> "Expecting a field name") <!> "Going to word" 
  .>> (pstring ":" <?> "Expecting field separator (:)")
  .>> spaces1 .>>. (word <?> "Expecting field type")
  |>> fun (fieldName, fieldValue) -> { FieldName = fieldName; FieldValue = fieldValue }

let structContent = between openBraces closedBraces (sepEndBy fieldParser spaces1)
let emptyBraces = openBraces .>> spaces .>>. closedBraces >>. preturn []


let structParser =
  let structName = word <?> "Expecting a struct name" 
  structWord .>> spaces1 >>. structName .>> spaces1
  .>>. (attempt emptyBraces <|> structContent)
  |>> fun ( _type , fields) -> { Type = _type; Fields = fields}