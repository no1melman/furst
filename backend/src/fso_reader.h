#pragma once

#include "ast_types.h"
#include "errors.h"
#include "result.h"

#include <string>

namespace furst {

Result<ast::FurstModule, CompileError> read_fso(const std::string& path);

} // namespace furst
