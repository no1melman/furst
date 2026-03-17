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
  pipe5
    enclosementOpenOperatorTokenParser
    (pManyWhitespace >>. parameterTokenParser .>> pManyWhitespace)
    (typeIdentifierTokenParser .>> pManyWhitespace1)
    typeChoicesTokenParser
    enclosementClosedOperatorTokenParser
    (fun openP param ti typ closeP -> [openP; param; ti; typ; closeP])
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
  pipe3
    openBracesTokenParser
    (sepEndBy fieldParser spaces1)
    closedBracesTokenParser
    (fun openB fields closeB ->
        let flatten = fields |> List.collect id
        [ openB; yield! flatten; closeB ])    
   
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
  
let (<!>) (p: Parser<_,_>) _label : Parser<_,_> = p

let pPotentialExpr =
  (attempt letBlockParser <!> "let block") <|> (tokenParser |>> List.singleton <!> "Token parser")
  
let fakeTokenListOption _ : TokenWithMetadata list option =
  Some []

let pOptionExpr : Parser<TokenWithMetadata list option> =
  pMatch [
    (structWordTokenParser |>> fakeTokenListOption, (attempt structParser |>> Some)) 
    (noneOf (seq { '\n' }) |>> fakeTokenListOption, (many1 (pPotentialExpr .>> pManyWhitespace) |>> List.collect id) |>> Some)
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
    

let rec rowReader (row: Row) : unit =
  let sb = StringBuilder()
  let append (s: string) = sb.Append(s) |> ignore

  row.Expressions
  |> List.iter (fun tokenWithMeta ->
       match tokenWithMeta.Token with
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
       | Name (Word w) -> sprintf "%s " w |> append
       | NumberLiteral (IntValue i)   -> sprintf "%d " i |> append
       | NumberLiteral (FloatValue f) -> sprintf "%g " f |> append
       | NoToken -> ()
       | OpenBrace -> "{ " |> append
       | ClosedBrace -> "} " |> append
       | OpenParen -> "( " |> append
       | ClosedParen -> ") " |> append
       )
  
  printfn "%s%s" (System.String(' ', row.Indent)) (sb.ToString())
  
  row.Body |> List.iter rowReader
