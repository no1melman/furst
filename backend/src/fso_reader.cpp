#include "fso_reader.h"
#include "furst_ast.pb.h"

#include <array>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <iostream>

namespace furst {

static constexpr std::array<uint8_t, 4> FSO_MAGIC = {'F', 'S', 'O', 0};
static constexpr uint16_t FSO_VERSION = 1;
static constexpr size_t HEADER_SIZE = 8;

// -- Type mapping --

static ast::BuiltinKind map_builtin_kind(::furst::BuiltinType_Kind kind) {
    switch (kind) {
    case ::furst::BuiltinType_Kind_I32:
        return ast::BuiltinKind::I32;
    case ::furst::BuiltinType_Kind_I64:
        return ast::BuiltinKind::I64;
    case ::furst::BuiltinType_Kind_FLOAT:
        return ast::BuiltinKind::Float;
    case ::furst::BuiltinType_Kind_DOUBLE:
        return ast::BuiltinKind::Double;
    case ::furst::BuiltinType_Kind_STRING:
        return ast::BuiltinKind::String;
    case ::furst::BuiltinType_Kind_VOID:
        return ast::BuiltinKind::Void;
    default:
        std::cerr << "fatal: unknown builtin kind: " << kind << "\n";
        std::abort();
    }
}

static ast::TypeRef map_type_ref(const ::furst::TypeRef& proto) {
    if (proto.has_builtin()) {
        return map_builtin_kind(proto.builtin().kind());
    }
    if (proto.has_user_defined()) {
        return proto.user_defined();
    }
    return ast::BuiltinKind::Void;
}

static ast::SourceLocation map_source_location(const ::furst::SourceLocation& proto) {
    return ast::SourceLocation{
        .start_line = proto.start_line(),
        .start_col = proto.start_col(),
        .end_line = proto.end_line(),
        .end_col = proto.end_col(),
    };
}

// -- Expression mapping --

static ast::Operator map_operator(::furst::Operator op) {
    switch (op) {
    case ::furst::OP_ADD:
        return ast::Operator::Add;
    case ::furst::OP_SUBTRACT:
        return ast::Operator::Subtract;
    case ::furst::OP_MULTIPLY:
        return ast::Operator::Multiply;
    default:
        std::cerr << "fatal: unknown operator: " << op << "\n";
        std::abort();
    }
}

static ast::Expression map_expression(const ::furst::Expression& proto);

static ast::LetBinding map_let_binding(const ::furst::LetBinding& proto) {
    return ast::LetBinding{
        .name = proto.name(),
        .type = map_type_ref(proto.type()),
        .value = map_expression(proto.value()),
    };
}

static ast::FunctionCall map_function_call(const ::furst::FunctionCall& proto) {
    auto args = std::vector<ast::Expression>{};
    args.reserve(proto.arguments_size());
    for (const auto& arg : proto.arguments()) {
        args.push_back(map_expression(arg));
    }
    return ast::FunctionCall{
        .name = proto.name(),
        .arguments = std::move(args),
    };
}

static ast::Operation map_operation(const ::furst::Operation& proto) {
    return ast::Operation{
        .left = map_expression(proto.left()),
        .op = map_operator(proto.op()),
        .right = map_expression(proto.right()),
    };
}

static ast::Expression map_expression(const ::furst::Expression& proto) {
    auto kind = std::make_unique<ast::ExpressionKind>();

    if (proto.has_let_binding()) {
        *kind = map_let_binding(proto.let_binding());
    } else if (proto.has_function_call()) {
        *kind = map_function_call(proto.function_call());
    } else if (proto.has_operation()) {
        *kind = map_operation(proto.operation());
    } else if (proto.has_identifier()) {
        *kind = proto.identifier();
    } else if (proto.has_literal()) {
        const auto& lit = proto.literal();
        if (lit.has_int_literal()) {
            *kind = ast::LiteralValue{lit.int_literal()};
        } else if (lit.has_float_literal()) {
            *kind = ast::LiteralValue{lit.float_literal()};
        } else if (lit.has_string_literal()) {
            *kind = ast::LiteralValue{lit.string_literal()};
        }
    }

    return ast::Expression{
        .kind = std::move(kind),
        .resolved_type = map_type_ref(proto.resolved_type()),
        .location = map_source_location(proto.location()),
    };
}

// -- Top-level mapping --

static ast::Parameter map_parameter(const ::furst::Parameter& proto) {
    return ast::Parameter{
        .name = proto.name(),
        .type = map_type_ref(proto.type()),
    };
}

static ast::FunctionDef map_function_def(const ::furst::FunctionDef& proto) {
    auto params = std::vector<ast::Parameter>{};
    params.reserve(proto.parameters_size());
    for (const auto& p : proto.parameters()) {
        params.push_back(map_parameter(p));
    }

    auto body = std::vector<ast::Expression>{};
    body.reserve(proto.body_size());
    for (const auto& e : proto.body()) {
        body.push_back(map_expression(e));
    }

    return ast::FunctionDef{
        .name = proto.name(),
        .return_type = map_type_ref(proto.return_type()),
        .parameters = std::move(params),
        .body = std::move(body),
        .location = map_source_location(proto.location()),
    };
}

static ast::StructDef map_struct_def(const ::furst::StructDef& proto) {
    auto fields = std::vector<ast::Parameter>{};
    fields.reserve(proto.fields_size());
    for (const auto& f : proto.fields()) {
        fields.push_back(map_parameter(f));
    }

    return ast::StructDef{
        .name = proto.name(),
        .fields = std::move(fields),
        .location = map_source_location(proto.location()),
    };
}

// -- Public API --

Result<ast::FurstModule, CompileError> read_fso(const std::string& path) {
    auto file = std::ifstream(path, std::ios::binary);
    if (!file.is_open()) {
        return CompileError(FileNotFound{.path = path});
    }

    // Read and validate header
    auto header = std::array<uint8_t, HEADER_SIZE>{};
    file.read(reinterpret_cast<char*>(header.data()), HEADER_SIZE);

    if (file.gcount() < static_cast<std::streamsize>(HEADER_SIZE)) {
        return CompileError(InvalidHeader{.path = path, .detail = "file too short"});
    }

    if (header[0] != FSO_MAGIC[0] || header[1] != FSO_MAGIC[1] || header[2] != FSO_MAGIC[2] ||
        header[3] != FSO_MAGIC[3]) {
        return CompileError(InvalidHeader{.path = path, .detail = "bad magic bytes"});
    }

    uint16_t version = 0;
    std::memcpy(&version, &header[4], sizeof(version));
    if (version != FSO_VERSION) {
        return CompileError(InvalidHeader{
            .path = path, .detail = "unsupported version " + std::to_string(version)});
    }

    // Read protobuf payload
    auto content = std::string(std::istreambuf_iterator<char>(file), {});

    auto proto_module = ::furst::FurstModule{};
    if (!proto_module.ParseFromString(content)) {
        return CompileError(MalformedProto{.path = path, .detail = "protobuf parse failed"});
    }

    // Map to internal types
    auto defs = std::vector<ast::TopLevel>{};
    defs.reserve(proto_module.definitions_size());
    for (const auto& tl : proto_module.definitions()) {
        if (tl.has_function()) {
            defs.push_back(map_function_def(tl.function()));
        } else if (tl.has_struct_def()) {
            defs.push_back(map_struct_def(tl.struct_def()));
        }
    }

    return ast::FurstModule{
        .source_file = proto_module.source_file(),
        .definitions = std::move(defs),
    };
}

} // namespace furst
