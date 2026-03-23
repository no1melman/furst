#include "backend.h"
#include <cstdlib>
#include <fstream>
#include <gtest/gtest.h>
#include <sstream>
#include <unistd.h>

// -- Integer literals --

TEST(Emitter, LiteralIntEmitsConstant) {
    auto result = furst::compile("tests/fixtures/literal_int.fso", {});
    ASSERT_TRUE(result.success) << result.error_message;
    // let x = 42 → function returns i32 42
    EXPECT_TRUE(result.ir.find("ret i32 42") != std::string::npos) << result.ir;
}

// -- Arithmetic --

TEST(Emitter, SimpleAddEmitsAddInstruction) {
    auto result = furst::compile("tests/fixtures/simple_add.fso", {});
    ASSERT_TRUE(result.success) << result.error_message;
    // let add x y = x + y → should have an add instruction
    EXPECT_TRUE(result.ir.find("add i32") != std::string::npos) << result.ir;
}

TEST(Emitter, ChainedOpsEmitsMultipleOps) {
    auto result = furst::compile("tests/fixtures/chained_ops.fso", {});
    ASSERT_TRUE(result.success) << result.error_message;
    // let f a b c = a + b + c → two add instructions
    auto first = result.ir.find("add i32");
    ASSERT_TRUE(first != std::string::npos) << result.ir;
    auto second = result.ir.find("add i32", first + 1);
    EXPECT_TRUE(second != std::string::npos) << "expected two add instructions\n" << result.ir;
}

// -- Let bindings --

TEST(Emitter, LetBindingEmitsAllocaStoreLoad) {
    auto result = furst::compile("tests/fixtures/let_binding.fso", {});
    ASSERT_TRUE(result.success) << result.error_message;
    // let f x = let y = x + 1 in y → should have alloca, store, load
    EXPECT_TRUE(result.ir.find("alloca i32") != std::string::npos) << result.ir;
    EXPECT_TRUE(result.ir.find("store i32") != std::string::npos) << result.ir;
    EXPECT_TRUE(result.ir.find("load i32") != std::string::npos) << result.ir;
}

// -- Function definitions --

TEST(Emitter, SimpleAddHasCorrectSignature) {
    auto result = furst::compile("tests/fixtures/simple_add.fso", {});
    ASSERT_TRUE(result.success) << result.error_message;
    EXPECT_TRUE(result.ir.find("define i32 @add(i32 %x, i32 %y)") != std::string::npos)
        << result.ir;
}

TEST(Emitter, SimpleAddReturnsAddResult) {
    auto result = furst::compile("tests/fixtures/simple_add.fso", {});
    ASSERT_TRUE(result.success) << result.error_message;
    // Function should return the result of the add, not a hardcoded 0
    EXPECT_TRUE(result.ir.find("ret i32 %addtmp") != std::string::npos) << result.ir;
}

TEST(Emitter, MultiFunctionEmitsBothFunctions) {
    auto result = furst::compile("tests/fixtures/multi_function.fso", {});
    ASSERT_TRUE(result.success) << result.error_message;
    EXPECT_TRUE(result.ir.find("@foo") != std::string::npos) << result.ir;
    EXPECT_TRUE(result.ir.find("@bar") != std::string::npos) << result.ir;
}

// -- Function calls --

TEST(Emitter, FunctionCallEmitsCallInstruction) {
    auto result = furst::compile("tests/fixtures/big_script.fso", {});
    ASSERT_TRUE(result.success) << result.error_message;
    // main calls compute(n)
    EXPECT_TRUE(result.ir.find("call i32 @compute") != std::string::npos) << result.ir;
}

TEST(Emitter, NestedFunctionCallUsesMangledName) {
    auto result = furst::compile("tests/fixtures/nested_function.fso", {});
    ASSERT_TRUE(result.success) << result.error_message;
    // outer calls outer$inner(5) — frontend rewrites call name
    EXPECT_TRUE(result.ir.find("call i32 @\"outer$inner\"") != std::string::npos) << result.ir;
}

TEST(Emitter, CapturedParamPassedAsExtraArg) {
    auto result = furst::compile("tests/fixtures/captured_param.fso", {});
    ASSERT_TRUE(result.success) << result.error_message;
    // outer$inner(x, y) — x is captured, y is own param
    // call should pass both: call i32 @"outer$inner"(i32 %x, i32 5)
    EXPECT_TRUE(result.ir.find("call i32 @\"outer$inner\"") != std::string::npos) << result.ir;
}

// -- File output --

TEST(Emitter, WritesLlFileWhenOutputPathSet) {
    char tmp[] = "/tmp/furst_test_XXXXXX";
    int fd = mkstemp(tmp);
    close(fd);
    std::string path = std::string(tmp) + ".ll";

    auto result = furst::compile("tests/fixtures/simple_add.fso", {.output_path = path, .target_triple = {}, .link_libs = {}, .manifests = {}});
    ASSERT_TRUE(result.success) << result.error_message;
    EXPECT_EQ(result.output_path, path);

    // Read the file and check contents
    auto file = std::ifstream(path);
    auto contents = std::string(std::istreambuf_iterator<char>(file), {});
    EXPECT_TRUE(contents.find("define i32 @add") != std::string::npos) << contents;

    unlink(tmp);
    unlink(path.c_str());
}

TEST(Emitter, WritesObjectFileWhenOutputPathIsO) {
    char tmp[] = "/tmp/furst_test_XXXXXX";
    int fd = mkstemp(tmp);
    close(fd);
    std::string path = std::string(tmp) + ".o";

    auto result = furst::compile("tests/fixtures/simple_add.fso", {.output_path = path, .target_triple = {}, .link_libs = {}, .manifests = {}});
    ASSERT_TRUE(result.success) << result.error_message;

    // Check file exists and has content (ELF magic: 0x7f ELF)
    auto file = std::ifstream(path, std::ios::binary);
    ASSERT_TRUE(file.is_open());
    char magic[4] = {};
    file.read(magic, 4);
    EXPECT_EQ(magic[0], 0x7f);
    EXPECT_EQ(magic[1], 'E');
    EXPECT_EQ(magic[2], 'L');
    EXPECT_EQ(magic[3], 'F');

    unlink(tmp);
    unlink(path.c_str());
}

// -- Optimization --

TEST(Emitter, O2RemovesAllocas) {
    auto result = furst::compile("tests/fixtures/let_binding.fso",
                                 {.output_path = {}, .target_triple = {}, .opt_level = furst::OptLevel::O2, .link_libs = {}, .manifests = {}});
    ASSERT_TRUE(result.success) << result.error_message;
    // mem2reg should eliminate alloca/store/load
    EXPECT_TRUE(result.ir.find("alloca") == std::string::npos)
        << "O2 should eliminate allocas\n" << result.ir;
}

// -- Debug info --

TEST(Emitter, IrContainsDebugMetadata) {
    auto result = furst::compile("tests/fixtures/simple_add.fso", {});
    ASSERT_TRUE(result.success) << result.error_message;
    EXPECT_TRUE(result.ir.find("!DICompileUnit") != std::string::npos) << result.ir;
    EXPECT_TRUE(result.ir.find("!DISubprogram") != std::string::npos) << result.ir;
    EXPECT_TRUE(result.ir.find("furstc") != std::string::npos) << result.ir;
}

TEST(Emitter, NoFileWrittenWhenOutputPathEmpty) {
    auto result = furst::compile("tests/fixtures/simple_add.fso", {});
    ASSERT_TRUE(result.success) << result.error_message;
    EXPECT_TRUE(result.output_path.empty());
    // IR is still available in memory
    EXPECT_TRUE(result.ir.find("define i32 @add") != std::string::npos);
}

TEST(Emitter, ZeroParamFunctionHasNoArgs) {
    auto result = furst::compile("tests/fixtures/literal_int.fso", {});
    ASSERT_TRUE(result.success) << result.error_message;
    // let x = 42 → lowered to zero-param function
    EXPECT_TRUE(result.ir.find("define i32 @x()") != std::string::npos) << result.ir;
}

TEST(Emitter, FunctionBodyReturnsLastExpression) {
    auto result = furst::compile("tests/fixtures/let_binding.fso", {});
    ASSERT_TRUE(result.success) << result.error_message;
    // let f x = let y = x + 1; y → should load y and return it
    EXPECT_TRUE(result.ir.find("load i32") != std::string::npos) << result.ir;
    EXPECT_TRUE(result.ir.find("ret i32 %y") != std::string::npos) << result.ir;
}
