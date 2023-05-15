module StructParser

open System
open FParsec
open CommonParsers

type ParsedField = {
    FieldName: string
    FieldValue: string
}

type ParsedStruct = {
    Name: string
    Type: string
    Fields: ParsedField list
}

let fieldParser =
    spaces >>. word .>> pstring ":" .>> spaces1 .>>. word
    |>> fun (fieldName, fieldValue) -> {
        FieldName = fieldName
        FieldValue = fieldValue
    }

let structParser =
    let typeHeader =
        let structName = word <?> "Expecting a struct name"
        structWord .>> spaces1 .>>. structName .>> spaces1

    let fieldDefs =
        between openBraces closedBraces (many (fieldParser .>> spaces))

    spaces >>. typeHeader .>>. fieldDefs
    |>> fun ((name, _type), fields) -> {
        Name = name
        Type = _type
        Fields = fields
    }
