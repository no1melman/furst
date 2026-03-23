module Lowering

open BasicTypes
open LanguageExpressions

// -- Lowered IR types (flat, all types resolved) --

type LoweredParam = {
    Name: string
    Type: TypeDefinitions
}

type LoweredFunctionDef = {
    Name: string
    ReturnType: TypeDefinitions
    Parameters: LoweredParam list
    Body: Expression list
    Location: SourceLocation
}

type LoweredStructDef = {
    Name: string
    Fields: (string * TypeDefinitions) list
    Location: SourceLocation
}

type TopLevelDef =
    | TopFunction of LoweredFunctionDef
    | TopExportedFunction of LoweredFunctionDef
    | TopStruct of LoweredStructDef

let private funcDetails (fd: FunctionDefinition) =
    match fd with
    | InternalFuncDef d | ExportedFuncDef d -> d

// -- Pass 1: Type resolution (stub — passes through since no inference yet) --

/// Run type inference and build a map of function name → (param types, return type)
/// This map is used during lowering to fill in concrete types instead of Inferred
let inferTypes (nodes: ExpressionNode list) : Map<string, TypeDefinitions list * TypeDefinitions> =
    TypeInference.resetVars ()
    let (typeMap, _) =
        nodes |> List.fold (fun (tmap, env) node ->
            match node.Expr with
            | FunctionDefinitionExpression funcDef ->
                let fd = funcDetails funcDef
                match TypeInference.inferFunction env fd with
                | Ok (fnType, subst) ->
                    let env' = Map.add fd.Identifier fnType env
                    match fnType with
                    | TypeInference.TFun (paramTypes, retType) ->
                        let paramTypeDefs = paramTypes |> List.map (TypeInference.applySubst subst >> TypeInference.toTypeDefinition)
                        let retTypeDef = TypeInference.applySubst subst retType |> TypeInference.toTypeDefinition
                        (Map.add fd.Identifier (paramTypeDefs, retTypeDef) tmap, env')
                    | _ -> (tmap, env')
                | Result.Error _e -> (tmap, env)
            | LetBindingExpression lb ->
                match TypeInference.infer env lb.Value with
                | Ok (t, _) ->
                    let typeDef = TypeInference.toTypeDefinition t
                    let env' = Map.add lb.Name t env
                    (Map.add lb.Name ([], typeDef) tmap, env')
                | Result.Error _ -> (tmap, env)
            | _ -> (tmap, env)
        ) (Map.empty, Map.empty)
    typeMap

// -- Pass 2: Lambda lifting --
// Hoists nested functions to top level. Captured vars become extra params.
// Name mangling: outer$inner

let liftLambdas (nodes: ExpressionNode list) : TopLevelDef list =
    let mutable hoisted : LoweredFunctionDef list = []
    // tracks original name → mangled name for call site rewriting
    let mutable nameRewrites : Map<string, string> = Map.empty

    let rec collectParams (exprs: Expression list) : string list =
        exprs |> List.collect (fun e ->
            match e with
            | IdentifierExpression name -> [ name ]
            | _ -> [])

    let rec liftExpr (parentName: string) (outerParams: LoweredParam list) (expr: Expression) : Expression =
        match expr with
        | FunctionDefinitionExpression funcDef ->
            let fd = funcDetails funcDef
            let mangledName = parentName + "$" + fd.Identifier
            let (BodyExpression bodyExprs) = fd.Body

            nameRewrites <- nameRewrites |> Map.add fd.Identifier mangledName

            let bodyIdents = collectIdents bodyExprs
            let captured =
                outerParams
                |> List.filter (fun p -> Set.contains p.Name bodyIdents)

            let ownParams =
                fd.Parameters
                |> List.map (fun p -> let (Word w) = p.Name in { Name = w; Type = p.Type })

            let allParams = captured @ ownParams

            let liftedBody = bodyExprs |> List.map (liftExpr mangledName allParams)

            hoisted <- {
                Name = mangledName
                ReturnType = fd.Type
                Parameters = allParams
                Body = liftedBody
                Location = { StartLine = Line 0L; StartCol = Column 0L; EndLine = Line 0L; EndCol = Column 0L }
            } :: hoisted

            IdentifierExpression mangledName

        | LetBindingExpression lb ->
            let liftedValue = liftExpr parentName outerParams lb.Value
            LetBindingExpression { lb with Value = liftedValue }

        | FunctionCallExpression fc ->
            let rewrittenName =
                match nameRewrites |> Map.tryFind fc.FunctionName with
                | Some mangled -> mangled
                | None -> fc.FunctionName
            let liftedArgs = fc.Arguments |> List.map (liftExpr parentName outerParams)
            // prepend captured params as extra args for lambda-lifted calls
            let extraArgs =
                match hoisted |> List.tryFind (fun h -> h.Name = rewrittenName) with
                | Some lifted ->
                    let capturedCount = lifted.Parameters.Length - fc.Arguments.Length
                    lifted.Parameters
                    |> List.take (max 0 capturedCount)
                    |> List.map (fun p -> IdentifierExpression p.Name)
                | None -> []
            FunctionCallExpression { fc with FunctionName = rewrittenName; Arguments = extraArgs @ liftedArgs }

        | OperatorExpression op ->
            let l = liftExpr parentName outerParams op.Left
            let r = liftExpr parentName outerParams op.Right
            OperatorExpression { op with Left = l; Right = r }

        | other -> other

    and collectIdents (exprs: Expression list) : Set<string> =
        exprs |> List.fold (fun acc e ->
            match e with
            | IdentifierExpression name -> Set.add name acc
            | FunctionCallExpression fc ->
                let argIdents = collectIdents fc.Arguments
                Set.add fc.FunctionName acc |> Set.union argIdents
            | OperatorExpression op ->
                collectIdents [ op.Left; op.Right ] |> Set.union acc
            | LetBindingExpression lb ->
                collectIdents [ lb.Value ] |> Set.union acc
            | FunctionDefinitionExpression funcDef ->
                let fd = funcDetails funcDef
                let (BodyExpression body) = fd.Body
                collectIdents body |> Set.union acc
            | LiteralExpression _ -> acc
            | StructExpression _ -> acc
        ) Set.empty

    let topDefs =
        nodes |> List.collect (fun node ->
            match node.Expr with
            | FunctionDefinitionExpression funcDef ->
                let fd = funcDetails funcDef
                let ownParams =
                    fd.Parameters
                    |> List.map (fun p -> let (Word w) = p.Name in { Name = w; Type = p.Type })
                let (BodyExpression bodyExprs) = fd.Body
                let liftedBody = bodyExprs |> List.map (liftExpr fd.Identifier ownParams)
                let lowered = {
                    Name = fd.Identifier
                    ReturnType = fd.Type
                    Parameters = ownParams
                    Body = liftedBody
                    Location = node.Location
                }
                let wrapper = match funcDef with ExportedFuncDef _ -> TopExportedFunction | InternalFuncDef _ -> TopFunction
                [ wrapper lowered ]
            | StructExpression sd ->
                [ TopStruct {
                    Name = sd.Name
                    Fields = sd.Fields
                    Location = node.Location
                } ]
            | LetBindingExpression lb ->
                // top-level let binding → zero-param function
                [ TopFunction {
                    Name = lb.Name
                    ReturnType = lb.Type
                    Parameters = []
                    Body = [ lb.Value ]
                    Location = node.Location
                } ]
            | other ->
                // top-level expression → wrap in anonymous
                [ TopFunction {
                    Name = "_main"
                    ReturnType = Inferred
                    Parameters = []
                    Body = [ other ]
                    Location = node.Location
                } ]
        )

    // hoisted fns come first (dependencies before dependents)
    (hoisted |> List.rev |> List.map TopFunction) @ topDefs

/// Apply inferred types to a list of lowered definitions.
/// Replaces Inferred parameter and return types with concrete types from the type map.
let private applyInferredTypes (typeMap: Map<string, TypeDefinitions list * TypeDefinitions>) (defs: TopLevelDef list) : TopLevelDef list =
    defs |> List.map (fun def ->
        match def with
        | TopFunction fd | TopExportedFunction fd ->
            match Map.tryFind fd.Name typeMap with
            | Some (paramTypes, retType) ->
                let updatedParams =
                    if paramTypes.Length = fd.Parameters.Length then
                        List.zip fd.Parameters paramTypes
                        |> List.map (fun (p, t) -> { p with Type = t })
                    else fd.Parameters
                let updated = { fd with ReturnType = retType; Parameters = updatedParams }
                match def with
                | TopExportedFunction _ -> TopExportedFunction updated
                | _ -> TopFunction updated
            | None -> def
        | TopStruct _ -> def
    )

// -- Pipeline --

let lower (nodes: ExpressionNode list) : TopLevelDef list =
    let typeMap = inferTypes nodes
    nodes
    |> liftLambdas
    |> applyInferredTypes typeMap
