module StructParser

open System
open FParsec
open Parsers
open Types

type ParsedField =
  {
    FieldName: TokenWithMetadata
    FieldValue: TokenWithMetadata
  }

type ParsedStruct =
  {
    Type: TokenWithMetadata
    Fields: ParsedField list
  }

let fieldParser =
  spaces >>. (wordTokenParser .>> spaces <?> "Expecting a field name")
  .>> (typeIdentifierTokenParser <?> "Expecting field separator (:)")
  .>> spaces1 .>>. (typeChoicesTokenParser <?> "Expecting field type")
  |>> fun (fieldName, fieldValue) -> { FieldName = fieldName; FieldValue = fieldValue }

let structContent = between openBracesTokenParser closedBracesTokenParser (sepEndBy fieldParser spaces1)
let emptyBraces = openBracesTokenParser .>> spaces .>>. closedBracesTokenParser >>. preturn []

let structParser =
  let structName = wordTokenParser <?> "Expecting a struct name"
  structWordTokenParser .>> spaces1 >>. structName .>> spaces1
  .>>. (attempt emptyBraces <|> structContent)
  |>> fun ( _type , fields) -> { Type = _type; Fields = fields}
