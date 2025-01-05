module BasicTypes

open FParsec.CharParsers

type TypeDefinitions =
  | I32
  | I64
  | Float
  | Double
  | String
  | Inferred
  | UserDefined of string

type WordToken = Word of string

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
  | NumberLiteral of NumberLiteral
  | NoToken

type Row =
    { Indent: int
      Expressions: Tokens list
      Body: Row list }


