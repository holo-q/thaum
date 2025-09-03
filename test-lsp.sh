#!/bin/bash

# Test script for LSP integration
# This script demonstrates how to install OmniSharp and test the LSP functionality

set -euo pipefail

echo "🧪 Thaum LSP Integration Test"
echo "=============================="
echo ""

echo "📋 Testing current LSP implementation..."
echo "   (This should fail if OmniSharp is not installed)"
echo ""

# Test without OmniSharp (expected to fail)
timeout 5s dotnet run --no-restore -- ls test_project --lang csharp 2>&1 | head -10 || echo ""

echo ""
echo "❓ Would you like to install OmniSharp now? [y/N]"
read -r response

if [[ "$response" =~ ^[Yy]$ ]]; then
    echo ""
    echo "🔧 Installing OmniSharp..."
    ./install-omnisharp.sh
    
    echo ""
    echo "✅ Testing LSP integration with OmniSharp installed..."
    echo ""
    
    # Test with OmniSharp installed
    timeout 30s dotnet run --no-restore -- ls test_project --lang csharp || echo ""
    
    echo ""
    echo "🎉 Test complete! If you see C# symbols above, the LSP integration is working correctly."
else
    echo ""
    echo "ℹ️  To install OmniSharp later, run: ./install-omnisharp.sh"
    echo "   Then test with: dotnet run -- ls test_project --lang csharp"
fi

echo ""
echo "📚 For more information, see:"
echo "   - LSP-INTEGRATION.md - Detailed LSP integration documentation"
echo "   - README.md - General usage and installation guide"