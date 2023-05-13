// For more information see https://aka.ms/fsharp-console-apps
open FParsec;

type Parser<'t> = Parser<'t, unit>

let isIndentifierChar c = isLetter c || isDigit c
let word = many1Satisfy2 isIndentifierChar isIndentifierChar


let letWord = pstring "let"
let structWord = pstring "struct" <?> "Expecting struct"

let openBraces = pstring "{"
let closedBraces : Parser<_> = pstring "}"
let assignmentOperator : Parser<_>  = pstring "->"
let enclosementOpenOperator : Parser<_>  = pstring "("
let enclosementClosedOperator : Parser<_> = pstring ")"

let fieldParser = 
  spaces >>. word .>> spaces .>> pstring ":" .>> spaces .>>. word .>> spaces

let structParser =
  let structName = word <?> "Expecting a struct name" 
  structWord .>> spaces .>>. structName .>> spaces .>> (openBraces <?> "Expecting opening brace") .>> spaces .>> fieldParser .>> (closedBraces <?> "Expecting closing brace")

let documentParser =
  spaces >>. (letWord <|> structWord) .>> spaces


let document = """
struct GodStruct {
  name: somekindofvalue
}
"""

let result = runParserOnString (spaces >>. structParser) () "code" document

match result with
| Success (r, _, _) -> printfn "all good :: %A" r
| Failure (e, _, _) -> printfn "nah we fooked :: %s" e

printfn "Hello from F#"
