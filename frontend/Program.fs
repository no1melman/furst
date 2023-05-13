// For more information see https://aka.ms/fsharp-console-apps
open FParsec;

let isIndentifierChar c = isLetter c || isDigit c
let word = many1Satisfy2 isIndentifierChar isIndentifierChar


let result = runParserOnString (spaces >>. word) () "code" "  what hello"

match result with
| Success (r, _, _) -> printfn "all good :: %A" r
| Failure (e, _, _) -> printfn "nah we fooked :: %s" e

printfn "Hello from F#"
