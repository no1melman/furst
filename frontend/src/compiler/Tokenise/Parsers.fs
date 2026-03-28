module Parsers

open FParsec
open Types

type IndentationCapture =
    | NoStatus
    | WeveLeftTheMethod
    | JustJumpedIn

type BlockScopeParserState =
  {
    Depth: int
    Capture: IndentationCapture
  }
  static member Default = { Depth = 0; Capture = NoStatus }
type Parser<'t> = Parser<'t, BlockScopeParserState>

let isIdentifierChar c = c <> ' ' && (isLetter c || isDigit c)
let wordParser = many1Satisfy2 isIdentifierChar isIdentifierChar

// Helper to capture position metadata for tokens
let withPosition (p: Parser<Tokens, _>) : Parser<TokenWithMetadata, _> =
  pipe3 getPosition p getPosition (fun startPos token endPos ->
    let length = int (endPos.Column - startPos.Column)
    { Line = Line startPos.Line
      Column = Column startPos.Column
      Length = TokenLength length
      Token = token })

let i32Type = "i32"
let i64Type = "i64"
let doubleType = "double"
let floatType = "float"
let stringType = "string"

let modWordTokenParser : Parser<_> = withPosition (pstring "mod" >>% Mod)
let openWordTokenParser : Parser<_> = withPosition (pstring "open" >>% Open)
let libWordTokenParser : Parser<_> = withPosition (pstring "lib" >>% Lib)
let privateWordTokenParser : Parser<_> = withPosition (pstring "private" >>% Private)
let letWordTokenParser : Parser<_> = withPosition (pstring "let" >>% Let)
let structWordTokenParser : Parser<_> = withPosition (pstring "struct" <?> "Expecting struct" >>% Struct)
let openBracesTokenParser : Parser<_> = withPosition (pstring "{" >>% OpenBrace)
let closedBracesTokenParser : Parser<_> = withPosition (pstring "}" >>% ClosedBrace)
let gotoSymbolTokenParser : Parser<_>  = withPosition (pstring "->" >>% Goto)
let assignmentSymbolTokenParser : Parser<_>  = withPosition (pchar '=' >>% Assignment)
let enclosementOpenOperatorTokenParser : Parser<_>  = withPosition (pstring "(" >>% OpenParen)
let enclosementClosedOperatorTokenParser : Parser<_> = withPosition (pstring ")" >>% ClosedParen)
let pipeSymbolTokenParser : Parser<_>  = withPosition (pstring "|" >>% Pipe)
let additionSymbolTokenParser : Parser<_>  = withPosition (pchar '+' >>% Addition)
let subtractionSymbolTokenParser : Parser<_>  = withPosition (pchar '-' >>% Subtraction)
let multiplySymbolTokenParser : Parser<_>  = withPosition (pchar '*' >>% Multiply)
let semiColonSymbolTokenParser : Parser<_> = withPosition (pstring ";" >>% SemiColonTerminator)
let greaterThanSymbolTokenParser : Parser<_>  = withPosition (pstring ">" >>% GreaterThan)
let lessThanSymbolTokenParser : Parser<_> = withPosition (pstring "<" >>% LessThan)
let matchWordTokenParser : Parser<_> = withPosition (pstring "match" >>% Match)
let typeWordTokenParser : Parser<_> = withPosition (pstring "type" >>% Type)
let typeChoicesTokenParser : Parser<_> = withPosition (choice [
    pstring i32Type >>. preturn (I32 |> TypeDefinition)
    pstring i64Type >>. preturn (I64 |> TypeDefinition)
    pstring doubleType >>. preturn (Double |> TypeDefinition)
    pstring floatType >>. preturn (Float |> TypeDefinition)
    pstring stringType >>. preturn (String |> TypeDefinition)
    wordParser |>> (UserDefined >> TypeDefinition)
])
let typeIdentifierTokenParser : Parser<_> = withPosition (pchar ':' >>% TypeIdentifier)

let qualifiedNameParser : Parser<_> =
    withPosition (
        sepBy1 wordParser (pchar '.')
        |>> fun parts ->
            match parts with
            | [single] -> single |> Word |> Name
            | parts -> QualifiedName parts)
let wordTokenParser : Parser<_> = withPosition (wordParser |>> (Word >> Name))
let parameterTokenParser : Parser<_> = withPosition (wordParser |>> Parameter)
let numberLiteralTokenParser : Parser<_> =
    let numberFormat =     NumberLiteralOptions.AllowMinusSign
                       ||| NumberLiteralOptions.AllowFraction
                       ||| NumberLiteralOptions.AllowExponent
    withPosition (numberLiteral numberFormat "number" |>> fun nl ->
        (match nl.IsInteger with
        | true  -> nl.String |> int   |> IntValue
        | false -> nl.String |> float |> FloatValue)
        |> NumberLiteral)




let allSpaces : Parser<_> =
  (fun stream ->
    let f = (fun c -> c = ' ')
    let n = stream.SkipCharsOrNewlinesWhile(f, f)
    if false || n <> 0 then Reply(n)
    else Reply(Error, expected "only spaces")
  )

let onlyNSpaces count : Parser<_> =
  (fun stream ->
    if count = 0 then
        printfn "ignoring eating any spaces"
        Reply(())
    else
        let f = (fun c -> c = ' ')
        let n = stream.SkipCharsOrNewlinesWhile(f, f)
        if false || n = count then Reply(())
        else Reply(Error, expected $"%i{count} spaces but got %i{n}")
  )

let (<!>) (p: Parser<_,_>) label : Parser<_,_> =
    fun stream ->
        printfn "%A: Entering %s" stream.Position label
        let reply = p stream
        printfn "%A: Leaving %s (%A)" stream.Position label reply.Status
        reply

let pBranch pLeftCondition (pLeftBranch: Parser<_,_>) pRightBranch : Parser<_,_> =
    fun stream ->
        printfn "%A" (stream.Peek())
        let mutable streamState = stream.State
        let leftBranchResult = (attempt pLeftCondition) stream
        stream.BacktrackTo(&streamState)

        if leftBranchResult.Status = Ok then

            pLeftBranch stream
        else
            pRightBranch stream

let pMatch (branches: (Parser<_,_> * Parser<_,_>) list) : Parser<_,_> =
    fun stream ->
        let rec goingThroughList list =
            match list with
            | (cond, branch) :: tail ->
                let mutable streamState = stream.State
                let condResult = (attempt cond) stream
                stream.BacktrackTo(&streamState)

                if condResult.Status = Error then
                    goingThroughList tail
                elif condResult.Status = Ok then
                    branch stream
                else // this is fatal error happening
                    condResult
            | [] -> Reply(FatalError, messageError "No branches met the parsers listed")

        let reply = goingThroughList branches
        if reply.Status = Error then
            Reply(FatalError, reply.Error)
        else
            reply
