#!/bin/bash
set -e

echo "Setting up test environment..."

if ! command -v python3 &> /dev/null; then
    echo "Python 3 is not installed or not in PATH."
    exit 1
fi

echo "Installing Perfetto python package..."
pip3 install perfetto

echo "Restoring dotnet packages..."
dotnet restore tests/NanoProfiler.Tests/NanoProfiler.Tests.csproj

echo "Setup complete. You can now run tests using 'dotnet test tests/NanoProfiler.Tests/NanoProfiler.Tests.csproj'"
