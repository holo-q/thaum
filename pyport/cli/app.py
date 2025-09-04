import logging
import argparse
import json
import os
import sys
from pathlib import Path

# Add parent directory to path for imports
sys.path.insert(0, str(Path(__file__).parent.parent))

from core.services.ls_treesitter import LSTreeSitter
from core.models import CodeSymbol, SymbolKind
from core.services.compressor import Compressor
from core.utils.perceptual_color_engine import PerceptualColorEngine
from core.http_llm import HttpLLM
from core.cache import Cache
from core.prompt_loader import PromptLoader

class HierarchyNode:
    def __init__(self, name, kind, symbol, children=None):
        self.name = name
        self.kind = kind
        self.symbol = symbol
        self.children = children if children is not None else []

class CliApplication:
    def __init__(self, logger: logging.Logger):
        self.logger = logger
        
        # Load configuration first
        self.configuration = self._load_configuration()
        
        # Initialize core components
        self.ls_treesitter = LSTreeSitter(logger)
        self.color_engine = PerceptualColorEngine()
        self.llm_provider = HttpLLM(self.configuration, logger)
        self.cache = Cache(self.configuration, logger)
        self.prompt_loader = PromptLoader(logger)
        
        # Initialize compressor with all dependencies
        self.summary_engine = Compressor(
            self.llm_provider, 
            self.ls_treesitter, 
            self.cache, 
            self.prompt_loader, 
            logger
        )
    
    def _load_configuration(self):
        """Load configuration from appsettings.json or environment"""
        config = {}
        
        # Try to load from appsettings.json
        config_path = os.path.join(os.path.dirname(__file__), '..', '..', 'appsettings.json')
        if os.path.exists(config_path):
            try:
                with open(config_path, 'r') as f:
                    config = json.load(f)
            except Exception as e:
                self.logger.warning(f"Failed to load configuration from appsettings.json: {e}")
        
        return config

    async def run(self, args):
        if len(args) == 0:
            self.show_help()
            return

        command = args[0].lower()

        if command == "ls":
            await self.handle_ls_command(args)
        elif command == "ls-env":
            self.handle_ls_env_command(args)
        elif command == "ls-cache":
            await self.handle_ls_cache_command(args)
        elif command == "ls-lsp":
            await self.handle_ls_lsp_command(args)
        elif command == "test-prompt":
            await self.handle_test_prompt_command(args)
        elif command == "summarize":
            await self.handle_summarize_command(args)
        elif command in ["help", "--help", "-h"]:
            self.show_help()
        else:
            print(f"Unknown command: {command}")
            self.show_help()
            return 1

    async def handle_ls_command(self, args):
        # Parse arguments manually for ls command
        path = "."
        lang = "python"
        depth = 10
        
        for i, arg in enumerate(args[1:], 1):
            if arg.startswith("--lang="):
                lang = arg.split("=", 1)[1]
            elif arg == "--lang" and i + 1 < len(args):
                lang = args[i + 1]
            elif arg.startswith("--depth="):
                depth = int(arg.split("=", 1)[1])
            elif arg == "--depth" and i + 1 < len(args):
                depth = int(args[i + 1])
            elif not arg.startswith("--") and not arg.startswith("-"):
                path = arg
        
        self.logger.info(f"Scanning {path} for {lang} symbols...")
        symbols = self.ls_treesitter.get_workspace_symbols(lang, path)

        if not symbols:
            print("No symbols found.")
            return

        hierarchy = self._build_hierarchy(symbols)
        self._display_hierarchy(hierarchy, depth)

        print(f"\nFound {len(symbols)} symbols total")

    def _build_hierarchy(self, symbols: list[CodeSymbol]) -> list[HierarchyNode]:
        nodes = {}
        for symbol in symbols:
            if symbol.file_path not in nodes:
                nodes[symbol.file_path] = HierarchyNode(symbol.file_path, SymbolKind.MODULE, None)
            
            nodes[symbol.file_path].children.append(HierarchyNode(symbol.name, symbol.kind, symbol))
        
        return list(nodes.values())

    def _display_hierarchy(self, nodes: list[HierarchyNode], max_depth: int, prefix="", depth=0):
        for i, node in enumerate(nodes):
            is_last = i == len(nodes) - 1
            self._display_node(node, prefix, is_last, max_depth, depth)

    def _display_node(self, node: HierarchyNode, prefix: str, is_last: bool, max_depth: int, depth: int):
        if depth >= max_depth:
            return

        connector = "└── " if is_last else "├── "
        print(f"{prefix}{connector}{self._get_symbol_icon(node.kind)} {node.name}")

        if node.children:
            new_prefix = prefix + ("    " if is_last else "│   ")
            self._display_hierarchy(node.children, max_depth, new_prefix, depth + 1)

    def _get_symbol_icon(self, kind: SymbolKind) -> str:
        return {
            SymbolKind.FUNCTION: "ƒ",
            SymbolKind.METHOD: "ƒ",
            SymbolKind.CLASS: "C",
            SymbolKind.INTERFACE: "I",
            SymbolKind.MODULE: "📁",
            SymbolKind.NAMESPACE: "N",
            SymbolKind.PROPERTY: "P",
            SymbolKind.FIELD: "F",
            SymbolKind.VARIABLE: "V",
            SymbolKind.PARAMETER: "p",
        }.get(kind, "?")

    def handle_ls_env_command(self, args):
        """List environment variables"""
        print("🔧 Environment Variables:")
        print("=" * 50)
        
        # Common environment variables for different LLM providers
        env_vars = [
            "OPENAI_API_KEY", "ANTHROPIC_API_KEY", "OPENROUTER_API_KEY",
            "LLM_PROVIDER", "LLM_BASE_URL", "LLM_MODEL",
            "THAUM_CACHE_DIR", "THAUM_PROMPTS_DIR"
        ]
        
        for var in env_vars:
            value = os.getenv(var)
            if value:
                # Mask sensitive keys
                if "KEY" in var:
                    display_value = f"{value[:8]}{'*' * (len(value) - 8)}" if len(value) > 8 else "*" * len(value)
                else:
                    display_value = value
                print(f"  {var}: {display_value}")
            else:
                print(f"  {var}: (not set)")

    async def handle_ls_cache_command(self, args):
        """List cache entries"""
        print("🔍 Thaum Cache Browser - Hierarchical Compressed Symbol Representations")
        print("=" * 80)
        print()
        
        try:
            entries = await self.cache.get_all_entries_async()
            
            if not entries:
                print("⚠️  No cached entries found. Run 'summarize' first to populate cache.")
                print("💡 Example: python main.py summarize --compression endgame")
                return
            
            # Group entries by type
            optimizations = [e for e in entries if e['key'].startswith('optimization_')]
            keys = [e for e in entries if e['key'].startswith('key_L')]
            
            if optimizations:
                print(f"📦 CACHED OPTIMIZATIONS ({len(optimizations)} symbols)")
                print()
                
                for entry in optimizations:
                    key_parts = entry['key'].split('_')
                    if len(key_parts) >= 3:
                        file_path = key_parts[1]
                        symbol_name = key_parts[2]
                        print(f"  📄 {file_path}")
                        print(f"    ƒ {symbol_name}")
                        if entry.get('prompt_name'):
                            print(f"    [{entry['prompt_name']}]")
                        if entry.get('model_name'):
                            print(f"    ({entry.get('provider_name', 'unknown')}:{entry['model_name']})")
                        print()
            
            if keys:
                print("🔑 EXTRACTED ARCHITECTURAL KEYS")
                print("=" * 40)
                print()
                
                for entry in keys:
                    key_parts = entry['key'].split('_')
                    if len(key_parts) >= 2:
                        level = key_parts[1][1:]  # Remove 'L' prefix
                        print(f"K{level} → Key pattern for level {level}")
                        if entry.get('prompt_name'):
                            print(f"    [{entry['prompt_name']}]")
                        if entry.get('model_name'):
                            print(f"    ({entry.get('provider_name', 'unknown')}:{entry['model_name']})")
                        print()
                        
        except Exception as ex:
            print(f"❌ Error reading cache: {ex}")
            return 1

    async def handle_ls_lsp_command(self, args):
        """List LSP information (placeholder - would integrate with LSP if available)"""
        print("🔍 LSP Server Information")
        print("=" * 30)
        print()
        print("⚠️  LSP integration not yet implemented in Python port.")
        print("💡 This would show language server information and status.")

    async def handle_test_prompt_command(self, args):
        """Test prompt functionality"""
        print("🧪 Testing Prompt System")
        print("=" * 25)
        print()
        
        # Test prompt loading
        try:
            test_prompt_name = "test"  # Default test prompt
            if len(args) > 1:
                test_prompt_name = args[1]
                
            prompt_content = await self.prompt_loader.load_prompt_async(test_prompt_name)
            print(f"✅ Successfully loaded prompt '{test_prompt_name}':")
            print(f"📄 Content: {prompt_content[:100]}{'...' if len(prompt_content) > 100 else ''}")
            print()
            
            # Test prompt formatting
            test_params = {"name": "test_symbol", "type": "function", "content": "def test(): pass"}
            formatted = await self.prompt_loader.format_prompt_async(test_prompt_name, test_params)
            print("🔄 Formatted with test parameters:")
            print(f"📄 Result: {formatted[:200]}{'...' if len(formatted) > 200 else ''}")
            print()
            
            # Test LLM if configured
            if self.configuration.get("llm", {}).get("provider"):
                print("🤖 Testing LLM connection...")
                try:
                    response = await self.llm_provider.complete_async("Hello, this is a test.", None)
                    print(f"✅ LLM Response: {response[:100]}{'...' if len(response) > 100 else ''}")
                except Exception as llm_ex:
                    print(f"❌ LLM Error: {llm_ex}")
            else:
                print("⚠️  No LLM provider configured. Set LLM:Provider in configuration.")
                
        except FileNotFoundError as fnf_ex:
            print(f"❌ Prompt file not found: {fnf_ex}")
            print("💡 Create a test.txt file in the prompts/ directory")
        except Exception as ex:
            print(f"❌ Error testing prompt system: {ex}")

    async def handle_summarize_command(self, args):
        """Generate codebase summary/optimization (placeholder)"""
        print("📊 Codebase Summarization")
        print("=" * 25)
        print()
        print("⚠️  Summarization engine not yet fully implemented in Python port.")
        print("💡 This would analyze code and generate hierarchical summaries.")
        
        # Parse basic arguments
        path = "."
        lang = "python"
        
        for i, arg in enumerate(args[1:], 1):
            if arg.startswith("--lang="):
                lang = arg.split("=", 1)[1]
            elif arg == "--lang" and i + 1 < len(args):
                lang = args[i + 1]
            elif not arg.startswith("--") and not arg.startswith("-"):
                path = arg
        
        print(f"📁 Target: {path}")
        print(f"🗣️  Language: {lang}")
        print()
        print("🔄 This would:")
        print("  1. Scan codebase for symbols")
        print("  2. Generate hierarchical representations")
        print("  3. Store compressed summaries in cache")
        print("  4. Extract architectural key patterns")

    def show_help(self):
        """Show help information"""
        print("🏛️  Thaum - Hierarchical Compression Engine")
        print("=" * 45)
        print()
        print("USAGE:")
        print("  python main.py <command> [options]")
        print()
        print("COMMANDS:")
        print("  ls [path]              List symbols in hierarchical format")
        print("    --lang <language>    Language (python, csharp, javascript, etc.)")
        print("    --depth <number>     Maximum nesting depth (default: 10)")
        print()
        print("  ls-env                 List environment variables")
        print("  ls-cache               List cached optimizations and keys")
        print("    --keys, -k           Show architectural keys")
        print("    --all, -a            Show all entries")
        print("    --details, -d        Show detailed information")
        print()
        print("  ls-lsp                 List LSP server information")
        print("  test-prompt [name]     Test prompt loading and LLM connection")
        print("  summarize [path]       Generate codebase optimizations")
        print("    --lang <language>    Language for analysis")
        print("    --compression <type> Compression type (endgame, etc.)")
        print()
        print("  help, --help, -h       Show this help message")
        print()
        print("EXAMPLES:")
        print("  python main.py ls src --lang python --depth 5")
        print("  python main.py ls-cache --details")
        print("  python main.py test-prompt vectorial_function")
        print("  python main.py summarize --compression endgame")
