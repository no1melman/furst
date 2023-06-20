# This file sets up a CMakeCache for a LLVM toolchain build
set(CMAKE_CXX_STANDARD 20)

#Enable LLVM projects and runtimes
set(LLVM_ENABLE_PROJECTS "clang;clang-tools-extra;lld;lldb;mlir" CACHE STRING "")
set(LLVM_ENABLE_RUNTIMES "compiler-rt;libcxx;libcxxabi;libunwind" CACHE STRING "")

# Distributions should never be built using the BUILD_SHARED_LIBS CMake option.
# That option exists for optimizing developer workflow only. Due to design and
# implementation decisions, LLVM relies on global data which can end up being
# duplicated across shared libraries resulting in bugs. As such this is not a
# safe way to distribute LLVM or LLVM-based tools.
set(BUILD_SHARED_LIBS OFF CACHE BOOL "")

#set(LLVM_BUILD_STATIC ON CACHE BOOL "")
set(LLVM_BUILD_LLVM_DYLIB OFF CACHE BOOL "")
set(LLVM_ENABLE_EH ON CACHE BOOL "")
set(LLVM_ENABLE_RTTI ON CACHE BOOL "")

set(LIBCLANG_BUILD_STATIC ON CACHE BOOL "")

set(LLVM_ENABLE_LIBXML2 OFF CACHE BOOL "")
set(LLVM_ENABLE_ASSERTIONS ON CACHE BOOL "")

set(COMPILER_RT_USE_BUILTINS_LIBRARY ON CACHE BOOL "")

# Create the builtin libs of compiler-rt
set(COMPILER_RT_BUILD_BUILTINS ON CACHE BOOL "")
set(COMPILER_RT_BUILD_STANDALONE_LIBATOMIC OFF CACHE BOOL "")
set(COMPILER_RT_EXCLUDE_ATOMIC_BUILTIN OFF CACHE BOOL "")
set(COMPILER_RT_CXX_LIBRARY "libcxx" CACHE STRING "")
set(COMPILER_RT_USE_LIBCXX ON CACHE BOOL "Enable compiler-rt to use libc++ from the source tree")

set(LIBUNWIND_ENABLE_STATIC ON CACHE BOOL "")
set(LIBCXXABI_ENABLE_STATIC ON CACHE BOOL "")
set(LIBCXX_DEFAULT_ABI_LIBRARY "libcxxabi" CACHE STRING "")
set(LIBCXX_ENABLE_STATIC ON CACHE BOOL "")
set(LIBCXX_USE_COMPILER_RT ON CACHE BOOL "")
set(LIBCXX_CXX_ABI libcxxabi CACHE BOOL "")
set(LIBCXXABI_USE_COMPILER_RT ON CACHE BOOL "")
set(LIBCXXABI_USE_LLVM_UNWINDER ON CACHE BOOL "")
set(LIBCXXABI_ENABLE_EXCEPTIONS ON CACHE BOOL "")
set(LIBCXXABI_ENABLE_NEW_DELETE_DEFINITIONS ON CACHE BOOL
  "Build libc++abi with definitions for operator new/delete. These are normally
   defined in libc++abi, but it is also possible to define them in libc++, in
   which case the definition in libc++abi should be turned off.")
set(LIBCXX_ENABLE_NEW_DELETE_DEFINITIONS OFF CACHE BOOL "")
set(LIBCXXABI_STATICALLY_LINK_UNWINDER_IN_STATIC_LIBRARY ON CACHE BOOL " ")
set(LIBCXX_HAS_GCC_LIB OFF CACHE BOOL "")
set(LIBCXX_ENABLE_EXCEPTIONS ON CACHE BOOL "")
set(LIBCXX_ENABLE_RTTI ON CACHE BOOL "")
set(LIBCXX_ENABLE_THREADS ON CACHE BOOL "")

# set(LLVM_EXTERNAL_PROJECTS iwyu ON CACHE STRING "")

# Only build the native target in stage1 since it is a throwaway build.

# Optimize the stage1 compiler, but don't LTO it because that wastes time.
set(CMAKE_BUILD_TYPE Release CACHE STRING "")

# Setting up the stage2 LTO option needs to be done on the stage1 build so that
# the proper LTO library dependencies can be connected.
set(LLVM_ENABLE_LTO ON CACHE BOOL "")

# Since LLVM_ENABLE_LTO is ON we need a LTO capable linker
set(LLVM_ENABLE_LLD ON CACHE BOOL "")

