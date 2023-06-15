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
let word : Parser<_> = many1Satisfy2 isIndentifierChar isIndentifierChar

let letWord : Parser<_> = pstring "let"
let matchWord : Parser<_> = pstring "match"
let typeWord : Parser<_> = pstring "type"
let structWord : Parser<_> = pstring "struct" <?> "Expecting struct"

let openBraces : Parser<_> = pstring "{"
let closedBraces : Parser<_> = pstring "}"
let gotoSymbol : Parser<_>  = pstring "->"
let assignmentSymbol : Parser<_>  = pchar '='
let enclosementOpenOperator : Parser<_>  = pstring "("
let enclosementClosedOperator : Parser<_> = pstring ")"

let pipeSymbol : Parser<_>  = pstring "|"
let additionSymbol : Parser<_>  = pchar '+'
let subtractionSymbol : Parser<_>  = pchar '-'
let multiplySymbol : Parser<_>  = pchar '*'
let greaterThanSymbol : Parser<_>  = pstring ">"
let lessThanSymbol : Parser<_> = pstring "<"
let semiColonSymbol : Parser<_> = pstring ";"

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

let couldExpect (pleft: Parser<'a,_>) (charsEitherOr: char * char) l : Parser<'a option,_> = 
    (fun stream ->
        let leftchar = fst charsEitherOr
        let rightchar = snd charsEitherOr
        let firstChar = attempt (pchar leftchar) stream

        if firstChar.Status = Ok then 
            let leftStr = (pleft .>> pchar rightchar) stream
            if leftStr.Status = Ok then
                Reply(Some leftStr.Result)
            else 
                Reply(FatalError, leftStr.Error)
            
        else 
            let secondChar = attempt (pchar rightchar) stream 
            if secondChar.Status = Ok then 
                Reply(None) 
            else 
                Reply(FatalError, messageError l)
    )
    
let pBranch pLeftCondition pLeftBranch pRightBranch : Parser<_,_> =
    fun stream ->
        printfn "%A" (stream.Peek())
        let mutable streamState = stream.State
        let leftBranchResult = (attempt pLeftCondition) stream
        stream.BacktrackTo(&streamState)
        if leftBranchResult.Status = Ok then
            pLeftBranch stream
        else
            pRightBranch stream

let typeChoices : Parser<TypeDefinitions> = choice [
    pstring i32Type >>. preturn I32
    pstring i64Type >>. preturn I64
    pstring doubleType >>. preturn Double
    pstring floatType >>. preturn Float
    pstring stringType >>. preturn String
]

// let rec matchThis (plist: (char * Parser<'a, _> * string) list) : Parser<'a, _> =
//   (fun stream -> 
//      
//   )
