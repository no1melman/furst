module StructParserTests

open Xunit
open StructParser
open BasicTypes

let createUserDefined = UserDefined >> TypeDefinition

[<Fact>]
let ``Struct with field should pass`` () =
    let document =
        """
  struct GodStruct {
    name: somekindofvalue
  }
  """

    ParserHelper.testParser structParser document ignore

[<Fact>]
let ``Struct with multiple fields should pass`` () =
    let document =
        """
  struct GodStruct {
    name: somekindofvalue
    someotherName: somestuff
    further: things 
  }
  """

    ParserHelper.testParser structParser document (fun s ->
        Assert.NotEmpty(s.Fields)

        s.Type
        |> function
            | Name t -> Assert.Equal("GodStruct", t)
            | _ -> invalidArg "Struct Type" "Needs to be TypeDefinition"

        Assert.True(
            s.Fields
            |> List.contains
                { FieldName = Name "name"
                  FieldValue = createUserDefined "somekindofvalue" }
        )

        Assert.True(
            s.Fields
            |> List.contains
                { FieldName = Name "someotherName"
                  FieldValue = createUserDefined "somestuff" }
        )

        Assert.True(
            s.Fields
            |> List.contains
                { FieldName = Name "further"
                  FieldValue = createUserDefined "things" }
        ))


[<Fact>]
let ``Struct without field should pass`` () =
    let document =
        """
  struct GodStruct {
  }
  """

    ParserHelper.testParser structParser document (fun s ->
        Assert.Empty(s.Fields)

        s.Type
        |> function
            | Name t -> Assert.Equal("GodStruct", t)
            | _ -> invalidArg "Struct Type" "Needs to be TypeDefinition")

[<Fact>]
let ``Struct without name should fail`` () =
    let document =
        """
  struct {
  }
  """

    ParserHelper.failParser structParser document (fun e -> Assert.True(e.Contains("Expecting a struct name")))

[<Fact>]
let ``Struct without field name should fail`` () =
    let document =
        """
  struct GodStruct {
    : value
  }
  """

    ParserHelper.failParser structParser document (fun e -> Assert.True(e.Contains("Expecting a field name")))

[<Fact>]
let ``Struct without field type should fail`` () =
    let document =
        """
  struct GodStruct {
    name :
  }
  """

    ParserHelper.failParser structParser document (fun e -> Assert.True(e.Contains("Expecting field type")))

[<Fact>]
let ``Struct without field separator should fail`` () =
    let document =
        """
  struct GodStruct {
    name value
  }
  """

    ParserHelper.failParser structParser document (fun e -> Assert.True(e.Contains("Expecting field separator (:)")))
