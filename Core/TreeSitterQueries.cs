using System.Collections.Generic;

namespace Thaum.Core.Services;

public static class TreeSitterQueries
{
    public static readonly string UniversalQuery = @"
(class_declaration name: (identifier) @class.name) @class.body
(method_declaration name: (identifier) @method.name) @method.body
(constructor_declaration name: (identifier) @constructor.name) @constructor.body
(property_declaration name: (identifier) @property.name) @property.body
";
}