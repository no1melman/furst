module TestTwoPhase

open System
open System.Collections.Generic
open System.Linq
open System.Text
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
      mutable Body: Row list }
    static member Default = { Indent = -1; Expressions = []; Body = [] }
  
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
         |> List.choose id
         |> List.iter (fun line -> printfn "%A" (line))
         Some lines
     | Failure (e,_,_) ->
         printfn "%s" e
         None

let tokenisedLines = maybeTokenisedLines.Value

let nestRows (items: Row list) =
    let rec sortViaIndent indent items =
      match items with
      | [ _ ] | [  ] -> items
      | _ ->
        let mutable blockScope = []
        let mutable blockScoped = []
        for item in items do
          if item.Indent <> indent then 
            blockScope <- blockScope @ [ item ]
          else
            blockScoped <- blockScoped @ [ { item with Body = blockScope @ item.Body } ]
            blockScope <- []
        
        // I guess once this is done, we can then go through each body and check it's indentation
        
        let mutable final = []
        for item in blockScoped do
          final <- final @ [ { item with Body = sortViaIndent (indent + 2) item.Body } ]
        
        final |> List.rev
    
    sortViaIndent 0 items
// Usage
let result = nestRows (maybeTokenisedLines.Value |> List.choose id |> List.rev)

let rec rowReader (row: Row) : unit =
  let sb = StringBuilder()
  let append (s: string) = sb.Append(s) |> ignore
  
  row.Expressions |> List.iter (
      function
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
      | Type as t -> sprintf "%s " (t.ToString().ToLowerInvariant()) |> append
      | Word w -> sprintf "%s " w |> append )
  
  let indentStr = String(' ', row.Indent)
  printfn "%s%s" indentStr (sb.ToString())
  
  row.Body |> List.iter rowReader
  
 


// for item in nestedItems do
//     printfn "Indent: %d, Expressions: %A" item.Indent item.Expressions
//     for nestedRow in item.Body do
//         printfn "b  Indent: %d, Expressions: %A" nestedRow.Indent nestedRow.Expressions
//         for nestedNestedRow in nestedRow.Body do
//             printfn "a    Indent: %d, Expressions: %A" nestedNestedRow.Indent nestedNestedRow.Expressions