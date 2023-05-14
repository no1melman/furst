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

