module Pipeline

open Types
open Ast
open Lowered

/// Run type inference and build a map of function name -> (param types, return type)
let inferTypes (nodes: ExpressionNode list) : Map<string, TypeDefinitions list * TypeDefinitions> =
    TypeInference.resetVars ()
    let (typeMap, _) =
        nodes |> List.fold (fun (typeMap, env) node ->
            match node.Expr with
            | FunctionDefinitionExpression funcDef ->
                let details = funcDetails funcDef
                match TypeInference.inferFunction env details with
                | Ok (fnType, substitution) ->
                    let env' = Map.add details.Identifier fnType env
                    match fnType with
                    | TypeInference.TFun (paramTypes, returnType) ->
                        let paramTypeDefs = paramTypes |> List.map (TypeInference.applySubst substitution >> TypeInference.toTypeDefinition)
                        let returnTypeDef = TypeInference.applySubst substitution returnType |> TypeInference.toTypeDefinition
                        (Map.add details.Identifier (paramTypeDefs, returnTypeDef) typeMap, env')
                    | _ -> (typeMap, env')
                | Result.Error _e -> (typeMap, env)
            | LetBindingExpression letBinding ->
                match TypeInference.infer env letBinding.Value with
                | Ok (inferredType, _) ->
                    let typeDef = TypeInference.toTypeDefinition inferredType
                    let env' = Map.add letBinding.Name inferredType env
                    (Map.add letBinding.Name ([], typeDef) typeMap, env')
                | Result.Error _ -> (typeMap, env)
            | _ -> (typeMap, env)
        ) (Map.empty, Map.empty)
    typeMap

/// Apply inferred types to a list of lowered definitions.
/// Replaces Inferred parameter and return types with concrete types from the type map.
let private applyInferredTypes (typeMap: Map<string, TypeDefinitions list * TypeDefinitions>) (defs: TopLevelDef list) : TopLevelDef list =
    defs |> List.map (fun def ->
        match def with
        | TopFunction functionDef ->
            match Map.tryFind functionDef.Name typeMap with
            | Some (paramTypes, returnType) ->
                let updatedParams =
                    if paramTypes.Length = functionDef.Parameters.Length then
                        List.zip functionDef.Parameters paramTypes
                        |> List.map (fun (param, inferredType) -> { param with Type = inferredType })
                    else functionDef.Parameters
                TopFunction { functionDef with ReturnType = returnType; Parameters = updatedParams }
            | None -> def
        | TopStruct _ -> def
    )

/// Full lowering pipeline: Check -> Lower -> Apply types
let lower (modulePath: ModulePath) (nodes: ExpressionNode list) : TopLevelDef list =
    let typeMap = inferTypes nodes
    nodes
    |> LambdaLifting.liftLambdas modulePath
    |> applyInferredTypes typeMap

/// Build symbol table from lowered defs, check for duplicates
let buildSymbolTable (defs: TopLevelDef list) : Result<SymbolTable.SymbolTable, string> =
    defs |> List.fold (fun tableResult def ->
        match tableResult with
        | Result.Error _ -> tableResult
        | Ok table ->
            match def with
            | TopFunction functionDef ->
                let (ModulePath parts) = functionDef.ModulePath
                let fullPath = parts @ [functionDef.Name]
                SymbolTable.addSymbol fullPath functionDef.Visibility functionDef.Parameters.Length table
            | TopStruct structDef ->
                let (ModulePath parts) = structDef.ModulePath
                let fullPath = parts @ [structDef.Name]
                SymbolTable.addSymbol fullPath Visibility.Public 0 table
    ) (Ok SymbolTable.empty)

/// Resolve qualified names in lowered function bodies using symbol table + opens
let private resolveNames (symTable: SymbolTable.SymbolTable) (defs: TopLevelDef list) : TopLevelDef list =
    let rec resolveExpr (expr: Expression) : Expression =
        match expr with
        | FunctionCallExpression call ->
            // try resolving via symbol table
            let resolvedName =
                match SymbolTable.resolveSymbol call.FunctionName symTable with
                | Some info -> SymbolTable.qualifiedKey info.FullPath
                | None -> call.FunctionName
            FunctionCallExpression { call with FunctionName = resolvedName; Arguments = call.Arguments |> List.map resolveExpr }
        | IdentifierExpression name ->
            match SymbolTable.resolveSymbol name symTable with
            | Some info -> IdentifierExpression (SymbolTable.qualifiedKey info.FullPath)
            | None -> expr
        | OperatorExpression op ->
            OperatorExpression { op with Left = resolveExpr op.Left; Right = resolveExpr op.Right }
        | LetBindingExpression lb ->
            LetBindingExpression { lb with Value = resolveExpr lb.Value }
        | _ -> expr

    defs |> List.map (fun def ->
        match def with
        | TopFunction fn ->
            TopFunction { fn with Body = fn.Body |> List.map resolveExpr }
        | other -> other
    )
