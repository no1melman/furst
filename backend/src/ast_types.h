#pragma once

#include <cstdint>
#include <memory>
#include <string>
#include <variant>
#include <vector>

namespace furst::ast {

// -- Types --

enum class BuiltinKind { I32, I64, Float, Double, String, Void };

using TypeRef = std::variant<BuiltinKind, std::string>;

// -- Operators --

enum class Operator { Add, Subtract, Multiply };

// -- Source locations --

struct SourceLocation {
    int64_t start_line = 0;
    int64_t start_col = 0;
    int64_t end_line = 0;
    int64_t end_col = 0;
};

// -- Literals --

using LiteralValue = std::variant<int32_t, double, std::string>;

// -- Expressions --

struct LetBinding;
struct FunctionCall;
struct Operation;

using ExpressionKind = std::variant<LetBinding, FunctionCall, Operation, std::string, LiteralValue>;

struct Expression {
    std::unique_ptr<ExpressionKind> kind;
    TypeRef resolved_type = BuiltinKind::Void;
    SourceLocation location;
};

struct LetBinding {
    std::string name;
    TypeRef type = BuiltinKind::Void;
    Expression value;
};

struct FunctionCall {
    std::string name;
    std::vector<Expression> arguments;
};

struct Operation {
    Expression left;
    Operator op = Operator::Add;
    Expression right;
};

// -- Top-level definitions --

struct Parameter {
    std::string name;
    TypeRef type = BuiltinKind::Void;
};

struct FunctionDef {
    std::string name;
    TypeRef return_type = BuiltinKind::Void;
    std::vector<Parameter> parameters;
    std::vector<Expression> body;
    SourceLocation location;
    std::vector<std::string> module_path;
    bool is_private = false;
};

struct StructDef {
    std::string name;
    std::vector<Parameter> fields;
    SourceLocation location;
    std::vector<std::string> module_path;
};

using TopLevel = std::variant<FunctionDef, StructDef>;

struct FurstModule {
    std::string source_file;
    std::vector<TopLevel> definitions;
};

} // namespace furst::ast
