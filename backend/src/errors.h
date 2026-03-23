#pragma once

#include <cstdint>
#include <string>
#include <variant>

namespace furst {

struct FileNotFound {
    std::string path;
};

struct InvalidHeader {
    std::string path;
    std::string detail;
};

struct MalformedProto {
    std::string path;
    std::string detail;
};

struct UnsupportedExpression {
    std::string context;
    std::string detail;
    int64_t line = 0;
    int64_t col = 0;
};

struct TypeMismatch {
    std::string context;
    std::string expected;
    std::string actual;
    int64_t line = 0;
    int64_t col = 0;
};

using CompileError =
    std::variant<FileNotFound, InvalidHeader, MalformedProto, UnsupportedExpression, TypeMismatch>;

std::string format_error(const CompileError& error);

} // namespace furst
