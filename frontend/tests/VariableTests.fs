module VariableTests

open Xunit
open System
open CommonParsers
open FParsec

open VariableDefinitionParser

[<Fact>]
let ``Let variable definition with value should succeed`` () =
  let document = """
  let thing = this
  """

  ParserHelper.testParser variableDefinitionParser document (fun e ->
    Assert.Equal("thing", e.Identifier)

    e.RightHandAssignment
    |> function 
      | Value vd ->
        Assert.Equal("this", vd.Value)
  
  )
