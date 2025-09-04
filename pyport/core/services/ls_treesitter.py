import logging
import glob
import tree_sitter_languages as tsl
from ..models import CodeSymbol, Position, SymbolKind

class LSTreeSitter:
    def __init__(self, logger: logging.Logger):
        self.logger = logger

    def get_workspace_symbols(self, language: str, workspace_path: str) -> list[CodeSymbol]:
        symbols = []
        try:
            parser = tsl.get_parser(language)
            lang = tsl.get_language(language)
        except Exception as e:
            self.logger.error(f"Failed to get parser for language '{language}': {e}")
            return symbols

        file_extensions = self._get_file_extensions(language)
        for file_extension in file_extensions:
            for file_path in glob.glob(f"{workspace_path}/**/*{file_extension}", recursive=True):
                self.logger.info(f"Parsing {file_path}")
                try:
                    with open(file_path, 'r') as f:
                        source_code = f.read()
                    
                    tree = parser.parse(bytes(source_code, "utf8"))
                    symbols.extend(self._query_tree(tree, lang, file_path))
                except Exception as e:
                    self.logger.error(f"Error parsing file {file_path}: {e}")
        
        return symbols

    def _query_tree(self, tree, lang, file_path) -> list[CodeSymbol]:
        symbols = []
        query_string = """
        (function_declaration name: (identifier) @function.name) @function.body
        (method_declaration name: (identifier) @method.name) @method.body
        (function_definition name: (identifier) @function.name) @function.body
        (class_declaration name: (identifier) @class.name) @class.body
        (class_definition name: (identifier) @class.name) @class.body
        (method_definition name: (property_identifier) @method.name) @method.body
        """
        query = lang.query(query_string)
        captures = query.captures(tree.root_node)

        # a bit complex to get the name and body nodes for each match
        matches = {}
        for capture in captures:
            match_id = capture[0].id
            if match_id not in matches:
                matches[match_id] = {}
            capture_name = query.capture_names[capture[1]]
            matches[match_id][capture_name] = capture[0]

        for match_id, match_captures in matches.items():
            name_node = None
            body_node = None
            symbol_kind = SymbolKind.VARIABLE # default

            for capture_name, node in match_captures.items():
                if capture_name.endswith(".name"):
                    name_node = node
                    kind_str = capture_name.split(".")[0]
                    if kind_str == "function":
                        symbol_kind = SymbolKind.FUNCTION
                    elif kind_str == "method":
                        symbol_kind = SymbolKind.METHOD
                    elif kind_str == "class":
                        symbol_kind = SymbolKind.CLASS
                elif capture_name.endswith(".body"):
                    body_node = node
            
            if name_node and body_node:
                symbols.append(
                    CodeSymbol(
                        name=name_node.text.decode(),
                        kind=symbol_kind,
                        file_path=file_path,
                        start_position=Position(name_node.start_point[0], name_node.start_point[1]),
                        end_position=Position(body_node.end_point[0], body_node.end_point[1]),
                    )
                )

        return symbols

    def _get_file_extensions(self, language: str) -> list[str]:
        if language == "python":
            return [".py"]
        elif language == "csharp":
            return [".cs"]
        elif language == "javascript":
            return [".js", ".jsx"]
        elif language == "typescript":
            return [".ts", ".tsx"]
        elif language == "rust":
            return [".rs"]
        elif language == "go":
            return [".go"]
        else:
            return []
