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

open BasicTypes
open LanguageExpressions

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
let rec applySubst (subst: Substitution) (t: InferType) : InferType =
    match t with
    | TVar tv ->
        match Map.tryFind tv subst with
        | Some resolved -> applySubst subst resolved  // follow the chain
        | None -> t  // unsolved, leave as-is
    | TFun (params_, ret) ->
        // apply to all parts of a function type
        TFun (params_ |> List.map (applySubst subst), applySubst subst ret)
    | _ -> t  // concrete types like TInt are already resolved

/// Compose two substitutions: first apply s1, then s2.
/// The result is a single substitution that does both.
let composeSubst (s1: Substitution) (s2: Substitution) : Substitution =
    // apply s1 to all types in s2, then merge with s1
    let applied = s2 |> Map.map (fun _ t -> applySubst s1 t)
    Map.fold (fun acc k v -> Map.add k v acc) applied s1

// -- Occurs check --
// Prevents infinite types like t0 = List<t0>.
// If we're trying to unify t0 with something that contains t0, that's an error.

let rec occursIn (tv: TypeVar) (t: InferType) : bool =
    match t with
    | TVar tv2 -> tv = tv2
    | TFun (params_, ret) ->
        params_ |> List.exists (occursIn tv) || occursIn tv ret
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
let rec unify (t1: InferType) (t2: InferType) : Result<Substitution, TypeError> =
    match t1, t2 with
    // already the same type — nothing to do
    | t1, t2 when t1 = t2 -> Result.Ok emptySubst

    // one side is a variable — bind it to the other side
    // this is where types actually get "solved"
    | TVar tv, t | t, TVar tv ->
        if occursIn tv t then
            Result.Error { Message = "infinite type"; Expected = t1; Actual = t2 }
        else
            Result.Ok (Map.ofList [ tv, t ])

    // both are function types — unify parameter-by-parameter and return types
    | TFun (p1, r1), TFun (p2, r2) ->
        if p1.Length <> p2.Length then
            Result.Error {
                Message = $"function arity mismatch: expected {p1.Length} params, got {p2.Length}"
                Expected = t1; Actual = t2
            }
        else
            // unify each pair (param1 with param1, param2 with param2, ... , return with return)
            // accumulate substitutions — each unification can solve variables used by later pairs
            let allPairs = (List.zip p1 p2) @ [ (r1, r2) ]
            allPairs |> List.fold (fun acc (a, b) ->
                match acc with
                | Result.Error e -> Result.Error e
                | Ok subst ->
                    // apply what we've solved so far before unifying the next pair
                    let a' = applySubst subst a
                    let b' = applySubst subst b
                    match unify a' b' with
                    | Result.Error e -> Result.Error e
                    | Ok s2 -> Result.Ok (composeSubst s2 subst)
            ) (Result.Ok emptySubst)

    // two different concrete types — can never be equal
    | _ ->
        Result.Error { Message = "type mismatch"; Expected = t1; Actual = t2 }

// -- Fresh type variable generator --
// Each call to freshVar() returns a new unique type variable.
// We reset between compilation units to keep variable numbers small.

let mutable private nextVar = 0

let freshVar () : InferType =
    let v = nextVar
    nextVar <- nextVar + 1
    TVar (TypeVar v)

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
        let t =
            match lit with
            | IntLiteral _ -> TInt
            | FloatLiteral _ -> TDouble
            | StringLiteral _ -> TString
        Result.Ok (t, emptySubst)

    // Identifiers — look up the name in the environment
    // If found, that's the type. If not, assign a fresh variable (external/unknown).
    | IdentifierExpression name ->
        match Map.tryFind name env with
        | Some t -> Result.Ok (t, emptySubst)
        | None -> Result.Ok (freshVar (), emptySubst)

    // Operators — both sides must be the same type, result is that type
    // e.g. `x + y` constrains x and y to be equal, and the result matches
    | OperatorExpression op ->
        match infer env op.Left with
        | Result.Error e -> Result.Error e
        | Ok (leftType, s1) ->
            // apply s1 to env before inferring right side — left might have solved variables
            let env' = env |> Map.map (fun _ t -> applySubst s1 t)
            match infer env' op.Right with
            | Result.Error e -> Result.Error e
            | Ok (rightType, s2) ->
                let s12 = composeSubst s2 s1
                let leftType' = applySubst s12 leftType
                let rightType' = applySubst s12 rightType
                // unify left and right — they must be the same type for arithmetic
                match unify leftType' rightType' with
                | Result.Error e -> Result.Error e
                | Ok s3 ->
                    let finalSubst = composeSubst s3 s12
                    Result.Ok (applySubst finalSubst leftType', finalSubst)

    // Function calls — infer arg types, then unify with the function's signature
    // e.g. `add 1 2` → infer args [i32, i32], look up add, unify
    | FunctionCallExpression fc ->
        let retVar = freshVar ()  // the return type is unknown until we unify

        // infer each argument's type, accumulating substitutions
        let argResults =
            fc.Arguments |> List.fold (fun acc arg ->
                match acc with
                | Result.Error e -> Result.Error e
                | Ok (types, subst, env') ->
                    match infer env' arg with
                    | Result.Error e -> Result.Error e
                    | Ok (argType, s) ->
                        let newSubst = composeSubst s subst
                        let newEnv = env' |> Map.map (fun _ t -> applySubst s t)
                        Result.Ok (types @ [argType], newSubst, newEnv)
            ) (Result.Ok ([], emptySubst, env))

        match argResults with
        | Result.Error e -> Result.Error e
        | Ok (argTypes, argSubst, _) ->
            match Map.tryFind fc.FunctionName env with
            | Some fnType ->
                // we know the function — build an expected type from the args and unify
                let expectedType = TFun (argTypes, retVar)
                let fnType' = applySubst argSubst fnType
                match unify fnType' expectedType with
                | Result.Error e -> Result.Error e
                | Ok s ->
                    let finalSubst = composeSubst s argSubst
                    // the return type is whatever retVar solved to
                    Result.Ok (applySubst finalSubst retVar, finalSubst)
            | None ->
                // unknown function — can't constrain, return fresh var
                Result.Ok (applySubst argSubst retVar, argSubst)

    // Let bindings — infer the value's type, that becomes the binding's type
    | LetBindingExpression lb ->
        match infer env lb.Value with
        | Result.Error e -> Result.Error e
        | Ok (valueType, subst) ->
            Result.Ok (valueType, subst)

    // Function definitions and structs are handled at top level, not here
    | FunctionDefinitionExpression _ -> Result.Ok (freshVar (), emptySubst)
    | StructExpression _ -> Result.Ok (freshVar (), emptySubst)

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

let inferFunction (env: TypeEnv) (fd: FunctionDetails) : Result<InferType * Substitution, TypeError> =
    // assign fresh type vars to each parameter — we don't know their types yet
    let paramTypes = fd.Parameters |> List.map (fun _ -> freshVar ())
    let paramNames = fd.Parameters |> List.map (fun p -> let (Word w) = p.Name in w)

    // extend the environment with parameter names → their type variables
    let localEnv =
        List.zip paramNames paramTypes
        |> List.fold (fun acc (name, t) -> Map.add name t acc) env

    // infer the body — process each expression, accumulating solved types
    // let bindings add to the environment so later expressions can reference them
    let (BodyExpression bodyExprs) = fd.Body
    let bodyResult =
        bodyExprs |> List.fold (fun acc expr ->
            match acc with
            | Result.Error e -> Result.Error e
            | Ok (_, subst, currentEnv) ->
                // apply what we've solved so far to the environment
                let env' = currentEnv |> Map.map (fun _ t -> applySubst subst t)
                match expr with
                | LetBindingExpression lb ->
                    // let bindings add a new name to the environment
                    match infer env' lb.Value with
                    | Result.Error e -> Result.Error e
                    | Ok (valType, s) ->
                        let newSubst = composeSubst s subst
                        let newEnv = Map.add lb.Name (applySubst newSubst valType) env'
                        Result.Ok (applySubst newSubst valType, newSubst, newEnv)
                | _ ->
                    match infer env' expr with
                    | Result.Error e -> Result.Error e
                    | Ok (t, s) ->
                        let newSubst = composeSubst s subst
                        Result.Ok (applySubst newSubst t, newSubst, env')
        ) (Result.Ok (TInt, emptySubst, localEnv))

    match bodyResult with
    | Result.Error e -> Result.Error e
    | Ok (retType, subst, _) ->
        // apply the final substitution to parameter types — they might have been solved
        // e.g. if the body does `x + 1`, then x's type variable gets unified with i32
        let resolvedParams = paramTypes |> List.map (applySubst subst)
        let fnType = TFun (resolvedParams, retType)
        Result.Ok (fnType, subst)

// -- Convert InferType back to TypeDefinitions --
// Used when writing the resolved types into the lowered AST / .fso output

let rec toTypeDefinition (t: InferType) : TypeDefinitions =
    match t with
    | TInt -> I32
    | TInt64 -> I64
    | TFloat -> Float
    | TDouble -> Double
    | TString -> String
    | TVar _ -> I32       // unresolved vars default to i32 (safe fallback)
    | TFun _ -> Inferred  // function types not yet representable in TypeDefinitions
