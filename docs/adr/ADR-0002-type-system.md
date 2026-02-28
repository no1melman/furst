# ADR-0002: Algebraic Type System and Type Inference

**Status:** Proposed
**Date:** 2026-02-27
**Context:** Type checking, type inference, algebraic data types
**Depends on:** ADR-0001 (symbol tracking)

---

## Context

Current type system is minimal:
```fsharp
type TypeDefinitions =
  | I32 | I64 | Float | Double | String
  | Inferred
  | UserDefined of string
```

Need full algebraic type system supporting:
- Sum types (discriminated unions): `type Option<T> = Some T | None`
- Product types (records/tuples): `{ x: Int, y: Int }`
- Function types: `Int -> Int -> Int`
- Type variables/generics: `<T>`
- Type inference: `let x = 5` infers `Int`

## Type System Requirements

### 1. Type Representation
Need Type ADT representing all type expressions:
```fsharp
type Type =
  // Primitives
  | TInt32 | TInt64 | TFloat | TDouble | TString | TBool

  // Compound types
  | TFunction of Type * Type           // a -> b
  | TTuple of Type list                // (a, b, c)
  | TRecord of (string * Type) list    // { x: Int, y: String }

  // Algebraic types
  | TSum of string * Type list         // Option<T> = Some T | None
  | TUserDefined of string             // User-defined type name

  // Polymorphism
  | TVar of string                     // Type variable: 'a, 'b
  | TApp of Type * Type list           // Generic application: List<Int>
```

### 2. Type Inference Algorithm

**Option A: Hindley-Milner (HM)**
- Full type inference
- No annotations required (but allowed)
- Principled polymorphism via let-generalization
- Used by: ML, Haskell, F#

**Option B: Bidirectional Typing**
- Mix of inference and checking
- Annotations required at function boundaries
- Simpler implementation than HM
- Used by: TypeScript, Flow

**Option C: Explicit Types Only**
- All types must be annotated
- No inference
- Simplest implementation
- Used by: C, Java (pre-generics)

### 3. Type Features

**Must have:**
- [x] Primitive types (Int, Float, String, Bool)
- [x] Function types with currying: `a -> b -> c`
- [x] Sum types: `type Result<T,E> = Ok T | Error E`
- [x] Product types: records and tuples
- [x] Type aliases: `type UserId = Int`

**Should have:**
- [ ] Parametric polymorphism: `List<T>`
- [ ] Type inference (at least local)
- [ ] Pattern matching on types
- [ ] Recursive types

**Could have:**
- [ ] Row polymorphism (extensible records)
- [ ] Higher-kinded types
- [ ] Type classes/traits

## Decision

**Adopt Hindley-Milner type inference with explicit annotations at top-level**

Hybrid approach:
1. **Top-level definitions:** require type signatures
   ```fsharp
   let add (a: Int) (b: Int): Int = a + b
   ```

2. **Local bindings:** full inference
   ```fsharp
   let add a b =
     let sum = a + b    // inferred as Int
     sum
   ```

3. **Generalization:** automatic at let-bindings
   ```fsharp
   let identity x = x  // inferred as ∀a. a -> a
   ```

**Rationale:**
- Top-level annotations help readability, documentation
- Local inference reduces boilerplate
- HM is well-studied, proven approach for F#-like languages
- Supports full polymorphism without runtime overhead

## Type System Implementation

### Phase 1: Type Representation
```fsharp
type Type =
  | TPrim of PrimType
  | TFunc of Type * Type
  | TTuple of Type list
  | TRecord of (string * Type) list
  | TVar of TypeVar
  | TApp of string * Type list

and PrimType =
  | TInt32 | TInt64 | TFloat | TDouble | TString | TBool

and TypeVar = {
  Id: int              // Unique ID for unification
  Name: string option  // Display name ('a, 'b, etc)
  mutable Binding: Type option  // Unified type
}
```

### Phase 2: Type Environment
```fsharp
type TypeEnv = {
  // Maps variable names to type schemes
  Variables: Map<string, TypeScheme>

  // Maps type names to type definitions
  Types: Map<string, TypeDefinition>
}

and TypeScheme = {
  // Quantified type variables: ∀a,b. ...
  Quantified: TypeVar list

  // The type: a -> b -> a
  Type: Type
}

and TypeDefinition =
  | TypeAlias of Type
  | SumType of (string * Type list) list  // Variants
  | RecordType of (string * Type) list    // Fields
```

### Phase 3: Unification Algorithm
```fsharp
let rec unify (t1: Type) (t2: Type) : Result<unit, TypeError> =
  match t1, t2 with
  | TPrim p1, TPrim p2 when p1 = p2 -> Ok ()
  | TFunc (a1, b1), TFunc (a2, b2) ->
      unify a1 a2 >>= fun _ -> unify b1 b2
  | TVar tv, t | t, TVar tv ->
      unifyVar tv t
  | _ -> Error (TypeMismatch (t1, t2))

and unifyVar (tv: TypeVar) (t: Type) : Result<unit, TypeError> =
  match tv.Binding with
  | Some bound -> unify bound t
  | None ->
      if occursCheck tv t then
        Error (InfiniteType (tv, t))
      else
        tv.Binding <- Some t
        Ok ()
```

### Phase 4: Type Inference (Algorithm W)
```fsharp
let rec infer (env: TypeEnv) (expr: Expression)
    : Result<Type * Constraints, TypeError> =
  match expr with
  | LiteralExpression (IntLiteral _) ->
      Ok (TPrim TInt32, [])

  | IdentifierExpression name ->
      match Map.tryFind name env.Variables with
      | Some scheme -> Ok (instantiate scheme, [])
      | None -> Error (UnboundVariable name)

  | FunctionExpression func ->
      // Infer parameter types
      let paramTypes = func.Parameters |> List.map (fun p ->
          match p.Type with
          | Inferred -> freshTypeVar ()
          | _ -> convertType p.Type
      )

      // Extend environment with parameters
      let env' = addParams env func.Parameters paramTypes

      // Infer body type
      let! bodyType, constraints = infer env' func.Body

      // Build function type: param1 -> param2 -> ... -> body
      let funcType = List.foldBack TFunc paramTypes bodyType

      Ok (funcType, constraints)

  | OperatorExpression op ->
      let! leftType, c1 = infer env op.Left
      let! rightType, c2 = infer env op.Right

      // Operators are polymorphic: ∀a. a -> a -> a (for numeric types)
      let resultType = freshTypeVar ()
      let constraints = [
          (leftType, resultType)
          (rightType, resultType)
          (resultType, numericConstraint)
      ] @ c1 @ c2

      Ok (resultType, constraints)

  | FunctionCallExpression call ->
      let! funcType, c1 = infer env (IdentifierExpression call.FunctionName)
      let! argTypes, c2 = inferList env call.Arguments

      let resultType = freshTypeVar ()
      let expectedType = List.foldBack TFunc argTypes resultType

      Ok (resultType, (funcType, expectedType) :: c1 @ c2)
```

### Phase 5: Type Checking
```fsharp
let check (env: TypeEnv) (expr: Expression) (expected: Type)
    : Result<Constraints, TypeError> =
  let! inferred, constraints = infer env expr
  let! () = unify inferred expected
  Ok constraints
```

## Algebraic Type Definitions

### Sum Types (Discriminated Unions)
```fsharp
// Syntax
type Option<T> =
  | Some of T
  | None

type Result<T, E> =
  | Ok of T
  | Error of E

// Representation in AST
type TypeDefinition = {
  Name: string
  TypeParams: string list
  Variants: (string * Type list) list
}
```

### Product Types (Records)
```fsharp
// Syntax
type Point = {
  x: Float
  y: Float
}

// Representation
type RecordDefinition = {
  Name: string
  Fields: (string * Type) list
}
```

## Type Checking Integration

1. **Parse** → AST with `Inferred` types
2. **Collect symbols** → Symbol table
3. **Infer types** → Solve constraints, fill in types
4. **Validate** → Check all expressions well-typed
5. **Emit** → Generate LLVM IR with concrete types

## Consequences

**Positive:**
- Full algebraic type system (sum + product types)
- Type inference reduces annotations
- Catches type errors at compile time
- Supports polymorphism without runtime cost
- Foundation for advanced features (type classes, etc)

**Negative:**
- Complex implementation (HM algorithm ~500 LOC)
- Type errors can be cryptic (need good error messages)
- Constraint solving can be slow for large expressions

**Trade-offs:**
- Require top-level annotations → better docs, simpler errors
- Allow local inference → less boilerplate, cleaner code
- HM over bidirectional → full inference vs simpler impl

## Implementation Order

1. ✅ Basic type representation (Type ADT)
2. [ ] Type environment and schemes
3. [ ] Unification algorithm
4. [ ] Fresh type variable generation
5. [ ] Type inference (Algorithm W)
6. [ ] Constraint solving
7. [ ] Generalization/instantiation
8. [ ] Sum type definitions
9. [ ] Record type definitions
10. [ ] Pattern matching on types

## Examples

```fsharp
// Primitive inference
let x = 5              // inferred: Int32
let y = 3.14           // inferred: Float

// Function inference
let add a b = a + b    // inferred: Int -> Int -> Int (with numeric constraint)

// Polymorphic function
let identity x = x     // inferred: ∀a. a -> a

// Sum type
type Option<T> =
  | Some of T
  | None

let safeDiv a b =
  if b = 0 then None
  else Some (a / b)
// inferred: Int -> Int -> Option<Int>

// Record type
type Point = { x: Float, y: Float }

let distance p1 p2 =
  let dx = p1.x - p2.x
  let dy = p1.y - p2.y
  sqrt (dx * dx + dy * dy)
// inferred: Point -> Point -> Float
```

## Related ADRs
- ADR-0001: Symbol tracking (provides symbol table for type env)
- (Future) ADR-0003: Pattern matching implementation
- (Future) ADR-0004: Type classes vs interfaces
