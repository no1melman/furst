module StructParserTests

open System
open Xunit
open FParsec
open CommonParsers
open StructParser

[<Fact>]
let ``Struct with field should pass`` () =
  let document = """
  struct GodStruct {
    name: somekindofvalue
  }
  """
  
  ParserHelper.testParser (spaces >>. structParser) document ignore

[<Fact>]
let ``Struct without field should pass`` () =
  let document = """
  struct GodStruct {
  }
  """
  
  ParserHelper.testParser (spaces >>. structParser) document ignore

[<Fact>]
let ``Struct without name should fail`` () =
  let document = """
  struct {
  }
  """
  
  ParserHelper.failParser (spaces >>. structParser) document (fun e -> Assert.True(e.Contains("Expecting a struct name")))