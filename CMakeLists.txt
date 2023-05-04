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
