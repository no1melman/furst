module LanguageExpressions

open BasicTypes

type CompileError = {
  Message: string
  Line: Line
  Column: Column
  Length: TokenLength
}
with
  static member Empty(message: string) = {
    Message = message
    Line = Line 0L
    Column = Column 0L
    Length = TokenLength 0
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
  | [ OpenParen; Parameter p; TypeIdentifier; TypeDefinition typeDefinition; ClosedParen ] ->
    TypedParameterExpressionMatch { Name = Word p; Type = typeDefinition }
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

// Create error from token
let tokenError (message: string) (token: TokenWithMetadata) : CompileError =
    {
        Message = message
        Line = token.Line
        Column = token.Column
        Length = token.Length
    }

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

// Split a flat token list into argument groups:
// single tokens become one group, (x + y) becomes one group (without parens)
let splitArgumentGroups (tokens: TokenWithMetadata list) : TokenWithMetadata list list =
    let rec loop acc currentGroup depth remaining =
        match remaining with
        | [] ->
            match currentGroup with
            | [] -> acc |> List.rev
            | g -> (g |> List.rev) :: acc |> List.rev
        | t :: rest ->
            match t.Token, depth with
            | OpenParen, 0 ->
                // start a paren group; flush any pending single token
                let acc' =
                    match currentGroup with
                    | [] -> acc
                    | g -> (g |> List.rev) :: acc
                loop acc' [] 1 rest
            | ClosedParen, 1 ->
                // close paren group — the inner tokens are one argument group
                loop ((currentGroup |> List.rev) :: acc) [] 0 rest
            | OpenParen, d ->
                loop acc (t :: currentGroup) (d + 1) rest
            | ClosedParen, d ->
                loop acc (t :: currentGroup) (d - 1) rest
            | _, 0 ->
                // top-level token — each is its own group
                loop ([ t ] :: acc) [] 0 rest
            | _ ->
                // inside parens — accumulate
                loop acc (t :: currentGroup) depth rest
    loop [] [] 0 tokens

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
        Result.Error (tokenError $"Expected value after '=' in let binding for '{name}'" assignTok)
    else
        // Parse value tokens into expression
        let valueTokensMeta = row.Expressions |> List.skipWhile (fun t -> t.Token <> Assignment) |> List.tail
        match parseExpression valueTokensMeta with
        | Ok valueExprNode ->
            let loc = rowLocation row
            Result.Ok {
                Expr = LetBindingExpression {
                    Name = name
                    Type = Inferred
                    Value = valueExprNode.Expr
                }
                Location = loc
            }
        | Error e -> Result.Error e

and parseExpression (tokens: TokenWithMetadata list) : Result<ExpressionNode, CompileError> =
    let loc = tokensLocation tokens

    // Convert token to operand Expression
    let tryParseOperand = function
        | Name (Word n) -> Some (IdentifierExpression n)
        | Parameter p -> Some (IdentifierExpression p)
        | NumberLiteral (IntValue i)   -> Some (LiteralExpression (IntLiteral i))
        | NumberLiteral (FloatValue f) -> Some (LiteralExpression (FloatLiteral f))
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
        | Some expr -> Result.Ok { Expr = expr; Location = loc }
        | None ->
            Result.Error (tokenError $"Invalid expression: {singleToken}" tokens.Head)

    // Binary operations (handles chains: a + b + c)
    // Only match top-level operators (not inside parens)
    | firstToken :: rest when
        rest |> List.fold (fun (depth, found) t ->
            match t, depth with
            | OpenParen, _ -> (depth + 1, found)
            | ClosedParen, _ -> (depth - 1, found)
            | op, 0 when tryParseOperator op |> Option.isSome -> (depth, true)
            | _ -> (depth, found)) (0, false) |> snd ->
        // Parse first operand
        match tryParseOperand firstToken with
        | None ->
            Result.Error (tokenError "Expression must start with valid operand" tokens.Head)
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
                        Result.Error (tokenError "Invalid operator or operand in expression" tokens.Head)
                | [] -> Result.Ok acc
                | _ ->
                    Result.Error (tokenError "Incomplete binary operation" tokens.Head)

            match foldOps firstExpr rest with
            | Ok expr -> Result.Ok { Expr = expr; Location = loc }
            | Error e -> Result.Error e

    // Function call: f a b, f a (2 + 3), etc.
    | funcToken :: _ when tryParseOperand funcToken |> Option.isSome ->
        match tryParseOperand funcToken with
        | Some (IdentifierExpression funcName) ->
            let argTokens = tokens |> List.tail
            let argGroups = splitArgumentGroups argTokens
            let argResults = argGroups |> List.map parseExpression
            let errors = argResults |> List.choose (function Result.Error e -> Some e | _ -> None)
            match errors with
            | e :: _ -> Result.Error e
            | [] ->
                let args = argResults |> List.choose (function Result.Ok n -> Some n.Expr | _ -> None)
                Result.Ok {
                    Expr = FunctionCallExpression {
                        FunctionName = funcName
                        Arguments = args
                    }
                    Location = loc
                }
        | _ ->
            Result.Error (tokenError "Function call requires identifier" tokens.Head)

    | [] ->
        Result.Error (CompileError.Empty "Empty expression")

    | _ ->
        Result.Error (tokenError "Unrecognized expression" tokens.Head)

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
            Result.Error errors.Head  // Return first error
        else
            let bodyExprNodes = bodyResults |> List.choose (function Ok e -> Some e | _ -> None)
            let bodyExprs = bodyExprNodes |> List.map _.Expr
            let loc = rowLocation row
            Result.Ok {
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
        Result.Error {
            Message = "Invalid function definition"
            Line = line
            Column = col
            Length = len
        }

and extractParameters (tokens: Tokens list) : ParameterExpression list =
    let rec loop acc remaining =
        match remaining with
        | [] -> acc |> List.rev
        | OpenParen :: Parameter p :: TypeIdentifier :: TypeDefinition td :: ClosedParen :: rest ->
            loop ({ Name = Word p; Type = td } :: acc) rest
        | OpenParen :: Name (Word n) :: TypeIdentifier :: TypeDefinition td :: ClosedParen :: rest ->
            loop ({ Name = Word n; Type = td } :: acc) rest
        | Name (Word n) :: rest ->
            loop ({ Name = Word n; Type = Inferred } :: acc) rest
        | Parameter p :: rest ->
            loop ({ Name = Word p; Type = Inferred } :: acc) rest
        | _ :: rest -> loop acc rest
    loop [] tokens

and buildStructDefinition (name: string) (row: Row) : Result<ExpressionNode, CompileError> =
    // TODO: parse fields from row.Body
    let line, col, len = getFirstTokenPos row
    Result.Error {
        Message = "Struct definitions not yet implemented"
        Line = line
        Column = col
        Length = len
    }
