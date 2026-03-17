module BasicTypes

type TypeDefinitions =
  | I32
  | I64
  | Float
  | Double
  | String
  | Inferred
  | UserDefined of string

type WordToken = Word of string

type NumberValue =
  | IntValue of int
  | FloatValue of float

type Tokens =
  | Let
  | Struct
  | OpenBrace
  | ClosedBrace
  | Goto
  | Assignment
  | OpenParen
  | ClosedParen
  | Pipe
  | Addition
  | Subtraction
  | Multiply
  | SemiColonTerminator
  | GreaterThan
  | LessThan
  | Match
  | Type
  | TypeDefinition of TypeDefinitions
  | TypeIdentifier
  | Name of WordToken
  | Parameter of string
  | NumberLiteral of NumberValue
  | NoToken

type Line = Line of int64
type Column = Column of int64
type TokenLength = TokenLength of int

type TokenWithMetadata =
    { Line: Line
      Column: Column
      Length: TokenLength
      Token: Tokens }

type Row =
    { Indent: int
      Expressions: TokenWithMetadata list
      Body: Row list }


