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

let createSimpleParameterExpr s : ParameterExpression = { Name = NameExpression s; Type = Inferred }

[<Fact>]
let ``parameter definition with a single parameter`` () =
  let parameters = """
  a  
  """

  ParserHelper.testParser parameterDefinitionParser parameters (fun e -> 
    Assert.Equal<ParameterExpression list>([createSimpleParameterExpr "a"], e)
  )
  
[<Fact>]
let ``parameter definition with a two parameters`` () =
  let parameters = """
  a b c d e f g h i j k l m n o p q r s
  """

  ParserHelper.testParser parameterDefinitionParser parameters (fun e -> 
    Assert.Equal<ParameterExpression list>(
      [createSimpleParameterExpr "a"; createSimpleParameterExpr "b"; createSimpleParameterExpr "c"; createSimpleParameterExpr "d"; createSimpleParameterExpr "e"; createSimpleParameterExpr "f"; createSimpleParameterExpr "g"; createSimpleParameterExpr "h"; createSimpleParameterExpr "i"; createSimpleParameterExpr "j"; createSimpleParameterExpr "k"; createSimpleParameterExpr "l"; createSimpleParameterExpr "m"; createSimpleParameterExpr "n"; createSimpleParameterExpr "o"; createSimpleParameterExpr "p"; createSimpleParameterExpr "q"; createSimpleParameterExpr "r"; createSimpleParameterExpr "s"],
      e
    )
  )

[<Fact>]
let ``parameter definition with a typed parameter`` () =
  let parameters = """
  a (b: string)
  """

  ParserHelper.testParser parameterDefinitionParser parameters (fun e -> 
    Assert.Equal<ParameterExpression list>(
      [createSimpleParameterExpr "a"; { Name = NameExpression "b"; Type = String } ],
      e
    )
  )
