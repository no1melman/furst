module LanguageExpressions

open BasicTypes

type CompileError = {
  Message: string
  Line: Line
  Column: Column
  Length: TokenLength
}

type SourceLocation = {
  StartLine: Line
  StartCol: Column
  EndLine: Line
  EndCol: Column
}

type ParameterExpression = { Name: WordToken; Type: TypeDefinitions }

type Expressions =
  | ParameterExpression

let (|TypedParameterExpressionMatch|ParameterExpressionMatch|Incorrect|) (tokens: TokenWithMetadata list) =
  match tokens |> List.map _.Token with
  | [ OpenParen; Name name; TypeIdentifier; TypeDefinition typeDefinition; ClosedParen ] ->
    TypedParameterExpressionMatch { Name = name; Type = typeDefinition }
  | [ Name name ] -> ParameterExpressionMatch { Name = name; Type = Inferred }
  | _ -> Incorrect

type Operator =
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
  and Operation =
    {
      Left: Expression
      Operator: Operator
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
      match row.Expressions |> List.map _.Token with
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
        match list |> List.map _.Token with
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
    | OperatorExpression of Operation
    | IdentifierExpression of string
    | LiteralExpression of LiteralValue
    | StructExpression of StructDefinition

type ExpressionNode = {
  Expr: Expression
  Location: SourceLocation
}

// Helper to extract token list from Row
let tokensFromRow (row: Row) : Tokens list =
    row.Expressions |> List.map _.Token

// Extract identifier name from Name token
let extractName (tokens: Tokens list) : string option =
    tokens
    |> List.tryPick (function Name (Word n) -> Some n | _ -> None)

// Helper to get first token position for errors
let getFirstTokenPos (row: Row) : Line * Column * TokenLength =
    match row.Expressions |> List.tryHead with
    | Some t -> (t.Line, t.Column, t.Length)
    | None -> (Line 0L, Column 0L, TokenLength 0)

// Calculate SourceLocation from a single token
let tokenLocation (token: TokenWithMetadata) : SourceLocation =
    let (Line startLine) = token.Line
    let (Column startCol) = token.Column
    let (TokenLength len) = token.Length
    {
        StartLine = token.Line
        StartCol = token.Column
        EndLine = token.Line  // assume single-line token
        EndCol = Column (startCol + int64 len)
    }

// Calculate SourceLocation spanning a list of tokens
let tokensLocation (tokens: TokenWithMetadata list) : SourceLocation =
    match tokens with
    | [] ->
        { StartLine = Line 0L; StartCol = Column 0L; EndLine = Line 0L; EndCol = Column 0L }
    | tokens ->
        let first = tokens.Head
        let last = tokens |> List.last
        let (Column lastCol) = last.Column
        let (TokenLength lastLen) = last.Length
        {
            StartLine = first.Line
            StartCol = first.Column
            EndLine = last.Line
            EndCol = Column (lastCol + int64 lastLen)
        }

// Calculate SourceLocation for an entire row (including body)
let rec rowLocation (row: Row) : SourceLocation =
    match row.Body with
    | [] ->
        // No body, just use tokens
        tokensLocation row.Expressions
    | bodyRows ->
        // Has body, span from first token to end of last body row
        let firstToken = row.Expressions |> List.tryHead
        let lastBodyRow = bodyRows |> List.last
        let endLoc = rowLocation lastBodyRow
        match firstToken with
        | Some first ->
            {
                StartLine = first.Line
                StartCol = first.Column
                EndLine = endLoc.EndLine
                EndCol = endLoc.EndCol
            }
        | None -> endLoc

// Main expression builder - converts Row to ExpressionNode
let rec rowToExpression (row: Row) : Result<ExpressionNode, CompileError> =
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

and buildLetBinding (name: string) (valueToks: Tokens list) (row: Row) : Result<ExpressionNode, CompileError> =
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
        | Ok valueExprNode ->
            let loc = rowLocation row
            Ok {
                Expr = LetBindingExpression {
                    Name = name
                    Type = Inferred
                    Value = valueExprNode.Expr
                }
                Location = loc
            }
        | Error e -> Error e

and parseExpression (tokens: TokenWithMetadata list) : Result<ExpressionNode, CompileError> =
    let loc = tokensLocation tokens

    // Convert token to operand Expression
    let tryParseOperand = function
        | Name (Word n) -> Some (IdentifierExpression n)
        | Parameter p -> Some (IdentifierExpression p)
        | NumberLiteral lit when lit.IsInteger -> Some (LiteralExpression (IntLiteral (int lit.String)))
        | NumberLiteral lit -> Some (LiteralExpression (FloatLiteral (float lit.String)))
        | _ -> None

    // Convert token to operator
    let tryParseOperator = function
        | Tokens.Addition -> Some Operator.Add
        | Tokens.Subtraction -> Some Operator.Subtract
        | Tokens.Multiply -> Some Operator.Multiply
        | _ -> None

    let tokenList = tokens |> List.map _.Token

    match tokenList with
    // Single operand
    | [ singleToken ] ->
        match tryParseOperand singleToken with
        | Some expr -> Ok { Expr = expr; Location = loc }
        | None ->
            Error {
                Message = $"Invalid expression: {singleToken}"
                Line = tokens.Head.Line
                Column = tokens.Head.Column
                Length = tokens.Head.Length
            }

    // Binary operations (handles chains: a + b + c)
    | firstToken :: rest when rest |> List.exists (tryParseOperator >> Option.isSome) ->
        // Parse first operand
        match tryParseOperand firstToken with
        | None ->
            Error {
                Message = "Expression must start with valid operand"
                Line = tokens.Head.Line
                Column = tokens.Head.Column
                Length = tokens.Head.Length
            }
        | Some firstExpr ->
            // Fold over (operator, operand) pairs
            // Left-associative: a + b + c = (a + b) + c
            let rec foldOps acc remaining =
                match remaining with
                | opToken :: operandToken :: tail ->
                    match tryParseOperator opToken, tryParseOperand operandToken with
                    | Some op, Some operand ->
                        let newExpr = OperatorExpression {
                            Left = acc
                            Operator = op
                            Right = operand
                        }
                        foldOps newExpr tail
                    | _ ->
                        Error {
                            Message = "Invalid operator or operand in expression"
                            Line = tokens.Head.Line
                            Column = tokens.Head.Column
                            Length = tokens.Head.Length
                        }
                | [] -> Ok acc
                | _ ->
                    Error {
                        Message = "Incomplete binary operation"
                        Line = tokens.Head.Line
                        Column = tokens.Head.Column
                        Length = tokens.Head.Length
                    }

            match foldOps firstExpr rest with
            | Ok expr -> Ok { Expr = expr; Location = loc }
            | Error e -> Error e

    // Function call: f a b c
    | funcToken :: argTokens ->
        match tryParseOperand funcToken with
        | Some (IdentifierExpression funcName) ->
            let args = argTokens |> List.choose tryParseOperand
            if args.Length = argTokens.Length then
                Ok {
                    Expr = FunctionCallExpression {
                        FunctionName = funcName
                        Arguments = args
                    }
                    Location = loc
                }
            else
                Error {
                    Message = "Invalid arguments in function call"
                    Line = tokens.Head.Line
                    Column = tokens.Head.Column
                    Length = tokens.Head.Length
                }
        | _ ->
            Error {
                Message = "Function call requires identifier"
                Line = tokens.Head.Line
                Column = tokens.Head.Column
                Length = tokens.Head.Length
            }

    | [] ->
        Error {
            Message = "Empty expression"
            Line = Line 0L
            Column = Column 0L
            Length = TokenLength 0
        }

and buildFunctionDefinition (row: Row) : Result<ExpressionNode, CompileError> =
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
            let bodyExprNodes = bodyResults |> List.choose (function Ok e -> Some e | _ -> None)
            let bodyExprs = bodyExprNodes |> List.map _.Expr
            let loc = rowLocation row
            Ok {
                Expr = FunctionExpression {
                    Identifier = funcName
                    Type = Inferred
                    Parameters = parameters
                    Body = BodyExpression bodyExprs
                }
                Location = loc
            }
    | _ ->
        let line, col, len = getFirstTokenPos row
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

and buildStructDefinition (name: string) (row: Row) : Result<ExpressionNode, CompileError> =
    // TODO: parse fields from row.Body
    let line, col, len = getFirstTokenPos row
    Error {
        Message = "Struct definitions not yet implemented"
        Line = line
        Column = col
        Length = len
    }
