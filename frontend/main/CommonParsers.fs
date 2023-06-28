module CommonParsers

open FParsec
open BasicTypes

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

let isIndentifierChar c = c <> ' ' && (isLetter c || isDigit c)
let wordParser = many1Satisfy2 isIndentifierChar isIndentifierChar

let i32Type = "i32"
let i64Type = "i64"
let doubleType = "double"
let floatType = "float"
let stringType = "string"

let letWordTokenParser : Parser<_> = pstring "let" >>% Let
let structWordTokenParser : Parser<_> = pstring "struct" <?> "Expecting struct" >>% Struct
let openBracesTokenParser : Parser<_> = pstring "{" >>% OpenBrace
let closedBracesTokenParser : Parser<_> = pstring "}" >>% ClosedBrace
let gotoSymbolTokenParser : Parser<_>  = pstring "->" >>% Goto
let assignmentSymbolTokenParser : Parser<_>  = pchar '=' >>% Assignment
let enclosementOpenOperatorTokenParser : Parser<_>  = pstring "(" >>% OpenParen
let enclosementClosedOperatorTokenParser : Parser<_> = pstring ")" >>% ClosedParen
let pipeSymbolTokenParser : Parser<_>  = pstring "|" >>% Pipe
let additionSymbolTokenParser : Parser<_>  = pchar '+' >>% Addition
let subtractionSymbolTokenParser : Parser<_>  = pchar '-' >>% Subtraction
let multiplySymbolTokenParser : Parser<_>  = pchar '*' >>% Multiply
let semiColonSymbolTokenParser : Parser<_> = pstring ";" >>% SemiColonTerminator
let greaterThanSymbolTokenParser : Parser<_>  = pstring ">" >>% GreaterThan
let lessThanSymbolTokenParser : Parser<_> = pstring "<" >>% LessThan
let matchWordTokenParser : Parser<_> = pstring "match" >>% Match
let typeWordTokenParser : Parser<_> = pstring "type" >>% Type
let typeChoicesTokenParser : Parser<_> = choice [
    pstring i32Type >>. preturn (I32 |> TypeDefinition)
    pstring i64Type >>. preturn (I64 |> TypeDefinition)
    pstring doubleType >>. preturn (Double |> TypeDefinition)
    pstring floatType >>. preturn (Float |> TypeDefinition)
    pstring stringType >>. preturn (String |> TypeDefinition)
    wordParser |>> (UserDefined >> TypeDefinition)
]
let typeIdentifierTokenParser : Parser<_> = pchar ':' >>% TypeIdentifier

let wordTokenParser : Parser<_> = wordParser |>> Word
let parameterTokenParser : Parser<_> = wordParser |>> Parameter
let numberLiteralTokenParser : Parser<_> =
    let numberFormat =     NumberLiteralOptions.AllowMinusSign
                       ||| NumberLiteralOptions.AllowFraction
                       ||| NumberLiteralOptions.AllowExponent
    numberLiteral numberFormat "number"
    |>> NumberLiteral




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

// let couldExpect (pleft: Parser<'a,_>) (charsEitherOr: char * char) l : Parser<'a option,_> = 
//     (fun stream ->
//         let leftchar = fst charsEitherOr
//         let rightchar = snd charsEitherOr
//         let firstChar = attempt (pchar leftchar) stream
//
//         if firstChar.Status = Ok then 
//             let leftStr = (pleft .>> pchar rightchar) stream
//             if leftStr.Status = Ok then
//                 Reply(Some leftStr.Result)
//             else 
//                 Reply(FatalError, leftStr.Error)
//             
//         else 
//             let secondChar = attempt (pchar rightchar) stream 
//             if secondChar.Status = Ok then 
//                 Reply(None) 
//             else 
//                 Reply(FatalError, messageError l)
//     )
    
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