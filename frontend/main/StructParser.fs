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
    Name: string
    Type: string
    Fields: ParsedField list
  }

let fieldParser = 
  word .>> spaces .>> pstring ":" .>> spaces1 .>>. word .>> spaces1 |>> fun (fieldName, fieldValue) -> { FieldName = fieldName; FieldValue = fieldValue }

let structParser =
  let structName = word <?> "Expecting a struct name" 
  structWord .>> spaces1 .>>. structName .>> spaces1
  .>>. between openBraces closedBraces
        (spaces1 >>. sepEndBy fieldParser (pchar '\n'))
  |>> fun ((name, _type) , fields) -> { Name = name; Type = _type; Fields = fields}