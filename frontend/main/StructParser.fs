module StructParser

open System
open FParsec
open CommonParsers

let fieldParser = 
  spaces >>. word .>> spaces .>> pstring ":" .>> spaces .>>. word .>> spaces

let structParser =
  let structName = word <?> "Expecting a struct name" 
  structWord 
    .>> spaces 
    .>>. structName 
    .>> spaces 
    .>> (openBraces <?> "Expecting opening brace") 
    .>> spaces 
    .>> (fieldParser <|> preturn (String.Empty, String.Empty)) 
    .>> (closedBraces <?> "Expecting closing brace")

