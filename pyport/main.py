import asyncio
import logging
import sys
import traceback
from pathlib import Path

# Add current directory to path for imports
sys.path.insert(0, str(Path(__file__).parent))

from cli.app import CliApplication

def setup_logging():
    """Setup logging configuration"""
    logging.basicConfig(
        level=logging.WARNING,
        format='%(asctime)s - %(name)s - %(levelname)s - %(message)s',
        handlers=[
            logging.StreamHandler(sys.stderr)
        ]
    )
    return logging.getLogger(__name__)

async def main():
    logger = setup_logging()

    if len(sys.argv) > 1:
        cli_app = CliApplication(logger)
        try:
            result = await cli_app.run(sys.argv[1:])
            if result == 1:
                sys.exit(1)
        except KeyboardInterrupt:
            logger.info("Operation cancelled by user")
            print("\n❌ Operation cancelled by user")
            sys.exit(130)
        except FileNotFoundError as e:
            logger.error(f"File not found: {e}")
            print(f"❌ File not found: {e}")
            sys.exit(2)
        except PermissionError as e:
            logger.error(f"Permission denied: {e}")
            print(f"❌ Permission denied: {e}")
            sys.exit(13)
        except ValueError as e:
            logger.error(f"Invalid configuration or arguments: {e}")
            print(f"❌ Configuration error: {e}")
            sys.exit(1)
        except ConnectionError as e:
            logger.error(f"Network connection error: {e}")
            print(f"❌ Network error: {e}")
            print("💡 Check your internet connection and API configuration")
            sys.exit(1)
        except Exception as e:
            logger.error(f"Unexpected error: {e}")
            logger.debug("Full traceback:", exc_info=True)
            print(f"❌ Unexpected error: {e}")
            
            # Only show traceback if in debug mode
            if '--debug' in sys.argv or '-v' in sys.argv:
                print("\n📋 Full traceback:")
                traceback.print_exc()
            else:
                print("💡 Run with --debug flag for detailed error information")
            
            sys.exit(1)
    else:
        print("TUI mode not implemented yet. Use CLI commands.")
        print("Run 'uv run main.py help' for usage information.")

if __name__ == "__main__":
    asyncio.run(main())
