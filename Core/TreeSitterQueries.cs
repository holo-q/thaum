using System.Collections.Generic;

namespace Thaum.Core.Services;

public static class TreeSitterQueries
{
    public static readonly string UniversalQuery = @"
(function_declaration name: (identifier) @function.name) @function.body
(method_declaration name: (identifier) @method.name) @method.body
(function_definition name: (identifier) @function.name) @function.body
(class_declaration name: (identifier) @class.name) @class.body
(class_definition name: (identifier) @class.name) @class.body
(method_definition name: (property_identifier) @method.name) @method.body
";
}