#include "backend.h"
#include <cstdlib>
#include <gtest/gtest.h>
#include <unistd.h>

static int compile_and_run(const std::string& fixture) {
    char tmp[] = "/tmp/furst_integ_XXXXXX";
    int fd = mkstemp(tmp);
    close(fd);
    auto exe_path = std::string(tmp);

    auto result = furst::compile(fixture, {.output_path = exe_path, .target_triple = {}, .link_libs = {}, .manifests = {}});
    if (!result.success) {
        unlink(tmp);
        ADD_FAILURE() << "compile failed: " << result.error_message;
        return -1;
    }

    auto status = system(exe_path.c_str());
    unlink(exe_path.c_str());

    // system() returns encoded status — extract exit code
    if (WIFEXITED(status)) {
        return WEXITSTATUS(status);
    }
    return -1;
}

TEST(Integration, Return42ExitsWithCode42) {
    auto exit_code = compile_and_run("tests/fixtures/return_42.fso");
    EXPECT_EQ(exit_code, 42);
}

TEST(Integration, AddAndReturnExitsWithCode42) {
    auto exit_code = compile_and_run("tests/fixtures/add_and_return.fso");
    EXPECT_EQ(exit_code, 42);
}
