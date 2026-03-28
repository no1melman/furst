#include "errors.h"
#include <gtest/gtest.h>

TEST(Errors, FileNotFoundFormatsPath) {
    furst::CompileError err = furst::FileNotFound{.path = "/tmp/missing.fso"};
    EXPECT_EQ(furst::format_error(err), "error: file not found: /tmp/missing.fso");
}

TEST(Errors, InvalidHeaderFormatsDetail) {
    furst::CompileError err = furst::InvalidHeader{.path = "test.fso", .detail = "bad magic bytes"};
    EXPECT_EQ(furst::format_error(err), "error: invalid .fso header in test.fso: bad magic bytes");
}

TEST(Errors, MalformedProtoFormatsDetail) {
    furst::CompileError err = furst::MalformedProto{.path = "test.fso", .detail = "unexpected EOF"};
    EXPECT_EQ(furst::format_error(err), "error: malformed protobuf in test.fso: unexpected EOF");
}

TEST(Errors, UnsupportedExpressionFormatsDetail) {
    furst::CompileError err =
        furst::UnsupportedExpression{.context = "emit_expr", .detail = "match expressions"};
    EXPECT_EQ(furst::format_error(err),
              "error: unsupported expression in emit_expr: match expressions");
}

TEST(Errors, TypeMismatchFormatsAll) {
    furst::CompileError err =
        furst::TypeMismatch{.context = "add", .expected = "i32", .actual = "float"};
    EXPECT_EQ(furst::format_error(err), "error: type mismatch in add: expected i32, got float");
}
