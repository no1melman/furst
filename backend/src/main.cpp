#include "backend.h"

#include <iostream>
#include <string>

static void print_usage() {
    std::cerr << "usage: furstc-backend <input.fso> <output> [options]\n";
    std::cerr << "\n";
    std::cerr << "output format determined by extension:\n";
    std::cerr << "  .ll    LLVM IR text\n";
    std::cerr << "  .o     object file\n";
    std::cerr << "  other  linked executable\n";
    std::cerr << "\n";
    std::cerr << "options:\n";
    std::cerr << "  --target <triple>   target triple (default: host)\n";
    std::cerr << "  --link <path>       additional library to link (repeatable)\n";
    std::cerr << "  --manifest <path>   external function manifest (repeatable)\n";
}

int main(int argc, char** argv) {
    if (argc < 3) {
        print_usage();
        return 1;
    }

    auto fso_path = std::string(argv[1]);
    auto output_path = std::string(argv[2]);
    auto target_triple = std::string{};
    auto link_libs = std::vector<std::string>{};
    auto manifests = std::vector<std::string>{};

    for (int i = 3; i < argc; i++) {
        auto arg = std::string(argv[i]);
        if (arg == "--target" && i + 1 < argc) {
            target_triple = argv[++i];
        } else if (arg == "--link" && i + 1 < argc) {
            link_libs.emplace_back(argv[++i]);
        } else if (arg == "--manifest" && i + 1 < argc) {
            manifests.emplace_back(argv[++i]);
        }
    }

    auto result = furst::compile(
        fso_path, {.output_path = output_path,
                   .target_triple = target_triple,
                   .link_libs = link_libs,
                   .manifests = manifests});

    if (!result.success) {
        std::cerr << result.error_message << "\n";
        return 1;
    }

    return 0;
}
