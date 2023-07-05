cmake_minimum_required(VERSION 3.19)

project(Furst
  VERSION 1.0.0
  DESCRIPTION "A memory safe functional language"
  LANGUAGES CXX C)


set(CMAKE_CXX_STANDARD 20)
set(CMAKE_EXPORT_COMPILE_COMMANDS 1)

# add_link_options(-fuse-ld=lld)

find_program(CCACHE_PROGRAM ccache)

if(CCACHE_PROGRAM)
  message(STATUS "Found CCache")
  set(FURST_CCACHE_MAXSIZE "" CACHE STRING "Size of ccache")
  set(FURST_CCACHE_DIR "" CACHE STRING "Directory to keep ccached data")
  set(FURST_CCACHE_PARAMS "CCACHE_CPP2=yes CCACHE_HASHDIR=yes"
    CACHE STRING "Parameters to pass through to ccache")

  set(CCACHE_PROGRAM "${FURST_CCACHE_PARAMS} ${CCACHE_PROGRAM}")

  if (FURST_CCACHE_MAXSIZE)
    set(CCACHE_PROGRAM "CCACHE_MAXSIZE=${FURST_CCACHE_MAXSIZE} ${CCACHE_PROGRAM}")
  endif()
  if (FURST_CCACHE_DIR)
    set(CCACHE_PROGRAM "CCACHE_DIR=${FURST_CCACHE_DIR} ${CCACHE_PROGRAM}")
  endif()
  message(STATUS "Using CCACHE: ${CCACHE_PROGRAM}")
  set_property(GLOBAL PROPERTY RULE_LAUNCH_COMPILE ${CCACHE_PROGRAM})
else()
  message(FATAL_ERROR "Unable to find the program ccache. Set FURST_DISABLE_CCACHE to ON")
endif()

# set(LLVM_INSTALL_DIR "" CACHE PATH "LLVM installation directory")
set(LLVM_INSTALL_DIR "D:/code/llvm-project/build" CACHE PATH "LLVM installation directory")
list(APPEND CMAKE_PREFIX_PATH "${LLVM_INSTALL_DIR}/lib/cmake/llvm/")
list(APPEND CMAKE_PREFIX_PATH "${LLVM_INSTALL_DIR}/lib/cmake/mlir")
find_package(LLVM 17 REQUIRED CONFIG)
find_package(MLIR REQUIRED CONFIG)
# find_package(LLD REQUIRED CONFIG)
# find_program(LLD_PROGRAM REQUIRED NAMES lld)
#
# find_package(Clang REQUIRED CONFIG)

message(STATUS "Found LLVM ${LLVM_PACKAGE_VERSION}")
message(STATUS "Using LLVMConfig.cmake in: ${LLVM_DIR}")
message(STATUS "Using MLIRConfig.cmake in: ${MLIR_DIR}")
# message(STATUS "Using LLDConfig.cmake in: ${LLD_DIR}")
# message(STATUS "Using CLANGConfig.cmake in: ${Clang_DIR}")

message(STATUS "Found LLVM Include ${LLVM_INCLUDE_DIRS}")
message(STATUS "Found LLVM Library ${LLVM_LIBRARY_DIRS}")
message(STATUS "Found LLVM Definitions ${LLVM_DEFINITIONS}")

set(LLVM_RUNTIME_OUTPUT_INTDIR ${CMAKE_BINARY_DIR}/bin)
set(LLVM_LIBRARY_OUTPUT_INTDIR ${CMAKE_BINARY_DIR}/lib)
set(MLIR_BINARY_DIR ${CMAKE_BINARY_DIR})

list(APPEND CMAKE_MODULE_PATH "${MLIR_DIR}")
list(APPEND CMAKE_MODULE_PATH "${LLVM_DIR}")
# list(APPEND CMAKE_MODULE_PATH "${LLD_DIR}")
# list(APPEND CMAKE_MODULE_PATH "${Clang_DIR}")

include(AddLLVM)
include(AddMLIR)

include_directories(SYSTEM ${LLVM_INCLUDE_DIRS})
link_directories(${LLVM_LIBRARY_DIRS})
add_definitions(${LLVM_DEFINITIONS})

if(NOT LLVM_ENABLE_RTTI)
  set(CMAKE_CXX_FLAGS "${CMAKE_CXX_FLAGS} -fno-rtti")
endif()

set(CMAKE_LIBRARY_OUTPUT_DIRECTORY "${PROJECT_BINARY_DIR}/lib")

include_directories(${INCLUDE_DIR})
add_executable(furstc)
add_subdirectory(lib)

# Now build our tools

# Find the libraries that correspond to the LLVM components
# that we wish to use
llvm_map_components_to_libnames(llvm_libs support core irreader)

# Link against LLVM libraries
target_link_libraries(furstc ${llvm_libs})
