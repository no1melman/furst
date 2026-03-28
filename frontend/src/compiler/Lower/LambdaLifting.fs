module LambdaLifting

open Types
open Ast
open Lowered

let liftLambdas (modulePath: ModulePath) (nodes: ExpressionNode list) : TopLevelDef list =
    let mutable hoisted : LoweredFunctionDef list = []
    // tracks original name -> mangled name for call site rewriting
    let mutable nameRewrites : Map<string, string> = Map.empty

    let rec collectParams (exprs: Expression list) : string list =
        exprs |> List.collect (fun expr ->
            match expr with
            | IdentifierExpression name -> [ name ]
            | _ -> [])

    let rec liftExpr (parentName: string) (outerParams: LoweredParam list) (expr: Expression) : Expression =
        match expr with
        | FunctionDefinitionExpression funcDef ->
            let details = funcDetails funcDef
            let mangledName = parentName + "$" + details.Identifier
            let (BodyExpression bodyExprs) = details.Body

            nameRewrites <- nameRewrites |> Map.add details.Identifier mangledName

            let bodyIdents = collectIdents bodyExprs
            let captured =
                outerParams
                |> List.filter (fun param -> Set.contains param.Name bodyIdents)

            let ownParams =
                details.Parameters
                |> List.map (fun param -> let (Word word) = param.Name in { Name = word; Type = param.Type })

            let allParams = captured @ ownParams

            let liftedBody = bodyExprs |> List.map (liftExpr mangledName allParams)

            hoisted <- {
                Name = mangledName
                ReturnType = details.Type
                Parameters = allParams
                Body = liftedBody
                Location = { StartLine = Line 0L; StartCol = Column 0L; EndLine = Line 0L; EndCol = Column 0L }
                ModulePath = modulePath
                Visibility = Visibility.Private
            } :: hoisted

            IdentifierExpression mangledName

        | LetBindingExpression letBinding ->
            let liftedValue = liftExpr parentName outerParams letBinding.Value
            LetBindingExpression { letBinding with Value = liftedValue }

        | FunctionCallExpression functionCall ->
            let rewrittenName =
                match nameRewrites |> Map.tryFind functionCall.FunctionName with
                | Some mangled -> mangled
                | None -> functionCall.FunctionName
            let liftedArgs = functionCall.Arguments |> List.map (liftExpr parentName outerParams)
            // prepend captured params as extra args for lambda-lifted calls
            let extraArgs =
                match hoisted |> List.tryFind (fun h -> h.Name = rewrittenName) with
                | Some lifted ->
                    let capturedCount = lifted.Parameters.Length - functionCall.Arguments.Length
                    lifted.Parameters
                    |> List.take (max 0 capturedCount)
                    |> List.map (fun param -> IdentifierExpression param.Name)
                | None -> []
            FunctionCallExpression { functionCall with FunctionName = rewrittenName; Arguments = extraArgs @ liftedArgs }

        | OperatorExpression operation ->
            let left = liftExpr parentName outerParams operation.Left
            let right = liftExpr parentName outerParams operation.Right
            OperatorExpression { operation with Left = left; Right = right }

        | NegateExpression inner ->
            NegateExpression (liftExpr parentName outerParams inner)

        | other -> other

    and collectIdents (exprs: Expression list) : Set<string> =
        exprs |> List.fold (fun acc expr ->
            match expr with
            | IdentifierExpression name -> Set.add name acc
            | FunctionCallExpression functionCall ->
                let argIdents = collectIdents functionCall.Arguments
                Set.add functionCall.FunctionName acc |> Set.union argIdents
            | OperatorExpression operation ->
                collectIdents [ operation.Left; operation.Right ] |> Set.union acc
            | LetBindingExpression letBinding ->
                collectIdents [ letBinding.Value ] |> Set.union acc
            | FunctionDefinitionExpression funcDef ->
                let details = funcDetails funcDef
                let (BodyExpression body) = details.Body
                collectIdents body |> Set.union acc
            | NegateExpression inner ->
                collectIdents [ inner ] |> Set.union acc
            | LiteralExpression _ -> acc
            | StructExpression _ -> acc
            | ModuleDeclaration _ -> acc
            | LibDeclaration _ -> acc
            | OpenDeclaration _ -> acc
        ) Set.empty

    let topDefs =
        nodes |> List.collect (fun node ->
            match node.Expr with
            | FunctionDefinitionExpression funcDef ->
                let details = funcDetails funcDef
                let ownParams =
                    details.Parameters
                    |> List.map (fun param -> let (Word word) = param.Name in { Name = word; Type = param.Type })
                let (BodyExpression bodyExprs) = details.Body
                let liftedBody = bodyExprs |> List.map (liftExpr details.Identifier ownParams)
                let lowered = {
                    Name = details.Identifier
                    ReturnType = details.Type
                    Parameters = ownParams
                    Body = liftedBody
                    Location = node.Location
                    ModulePath = modulePath
                    Visibility = details.Visibility
                }
                [ TopFunction lowered ]
            | StructExpression structDef ->
                [ TopStruct {
                    Name = structDef.Name
                    Fields = structDef.Fields
                    Location = node.Location
                    ModulePath = modulePath
                } ]
            | LetBindingExpression letBinding ->
                // top-level let binding -> zero-param function
                [ TopFunction {
                    Name = letBinding.Name
                    ReturnType = letBinding.Type
                    Parameters = []
                    Body = [ letBinding.Value ]
                    Location = node.Location
                    ModulePath = modulePath
                    Visibility = Visibility.Public
                } ]
            | OpenDeclaration parts ->
                [ TopOpen parts ]
            | ModuleDeclaration _ | LibDeclaration _ ->
                [] // declarations don't produce lowered defs
            | other ->
                // top-level expression -> wrap in anonymous
                [ TopFunction {
                    Name = "_main"
                    ReturnType = Inferred
                    Parameters = []
                    Body = [ other ]
                    Location = node.Location
                    ModulePath = modulePath
                    Visibility = Visibility.Public
                } ]
        )

    // hoisted fns come first (dependencies before dependents)
    (hoisted |> List.rev |> List.map TopFunction) @ topDefs
