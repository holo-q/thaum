from dataclasses import dataclass, field
from enum import Enum
from typing import List, Optional

class SymbolKind(Enum):
    FUNCTION = "function"
    METHOD = "method"
    CLASS = "class"
    INTERFACE = "interface"
    MODULE = "module"
    NAMESPACE = "namespace"
    PROPERTY = "property"
    FIELD = "field"
    VARIABLE = "variable"
    PARAMETER = "parameter"

@dataclass
class Position:
    line: int
    character: int

@dataclass
class CodeSymbol:
    name: str
    kind: SymbolKind
    file_path: str
    start_position: Position
    end_position: Position
    summary: Optional[str] = None
    extracted_key: Optional[str] = None
    children: List["CodeSymbol"] = field(default_factory=list)
    dependencies: List[str] = field(default_factory=list)
    last_modified: Optional[str] = None
