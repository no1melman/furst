module CommonParsersTests

open CommonParsers
open FParsec

open Xunit

[<Fact>]
let ``Given 2 whitespace when allSpaces should give correct whitespace`` () =
  let document = "  "

  ParserHelper.pureTestParser (allSpaces) document (fun e ->
    Assert.Equal(2, e)
  ) 

[<Fact>]
let ``Given 4 whitespace when allSpaces should give correct whitespace`` () =
  let document = "    "

  ParserHelper.pureTestParser (allSpaces) document (fun e ->
    Assert.Equal(4, e)
  ) 

[<Fact>]
let ``Given 4 whitespace when onlyNSpaces should succeed`` () =
  let document = "    "

  ParserHelper.pureTestParser (onlyNSpaces 4) document id

[<Fact>]
let ``Given 3 whitespace when onlyNSpaces expects 4 should fail`` () =
  let document = "   "

  ParserHelper.pureFailParser (onlyNSpaces 4) document (fun e ->
    Assert.True(e.Contains "Expecting: 4 spaces but got 3")
  ) 
