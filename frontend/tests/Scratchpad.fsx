#r "nuget:FParsec"
#load "../main/CommonParsers.fs" "../main/StructParser.fs"

open FParsec
open StructParser
open CommonParsers

let typeHeader =
    let structName = word <?> "Expecting a struct name"
    structWord .>> spaces1 .>>. structName .>> spaces1

let fieldDefs =
    between openBraces closedBraces (many (fieldParser .>> spaces))

let structParser =
    spaces >>. typeHeader .>>. fieldDefs
    |>> fun ((name, _type), fields) -> {
        Name = name
        Type = _type
        Fields = fields
    }


"""
  struct GodStruct {
}"""
|> runParserOnString ((spaces >>. typeHeader) .>> (between openBraces closedBraces (many fieldParser))) () "code"
