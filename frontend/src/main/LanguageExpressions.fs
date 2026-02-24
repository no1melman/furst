module LanguageExpressions

open BasicTypes

type CompileError = {
  Message: string
  Line: Line
  Column: Column
  Length: TokenLength
}

type ParameterExpression = { Name: WordToken; Type: TypeDefinitions }

type Expressions =
  | ParameterExpression

let (|TypedParameterExpressionMatch|ParameterExpressionMatch|Incorrect|) (tokens: TokenWithMetadata list) =
  match tokens |> List.map (fun t -> t.Token) with
  | [ OpenParen; Name name; TypeIdentifier; TypeDefinition typeDefinition; ClosedParen ] ->
    TypedParameterExpressionMatch { Name = name; Type = typeDefinition }
  | [ Name name ] -> ParameterExpressionMatch { Name = name; Type = Inferred }
  | _ -> Incorrect

type BinaryOperator =
  | Add
  | Subtract
  | Multiply

type LiteralValue =
  | IntLiteral of int
  | FloatLiteral of float
  | StringLiteral of string

type BodyExpression = BodyExpression of Expression list
  and LetBinding =
    {
      Name: string
      Type: TypeDefinitions
      Value: Expression
    }
  and FunctionCall =
    {
      FunctionName: string
      Arguments: Expression list
    }
  and BinaryOperation =
    {
      Left: Expression
      Operator: BinaryOperator
      Right: Expression
    }
  and StructDefinition =
    {
      Name: string
      Fields: (string * TypeDefinitions) list
    }
  and FunctionDefinition =
    {
      Identifier: string
      Type: TypeDefinitions
      Parameters: ParameterExpression list
      Body: BodyExpression
    }
    // Building FunctionDefinition from Row:
    // After parsing + nesting (via TestTwoPhase.nestRows), indented continuations
    // are already in row.Body. Example:
    //
    // let thing a =
    //   a
    //   b
    //
    // Becomes:
    // Row {
    //   indent = 0
    //   expressions = [Let; Name "thing"; Name "a"; Assignment]
    //   body = [
    //     Row { indent=2; expressions=[Name "a"]; body=[] }
    //     Row { indent=2; expressions=[Name "b"]; body=[] }
    //   ]
    // }
    //
    // FromRow should:
    // 1. Extract identifier, params from row.Expressions
    // 2. Recursively convert row.Body rows -> Expression list
    // 3. Wrap in BodyExpression
    // 4. Return Some FunctionDefinition or None

    static member IsFunctionDefinition (row: Row) =
      match row.Expressions |> List.map (fun t -> t.Token) with
      | [ Let; Name _; Assignment ] ->
          // Variable binding with no params - only function if has body
          not row.Body.IsEmpty
      | Let :: Name _ :: rest ->
          // Extract tokens between function name and Assignment
          let paramsBeforeAssignment = rest |> List.takeWhile ((<>) Assignment)
          if paramsBeforeAssignment.IsEmpty then
              // No params, check if has body
              not row.Body.IsEmpty
          else
              // Has params - it's a function
              let paramTokens = row.Expressions |> List.skip 2 |> List.takeWhile (fun t -> t.Token <> Assignment)
              FunctionDefinition.IsParameterListExpression paramTokens
      | _ -> false
    static member IsParameterListExpression (tokens: TokenWithMetadata list) =
      let isParameterExpression tokens =
        match tokens with
        | TypedParameterExpressionMatch expr -> Some expr
        | ParameterExpressionMatch expr -> Some expr
        | Incorrect -> None

      let rec walkList list =
        match list |> List.map (fun t -> t.Token) with
        | OpenParen :: _ ->
          let maybeExpr = isParameterExpression list.[0..4]
          maybeExpr |> Option.bind (fun expr ->
              let remaining = list.[5..]
              match remaining with
              | [] -> Some [ expr ]
              | _ -> walkList list.[5..]
            )
        | Name _ :: tail -> walkList (List.tail list)
        | Parameter _ :: tail -> walkList (List.tail list)  // Handle Parameter tokens
        // once we're empty we can just leave
        | [] -> Some []
        | _ -> None

      match tokens with
      | [] -> false
      | _ -> walkList tokens |> Option.isSome
      
            
          
  and Expression =
    | LetBindingExpression of LetBinding
    | FunctionExpression of FunctionDefinition
    | FunctionCallExpression of FunctionCall
    | BinaryOpExpression of BinaryOperation
    | IdentifierExpression of string
    | LiteralExpression of LiteralValue
    | StructExpression of StructDefinition

// Helper to extract token list from Row
let tokensFromRow (row: Row) : Tokens list =
    row.Expressions |> List.map (fun t -> t.Token)

// Extract identifier name from Name token
let extractName (tokens: Tokens list) : string option =
    tokens
    |> List.tryPick (function Name (Word n) -> Some n | _ -> None)

// Helper to get first token position for errors
let getFirstTokenPos (row: Row) : (Line * Column * TokenLength) =
    match row.Expressions |> List.tryHead with
    | Some t -> (t.Line, t.Column, t.Length)
    | None -> (Line 0L, Column 0L, TokenLength 0)

// Main expression builder - converts Row to Expression
let rec rowToExpression (row: Row) : Result<Expression, CompileError> =
    match tokensFromRow row with
    // let x = value (no body = variable binding)
    | Let :: Name (Word name) :: Assignment :: valueToks when row.Body.IsEmpty ->
        buildLetBinding name valueToks row
    // let f params = body (has body = function)
    | Let :: Name (Word name) :: rest when FunctionDefinition.IsFunctionDefinition row ->
        buildFunctionDefinition row
    // struct X { ... }
    | Struct :: Name (Word name) :: _ ->
        buildStructDefinition name row
    // expression (identifier, literal, call, binop)
    | _ ->
        parseExpression row.Expressions

and buildLetBinding (name: string) (valueToks: Tokens list) (row: Row) : Result<Expression, CompileError> =
    if valueToks.IsEmpty then
        // Find Assignment token for position
        let assignTok = row.Expressions |> List.find (fun t -> t.Token = Assignment)
        Error {
            Message = $"Expected value after '=' in let binding for '{name}'"
            Line = assignTok.Line
            Column = assignTok.Column
            Length = assignTok.Length
        }
    else
        // Parse value tokens into expression
        let valueTokensMeta = row.Expressions |> List.skipWhile (fun t -> t.Token <> Assignment) |> List.tail
        match parseExpression valueTokensMeta with
        | Ok valueExpr ->
            Ok (LetBindingExpression {
                Name = name
                Type = Inferred
                Value = valueExpr
            })
        | Error e -> Error e

and parseExpression (tokens: TokenWithMetadata list) : Result<Expression, CompileError> =
    // Helper to extract identifier from Name or Parameter token
    let getIdentifier = function
        | Name (Word n) -> Some n
        | Parameter p -> Some p
        | _ -> None

    match tokens |> List.map (fun t -> t.Token) with
    // Single identifier
    | [ Name (Word n) ] -> Ok (IdentifierExpression n)
    | [ Parameter p ] -> Ok (IdentifierExpression p)
    // Single literal
    | [ NumberLiteral lit ] when lit.IsInteger ->
        Ok (LiteralExpression (IntLiteral (int lit.String)))
    | [ NumberLiteral lit ] ->
        Ok (LiteralExpression (FloatLiteral (float lit.String)))
    // Binary operation: a + b (handle identifiers and literals)
    | [ leftToken; Tokens.Addition; rightToken ] ->
        let parseOperand token =
            match getIdentifier token with
            | Some id -> Some (IdentifierExpression id)
            | None ->
                match token with
                | NumberLiteral lit when lit.IsInteger -> Some (LiteralExpression (IntLiteral (int lit.String)))
                | NumberLiteral lit -> Some (LiteralExpression (FloatLiteral (float lit.String)))
                | _ -> None

        match parseOperand leftToken, parseOperand rightToken with
        | Some left, Some right ->
            Ok (BinaryOpExpression {
                Left = left
                Operator = BinaryOperator.Add
                Right = right
            })
        | _ ->
            let firstToken = tokens.Head
            Error {
                Message = "Binary operation requires valid operands"
                Line = firstToken.Line
                Column = firstToken.Column
                Length = firstToken.Length
            }
    | [ leftToken; Tokens.Subtraction; rightToken ] ->
        let parseOperand token =
            match getIdentifier token with
            | Some id -> Some (IdentifierExpression id)
            | None ->
                match token with
                | NumberLiteral lit when lit.IsInteger -> Some (LiteralExpression (IntLiteral (int lit.String)))
                | NumberLiteral lit -> Some (LiteralExpression (FloatLiteral (float lit.String)))
                | _ -> None

        match parseOperand leftToken, parseOperand rightToken with
        | Some left, Some right ->
            Ok (BinaryOpExpression {
                Left = left
                Operator = BinaryOperator.Subtract
                Right = right
            })
        | _ ->
            let firstToken = tokens.Head
            Error {
                Message = "Binary operation requires valid operands"
                Line = firstToken.Line
                Column = firstToken.Column
                Length = firstToken.Length
            }
    | [ leftToken; Tokens.Multiply; rightToken ] ->
        let parseOperand token =
            match getIdentifier token with
            | Some id -> Some (IdentifierExpression id)
            | None ->
                match token with
                | NumberLiteral lit when lit.IsInteger -> Some (LiteralExpression (IntLiteral (int lit.String)))
                | NumberLiteral lit -> Some (LiteralExpression (FloatLiteral (float lit.String)))
                | _ -> None

        match parseOperand leftToken, parseOperand rightToken with
        | Some left, Some right ->
            Ok (BinaryOpExpression {
                Left = left
                Operator = BinaryOperator.Multiply
                Right = right
            })
        | _ ->
            let firstToken = tokens.Head
            Error {
                Message = "Binary operation requires valid operands"
                Line = firstToken.Line
                Column = firstToken.Column
                Length = firstToken.Length
            }
    // Function call: f a b
    | funcToken :: argTokens when not argTokens.IsEmpty ->
        match getIdentifier funcToken with
        | Some funcName ->
            // Parse arguments
            let args =
                argTokens
                |> List.choose (fun token ->
                    match token with
                    | Name (Word n) -> Some (IdentifierExpression n)
                    | Parameter p -> Some (IdentifierExpression p)
                    | NumberLiteral lit when lit.IsInteger -> Some (LiteralExpression (IntLiteral (int lit.String)))
                    | NumberLiteral lit -> Some (LiteralExpression (FloatLiteral (float lit.String)))
                    | _ -> None)

            if args.Length = argTokens.Length then
                Ok (FunctionCallExpression {
                    FunctionName = funcName
                    Arguments = args
                })
            else
                let firstToken = tokens.Head
                Error {
                    Message = "Invalid arguments in function call"
                    Line = firstToken.Line
                    Column = firstToken.Column
                    Length = firstToken.Length
                }
        | None ->
            let firstToken = tokens.Head
            Error {
                Message = "Function call requires identifier"
                Line = firstToken.Line
                Column = firstToken.Column
                Length = firstToken.Length
            }
    | [] ->
        Error {
            Message = "Empty expression"
            Line = Line 0L
            Column = Column 0L
            Length = TokenLength 0
        }
    | tokenList ->
        let firstToken = tokens |> List.head
        let tokenString = tokenList |> List.map (fun t -> t.ToString()) |> String.concat ", "
        Error {
            Message = $"Unsupported expression type: [{tokenString}]"
            Line = firstToken.Line
            Column = firstToken.Column
            Length = firstToken.Length
        }

and buildFunctionDefinition (row: Row) : Result<Expression, CompileError> =
    let tokens = tokensFromRow row
    match tokens with
    | Let :: Name (Word funcName) :: rest ->
        // Extract parameters from rest (before Assignment)
        let paramsAndAssignment = rest |> List.takeWhile ((<>) Assignment)
        let parameters = extractParameters paramsAndAssignment

        // Build body expressions from nested rows
        let bodyResults = row.Body |> List.map rowToExpression

        // Collect errors or successes
        let errors = bodyResults |> List.choose (function Error e -> Some e | _ -> None)
        if not errors.IsEmpty then
            Error errors.Head  // Return first error
        else
            let bodyExprs = bodyResults |> List.choose (function Ok e -> Some e | _ -> None)
            Ok (FunctionExpression {
                Identifier = funcName
                Type = Inferred
                Parameters = parameters
                Body = BodyExpression bodyExprs
            })
    | _ ->
        let (line, col, len) = getFirstTokenPos row
        Error {
            Message = "Invalid function definition"
            Line = line
            Column = col
            Length = len
        }

and extractParameters (tokens: Tokens list) : ParameterExpression list =
    // Simple parameter extraction - just names for now
    tokens
    |> List.choose (function
        | Name (Word n) -> Some { Name = Word n; Type = Inferred }
        | Parameter p -> Some { Name = Word p; Type = Inferred }
        | _ -> None)

and buildStructDefinition (name: string) (row: Row) : Result<Expression, CompileError> =
    // TODO: parse fields from row.Body
    let (line, col, len) = getFirstTokenPos row
    Error {
        Message = "Struct definitions not yet implemented"
        Line = line
        Column = col
        Length = len
    }
