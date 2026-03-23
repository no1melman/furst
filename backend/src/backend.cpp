#include "backend.h"
#include "emitter.h"
#include "errors.h"
#include "fso_reader.h"

#include <array>
#include <cstdio>
#include <fstream>
#include <memory>

namespace furst {

static CompileResult make_error(const CompileError& err) {
    return CompileResult{
        .success = false,
        .output_path = {},
        .ir = {},
        .error_message = format_error(err),
    };
}

static Result<std::string, CompileError> run_linker(const std::string& obj_path,
                                                    const std::string& exe_path,
                                                    const std::vector<std::string>& link_libs) {
    auto cmd = std::string("cc -o ") + exe_path + " " + obj_path;
    for (const auto& lib : link_libs) {
        cmd += " " + lib;
    }
    cmd += " 2>&1";
    auto pipe = std::unique_ptr<FILE, decltype(&pclose)>(popen(cmd.c_str(), "r"), pclose);
    if (!pipe) {
        return CompileError(UnsupportedExpression{
            .context = "linker",
            .detail = "failed to invoke cc",
        });
    }

    auto output = std::string{};
    auto buf = std::array<char, 256>{};
    while (fgets(buf.data(), buf.size(), pipe.get()) != nullptr) {
        output += buf.data();
    }

    auto status = pclose(pipe.release());
    if (status != 0) {
        return CompileError(UnsupportedExpression{
            .context = "linker",
            .detail = output.empty() ? "cc returned non-zero" : output,
        });
    }

    return exe_path;
}

CompileResult compile(const std::string& fso_path, const CompileOptions& options) {
    auto read_result = read_fso(fso_path);
    if (read_result.is_error()) {
        return make_error(read_result.error());
    }

    auto emit_result = emit_module(read_result.value(), options.manifests);
    if (emit_result.is_error()) {
        return make_error(emit_result.error());
    }

    auto& emitted = emit_result.value();
    optimize_module(*emitted.module, options.opt_level);
    auto ir = print_ir(*emitted.module);

    if (!options.output_path.empty()) {
        if (options.output_path.ends_with(".ll")) {
            // Write LLVM IR text
            auto out = std::ofstream(options.output_path);
            if (!out.is_open()) {
                return make_error(FileNotFound{.path = options.output_path});
            }
            out << ir;
        } else if (options.output_path.ends_with(".o")) {
            // Emit object file only
            auto obj_result = emit_object(emitted, options.output_path, options.target_triple);
            if (obj_result.is_error()) {
                return make_error(obj_result.error());
            }
        } else {
            // Emit object file to temp, then link to executable
            auto tmp_obj = options.output_path + ".tmp.o";

            auto obj_result = emit_object(emitted, tmp_obj, options.target_triple);
            if (obj_result.is_error()) {
                std::remove(tmp_obj.c_str());
                return make_error(obj_result.error());
            }

            auto link_result = run_linker(tmp_obj, options.output_path, options.link_libs);
            std::remove(tmp_obj.c_str());

            if (link_result.is_error()) {
                return make_error(link_result.error());
            }
        }
    }

    return CompileResult{
        .success = true,
        .output_path = options.output_path,
        .ir = std::move(ir),
        .error_message = {},
    };
}

} // namespace furst
