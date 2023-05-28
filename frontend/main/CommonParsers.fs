module CommonParsers

open FParsec;

type Parser<'t> = Parser<'t, unit>

let isIndentifierChar c = isLetter c || isDigit c
let word : Parser<_> = many1Satisfy2 isIndentifierChar isIndentifierChar

let letWord : Parser<_> = pstring "let"
let structWord : Parser<_> = pstring "struct" <?> "Expecting struct"

let openBraces : Parser<_> = pstring "{"
let closedBraces : Parser<_> = pstring "}"
let assignmentOperator : Parser<_>  = pstring "->"
let enclosementOpenOperator : Parser<_>  = pstring "("
let enclosementClosedOperator : Parser<_> = pstring ")"

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

// let rec matchThis (plist: (char * Parser<'a, _> * string) list) : Parser<'a, _> =
//   (fun stream -> 
//      
//   )
