cd build 
cmake ../ -G "Visual Studio 17 2022" -DCMAKE_BUILD_TYPE=Release -Thost=x64
cmake --build . --parallel
