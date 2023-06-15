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
  (letWordTokenParser <?> "Expecting let keyword") .>> pManyWhitespace
  .>>. (wordTokenParser <?> "Expecting variable identifier") .>> pManyWhitespace
  .>>. opt (parameterDefinitionParser .>> pManyWhitespace)
  .>>. opt (typeIdentifierTokenParser .>> pManyWhitespace1 .>>. typeChoicesTokenParser .>> pManyWhitespace1 |>> List.fromPair )
  .>> pManyWhitespace .>>. assignmentSymbolTokenParser
  |>> fun ((((letToken, variableName), maybeParameters), maybeVariableType), assignment) ->
        let variableType = maybeVariableType |> Option.defaultValue []
        let parameters = maybeParameters |> Option.defaultValue []
        [ letToken; variableName; yield! parameters; yield! variableType; assignment ]
  
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
        reply

let pPotentialExpr =
  (attempt letBlockParser <!> "let block") <|> (tokenParser |>> List.singleton <!> "Token parser")
let pOptionExpr =
  opt ((sepEndBy1 pPotentialExpr pManyWhitespace1) |>> List.collect id)

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
  
let f = g
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
    
let result = nestRows maybeTokenisedLines.Value

let rec rowReader (row: Row) : unit =
  let sb = StringBuilder()
  let append (s: string) = sb.Append(s) |> ignore
  
  row.Expressions
  |> List.iter (
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
       | TypeIdentifier
       | Type as t -> sprintf "%s " (t.ToString().ToLowerInvariant()) |> append
       | TypeDefinition t -> sprintf "%s " (t.ToString().ToLowerInvariant()) |> append
       | Parameter w
       | Word w -> sprintf "%s " w |> append
       | NumberLiteral numberLiteral -> sprintf "%s " (numberLiteral.String) |> append
       | NoToken -> ())
  
  printfn "%s%s" (System.String(' ', row.Indent)) (sb.ToString())
  
  row.Body |> List.iter rowReader