/// Hindley-Milner type inference (Algorithm W)
///
/// The goal: given an expression with unknown types, figure out what every type must be.
///
/// How it works:
/// 1. Every unknown type gets a fresh "type variable" (a placeholder like t0, t1, t2)
/// 2. We walk the expression tree and generate constraints:
///    - A literal `42` constrains its type to `i32`
///    - An operator `x + y` constrains both sides to be the same type
///    - A function call `add 1 2` constrains the args to match the function's param types
/// 3. We solve constraints by "unification" — if t0 must be i32 and t0 must equal t1,
///    then t1 is also i32. This produces a "substitution" (a map from variables to types).
/// 4. We apply the substitution to replace all solved variables with concrete types.
///
/// Source ordering matters: we process top-level definitions in the order they appear
/// in the source file. When we infer function A's type, we add it to the environment.
/// When function B calls A, we already know A's type. This is the same model as F#.
module TypeInference

open Types
open Ast

// -- Type representation for inference --
// These are internal to the inference engine. The rest of the compiler uses TypeDefinitions.

/// A type variable — a placeholder for an unknown type, identified by a unique integer.
/// During inference, we try to solve what each variable must be.
type TypeVar = TypeVar of int

/// The types we reason about during inference.
/// TVar is the key innovation — it represents "I don't know yet, but I'll figure it out".
type InferType =
    | TVar of TypeVar       // unknown, to be solved by unification
    | TInt                  // i32
    | TInt64                // i64
    | TFloat                // float
    | TDouble               // double
    | TString               // string
    | TFun of parameters: InferType list * returns: InferType  // function: (param types) -> return type

// -- Substitution --
// A substitution maps type variables to their solved types.
// Example: { t0 -> i32, t1 -> i32 } means "wherever you see t0 or t1, it's actually i32"

type Substitution = Map<TypeVar, InferType>

let emptySubst : Substitution = Map.empty

/// Apply a substitution to a type — replace any solved variables with their concrete types.
/// This is recursive because a variable might map to another variable that's also solved.
let rec applySubst (subst: Substitution) (inferType: InferType) : InferType =
    match inferType with
    | TVar typeVar ->
        match Map.tryFind typeVar subst with
        | Some resolved -> applySubst subst resolved  // follow the chain
        | None -> inferType  // unsolved, leave as-is
    | TFun (parameters, returnType) ->
        // apply to all parts of a function type
        TFun (parameters |> List.map (applySubst subst), applySubst subst returnType)
    | _ -> inferType  // concrete types like TInt are already resolved

/// Compose two substitutions: first apply s1, then s2.
/// The result is a single substitution that does both.
let composeSubst (s1: Substitution) (s2: Substitution) : Substitution =
    // apply s1 to all types in s2, then merge with s1
    let applied = s2 |> Map.map (fun _ inferType -> applySubst s1 inferType)
    Map.fold (fun acc key value -> Map.add key value acc) applied s1

// -- Occurs check --
// Prevents infinite types like t0 = List<t0>.
// If we're trying to unify t0 with something that contains t0, that's an error.

let rec occursIn (typeVar: TypeVar) (inferType: InferType) : bool =
    match inferType with
    | TVar otherVar -> typeVar = otherVar
    | TFun (parameters, returnType) ->
        parameters |> List.exists (occursIn typeVar) || occursIn typeVar returnType
    | _ -> false

// -- Unification --
// The core of type inference. Given two types that must be equal,
// figure out what substitution makes them equal.
//
// Examples:
//   unify(i32, i32) = {} — already equal, nothing to do
//   unify(t0, i32) = {t0 -> i32} — t0 must be i32
//   unify(t0, t1) = {t0 -> t1} — t0 and t1 are the same (we pick one)
//   unify(i32, string) = Error — can never be equal

type TypeError = {
    Message: string
    Expected: InferType
    Actual: InferType
}

/// Try to make two types equal. Returns a substitution that achieves this, or an error.
let rec unify (type1: InferType) (type2: InferType) : Result<Substitution, TypeError> =
    match type1, type2 with
    // already the same type — nothing to do
    | type1, type2 when type1 = type2 -> Result.Ok emptySubst

    // one side is a variable — bind it to the other side
    // this is where types actually get "solved"
    | TVar typeVar, otherType | otherType, TVar typeVar ->
        if occursIn typeVar otherType then
            Result.Error { Message = "infinite type"; Expected = type1; Actual = type2 }
        else
            Result.Ok (Map.ofList [ typeVar, otherType ])

    // both are function types — unify parameter-by-parameter and return types
    | TFun (params1, return1), TFun (params2, return2) ->
        if params1.Length <> params2.Length then
            Result.Error {
                Message = $"function arity mismatch: expected {params1.Length} params, got {params2.Length}"
                Expected = type1; Actual = type2
            }
        else
            // unify each pair (param1 with param1, param2 with param2, ... , return with return)
            // accumulate substitutions — each unification can solve variables used by later pairs
            let allPairs = (List.zip params1 params2) @ [ (return1, return2) ]
            allPairs |> List.fold (fun acc (left, right) ->
                match acc with
                | Result.Error err -> Result.Error err
                | Ok subst ->
                    // apply what we've solved so far before unifying the next pair
                    let left' = applySubst subst left
                    let right' = applySubst subst right
                    match unify left' right' with
                    | Result.Error err -> Result.Error err
                    | Ok newSubst -> Result.Ok (composeSubst newSubst subst)
            ) (Result.Ok emptySubst)

    // two different concrete types — can never be equal
    | _ ->
        Result.Error { Message = "type mismatch"; Expected = type1; Actual = type2 }

// -- Fresh type variable generator --
// Each call to freshVar() returns a new unique type variable.
// We reset between compilation units to keep variable numbers small.

let mutable private nextVar = 0

let freshVar () : InferType =
    let current = nextVar
    nextVar <- nextVar + 1
    TVar (TypeVar current)

let resetVars () =
    nextVar <- 0

// -- Type environment --
// Maps variable/function names to their types.
// Built up as we process definitions top-to-bottom.
// When we see `let add x y = x + y`, we add `add -> (t0, t0) -> t0` to the env.
// When we later see `add 1 2`, we look up `add` and unify with the argument types.

type TypeEnv = Map<string, InferType>

// -- Algorithm W: infer the type of an expression --
// Given a type environment and an expression, returns:
//   - The inferred type of the expression
//   - A substitution of solved type variables
//
// The substitution accumulates as we walk the tree — each sub-expression
// might solve variables that constrain later sub-expressions.

let rec infer (env: TypeEnv) (expr: Expression) : Result<InferType * Substitution, TypeError> =
    match expr with
    // Literals have known types — no inference needed
    | LiteralExpression lit ->
        let inferredType =
            match lit with
            | IntLiteral _ -> TInt
            | FloatLiteral _ -> TDouble
            | StringLiteral _ -> TString
        Result.Ok (inferredType, emptySubst)

    // Identifiers — look up the name in the environment
    // If found, that's the type. If not, assign a fresh variable (external/unknown).
    | IdentifierExpression name ->
        match Map.tryFind name env with
        | Some inferredType -> Result.Ok (inferredType, emptySubst)
        | None -> Result.Ok (freshVar (), emptySubst)

    // Operators — both sides must be the same type, result is that type
    // e.g. `x + y` constrains x and y to be equal, and the result matches
    | OperatorExpression operation ->
        match infer env operation.Left with
        | Result.Error err -> Result.Error err
        | Ok (leftType, leftSubst) ->
            // apply leftSubst to env before inferring right side — left might have solved variables
            let env' = env |> Map.map (fun _ inferType -> applySubst leftSubst inferType)
            match infer env' operation.Right with
            | Result.Error err -> Result.Error err
            | Ok (rightType, rightSubst) ->
                let combinedSubst = composeSubst rightSubst leftSubst
                let leftType' = applySubst combinedSubst leftType
                let rightType' = applySubst combinedSubst rightType
                // unify left and right — they must be the same type for arithmetic
                match unify leftType' rightType' with
                | Result.Error err -> Result.Error err
                | Ok unifySubst ->
                    let finalSubst = composeSubst unifySubst combinedSubst
                    Result.Ok (applySubst finalSubst leftType', finalSubst)

    // Function calls — infer arg types, then unify with the function's signature
    // e.g. `add 1 2` → infer args [i32, i32], look up add, unify
    | FunctionCallExpression functionCall ->
        let returnTypeVar = freshVar ()  // the return type is unknown until we unify

        // infer each argument's type, accumulating substitutions
        let argResults =
            functionCall.Arguments |> List.fold (fun acc arg ->
                match acc with
                | Result.Error err -> Result.Error err
                | Ok (types, subst, env') ->
                    match infer env' arg with
                    | Result.Error err -> Result.Error err
                    | Ok (argType, argSubst) ->
                        let newSubst = composeSubst argSubst subst
                        let newEnv = env' |> Map.map (fun _ inferType -> applySubst argSubst inferType)
                        Result.Ok (types @ [argType], newSubst, newEnv)
            ) (Result.Ok ([], emptySubst, env))

        match argResults with
        | Result.Error err -> Result.Error err
        | Ok (argTypes, argSubst, _) ->
            match Map.tryFind functionCall.FunctionName env with
            | Some fnType ->
                // we know the function — build an expected type from the args and unify
                let expectedType = TFun (argTypes, returnTypeVar)
                let fnType' = applySubst argSubst fnType
                match unify fnType' expectedType with
                | Result.Error err -> Result.Error err
                | Ok unifySubst ->
                    let finalSubst = composeSubst unifySubst argSubst
                    // the return type is whatever returnTypeVar solved to
                    Result.Ok (applySubst finalSubst returnTypeVar, finalSubst)
            | None ->
                // unknown function — can't constrain, return fresh var
                Result.Ok (applySubst argSubst returnTypeVar, argSubst)

    // Let bindings — infer the value's type, that becomes the binding's type
    | LetBindingExpression letBinding ->
        match infer env letBinding.Value with
        | Result.Error err -> Result.Error err
        | Ok (valueType, subst) ->
            Result.Ok (valueType, subst)

    // Function definitions and structs are handled at top level, not here
    | FunctionDefinitionExpression _ -> Result.Ok (freshVar (), emptySubst)
    | StructExpression _ -> Result.Ok (freshVar (), emptySubst)
    | ModuleDeclaration _ -> Result.Ok (freshVar (), emptySubst)

    | OpenDeclaration _ -> Result.Ok (freshVar (), emptySubst)

// -- Infer a top-level function definition --
// This is where the "top-down ordering" matters.
// We process functions in source order, adding each to the environment
// so later functions can reference earlier ones.
//
// For `let add x y = x + y`:
//   1. Assign fresh vars: x -> t0, y -> t1
//   2. Infer body: x + y → unify(t0, t1) → both must be same type
//   3. Return type = type of last expression = t0 (which equals t1)
//   4. Function type: (t0, t0) -> t0 (polymorphic — works on any type with +)

let inferFunction (env: TypeEnv) (details: FunctionDetails) : Result<InferType * Substitution, TypeError> =
    // assign fresh type vars to each parameter — we don't know their types yet
    let paramTypes = details.Parameters |> List.map (fun _ -> freshVar ())
    let paramNames = details.Parameters |> List.map (fun param -> let (Word word) = param.Name in word)

    // extend the environment with parameter names → their type variables
    let localEnv =
        List.zip paramNames paramTypes
        |> List.fold (fun acc (name, inferType) -> Map.add name inferType acc) env

    // infer the body — process each expression, accumulating solved types
    // let bindings add to the environment so later expressions can reference them
    let (BodyExpression bodyExprs) = details.Body
    let bodyResult =
        bodyExprs |> List.fold (fun acc expr ->
            match acc with
            | Result.Error err -> Result.Error err
            | Ok (_, subst, currentEnv) ->
                // apply what we've solved so far to the environment
                let env' = currentEnv |> Map.map (fun _ inferType -> applySubst subst inferType)
                match expr with
                | LetBindingExpression letBinding ->
                    // let bindings add a new name to the environment
                    match infer env' letBinding.Value with
                    | Result.Error err -> Result.Error err
                    | Ok (valueType, valueSubst) ->
                        let newSubst = composeSubst valueSubst subst
                        let newEnv = Map.add letBinding.Name (applySubst newSubst valueType) env'
                        Result.Ok (applySubst newSubst valueType, newSubst, newEnv)
                | _ ->
                    match infer env' expr with
                    | Result.Error err -> Result.Error err
                    | Ok (inferredType, exprSubst) ->
                        let newSubst = composeSubst exprSubst subst
                        Result.Ok (applySubst newSubst inferredType, newSubst, env')
        ) (Result.Ok (TInt, emptySubst, localEnv))

    match bodyResult with
    | Result.Error err -> Result.Error err
    | Ok (returnType, subst, _) ->
        // apply the final substitution to parameter types — they might have been solved
        // e.g. if the body does `x + 1`, then x's type variable gets unified with i32
        let resolvedParams = paramTypes |> List.map (applySubst subst)
        let fnType = TFun (resolvedParams, returnType)
        Result.Ok (fnType, subst)

// -- Convert InferType back to TypeDefinitions --
// Used when writing the resolved types into the lowered AST / .fso output

let rec toTypeDefinition (inferType: InferType) : TypeDefinitions =
    match inferType with
    | TInt -> I32
    | TInt64 -> I64
    | TFloat -> Float
    | TDouble -> Double
    | TString -> String
    | TVar _ -> I32       // unresolved vars default to i32 (safe fallback)
    | TFun _ -> Inferred  // function types not yet representable in TypeDefinitions
