#include "errors.h"

namespace furst {

static std::string location_suffix(int64_t line, int64_t col) {
    if (line == 0 && col == 0) {
        return "";
    }
    return " at " + std::to_string(line) + ":" + std::to_string(col);
}

struct ErrorFormatter {
    std::string operator()(const FileNotFound& e) const {
        return "error: file not found: " + e.path;
    }

    std::string operator()(const InvalidHeader& e) const {
        return "error: invalid .fso header in " + e.path + ": " + e.detail;
    }

    std::string operator()(const MalformedProto& e) const {
        return "error: malformed protobuf in " + e.path + ": " + e.detail;
    }

    std::string operator()(const UnsupportedExpression& e) const {
        return "error: unsupported expression in " + e.context + ": " + e.detail +
               location_suffix(e.line, e.col);
    }

    std::string operator()(const TypeMismatch& e) const {
        return "error: type mismatch in " + e.context + ": expected " + e.expected + ", got " +
               e.actual + location_suffix(e.line, e.col);
    }
};

std::string format_error(const CompileError& error) {
    return std::visit(ErrorFormatter{}, error);
}

} // namespace furst
