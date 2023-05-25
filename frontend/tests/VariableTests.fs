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
    Assert.Equal(Inferred, e.Type)

    e.RightHandAssignment
    |> function 
       | Value vd -> Assert.Equal("this", vd.Value)
  
  )
  
[<Fact>]
let ``Let variable definition with incorrect assignment should fail`` () =
  let document = """
  let thing =thig
  """

  ParserHelper.failParser variableDefinitionParser document (fun e ->
    Assert.True(e.Contains("Expecting: whitespace"))
  )

[<Fact>]
let ``Let variable definition along with type definition with value should succeed`` () =
  let document = """
  let thing: i32 = this
  """

  ParserHelper.testParser variableDefinitionParser document (fun e ->
    Assert.Equal("thing", e.Identifier)
    Assert.Equal(I32, e.Type)

    e.RightHandAssignment
    |> function 
       | Value vd -> Assert.Equal("this", vd.Value)
  )

[<Fact>]
let ``Let variable definition along with invalid type definition with value should fail`` () =
  let document = """
  let thing: i2 = this
  """

  ParserHelper.failParser variableDefinitionParser document (fun e ->
     Assert.True(e.Contains("Expecting: 'double', 'float', 'i32', 'i64' or 'string'"))
  )
  
[<Fact>]
let ``Let variable definition along with valid type definition but invalid type definition should fail`` () =
  let document = """
  let thing: i32 =this
  """

  ParserHelper.failParser variableDefinitionParser document (fun e ->
     Assert.True(e.Contains("Expecting: whitespace"))
  )
