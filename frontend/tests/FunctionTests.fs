module FunctionTests

open Xunit
open System
open CommonParsers

open FunctionDefinitionParser
open BasicTypes
open LanguageExpressions

// [<Fact>]
let ``Let function definition with single parameter should succeed`` () =
  let document = """
  let thing a = a
  """

  ParserHelper.testParser functionDefinitionParser document (fun e ->
    Assert.Equal("thing", e.Identifier)
    Assert.Equal(Inferred, e.Type)

    e.RightHandAssignment
    |> function 
       | Value vd -> Assert.Equal("this", vd.Value)
  )

[<Fact>]
let ``parameter definition with a single parameter`` () =
  let parameters = """
  a  
  """

  ParserHelper.testParser parameterDefinitionParser parameters (fun e -> 
    Assert.Equal<string list>(["a"], e)
  )
  
[<Fact>]
let ``parameter definition with a two parameters`` () =
  let parameters = """
  a b c d e f g h i j k l m n o p q r s
  """

  ParserHelper.testParser parameterDefinitionParser parameters (fun e -> 
    Assert.Equal<string list>(
      ["a"; "b"; "c"; "d"; "e"; "f"; "g"; "h"; "i"; "j"; "k"; "l"; "m"; "n"; "o"; "p"; "q"; "r"; "s"],
      e
    )
  )
