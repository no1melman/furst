cmake_minimum_required(VERSION 3.25)

project(FurstBackend
    VERSION 0.1.0
    DESCRIPTION "Furst compiler backend — .fso to LLVM IR"
    LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)

# --- Compiler warnings ---
add_compile_options(-Wall -Wextra -Wpedantic -Werror -Wno-error=cpp)

# --- Dependencies ---
find_package(Protobuf REQUIRED)
find_package(absl REQUIRED)
find_package(LLVM REQUIRED CONFIG)
find_package(GTest REQUIRED)

message(STATUS "Found Protobuf ${Protobuf_VERSION}")
message(STATUS "Found LLVM ${LLVM_PACKAGE_VERSION}")
message(STATUS "Found GTest ${GTest_VERSION}")

# LLVM setup
include_directories(SYSTEM ${LLVM_INCLUDE_DIRS})
link_directories(${LLVM_LIBRARY_DIRS})
add_definitions(${LLVM_DEFINITIONS})

if(NOT LLVM_ENABLE_RTTI)
    add_compile_options(-fno-rtti)
endif()

llvm_map_components_to_libnames(LLVM_LIBS
    support
    core
    irreader
    target
    codegen
    nativecodegen
    passes
    ipo
    coroutines
)

# --- Proto codegen ---
set(PROTO_SRC_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../proto")
protobuf_generate_cpp(PROTO_SRCS PROTO_HDRS "${PROTO_SRC_DIR}/furst_ast.proto")

# --- Backend library (shared between exe and tests) ---
add_library(furst_backend STATIC
    src/backend.cpp
    src/errors.cpp
    src/fso_reader.cpp
    src/emitter.cpp
    ${PROTO_SRCS}
)
target_include_directories(furst_backend PUBLIC
    src
    ${CMAKE_CURRENT_BINARY_DIR}  # for generated .pb.h
)
target_link_libraries(furst_backend PUBLIC
    protobuf::libprotobuf
    absl::log_internal_check_op
    absl::strings
    ${LLVM_LIBS}
)

# --- Backend CLI binary ---
add_executable(furstc-backend
    src/main.cpp
)
target_link_libraries(furstc-backend PRIVATE furst_backend)

# Copy binary to root build/ dir after building
set(ROOT_BUILD_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../build")
add_custom_command(TARGET furstc-backend POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E make_directory ${ROOT_BUILD_DIR}
    COMMAND ${CMAKE_COMMAND} -E copy $<TARGET_FILE:furstc-backend> ${ROOT_BUILD_DIR}/
    COMMENT "Copying furstc-backend to ${ROOT_BUILD_DIR}"
)

# --- Tests ---
enable_testing()

add_executable(backend_tests
    tests/test_smoke.cpp
    tests/test_errors.cpp
    tests/test_reader.cpp
    tests/test_emitter.cpp
    tests/test_integration.cpp
)
target_link_libraries(backend_tests PRIVATE
    furst_backend
    GTest::gtest_main
)
include(GoogleTest)
gtest_discover_tests(backend_tests
    WORKING_DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR}
)

# --- Format target ---
find_program(CLANG_FORMAT clang-format)
if(CLANG_FORMAT)
    file(GLOB_RECURSE ALL_CXX_SOURCES
        "${CMAKE_CURRENT_SOURCE_DIR}/src/*.cpp"
        "${CMAKE_CURRENT_SOURCE_DIR}/src/*.h"
        "${CMAKE_CURRENT_SOURCE_DIR}/tests/*.cpp"
        "${CMAKE_CURRENT_SOURCE_DIR}/tests/*.h"
    )
    add_custom_target(format
        COMMAND ${CLANG_FORMAT} -i ${ALL_CXX_SOURCES}
        COMMENT "Running clang-format"
        VERBATIM
    )
endif()

# --- Lint target ---
find_program(CLANG_TIDY clang-tidy)
if(CLANG_TIDY)
    add_custom_target(lint
        COMMAND ${CLANG_TIDY} -p ${CMAKE_BINARY_DIR}
            ${CMAKE_CURRENT_SOURCE_DIR}/src/*.cpp
            ${CMAKE_CURRENT_SOURCE_DIR}/src/*.h
        COMMENT "Running clang-tidy"
        VERBATIM
    )
endif()
