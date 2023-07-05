module Furst.Tests.LlvmirTests.GeneratorTests

open System
open Furst.Llvmir
open Xunit

[<Fact>]
let ``Given source code AST when generating should give correct response`` () =
    let sourceCode = """

let doSomething a =
    b
    
let a = 2

doSomething a

"""

    let ast = TestTwoPhase.createAST "test" sourceCode
    
    let result = ast |> function
                         | Ok parsedLines -> Generator.createLlvmir parsedLines
                         | Error e -> raise (Exception(e))
    
    Assert.Equal("4", result)