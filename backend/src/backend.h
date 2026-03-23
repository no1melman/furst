#pragma once

#include <string>
#include <vector>

namespace furst {

enum class OptLevel { O0, O1, O2, O3 };

struct CompileOptions {
    std::string output_path;
    std::string target_triple; // empty = host default
    OptLevel opt_level = OptLevel::O0;
    std::vector<std::string> link_libs;
    std::vector<std::string> manifests;
};

struct CompileResult {
    bool success;
    std::string output_path;
    std::string ir;
    std::string error_message;
};

CompileResult compile(const std::string& fso_path, const CompileOptions& options);

} // namespace furst
