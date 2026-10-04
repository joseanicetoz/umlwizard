namespace UmlWizard.CodeGeneration;

/// <summary>The built-in types every language knows under some name.</summary>
public static class BuiltInTypes
{
    /// <summary>
    /// Maps the common names of built-in types (UML's Integer, Boolean, String, Real; C#'s and Java's names) to a key
    /// that each language translates, e.g. "Integer", "int" and "Int32" are all "int". Other names give <c>null</c>.
    /// </summary>
    public static string? Key(string name) => name.ToLowerInvariant() switch
    {
        "string" or "str" or "text" => "string",
        "int" or "integer" or "int32" or "unlimitednatural" => "int",
        "long" or "int64" => "long",
        "short" or "int16" => "short",
        "byte" => "byte",
        "double" or "real" or "number" => "double",
        "float" or "single" => "float",
        "decimal" or "money" or "bigdecimal" => "decimal",
        "bool" or "boolean" => "bool",
        "char" or "character" => "char",
        "void" => "void",
        "object" or "any" => "object",
        "date" or "datetime" or "time" or "timestamp" or "localdatetime" => "datetime",
        "guid" or "uuid" => "guid",
        "list" or "ilist" or "arraylist" or "collection" or "icollection" or "ienumerable" or "sequence" or "array" or "vector" => "list",
        "set" or "hashset" or "iset" => "set",
        "map" or "dictionary" or "hashmap" or "idictionary" => "map",
        _ => null,
    };
}
