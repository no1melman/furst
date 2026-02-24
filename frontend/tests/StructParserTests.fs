module StructParserTests

open Xunit
open StructParser
open BasicTypes
open ParserHelper

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

        match s.Type with
        | AnyToken (Name (Word t)) -> Assert.Equal("GodStruct", t)
        | _ -> invalidArg "Struct Type" "Needs to be TypeDefinition"

        Assert.True(
            s.Fields
            |> List.exists (fun f ->
                match f.FieldName, f.FieldValue with
                | AnyToken (Name (Word "name")), AnyToken (TypeDefinition (UserDefined "somekindofvalue")) -> true
                | _ -> false)
        )

        Assert.True(
            s.Fields
            |> List.exists (fun f ->
                match f.FieldName, f.FieldValue with
                | AnyToken (Name (Word "someotherName")), AnyToken (TypeDefinition (UserDefined "somestuff")) -> true
                | _ -> false)
        )

        Assert.True(
            s.Fields
            |> List.exists (fun f ->
                match f.FieldName, f.FieldValue with
                | AnyToken (Name (Word "further")), AnyToken (TypeDefinition (UserDefined "things")) -> true
                | _ -> false)
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

        match s.Type with
        | AnyToken (Name (Word t)) -> Assert.Equal("GodStruct", t)
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
