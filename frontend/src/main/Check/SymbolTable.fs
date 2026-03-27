module SymbolTable

open Types

type SymbolInfo = {
    FullPath: string list
    Visibility: Visibility
    ParamCount: int
}

type SymbolTable = {
    Symbols: Map<string, SymbolInfo>  // keyed by fully qualified name (dotted)
    OpenedModules: string list list    // list of opened module paths
}

let empty : SymbolTable = {
    Symbols = Map.empty
    OpenedModules = []
}

let qualifiedKey (parts: string list) : string =
    System.String.Join(".", parts)

let addSymbol (path: string list) (visibility: Visibility) (paramCount: int) (table: SymbolTable) : Result<SymbolTable, string> =
    let key = qualifiedKey path
    if Map.containsKey key table.Symbols then
        Result.Error $"duplicate symbol: {key}"
    else
        let info = { FullPath = path; Visibility = visibility; ParamCount = paramCount }
        Ok { table with Symbols = Map.add key info table.Symbols }

let addOpen (modulePath: string list) (table: SymbolTable) : SymbolTable =
    { table with OpenedModules = modulePath :: table.OpenedModules }

/// Resolve a name — try qualified first, then check opened modules
let resolveSymbol (name: string) (table: SymbolTable) : SymbolInfo option =
    // try exact match first (already qualified)
    match Map.tryFind name table.Symbols with
    | Some info -> Some info
    | None ->
        // try opened modules: prepend each opened path
        let parts = name.Split('.') |> Array.toList
        table.OpenedModules
        |> List.tryPick (fun openedPath ->
            let candidate = qualifiedKey (openedPath @ parts)
            Map.tryFind candidate table.Symbols)
