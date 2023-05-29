module FunctionTests

open Xunit
open System
open CommonParsers

open VariableDefinitionParser
open BasicTypes

[<Fact>]
let ``Let function definition with single parameter should succeed`` () =
  let document = """
  let thing a = a
  """

  ParserHelper.testParser variableDefinitionParser document (fun e ->
    Assert.Equal("thing", e.Identifier)
    Assert.Equal(Inferred, e.Type)

    e.RightHandAssignment
    |> function 
       | Value vd -> Assert.Equal("this", vd.Value)
  
  )
