module FunctionDefinitionParser

open FParsec
open CommonParsers
open BasicTypes
open CommonParsers
open LanguageExpressions

let typedParameterParser =
  between (pchar '(') (pchar ')') (
    spaces >>. word 
    .>> spaces .>> pchar ':' 
    .>> spaces1 .>>. typeChoices 
    |>> fun (n, t) -> { Name = NameExpression n; Type = t } )
    <?> "Expect typed parameter :: (a: string)"

let singleParameterParser = 
  word |>> fun name -> { Name = NameExpression name; Type = Inferred }

let parameterDefinitionParser =
  sepEndBy1 ((attempt singleParameterParser <|> typedParameterParser)) (pchar ' ') 

let bodyDefinitionParser =
  spaces1 >>. sepEndBy1 (word |>> fun w -> ValueExpression w |> ReturnExpression) newline

let functionDefinitionParser =
 (letWord <?> "Expecting let keyword") .>> spaces
 >>. (word <?> "Expecting variable identifier") .>> spaces
 .>>. parameterDefinitionParser .>> spaces
 .>>. opt ( pchar ':' >>. spaces1 >>. typeChoices .>> spaces1 )
 .>> pchar '=' <?> "Expected assignment operator"
 .>>. bodyDefinitionParser 
 |>> (fun (((a, b), c), d) -> 
   { Identifier = a
     Parameters = b
     Type = c 
            |> function 
               | Some t -> t
               | None -> Inferred
     Body = BodyExpression d
   })
