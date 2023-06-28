# add_library(AddConst MODULE
#   AddConstAnalysis.cpp
#   AddConstPlugin.cpp)
#
# target_include_directories(AddConst PRIVATE
#   "${CMAKE_CURRENT_SOURCE_DIR}/../include")

target_sources(furstc PRIVATE main.cpp AddConstPlugin.cpp AddConstAnalysis.cpp)
