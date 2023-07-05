#r "nuget:FParsec"
#load "./main/BasicTypes.fs" "./main/CommonParsers.fs" 

open System.IO
open FParsec
open CommonParsers

type Tokens =
  | Let
  | Struct
  | OpenBrace
  | ClosedBrace
  | Goto
  | Assignment
  | OpenParen
  | ClosedParen
  | Pipe
  | Addition
  | Subtraction
  | Multiply
  | SemiColonTerminator
  | GreaterThan
  | LessThan
  | Match
  | Type
  | Word of string
  
type Row =
    { Indent: int
      Expressions: Tokens list
      Body: Row list }
  
let indentTokenParser =
  manySatisfy (fun c -> c = ' ' || c = '\t') |>> fun spaces -> spaces.Length
  
let tokenParser =
  choice [
    letWord >>% Let
    structWord >>% Struct
    openBraces >>% OpenBrace
    closedBraces >>% ClosedBrace
    gotoSymbol >>% Goto
    assignmentSymbol >>% Assignment
    enclosementOpenOperator >>% OpenParen
    enclosementClosedOperator >>% ClosedParen
    pipeSymbol >>% Pipe
    additionSymbol >>% Addition
    subtractionSymbol >>% Subtraction
    multiplySymbol >>% Multiply
    semiColonSymbol >>% SemiColonTerminator
    greaterThanSymbol >>% GreaterThan
    lessThanSymbol >>% LessThan
    matchWord >>% Match
    typeWord >>% Match
    word |>> Word
  ]
  
let (<!>) (p: Parser<_,_>) label : Parser<_,_> =
    fun stream ->
        printfn "%A: Entering %s" stream.Position label
        let reply = p stream
        printfn "%A: Leaving %s (%A)" stream.Position label reply.Status
        reply

let pWhitespace =
  skipAnyOf (seq { ' '; '\t'; '\f' })
let pManyWhitespace1 =
  skipMany1 pWhitespace
let pManyWhitespace =
  skipMany pWhitespace

let pExpr =
  opt (sepEndBy1 tokenParser pManyWhitespace1)

let pLineExpr =
  sepEndBy1 (indentTokenParser
             .>>. pExpr
             |>> fun (indent, expr) ->
                    expr
                    |> Option.map (fun e -> { Indent = indent; Expressions = e; Body = [] })
  ) newline

let document = """let a =
  b
  let c =
    d
  e
  
let f = g
let h =
  i * j
"""

let maybeTokenisedLines =
  runParserOnString pLineExpr BlockScopeParserState.Default "code" document
  |> function
     | Success (lines, _, _) ->
         lines
         |> List.iter (fun line -> printfn "%A" (line.Value))
         Some lines
     | Failure (e,_,_) ->
         printfn "%s" e
         None

let tokenisedLines = maybeTokenisedLines.Value

let rec nestRows (rows: Row list) (currentIndent: int) =
    match rows with
    | [] -> []
    | row :: remainingRows ->
        if row.Indent = currentIndent then
            // If the row has the same indent as the current level,
            // add it to the body of the previous row
            let updatedRow =
                { row with
                    Body = nestRows row.Body (currentIndent + 2) }

            updatedRow :: nestRows remainingRows currentIndent
        elif row.Indent > currentIndent then
            // If the row has a higher indent, recursively nest the remaining rows
            // under the current row's body and continue with the remaining rows
            let updatedRow =
                { row with
                    Body = nestRows remainingRows (row.Indent + 2) }

            [ updatedRow ]
        else
            // If the row has a lower indent, return the remaining rows
            rows

let nestedItems = nestRows (tokenisedLines |> List.choose id) -2

printfn ""
printfn ""
printfn "%A" nestedItems
