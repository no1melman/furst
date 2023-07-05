namespace Furst.Llvmir

open System
open System.Text
open BasicTypes

module Generator =
    
    let mapToken (expressions: Tokens list) (matcher: Tokens -> bool) =
        let rec searchTokens tokens rtnToken =
            match tokens with
            | [] -> rtnToken
            | head :: tail ->
                if matcher head then
                    Some head
                else
                    searchTokens tail None
        
        searchTokens expressions None
    
    let rec createFunc (func: Row) =
        let hopefullyName =
            mapToken func.Expressions (function Word _ -> true | _ -> false)
        
        hopefullyName
        |> Option.map (function
            | Word exprName ->
                Ok $"func.func %s{exprName} {{\n\n}}"
            | _ -> Error "Developer error, mapToken not working correctly")
        |> Option.defaultValue (Error "Couldn't find functions name")
        
    
    let createLlvmir (rows: Row list) =
        // each row is a block of code, variables need to be treated as variables and funcs funcs...
        
        let rec getTotalLineCount (therows: Row list) count =
            match therows with
            | [] -> count
            | head :: tail ->
                let newCount = count + 1
                let innerCount = getTotalLineCount head.Body newCount
                getTotalLineCount tail innerCount
        
        let rec produceLlvmIr (therows: Row list) (sb: StringBuilder) =
            match therows with
            | [] -> Result.Ok sb
            | head :: tail ->
                let tryExpr =
                    if head.Body.Length <> 0 then
                        createFunc head
                    else 
                        Result.Ok ""
                
                let nextProduce = produceLlvmIr tail
                        
                tryExpr
                |> Result.bind (sb.Append >> nextProduce)
                    
        let count = getTotalLineCount rows 0
        
        let sb = StringBuilder(80 * rows.Length)

        produceLlvmIr rows sb
        |> function
            | Ok llvmIr -> llvmIr.ToString()
            | Error e -> raise (Exception e)
