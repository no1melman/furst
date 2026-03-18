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
    | TopStruct of LoweredStructDef

// -- Pass 1: Type resolution (stub — passes through since no inference yet) --

let resolveTypes (nodes: ExpressionNode list) : ExpressionNode list =
    // TODO: replace Inferred with concrete types once inference exists
    nodes

// -- Pass 2: Lambda lifting --
// Hoists nested functions to top level. Captured vars become extra params.
// Name mangling: outer$inner

let liftLambdas (nodes: ExpressionNode list) : TopLevelDef list =
    let mutable hoisted : LoweredFunctionDef list = []

    let rec collectParams (exprs: Expression list) : string list =
        exprs |> List.collect (fun e ->
            match e with
            | IdentifierExpression name -> [ name ]
            | _ -> [])

    let rec liftExpr (parentName: string) (outerParams: LoweredParam list) (expr: Expression) : Expression =
        match expr with
        | FunctionExpression fd ->
            let mangledName = parentName + "$" + fd.Identifier
            let (BodyExpression bodyExprs) = fd.Body

            // captured vars = outer params that appear in body
            let bodyIdents = collectIdents bodyExprs
            let captured =
                outerParams
                |> List.filter (fun p -> Set.contains p.Name bodyIdents)

            let ownParams =
                fd.Parameters
                |> List.map (fun p -> let (Word w) = p.Name in { Name = w; Type = p.Type })

            let allParams = captured @ ownParams

            // recursively lift nested fns in body
            let liftedBody = bodyExprs |> List.map (liftExpr mangledName allParams)

            hoisted <- {
                Name = mangledName
                ReturnType = fd.Type
                Parameters = allParams
                Body = liftedBody
                Location = { StartLine = Line 0L; StartCol = Column 0L; EndLine = Line 0L; EndCol = Column 0L }
            } :: hoisted

            // replace with call passing captured vars + original args
            // but in parent body context we just leave calls as-is for now
            // the nested fn reference becomes a call site rewrite handled below
            IdentifierExpression mangledName

        | LetBindingExpression lb ->
            let liftedValue = liftExpr parentName outerParams lb.Value
            LetBindingExpression { lb with Value = liftedValue }

        | FunctionCallExpression fc ->
            let liftedArgs = fc.Arguments |> List.map (liftExpr parentName outerParams)
            FunctionCallExpression { fc with Arguments = liftedArgs }

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
            | FunctionExpression fd ->
                let (BodyExpression body) = fd.Body
                collectIdents body |> Set.union acc
            | LiteralExpression _ -> acc
            | StructExpression _ -> acc
        ) Set.empty

    let topDefs =
        nodes |> List.collect (fun node ->
            match node.Expr with
            | FunctionExpression fd ->
                let ownParams =
                    fd.Parameters
                    |> List.map (fun p -> let (Word w) = p.Name in { Name = w; Type = p.Type })
                let (BodyExpression bodyExprs) = fd.Body
                let liftedBody = bodyExprs |> List.map (liftExpr fd.Identifier ownParams)
                [ TopFunction {
                    Name = fd.Identifier
                    ReturnType = fd.Type
                    Parameters = ownParams
                    Body = liftedBody
                    Location = node.Location
                } ]
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

// -- Pipeline --

let lower (nodes: ExpressionNode list) : TopLevelDef list =
    nodes
    |> resolveTypes
    |> liftLambdas
