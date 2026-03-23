#include "backend.h"
#include <gtest/gtest.h>

TEST(Smoke, CompileValidFsoSucceeds) {
    auto result = furst::compile("tests/fixtures/simple_add.fso", {});
    EXPECT_TRUE(result.success) << result.error_message;
}
