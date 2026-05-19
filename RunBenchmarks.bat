@echo off
setlocal

set "ROOT_DIR=%~dp0"
set "PROJECT_FILE=%ROOT_DIR%src\Test.PerformanceBenchmark\Test.PerformanceBenchmark.csproj"
set "BENCHMARK_EXE=%ROOT_DIR%src\Test.PerformanceBenchmark\bin\Debug\net10.0\Test.PerformanceBenchmark.exe"

if not exist "%ROOT_DIR%benchmarks" mkdir "%ROOT_DIR%benchmarks"

for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format 'yyyyMMdd-HHmmss'"') do set "TIMESTAMP=%%i"
set "OUTPUT_FILE=%ROOT_DIR%benchmarks\Benchmark-%TIMESTAMP%.txt"

echo Building Test.PerformanceBenchmark for Debug/net10.0...
dotnet build "%PROJECT_FILE%" -c Debug -f net10.0
if errorlevel 1 goto Fail

echo Running benchmark suite...
"%BENCHMARK_EXE%" --summary-only %* > "%OUTPUT_FILE%"
if errorlevel 1 goto Fail

echo Benchmark summary saved to "%OUTPUT_FILE%"
endlocal
exit /b 0

:Fail
set "EXIT_CODE=%ERRORLEVEL%"
if exist "%OUTPUT_FILE%" del "%OUTPUT_FILE%"
endlocal & exit /b %EXIT_CODE%
