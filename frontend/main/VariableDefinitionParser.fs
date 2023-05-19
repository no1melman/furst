module VariableDefinitionParser

open System
open FParsec
open CommonParsers
type ValueDefinition =
  {
    Value: string
  }

type RightHandAssignment =
  | Value of ValueDefinition

type VariableDefinition =
  {
    Identifier: string
    RightHandAssignment: RightHandAssignment
  }

let variableDefinitionParser =
 (letWord <?> "Expecting let keyword") .>> spaces
 >>. (word <?> "Expecting variable identifier") .>> spaces
 .>> (pchar '=' <?> "Expecting assignment operator") .>> spaces
 .>>. word 
 |>> (fun (a, b) -> { Identifier = a; RightHandAssignment = Value { Value = b }})
