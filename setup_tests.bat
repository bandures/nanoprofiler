@echo off
echo Setting up test environment...

python --version >nul 2>&1
if %errorlevel% neq 0 (
    echo Python is not installed or not in PATH. Please install Python.
    exit /b 1
)

echo Installing Perfetto python package...
pip install perfetto
if %errorlevel% neq 0 (
    echo Failed to install perfetto package.
    exit /b 1
)

echo Restoring dotnet packages...
dotnet restore tests/NanoProfiler.Tests/NanoProfiler.Tests.csproj
if %errorlevel% neq 0 (
    echo Failed to restore dotnet packages.
    exit /b 1
)

echo Setup complete. You can now run tests using 'dotnet test tests/NanoProfiler.Tests/NanoProfiler.Tests.csproj'
