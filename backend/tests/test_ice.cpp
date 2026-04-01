#include "ast_types.h"
#include "emitter.h"
#include <gtest/gtest.h>

// Justified exception to "test through entrypoint only":
// ICE assertions verify invariants the frontend guarantees. We can't produce
// malformed AST through .fso fixtures, so we hand-craft bad AST and call
// emit_module directly.

// -- AST construction helpers --

static furst::ast::Expression make_lit_i32(int32_t val, int64_t line = 1, int64_t col = 1) {
    auto kind = furst::ast::LiteralValue{val};
    return furst::ast::Expression{
        .kind = std::make_unique<furst::ast::ExpressionKind>(std::move(kind)),
        .resolved_type = furst::ast::BuiltinKind::I32,
        .location = {.start_line = line, .start_col = col},
    };
}

static furst::ast::Expression make_lit_double(double val, int64_t line = 1, int64_t col = 1) {
    auto kind = furst::ast::LiteralValue{val};
    return furst::ast::Expression{
        .kind = std::make_unique<furst::ast::ExpressionKind>(std::move(kind)),
        .resolved_type = furst::ast::BuiltinKind::Double,
        .location = {.start_line = line, .start_col = col},
    };
}

static furst::ast::Expression make_op(furst::ast::Expression left, furst::ast::Operator op,
                                      furst::ast::Expression right) {
    auto operation = furst::ast::Operation{
        .left = std::move(left),
        .op = op,
        .right = std::move(right),
    };
    return furst::ast::Expression{
        .kind = std::make_unique<furst::ast::ExpressionKind>(std::move(operation)),
        .resolved_type = furst::ast::BuiltinKind::I32,
        .location = {.start_line = 1, .start_col = 1},
    };
}

static furst::ast::Expression make_call(const std::string& name,
                                        std::vector<furst::ast::Expression> args, int64_t line = 1,
                                        int64_t col = 1) {
    auto call = furst::ast::FunctionCall{
        .name = name,
        .arguments = std::move(args),
    };
    return furst::ast::Expression{
        .kind = std::make_unique<furst::ast::ExpressionKind>(std::move(call)),
        .resolved_type = furst::ast::BuiltinKind::I32,
        .location = {.start_line = line, .start_col = col},
    };
}

static furst::ast::FunctionDef make_fn(const std::string& name,
                                       std::vector<furst::ast::Parameter> params,
                                       furst::ast::BuiltinKind ret_type,
                                       std::vector<furst::ast::Expression> body, int64_t line = 1) {
    return furst::ast::FunctionDef{
        .name = name,
        .return_type = ret_type,
        .parameters = std::move(params),
        .body = std::move(body),
        .location = {.start_line = line, .start_col = 1},
        .module_path = {},
    };
}

static furst::ast::FurstModule make_module(std::vector<furst::ast::FunctionDef> fns) {
    auto defs = std::vector<furst::ast::TopLevel>{};
    for (auto& fn : fns) {
        defs.push_back(std::move(fn));
    }
    return furst::ast::FurstModule{
        .source_file = "test.fu",
        .definitions = std::move(defs),
    };
}

// -- ICE death tests --

TEST(ICE, BinaryOpTypeMismatchAborts) {
    // i32 + double → ICE
    auto body = std::vector<furst::ast::Expression>{};
    body.push_back(make_op(make_lit_i32(1, 5, 3), furst::ast::Operator::Add, make_lit_double(2.0)));

    auto fns = std::vector<furst::ast::FunctionDef>{};
    fns.push_back(make_fn("main", {}, furst::ast::BuiltinKind::I32, std::move(body)));
    auto mod = make_module(std::move(fns));

    EXPECT_DEATH(furst::emit_module(mod), "ICE.*type mismatch.*binary operation.*i32.*double.*5:3");
}

TEST(ICE, CallArgCountMismatchAborts) {
    // define add(x, y) then call add(1) — wrong arity
    auto add_body = std::vector<furst::ast::Expression>{};
    add_body.push_back(make_op(make_lit_i32(0), furst::ast::Operator::Add, make_lit_i32(0)));
    auto add_fn = make_fn("add",
                          {{.name = "x", .type = furst::ast::BuiltinKind::I32},
                           {.name = "y", .type = furst::ast::BuiltinKind::I32}},
                          furst::ast::BuiltinKind::I32, std::move(add_body));

    auto call_args = std::vector<furst::ast::Expression>{};
    call_args.push_back(make_lit_i32(1));
    auto main_body = std::vector<furst::ast::Expression>{};
    main_body.push_back(make_call("add", std::move(call_args), 10, 5));
    auto main_fn = make_fn("main", {}, furst::ast::BuiltinKind::I32, std::move(main_body));

    auto fns = std::vector<furst::ast::FunctionDef>{};
    fns.push_back(std::move(add_fn));
    fns.push_back(std::move(main_fn));
    auto mod = make_module(std::move(fns));

    EXPECT_DEATH(furst::emit_module(mod), "ICE.*type mismatch.*add.*expected 2 arguments.*got 1");
}

TEST(ICE, CallArgTypeMismatchAborts) {
    // define add(x: i32, y: i32) then call add(1, 2.0) — double where i32 expected
    auto add_body = std::vector<furst::ast::Expression>{};
    add_body.push_back(make_op(make_lit_i32(0), furst::ast::Operator::Add, make_lit_i32(0)));
    auto add_fn = make_fn("add",
                          {{.name = "x", .type = furst::ast::BuiltinKind::I32},
                           {.name = "y", .type = furst::ast::BuiltinKind::I32}},
                          furst::ast::BuiltinKind::I32, std::move(add_body));

    auto call_args = std::vector<furst::ast::Expression>{};
    call_args.push_back(make_lit_i32(1));
    call_args.push_back(make_lit_double(2.0, 7, 12));
    auto main_body = std::vector<furst::ast::Expression>{};
    main_body.push_back(make_call("add", std::move(call_args)));
    auto main_fn = make_fn("main", {}, furst::ast::BuiltinKind::I32, std::move(main_body));

    auto fns = std::vector<furst::ast::FunctionDef>{};
    fns.push_back(std::move(add_fn));
    fns.push_back(std::move(main_fn));
    auto mod = make_module(std::move(fns));

    EXPECT_DEATH(furst::emit_module(mod),
                 "ICE.*type mismatch.*add argument 2.*expected i32.*got double.*7:12");
}

TEST(ICE, ReturnTypeMismatchAborts) {
    // function declared i32, body returns double
    auto body = std::vector<furst::ast::Expression>{};
    body.push_back(make_lit_double(3.14));

    auto fns = std::vector<furst::ast::FunctionDef>{};
    fns.push_back(make_fn("main", {}, furst::ast::BuiltinKind::I32, std::move(body), 3));
    auto mod = make_module(std::move(fns));

    EXPECT_DEATH(furst::emit_module(mod),
                 "ICE.*type mismatch.*main return.*expected i32.*got double.*3:");
}
