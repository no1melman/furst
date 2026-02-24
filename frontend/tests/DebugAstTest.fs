module DebugAstTest

open Xunit
open TestTwoPhase

[<Fact>]
let ``Debug function parsing structure`` () =
    let source = """let add a b =
  a + b"""

    match createAST "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        // Print structure
        printfn "Number of rows: %d" rows.Length

        for i, row in rows |> List.indexed do
            printfn "\nRow %d:" i
            printfn "  Indent: %d" row.Indent
            printfn "  Expressions: %A" (row.Expressions |> List.map (fun t -> t.Token))
            printfn "  Body rows: %d" row.Body.Length

            for j, bodyRow in row.Body |> List.indexed do
                printfn "\n  Body row %d:" j
                printfn "    Indent: %d" bodyRow.Indent
                printfn "    Expressions: %A" (bodyRow.Expressions |> List.map (fun t -> t.Token))
                printfn "    Body rows: %d" bodyRow.Body.Length
