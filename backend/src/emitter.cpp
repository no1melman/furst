#include "emitter.h"
#include "backend.h"

#include <llvm/IR/DIBuilder.h>
#include <llvm/IR/IRBuilder.h>
#include <llvm/IR/LegacyPassManager.h>
#include <llvm/IR/Verifier.h>
#include <llvm/MC/TargetRegistry.h>
#include <llvm/Passes/PassBuilder.h>
#include <llvm/Support/FileSystem.h>
#include <llvm/Support/raw_ostream.h>
#include <llvm/Support/TargetSelect.h>
#include <llvm/Target/TargetMachine.h>
#include <llvm/Target/TargetOptions.h>
#include <llvm/TargetParser/Host.h>

#include <fstream>
#include <unordered_map>

namespace furst {

using NamedValues = std::unordered_map<std::string, llvm::Value*>;
using NameMap = std::unordered_map<std::string, std::string>;

// Debug info context passed through emission functions
struct DebugCtx {
    llvm::DIBuilder* dib = nullptr;
    llvm::DICompileUnit* cu = nullptr;
    llvm::DIFile* file = nullptr;
    llvm::DIScope* scope = nullptr;
};

static void set_debug_loc(llvm::IRBuilder<>& builder, const DebugCtx& dbg,
                          const ast::SourceLocation& loc) {
    if (dbg.scope == nullptr || loc.start_line == 0) {
        return;
    }
    builder.SetCurrentDebugLocation(
        llvm::DILocation::get(builder.getContext(), static_cast<unsigned>(loc.start_line),
                              static_cast<unsigned>(loc.start_col), dbg.scope));
}

static llvm::Type* resolve_type(llvm::LLVMContext& ctx, const ast::TypeRef& type_ref) {
    if (std::holds_alternative<ast::BuiltinKind>(type_ref)) {
        switch (std::get<ast::BuiltinKind>(type_ref)) {
        case ast::BuiltinKind::I32:
            return llvm::Type::getInt32Ty(ctx);
        case ast::BuiltinKind::I64:
            return llvm::Type::getInt64Ty(ctx);
        case ast::BuiltinKind::Float:
            return llvm::Type::getFloatTy(ctx);
        case ast::BuiltinKind::Double:
            return llvm::Type::getDoubleTy(ctx);
        case ast::BuiltinKind::Void:
            return llvm::Type::getInt32Ty(ctx); // functions return i32 by default
        case ast::BuiltinKind::String:
            return llvm::PointerType::getUnqual(llvm::Type::getInt8Ty(ctx));
        }
    }
    // User-defined types not yet supported
    return llvm::Type::getInt32Ty(ctx);
}

// Forward declaration — mutually recursive with emit_operation and emit_let_binding
static llvm::Value* emit_expression(llvm::IRBuilder<>& builder, llvm::Module& mod,
                                    NamedValues& names, const DebugCtx& dbg,
                                    const NameMap& name_map,
                                    const ast::Expression& expr);

static llvm::Value* emit_operation(llvm::IRBuilder<>& builder, llvm::Module& mod,
                                   NamedValues& names, const DebugCtx& dbg,
                                   const NameMap& name_map,
                                   const ast::Operation& op) {
    auto* lhs = emit_expression(builder, mod, names, dbg, name_map, op.left);
    auto* rhs = emit_expression(builder, mod, names, dbg, name_map, op.right);

    if (lhs == nullptr || rhs == nullptr) {
        return nullptr;
    }

    switch (op.op) {
    case ast::Operator::Add:
        return builder.CreateAdd(lhs, rhs, "addtmp");
    case ast::Operator::Subtract:
        return builder.CreateSub(lhs, rhs, "subtmp");
    case ast::Operator::Multiply:
        return builder.CreateMul(lhs, rhs, "multmp");
    }

    return nullptr;
}

static llvm::Value* emit_let_binding(llvm::IRBuilder<>& builder, llvm::Module& mod,
                                     NamedValues& names, const DebugCtx& dbg,
                                     const NameMap& name_map,
                                     const ast::LetBinding& binding) {
    auto* val = emit_expression(builder, mod, names, dbg, name_map, binding.value);
    if (val == nullptr) {
        return nullptr;
    }

    auto* alloca = builder.CreateAlloca(val->getType(), nullptr, binding.name);
    builder.CreateStore(val, alloca);
    names[binding.name] = alloca;

    return nullptr;
}

static llvm::Value* emit_expression(llvm::IRBuilder<>& builder, llvm::Module& mod,
                                    NamedValues& names, const DebugCtx& dbg,
                                    const NameMap& name_map,
                                    const ast::Expression& expr) {
    if (!expr.kind) {
        return nullptr;
    }

    // Set debug location for any instructions emitted from this expression
    set_debug_loc(builder, dbg, expr.location);

    const auto& kind = *expr.kind;

    // Literal
    if (std::holds_alternative<ast::LiteralValue>(kind)) {
        const auto& lit = std::get<ast::LiteralValue>(kind);
        if (std::holds_alternative<int32_t>(lit)) {
            return builder.getInt32(std::get<int32_t>(lit));
        }
        if (std::holds_alternative<double>(lit)) {
            return llvm::ConstantFP::get(builder.getDoubleTy(), std::get<double>(lit));
        }
    }

    // Identifier — look up in named values
    if (std::holds_alternative<std::string>(kind)) {
        const auto& name = std::get<std::string>(kind);
        auto it = names.find(name);
        if (it != names.end()) {
            auto* val = it->second;
            if (auto* alloca = llvm::dyn_cast<llvm::AllocaInst>(val)) {
                return builder.CreateLoad(alloca->getAllocatedType(), alloca, name);
            }
            return val;
        }
        return nullptr;
    }

    // Binary operation
    if (std::holds_alternative<ast::Operation>(kind)) {
        return emit_operation(builder, mod, names, dbg, name_map, std::get<ast::Operation>(kind));
    }

    // Let binding
    if (std::holds_alternative<ast::LetBinding>(kind)) {
        return emit_let_binding(builder, mod, names, dbg, name_map, std::get<ast::LetBinding>(kind));
    }

    // Function call
    if (std::holds_alternative<ast::FunctionCall>(kind)) {
        const auto& call = std::get<ast::FunctionCall>(kind);

        // Resolve mangled name via name map
        auto resolved_name = call.name;
        auto it_nm = name_map.find(call.name);
        if (it_nm != name_map.end()) {
            resolved_name = it_nm->second;
        }
        auto* callee = mod.getFunction(resolved_name);
        if (callee == nullptr) {
            return nullptr;
        }

        auto args = std::vector<llvm::Value*>{};
        args.reserve(call.arguments.size());
        for (const auto& arg : call.arguments) {
            auto* val = emit_expression(builder, mod, names, dbg, name_map, arg);
            if (val == nullptr) {
                return nullptr;
            }
            args.push_back(val);
        }

        return builder.CreateCall(callee, args, "calltmp");
    }

    return nullptr;
}

// -- Public API --

static void declare_externals(llvm::LLVMContext& ctx, llvm::Module& mod,
                              const std::vector<std::string>& manifests) {
    for (const auto& manifest_path : manifests) {
        auto file = std::ifstream(manifest_path);
        if (!file.is_open()) {
            continue;
        }
        auto line = std::string{};
        while (std::getline(file, line)) {
            // format: "qualified.path param_count"
            auto space = line.find(' ');
            if (space == std::string::npos) {
                continue;
            }
            auto qualified_name = line.substr(0, space);
            auto param_count = std::stoi(line.substr(space + 1));

            // mangle dotted path to __ separators (e.g. Dep.Helpers.greet -> Dep__Helpers__greet)
            auto mangled = std::string{};
            for (size_t i = 0; i < qualified_name.size(); ++i) {
                if (qualified_name[i] == '.') {
                    mangled += "__";
                } else {
                    mangled += qualified_name[i];
                }
            }

            // skip if already declared
            if (mod.getFunction(mangled) != nullptr) {
                continue;
            }

            // all params are i32 for now
            auto param_types = std::vector<llvm::Type*>(static_cast<size_t>(param_count),
                                                        llvm::Type::getInt32Ty(ctx));
            auto* fn_type =
                llvm::FunctionType::get(llvm::Type::getInt32Ty(ctx), param_types, false);
            llvm::Function::Create(fn_type, llvm::Function::ExternalLinkage, mangled, mod);
        }
    }
}

Result<EmittedModule, CompileError> emit_module(const ast::FurstModule& module,
                                                const std::vector<std::string>& manifests) {
    auto ctx = std::make_unique<llvm::LLVMContext>();
    auto llvm_mod = std::make_unique<llvm::Module>(module.source_file, *ctx);

    // Declare external functions from dependency manifests
    declare_externals(*ctx, *llvm_mod, manifests);

    // Set up debug info
    auto dib = llvm::DIBuilder{*llvm_mod};
    auto* di_file = dib.createFile(module.source_file, ".");
    auto* di_cu = dib.createCompileUnit(llvm::dwarf::DW_LANG_C, di_file, "furstc", false, "", 0);

    llvm_mod->addModuleFlag(llvm::Module::Warning, "Debug Info Version",
                            llvm::DEBUG_METADATA_VERSION);
    llvm_mod->addModuleFlag(llvm::Module::Warning, "Dwarf Version", 4);

    // Build name map: original name -> mangled name
    auto name_map = NameMap{};
    for (const auto& def : module.definitions) {
        if (!std::holds_alternative<ast::FunctionDef>(def)) continue;
        const auto& fn = std::get<ast::FunctionDef>(def);
        if (fn.name != "main" && !fn.module_path.empty()) {
            auto mangled = std::string{};
            auto dotted = std::string{};
            for (const auto& part : fn.module_path) {
                mangled += part + "__";
                dotted += part + ".";
            }
            mangled += fn.name;
            dotted += fn.name;
            name_map[fn.name] = mangled;
            name_map[dotted] = mangled;
        }
    }

    for (const auto& def : module.definitions) {
        if (!std::holds_alternative<ast::FunctionDef>(def)) {
            continue;
        }
        const auto& fn = std::get<ast::FunctionDef>(def);

        // Mangle name with module path (e.g. Math__add), except bare "main"
        auto mangled_name = fn.name;
        if (fn.name != "main" && !fn.module_path.empty()) {
            mangled_name = "";
            for (const auto& part : fn.module_path) {
                mangled_name += part + "__";
            }
            mangled_name += fn.name;
        }

        auto param_types = std::vector<llvm::Type*>{};
        param_types.reserve(fn.parameters.size());
        for (const auto& p : fn.parameters) {
            param_types.push_back(resolve_type(*ctx, p.type));
        }

        auto* ret_type = resolve_type(*ctx, fn.return_type);
        auto* fn_type = llvm::FunctionType::get(ret_type, param_types, false);
        auto linkage = fn.is_private ? llvm::Function::InternalLinkage : llvm::Function::ExternalLinkage;
        auto* llvm_fn =
            llvm::Function::Create(fn_type, linkage, mangled_name, *llvm_mod);

        // Create debug info for function
        auto line = static_cast<unsigned>(fn.location.start_line);
        auto* di_ret_type = dib.createBasicType("i32", 32, llvm::dwarf::DW_ATE_signed);
        auto* di_fn_type = dib.createSubroutineType(dib.getOrCreateTypeArray({di_ret_type}));
        auto* di_sp =
            dib.createFunction(di_file, fn.name, mangled_name, di_file, line, di_fn_type, line,
                               llvm::DINode::FlagPrototyped, llvm::DISubprogram::SPFlagDefinition);
        llvm_fn->setSubprogram(di_sp);

        auto dbg = DebugCtx{
            .dib = &dib,
            .cu = di_cu,
            .file = di_file,
            .scope = di_sp,
        };

        auto names = NamedValues{};
        auto param_idx = 0u;
        for (auto& arg : llvm_fn->args()) {
            arg.setName(fn.parameters[param_idx].name);
            names[fn.parameters[param_idx].name] = &arg;
            param_idx++;
        }

        auto* entry = llvm::BasicBlock::Create(*ctx, "entry", llvm_fn);
        auto builder = llvm::IRBuilder<>{entry};

        // Set initial debug location to function start
        set_debug_loc(builder, dbg, fn.location);

        llvm::Value* last_val = nullptr;
        for (const auto& expr : fn.body) {
            last_val = emit_expression(builder, *llvm_mod, names, dbg, name_map, expr);
        }

        if (last_val != nullptr) {
            builder.CreateRet(last_val);
        } else {
            builder.CreateRet(builder.getInt32(0));
        }
    }

    dib.finalize();

    // Verify module
    auto error_str = std::string{};
    auto error_stream = llvm::raw_string_ostream{error_str};
    if (llvm::verifyModule(*llvm_mod, &error_stream)) {
        return CompileError(UnsupportedExpression{
            .context = "module verification",
            .detail = error_str,
        });
    }

    return EmittedModule{
        .context = std::move(ctx),
        .module = std::move(llvm_mod),
    };
}

void optimize_module(llvm::Module& module, OptLevel level) {
    if (level == OptLevel::O0) {
        return;
    }

    auto opt = llvm::OptimizationLevel::O1;
    switch (level) {
    case OptLevel::O0:
        return;
    case OptLevel::O1:
        opt = llvm::OptimizationLevel::O1;
        break;
    case OptLevel::O2:
        opt = llvm::OptimizationLevel::O2;
        break;
    case OptLevel::O3:
        opt = llvm::OptimizationLevel::O3;
        break;
    }

    auto pb = llvm::PassBuilder{};
    auto lam = llvm::LoopAnalysisManager{};
    auto fam = llvm::FunctionAnalysisManager{};
    auto cgam = llvm::CGSCCAnalysisManager{};
    auto mam = llvm::ModuleAnalysisManager{};

    pb.registerModuleAnalyses(mam);
    pb.registerCGSCCAnalyses(cgam);
    pb.registerFunctionAnalyses(fam);
    pb.registerLoopAnalyses(lam);
    pb.crossRegisterProxies(lam, fam, cgam, mam);

    auto mpm = pb.buildPerModuleDefaultPipeline(opt);
    mpm.run(module, mam);
}

std::string print_ir(const llvm::Module& module) {
    auto ir_str = std::string{};
    auto ir_stream = llvm::raw_string_ostream{ir_str};
    module.print(ir_stream, nullptr);
    return ir_str;
}

Result<std::string, CompileError> emit_object(EmittedModule& emitted, const std::string& path,
                                              const std::string& target_triple_override) {
    llvm::InitializeNativeTarget();
    llvm::InitializeNativeTargetAsmPrinter();

    auto target_triple = target_triple_override.empty() ? llvm::sys::getDefaultTargetTriple()
                                                        : target_triple_override;
    emitted.module->setTargetTriple(target_triple);

    auto error = std::string{};
    auto* target = llvm::TargetRegistry::lookupTarget(target_triple, error);
    if (target == nullptr) {
        return CompileError(UnsupportedExpression{
            .context = "target lookup",
            .detail = error,
        });
    }

    auto* target_machine = target->createTargetMachine(target_triple, "generic", "",
                                                       llvm::TargetOptions{}, llvm::Reloc::PIC_);

    emitted.module->setDataLayout(target_machine->createDataLayout());

    auto ec = std::error_code{};
    auto dest = llvm::raw_fd_ostream{path, ec, llvm::sys::fs::OF_None};
    if (ec) {
        return CompileError(FileNotFound{.path = path});
    }

    auto pass = llvm::legacy::PassManager{};
    if (target_machine->addPassesToEmitFile(pass, dest, nullptr,
                                            llvm::CodeGenFileType::ObjectFile)) {
        return CompileError(UnsupportedExpression{
            .context = "object emission",
            .detail = "target machine cannot emit object files",
        });
    }

    pass.run(*emitted.module);
    dest.flush();

    return path;
}

} // namespace furst
