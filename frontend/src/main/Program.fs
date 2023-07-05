// For more information see https://aka.ms/fsharp-console-apps
open FParsec
open CommonParsers
open StructParser

// let documentParser =
// spaces >>. (letWord <|> structParser) .>> spaces

let res = TestTwoPhase.result

res |> List.iter TestTwoPhase.rowReader

let thing = "what"
//
// let document = """
// struct GodStruct {
//   : somekindofvalue
// }
// """
//
// let result = runParserOnString (spaces >>. structParser) BlockScopeParserState.Default "code" document
//
// match result with
// | Success (r, _, _) -> printfn "all good :: %A" r
// | Failure (e, _, _) -> printfn "nah we fooked :: %s" e

printfn "Hello from F#"
