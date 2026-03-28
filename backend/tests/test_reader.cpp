#include "backend.h"
#include <cstdlib>
#include <fstream>
#include <gtest/gtest.h>
#include <unistd.h>

// -- Error cases --

TEST(Reader, MissingFileReturnsError) {
    auto result = furst::compile("nonexistent.fso", {});
    EXPECT_FALSE(result.success);
    EXPECT_EQ(result.error_message, "error: file not found: nonexistent.fso");
}

TEST(Reader, EmptyFileReturnsHeaderError) {
    auto result = furst::compile("/dev/null", {});
    EXPECT_FALSE(result.success);
    EXPECT_EQ(result.error_message, "error: invalid .fso header in /dev/null: file too short");
}

TEST(Reader, BadMagicReturnsHeaderError) {
    // Create a temp file with wrong magic bytes
    char tmp[] = "/tmp/furst_test_XXXXXX";
    int fd = mkstemp(tmp);
    auto bytes_written = write(fd, "NOTFSO\0\0garbage", 15);
    ASSERT_EQ(bytes_written, 15);
    close(fd);

    auto result = furst::compile(tmp, {});
    EXPECT_FALSE(result.success);
    EXPECT_TRUE(result.error_message.find("bad magic bytes") != std::string::npos);

    unlink(tmp);
}

// -- Valid fixtures compile successfully --

TEST(Reader, SimpleAddCompiles) {
    auto result = furst::compile("tests/fixtures/simple_add.fso", {});
    EXPECT_TRUE(result.success) << result.error_message;
}

TEST(Reader, LiteralIntCompiles) {
    auto result = furst::compile("tests/fixtures/literal_int.fso", {});
    EXPECT_TRUE(result.success) << result.error_message;
}

TEST(Reader, NestedFunctionCompiles) {
    auto result = furst::compile("tests/fixtures/nested_function.fso", {});
    EXPECT_TRUE(result.success) << result.error_message;
}

TEST(Reader, CapturedParamCompiles) {
    auto result = furst::compile("tests/fixtures/captured_param.fso", {});
    EXPECT_TRUE(result.success) << result.error_message;
}

TEST(Reader, MultiFunctionCompiles) {
    auto result = furst::compile("tests/fixtures/multi_function.fso", {});
    EXPECT_TRUE(result.success) << result.error_message;
}

TEST(Reader, ChainedOpsCompiles) {
    auto result = furst::compile("tests/fixtures/chained_ops.fso", {});
    EXPECT_TRUE(result.success) << result.error_message;
}

TEST(Reader, BigScriptCompiles) {
    auto result = furst::compile("tests/fixtures/big_script.fso", {});
    EXPECT_TRUE(result.success) << result.error_message;
}
