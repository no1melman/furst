module Furstp.Main

open System
open System.IO
open Types
open Lowered

let private usage () =
    eprintfn "furstp — Furst frontend compiler"
    eprintfn ""
    eprintfn "Usage: furstp -o <output.fso> <file1.fu> [file2.fu ...]"
    2

[<EntryPoint>]
let main argv =
    let args = argv |> Array.toList
    match args with
    | "-o" :: outputPath :: files when not files.IsEmpty ->
        match Compiler.compileFiles None files [] with
        | Result.Error error -> eprintfn "%s" error; 1
        | Ok allLowered ->
            let sourceFile = files.Head
            FsoWriter.writeFso outputPath sourceFile allLowered
            0
    | _ -> usage ()
