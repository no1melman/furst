module Pipeline

open Types
open Ast
open Lowered

/// Convert an InferType to TypeDefinitions, formatting error with context
let private convertType (context: string) (inferType: TypeInference.InferType) : Result<TypeDefinitions, string> =
    match TypeInference.toTypeDefinition inferType with
    | Ok td -> Ok td
    | Error msg -> Error $"in '{context}': {msg}"

/// Type map: function/binding name → (param types, return type)
/// Body type map: function name → (local binding name → type)
type InferResult = {
    TypeMap: Map<string, TypeDefinitions list * TypeDefinitions>
    BodyTypes: Map<string, Map<string, TypeDefinitions>>
}

/// Convert local env to TypeDefinitions map, ignoring unconvertible entries
let private convertLocalEnv (fnName: string) (localEnv: TypeInference.TypeEnv) (paramNames: Set<string>) (outerNames: Set<string>)
    : Result<Map<string, TypeDefinitions>, string> =
    localEnv |> Map.fold (fun acc name inferType ->
        match acc with
        | Error _ -> acc
        | Ok m ->
            // skip params and outer-scope names — only want body let bindings
            if Set.contains name paramNames || Set.contains name outerNames then Ok m
            else
                match convertType fnName inferType with
                | Ok td -> Ok (Map.add name td m)
                | Error e -> Error e
    ) (Ok Map.empty)

/// Intermediate inference state — keeps InferTypes (not yet converted to TypeDefinitions)
type private FnInferInfo = {
    ParamTypes: TypeInference.InferType list
    ReturnType: TypeInference.InferType
    LocalEnv: TypeInference.TypeEnv
    ParamNames: Set<string>
    OuterNames: Set<string>
}

/// Run type inference and build type maps.
/// Two-phase: first infer all functions (keeping TVars), then convert to TypeDefinitions.
/// This lets call sites constrain earlier functions' TVars before defaulting to i32.
let inferTypes (nodes: ExpressionNode list) : Result<InferResult, string> =
    TypeInference.resetVars ()

    // Phase 1: infer all function types, accumulating a global substitution
    let phase1 =
        nodes |> List.fold (fun acc node ->
            match acc with
            | Error _ -> acc
            | Ok (fnInfos, globalSubst, env) ->
                match node.Expr with
                | FunctionDefinitionExpression funcDef ->
                    let details = funcDetails funcDef
                    // apply global substitution to env so call sites see solved types
                    let env' = env |> Map.map (fun _ t -> TypeInference.applySubst globalSubst t)
                    match TypeInference.inferFunction env' details with
                    | Ok (fnType, substitution, localEnv) ->
                        let globalSubst' = TypeInference.composeSubst substitution globalSubst
                        let env'' = Map.add details.Identifier fnType env
                        match fnType with
                        | TypeInference.TFun (paramTypes, returnType) ->
                            let paramNames = details.Parameters |> List.map (fun p -> let (Word w) = p.Name in w) |> Set.ofList
                            let outerNames = env |> Map.toSeq |> Seq.map fst |> Set.ofSeq
                            let info = {
                                ParamTypes = paramTypes
                                ReturnType = returnType
                                LocalEnv = localEnv
                                ParamNames = paramNames
                                OuterNames = outerNames
                            }
                            Ok ((details.Identifier, info) :: fnInfos, globalSubst', env'')
                        | _ -> Ok (fnInfos, globalSubst', env'')
                    | Result.Error e ->
                        let e' = { e with TypeInference.TypeError.Location = Some node.Location }
                        Error (TypeInference.formatTypeError e')
                | LetBindingExpression letBinding ->
                    let env' = env |> Map.map (fun _ t -> TypeInference.applySubst globalSubst t)
                    match TypeInference.infer env' letBinding.Value with
                    | Ok (inferredType, subst) ->
                        let globalSubst' = TypeInference.composeSubst subst globalSubst
                        let env'' = Map.add letBinding.Name inferredType env
                        let info = {
                            ParamTypes = []
                            ReturnType = inferredType
                            LocalEnv = Map.empty
                            ParamNames = Set.empty
                            OuterNames = Set.empty
                        }
                        Ok ((letBinding.Name, info) :: fnInfos, globalSubst', env'')
                    | Result.Error e ->
                        let e' = { e with TypeInference.TypeError.Location = Some node.Location }
                        Error (TypeInference.formatTypeError e')
                | _ -> Ok (fnInfos, globalSubst, env)
        ) (Ok ([], TypeInference.emptySubst, Map.empty))

    match phase1 with
    | Error e -> Error e
    | Ok (fnInfos, globalSubst, _) ->
        // Phase 2: apply global substitution and convert to TypeDefinitions
        let fnInfos = List.rev fnInfos
        fnInfos |> List.fold (fun acc (name, info) ->
            match acc with
            | Error _ -> acc
            | Ok result ->
                let resolvedParams = info.ParamTypes |> List.map (TypeInference.applySubst globalSubst)
                let resolvedReturn = TypeInference.applySubst globalSubst info.ReturnType
                match resolvedParams |> List.map (convertType name) |> List.fold (fun acc r ->
                    match acc, r with
                    | Error e, _ -> Error e
                    | _, Error e -> Error e
                    | Ok xs, Ok x -> Ok (xs @ [x])) (Ok []) with
                | Error e -> Error e
                | Ok paramTypeDefs ->
                    match convertType name resolvedReturn with
                    | Error e -> Error e
                    | Ok returnTypeDef ->
                        let resolvedLocalEnv = info.LocalEnv |> Map.map (fun _ t -> TypeInference.applySubst globalSubst t)
                        match convertLocalEnv name resolvedLocalEnv info.ParamNames info.OuterNames with
                        | Error e -> Error e
                        | Ok bodyTypes ->
                            let result' = {
                                result with
                                    TypeMap = Map.add name (paramTypeDefs, returnTypeDef) result.TypeMap
                                    BodyTypes = Map.add name bodyTypes result.BodyTypes
                            }
                            Ok result'
        ) (Ok { TypeMap = Map.empty; BodyTypes = Map.empty })

/// Update let binding types in expression bodies using body type map
let rec private applyBodyTypes (bodyTypes: Map<string, TypeDefinitions>) (expr: Expression) : Expression =
    match expr with
    | LetBindingExpression lb ->
        let newType = Map.tryFind lb.Name bodyTypes |> Option.defaultValue lb.Type
        LetBindingExpression { lb with Type = newType; Value = applyBodyTypes bodyTypes lb.Value }
    | OperatorExpression op ->
        OperatorExpression { op with Left = applyBodyTypes bodyTypes op.Left; Right = applyBodyTypes bodyTypes op.Right }
    | FunctionCallExpression call ->
        FunctionCallExpression { call with Arguments = call.Arguments |> List.map (applyBodyTypes bodyTypes) }
    | NegateExpression inner -> NegateExpression (applyBodyTypes bodyTypes inner)
    | _ -> expr

/// Apply inferred types to a list of lowered definitions.
/// Replaces Inferred parameter and return types with concrete types from the type map.
/// Also updates let binding types in function bodies.
let private applyInferredTypes (result: InferResult) (defs: TopLevelDef list) : TopLevelDef list =
    defs |> List.map (fun def ->
        match def with
        | TopFunction functionDef ->
            match Map.tryFind functionDef.Name result.TypeMap with
            | Some (paramTypes, returnType) ->
                let updatedParams =
                    if paramTypes.Length = functionDef.Parameters.Length then
                        List.zip functionDef.Parameters paramTypes
                        |> List.map (fun (param, inferredType) -> { param with Type = inferredType })
                    else functionDef.Parameters
                let bodyTypes = Map.tryFind functionDef.Name result.BodyTypes |> Option.defaultValue Map.empty
                let updatedBody = functionDef.Body |> List.map (applyBodyTypes bodyTypes)
                TopFunction { functionDef with ReturnType = returnType; Parameters = updatedParams; Body = updatedBody }
            | None -> def
        | TopStruct _ | TopOpen _ -> def
    )

/// Full lowering pipeline: Check -> Lower -> Apply types
let lower (modulePath: ModulePath) (nodes: ExpressionNode list) : Result<TopLevelDef list, string> =
    match inferTypes nodes with
    | Error e -> Error e
    | Ok result ->
        nodes
        |> LambdaLifting.liftLambdas modulePath
        |> applyInferredTypes result
        |> Ok

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
            | TopOpen openParts ->
                Ok (SymbolTable.addOpen openParts table)
    ) (Ok SymbolTable.empty)

/// Collect all referenced identifiers from an expression tree
let rec private collectRefs (expr: Expression) : Set<string> =
    match expr with
    | IdentifierExpression name -> Set.singleton name
    | FunctionCallExpression call ->
        let argRefs = call.Arguments |> List.map collectRefs |> Set.unionMany
        Set.add call.FunctionName argRefs
    | OperatorExpression op ->
        Set.union (collectRefs op.Left) (collectRefs op.Right)
    | LetBindingExpression lb ->
        collectRefs lb.Value
    | _ -> Set.empty

/// Check for forward references: each def's body can only reference symbols declared before it.
/// Accepts an initial symbol table (e.g. pre-seeded with dependency symbols).
/// Returns Error with location info on first forward reference found.
let checkForwardReferences (initialTable: SymbolTable.SymbolTable) (defs: TopLevelDef list) : Result<SymbolTable.SymbolTable, string> =
    defs |> List.fold (fun state def ->
        match state with
        | Result.Error _ -> state
        | Ok table ->
            // check body refs against current table (only prior defs)
            match def with
            | TopFunction fn ->
                let paramNames = fn.Parameters |> List.map (fun p -> p.Name) |> Set.ofList
                let bodyRefs = fn.Body |> List.map collectRefs |> Set.unionMany
                // exclude self-params and the function's own name (recursion not checked here)
                let externalRefs = bodyRefs - paramNames |> Set.remove fn.Name
                let (ModulePath parts) = fn.ModulePath
                // same-module symbols are reachable by short name
                let tableWithModScope = SymbolTable.addOpen parts table
                let forwardRef =
                    externalRefs |> Set.toList |> List.tryFind (fun name ->
                        SymbolTable.resolveSymbolFrom parts name tableWithModScope |> Option.isNone)
                match forwardRef with
                | Some name ->
                    let (Line line) = fn.Location.StartLine
                    let (Column col) = fn.Location.StartCol
                    Result.Error $"forward reference to undeclared symbol '{name}' in '{fn.Name}' at line {line}, column {col}"
                | None ->
                    let fullPath = parts @ [fn.Name]
                    SymbolTable.addSymbol fullPath fn.Visibility fn.Parameters.Length table
            | TopStruct structDef ->
                let (ModulePath parts) = structDef.ModulePath
                let fullPath = parts @ [structDef.Name]
                SymbolTable.addSymbol fullPath Visibility.Public 0 table
            | TopOpen openParts ->
                Ok (SymbolTable.addOpen openParts table)
    ) (Ok initialTable)

/// Resolve qualified names in lowered function bodies using symbol table + opens
let resolveNames (symTable: SymbolTable.SymbolTable) (defs: TopLevelDef list) : TopLevelDef list =
    let resolveExprFrom (callerMod: string list) =
        let rec resolveExpr (expr: Expression) : Expression =
            match expr with
            | FunctionCallExpression call ->
                let resolvedName =
                    match SymbolTable.resolveSymbolFrom callerMod call.FunctionName symTable with
                    | Some info -> SymbolTable.qualifiedKey info.FullPath
                    | None -> call.FunctionName
                FunctionCallExpression { call with FunctionName = resolvedName; Arguments = call.Arguments |> List.map resolveExpr }
            | IdentifierExpression name ->
                match SymbolTable.resolveSymbolFrom callerMod name symTable with
                | Some info -> IdentifierExpression (SymbolTable.qualifiedKey info.FullPath)
                | None -> expr
            | OperatorExpression op ->
                OperatorExpression { op with Left = resolveExpr op.Left; Right = resolveExpr op.Right }
            | LetBindingExpression lb ->
                LetBindingExpression { lb with Value = resolveExpr lb.Value }
            | _ -> expr
        resolveExpr

    defs |> List.map (fun def ->
        match def with
        | TopFunction fn ->
            let (ModulePath parts) = fn.ModulePath
            TopFunction { fn with Body = fn.Body |> List.map (resolveExprFrom parts) }
        | other -> other
    )
