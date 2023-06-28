module TestTwoPhase

open System.Text
open FParsec
open CommonParsers
open BasicTypes

module List =
  let fromPair (input: 'a * 'a) =
    [ fst input; snd input ]
  
  ()

let pWhitespace =
  skipAnyOf (seq { ' '; '\t'; '\f' })
let pManyWhitespace1 =
  skipMany1 pWhitespace
let pManyWhitespace =
  skipMany pWhitespace

let typedParameterParser =
  between enclosementOpenOperatorTokenParser enclosementClosedOperatorTokenParser (
    pManyWhitespace
    >>. parameterTokenParser .>> pManyWhitespace
    .>>. typeIdentifierTokenParser .>> pManyWhitespace1
    .>>. typeChoicesTokenParser 
    |>> fun ((w, ti), t) -> [OpenParen; w; ti; t; ClosedParen] )
    <?> "Expect typed parameter :: (a: string)"

let singleParameterParser = 
  parameterTokenParser |>> List.singleton

let parameterDefinitionParser =
  sepEndBy1 (attempt singleParameterParser <|> typedParameterParser) pWhitespace
  |>> List.collect id
 
let letBlockParser =
  pipe5
    (letWordTokenParser <?> "Expecting let keyword" .>> pManyWhitespace)
    (wordTokenParser <?> "Expecting variable identifier" .>> pManyWhitespace)
    (opt (parameterDefinitionParser .>> pManyWhitespace))
    (opt (typeIdentifierTokenParser .>> pManyWhitespace1 .>>. typeChoicesTokenParser .>> pManyWhitespace1 |>> List.fromPair ) .>> pManyWhitespace)
    assignmentSymbolTokenParser
    (fun letToken variableName maybeParameters maybeVariableType assignment ->
        let variableType = maybeVariableType |> Option.defaultValue []
        let parameters = maybeParameters |> Option.defaultValue []
        [ letToken; variableName; yield! parameters; yield! variableType; assignment ])

let fieldParser = 
  pipe3
    (spaces >>. wordTokenParser .>> spaces <?> "Expecting a field name")
    (typeIdentifierTokenParser .>> spaces1 <?> "Expecting field separator (:)")
    (typeChoicesTokenParser <?> "Expecting field type")
    (fun a b c -> [a; b; c])
  
let structContent =
  between
    openBracesTokenParser closedBracesTokenParser
    (sepEndBy fieldParser spaces1)
  |>> fun fields ->
        let flatten = fields |> List.collect id
        [ OpenBrace; yield! flatten; ClosedBrace ]    
   
let emptyBraces =
  openBracesTokenParser .>> spaces .>>. closedBracesTokenParser
  |>> List.fromPair

let structParser =
  let structName = wordTokenParser <?> "Expecting a struct name" 
  pipe3
    structWordTokenParser
    (spaces1 >>. structName .>> spaces1)
    (attempt emptyBraces <|> structContent)
    (fun a b c -> [ a; b; yield! c ])
    
let indentTokenParser =
  manySatisfy (fun c -> c = ' ' || c = '\t') |>> fun spaces -> spaces.Length
  
let tokenParser =
  choice [
    letWordTokenParser
    structWordTokenParser
    openBracesTokenParser
    closedBracesTokenParser
    gotoSymbolTokenParser
    assignmentSymbolTokenParser
    enclosementOpenOperatorTokenParser
    enclosementClosedOperatorTokenParser
    pipeSymbolTokenParser
    additionSymbolTokenParser
    subtractionSymbolTokenParser
    multiplySymbolTokenParser
    semiColonSymbolTokenParser
    greaterThanSymbolTokenParser
    lessThanSymbolTokenParser
    matchWordTokenParser
    typeWordTokenParser
    numberLiteralTokenParser
    wordTokenParser
  ]
  
let (<!>) (p: Parser<_,_>) label : Parser<_,_> =
    fun stream ->
        printfn "%A: Entering %s" stream.Position label
        let reply = p stream
        printfn "%A: Leaving %s (%A)" stream.Position label reply.Status
        if isNull reply.Error |> not then 
          reply.Error.Head
          |> function
              | ExpectedString s
              | ExpectedStringCI s
              | Unexpected s
              | UnexpectedString s
              | UnexpectedStringCI s
              | Message s
              | Expected s -> printfn "%s" s
              | l -> printfn "Was unknown error"
        reply

let pPotentialExpr =
  (attempt letBlockParser <!> "let block") <|> (tokenParser |>> List.singleton <!> "Token parser")
  
let fakeTokenListOption _ : Tokens list option =
  Some []
  
let pOptionExpr : Parser<Tokens list option> =
  pMatch [
    (structWordTokenParser |>> fakeTokenListOption, (attempt structParser |>> Some)) 
    (noneOf (seq { '\n' }) |>> fakeTokenListOption, (sepEndBy1 pPotentialExpr pManyWhitespace1 |>> List.collect id) |>> Some)
    (anyOf (seq { '\n' }) |>> fakeTokenListOption, preturn None)
    (eof |>> fakeTokenListOption, preturn None)
  ] <!> "Match Block"

let pLineExpr =
  sepEndBy1 (indentTokenParser
             .>>. pOptionExpr
             |>> fun (indent, expr) ->
                    expr
                    |> Option.map (fun e -> { Indent = indent; Expressions = e; Body = [] })
  ) newline

let document = """let a =
  b
  let c =
    d
  e

struct o {
  callum: string
}

let f = 
let h =
  i * j
  
let k l =
  l * m
  
let n (o: i32) =
  o + 2
"""

let maybeTokenisedLines =
  runParserOnString pLineExpr BlockScopeParserState.Default "code" document
  |> function
     | Success (lines, _, _) ->
         let parsedLines = 
           lines
           |> List.choose id
         parsedLines
         |> List.iter (fun line -> printfn "%A" line)
         Some parsedLines
     | Failure (e,_,_) ->
         printfn "%s" e
         None
         


let tokenisedLines = maybeTokenisedLines.Value

let nestRows (items: Row list) =
    let rec sortViaIndent indent items =
      match items with
      | [ _ ] | [  ] -> items
      | _ ->
        // start of with blockScope
        ([], [])
        |> List.foldBack (fun item (currentScope, completeScope) ->
              // while the indents are not at the current level
              if item.Indent <> indent then
                  // store them in the current scope
                  item :: currentScope, completeScope
              else
                  // when we hit the next item with same indent
                  // everything from the completeScope should now be nested in its body
                  [], { item with Body = currentScope @ item.Body } :: completeScope ) items 
        |> snd
        |> List.map (fun item ->
             // keep drilling down until or nested bodies have been sorted.
             { item with Body = sortViaIndent (indent + 2) item.Body })
        
    sortViaIndent 0 items
    
let createAST docName code =
  runParserOnString pLineExpr BlockScopeParserState.Default docName code
  |> function
     | Success (lines, _, _) ->
         let parsedLines = 
           lines
           |> List.choose id
         Result.Ok (parsedLines |> nestRows)
     | Failure (e,_,_) ->
         Result.Error e
    
let result = nestRows maybeTokenisedLines.Value

let rec rowReader (row: Row) : unit =
  let sb = StringBuilder()
  let append (s: string) = sb.Append(s) |> ignore
  
  row.Expressions
  |> List.iter (
       function
       | Let
       | Struct
       | Goto
       | Assignment
       | Pipe
       | Addition
       | Subtraction
       | Multiply
       | SemiColonTerminator
       | GreaterThan
       | LessThan
       | Match
       | TypeIdentifier
       | Type as t -> sprintf "%s " (t.ToString().ToLowerInvariant()) |> append
       | TypeDefinition t -> sprintf "%s " (t.ToString().ToLowerInvariant()) |> append
       | Parameter p -> sprintf "(Parameter %s) " p |> append
       | Word w -> sprintf "%s " w |> append
       | NumberLiteral numberLiteral -> sprintf "%s " (numberLiteral.String) |> append
       | NoToken -> ()
       | OpenBrace -> "{ " |> append
       | ClosedBrace -> "} " |> append
       | OpenParen -> "( " |> append
       | ClosedParen -> ") " |> append
       )
  
  printfn "%s%s" (System.String(' ', row.Indent)) (sb.ToString())
  
  row.Body |> List.iter rowReader
