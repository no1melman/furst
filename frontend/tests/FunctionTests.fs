module FunctionTests

open Xunit

open FunctionDefinitionParser
open BasicTypes
open LanguageExpressions

let assertBodyExpression (e: FunctionDefinition) count (getExprs: Expression list -> unit) =
    let exprs = 
      match e.Body with
      | BodyExpression exprs -> exprs
      | _ -> invalidOp "No Body Expression in body"
    
    Assert.Equal(count, exprs |> List.length)
    getExprs exprs

[<Fact>]
let ``Let function definition with single parameter should succeed`` () =
  let document = """
  let thing a = a
  """

  ParserHelper.testParser functionDefinitionParser document (fun e ->
    Assert.Equal("thing", e.Identifier)
    Assert.Equal(Inferred, e.Type)
    Assert.Equal(e.Parameters |> List.head, { Name = NameExpression "a"; Type = Inferred })

    assertBodyExpression e 1 (fun exprs ->
      exprs[0]
      |> function
      | ReturnExpression v -> 
        v |> function ValueExpression v -> Assert.Equal("a", v)
      | _ -> invalidOp "Wrong body"
    )
  )

let createSimpleParameterExpr s : ParameterExpression = { Name = NameExpression s; Type = Inferred }

[<Fact>]
let ``Let function definition with single inferred parameter and single typed parameter should succeed`` () =
  let document = """
  let thing a (b: i32) = a
  """

  ParserHelper.testParser functionDefinitionParser document (fun e ->
    Assert.Equal("thing", e.Identifier)
    Assert.Equal(Inferred, e.Type)
    Assert.Equal(e.Parameters[0], { Name = NameExpression "a"; Type = Inferred })
    Assert.Equal(e.Parameters[1], { Name = NameExpression "b"; Type = I32 })

    assertBodyExpression e 1 (fun exprs ->
      exprs[0]
      |> function
      | ReturnExpression v -> 
        v |> function ValueExpression v -> Assert.Equal("a", v)
      | _ -> invalidOp "Wrong body"
    )
  )

[<Fact>]
let ``Let function definition with single inferred parameter and new line for body should succeed`` () =
  let document = """
  let thing a =
    a
  """

  ParserHelper.testParser functionDefinitionParser document (fun e ->
    Assert.Equal("thing", e.Identifier)
    Assert.Equal(Inferred, e.Type)
    Assert.Equal(e.Parameters[0], { Name = NameExpression "a"; Type = Inferred })

    assertBodyExpression e 1 (fun exprs ->
      exprs[0]
      |> function
      | ReturnExpression v ->
        v |> function ValueExpression v -> Assert.Equal("a", v)
      | _ -> invalidOp "Wrong body"
    )
  )

[<Fact>]
let ``Let function definition with single inferred parameter and 2 new lines for body should succeed`` () =
  let document = """
  let thing a =
    a
    b
  """

  ParserHelper.testParser functionDefinitionParser document (fun e ->
    Assert.Equal("thing", e.Identifier)
    Assert.Equal(Inferred, e.Type)
    Assert.Equal(e.Parameters[0], { Name = NameExpression "a"; Type = Inferred })

    assertBodyExpression e 2 (fun exprs ->
      exprs[0]
        |> function
        | ReturnExpression v ->
          v |> function ValueExpression v -> Assert.Equal("a", v)
        | _ -> invalidOp "Wrong body"
      exprs[1]
        |> function
        | ReturnExpression v ->
          v |> function ValueExpression v -> Assert.Equal("b", v)
        | _ -> invalidOp "Wrong body"
    )
  )

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
      [ createSimpleParameterExpr "a"
        createSimpleParameterExpr "b"
        createSimpleParameterExpr "c"
        createSimpleParameterExpr "d"
        createSimpleParameterExpr "e"
        createSimpleParameterExpr "f"
        createSimpleParameterExpr "g"
        createSimpleParameterExpr "h"
        createSimpleParameterExpr "i"
        createSimpleParameterExpr "j"
        createSimpleParameterExpr "k"
        createSimpleParameterExpr "l"
        createSimpleParameterExpr "m"
        createSimpleParameterExpr "n"
        createSimpleParameterExpr "o"
        createSimpleParameterExpr "p"
        createSimpleParameterExpr "q"
        createSimpleParameterExpr "r"
        createSimpleParameterExpr "s" ],
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
      [ createSimpleParameterExpr "a"
        { Name = NameExpression "b"; Type = String } ],
      e
    )
  )
