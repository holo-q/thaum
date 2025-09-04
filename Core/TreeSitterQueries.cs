using System.Collections.Generic;

namespace Thaum.Core.Services;

public static class TreeSitterQueries
{
    public static readonly string UniversalQuery = @"
(namespace_declaration name: (_) @namespace.name) @namespace.body
(class_declaration name: (identifier) @class.name) @class.body
(method_declaration name: (identifier) @method.name) @method.body
(constructor_declaration name: (identifier) @constructor.name) @constructor.body
(property_declaration name: (identifier) @property.name) @property.body
(interface_declaration name: (identifier) @interface.name) @interface.body
(field_declaration (variable_declaration (variable_declarator name: (identifier) @field.name))) @field.body
(enum_declaration name: (identifier) @enum.name) @enum.body
(enum_member_declaration name: (identifier) @enum_member.name) @enum_member.body
";
}