#!/bin/bash
# Local validation script - run before pushing to GitHub
# This saves CI time and catches issues early

set -e

echo "=== HeroMessaging Local Validation ==="
echo ""

# Check if dotnet is available
if ! command -v dotnet &> /dev/null; then
    echo "❌ dotnet CLI not found. Please install .NET SDK."
    exit 1
fi

echo "✅ dotnet CLI found: $(dotnet --version)"
echo ""

# Restore dependencies
echo "📦 Restoring dependencies..."
dotnet restore --verbosity quiet
echo "✅ Dependencies restored"
echo ""

# Build solution
echo "🔨 Building solution..."
dotnet build --configuration Release --no-restore --verbosity quiet
echo "✅ Build successful"
echo ""

# Run contract tests
echo "🧪 Running contract tests..."
for framework in net6.0 net8.0 net9.0; do
    echo "  Testing with $framework..."
    dotnet test tests/HeroMessaging.Contract.Tests/HeroMessaging.Contract.Tests.csproj \
        --configuration Release \
        --no-build \
        --framework $framework \
        --filter "Category=Contract" \
        --verbosity quiet \
        --logger "console;verbosity=minimal"
    echo "  ✅ $framework tests passed"
done

echo ""
echo "🎉 All local validations passed!"
echo ""
echo "Ready to push to GitHub."
