#r "nuget:FParsec"
#load "./main/CommonParsers.fs" "./main/StructParser.fs"

open FParsec
open StructParser
open CommonParsers

let structText = """
struct thingy {

}
"""

let (<!>) (p: Parser<_,_>) label : Parser<_,_> =
    fun stream ->
        printfn "%A: Entering %s" stream.Position label
        let reply = p stream
        printfn "%A: Leaving %s (%A)" stream.Position label reply.Status
        reply

let emptyBraces = openBraces .>> spaces .>>. closedBraces |>>( fun ((a, b)) -> [a;b]) <!> "Empty Braces"

let fieldParser = 
    spaces >>. word .>> spaces .>>. pstring ":" |>> (fun (a,b) -> [a;b]) 
    .>> spaces1 .>>. word |>> (fun (a,b) -> a @ [b])
    <!> "Field Parser"

let structContent = between openBraces closedBraces (sepEndBy fieldParser spaces1 <!> "Field Sep Parser") |>> (fun l -> l |> List.collect id) <!> "Struct Content"

let actualStructParser =
    structWord .>> spaces1 .>>. word .>> spaces1 
    |>> fun ((a, b)) -> [a;b]
    .>>. (attempt emptyBraces <|> structContent)
    |>> fun (a, b) -> a @ b


runParserOnString (spaces >>. actualStructParser) () "code" structText
|> printfn "%A"
