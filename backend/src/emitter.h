#pragma once

#include "ast_types.h"
#include "errors.h"
#include "result.h"

#include <llvm/IR/LLVMContext.h>
#include <llvm/IR/Module.h>

#include <memory>
#include <string>

namespace furst {

struct EmittedModule {
    std::unique_ptr<llvm::LLVMContext> context;
    std::unique_ptr<llvm::Module> module;
};

enum class OptLevel : int;

Result<EmittedModule, CompileError> emit_module(const ast::FurstModule& ast_module,
                                                const std::vector<std::string>& manifests = {});

void optimize_module(llvm::Module& module, OptLevel level);

std::string print_ir(const llvm::Module& module);

Result<std::string, CompileError> emit_object(EmittedModule& emitted, const std::string& path,
                                              const std::string& target_triple = {});

} // namespace furst
