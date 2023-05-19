module StructParserTests

open System
open Xunit
open FParsec
open CommonParsers
open StructParser
open Xunit.Abstractions

type StructTests(testoutput: ITestOutputHelper) =

  [<Fact>]
  let ``Struct with field should pass`` () =
    let document = """
    struct GodStruct {
      name: somekindofvalue
    }
    """
    
    ParserHelper.testParser (spaces >>. structParser) document ignore

  [<Fact>]
  let ``Struct with multiple fields should pass`` () =
    let document = """
    struct GodStruct {
      name: somekindofvalue
      someotherName: somestuff
      further: things 
    }
    """
    
    ParserHelper.testParser (spaces >>. structParser) document (fun s -> 
      Assert.NotEmpty(s.Fields)
      Assert.Equal("GodStruct", s.Type)

      Assert.True(s.Fields |> List.contains { FieldName = "name"; FieldValue = "somekindofvalue" })
      Assert.True(s.Fields |> List.contains { FieldName = "someotherName"; FieldValue = "somestuff" })
      Assert.True(s.Fields |> List.contains { FieldName = "further"; FieldValue = "things" })
    )


  [<Fact>]
  let ``Struct without field should pass`` () =
    let document = """
    struct GodStruct {
    }
    """
    
    ParserHelper.testParser (spaces >>. structParser) document (fun s -> 
      Assert.Empty(s.Fields)
      Assert.Equal("GodStruct", s.Type)
    )

  [<Fact>]
  let ``Struct without name should fail`` () =
    let document = """
    struct {
    }
    """
    
    ParserHelper.failParser (spaces >>. structParser) document (fun e -> Assert.True(e.Contains("Expecting a struct name")))

  [<Fact>]
  let ``Struct without field name should fail`` () =
    let document = """
    struct GodStruct {
      : value
    }
    """

    ParserHelper.failParser (spaces >>. structParser) document (fun e -> Assert.True(e.Contains("Expecting a field name")))

  [<Fact>]
  let ``Struct without field type should fail`` () =
    let document = """
    struct GodStruct {
      name :
    }
    """

    ParserHelper.failParser (spaces >>. structParser) document (fun e -> Assert.True(e.Contains("Expecting field type")))

  [<Fact>]
  let ``Struct without field separator should fail`` () =
    let document = """
    struct GodStruct {
      name value
    }
    """

    ParserHelper.failParser (spaces >>. structParser) document (fun e -> Assert.True(e.Contains("Expecting field separator (:)")))

