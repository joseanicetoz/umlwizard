using System.Text;
using UmlWizard.CodeImport;
using UmlWizard.Core.Model;

namespace UmlWizard.CodeImport.Kotlin;

/// <summary>
/// A declaration parser for one Kotlin file. Declarations end at a line break unless the code clearly goes on (after
/// an operator, or before one), as in Kotlin itself.
/// </summary>
internal sealed class KotlinParser(List<Token> tokens, List<ImportedType> types, KotlinCodeImporter.Losses loss)
{
    private static readonly HashSet<string> Modifiers =
    [
        "public", "private", "protected", "internal", "abstract", "open", "final", "sealed", "data", "enum", "annotation",
        "inner", "value", "inline", "expect", "actual", "external", "override", "lateinit", "const", "suspend", "tailrec",
        "operator", "infix", "companion", "vararg", "crossinline", "noinline",
    ];

    /// <summary>Types that are not classes of the code.</summary>
    private static readonly HashSet<string> Primitives =
    [
        "Int", "Long", "Short", "Byte", "Double", "Float", "Boolean", "Char", "String", "Unit", "Any", "Nothing", "Number",
        "UInt", "ULong", "UShort", "UByte", "CharSequence", "IntArray", "LongArray", "ShortArray", "ByteArray", "DoubleArray",
        "FloatArray", "BooleanArray", "CharArray",
    ];

    /// <summary>Generic types whose last type argument is the element of a collection (the value of a map).</summary>
    private static readonly HashSet<string> Collections =
    [
        "List", "MutableList", "ArrayList", "Set", "MutableSet", "HashSet", "LinkedHashSet", "Collection", "MutableCollection",
        "Iterable", "MutableIterable", "Sequence", "Map", "MutableMap", "HashMap", "LinkedHashMap", "Array",
    ];

    /// <summary>
    /// Tokens after which the code goes on on the next line. Not <c>&gt;</c>: a line that ends with one mostly ends
    /// with type arguments, e.g. <c>typealias Orders = List&lt;Order&gt;</c>.
    /// </summary>
    private static readonly HashSet<string> ContinueAfter =
    [
        "=", "+", "-", "*", "/", "%", "&", "|", "!", "?", ":", ",", ".", "<", "->", "(", "[", "{", "as", "is", "in",
        "by", "where",
    ];

    /// <summary>Tokens with which the code on a new line goes on from the previous one.</summary>
    private static readonly HashSet<string> ContinueWith =
    [
        ".", "?", ":", "+", "*", "/", "%", "&", "|", "=", ">", "<", "->", ",", ")", "]", "}", "as", "is", "in", "else",
        "by", "where",
    ];

    private string _package = string.Empty;
    private int _i;

    private Token Current => At(_i);

    private Token Next => At(_i + 1);

    private bool AtEnd => _i >= tokens.Count;

    public void ParseFile()
    {
        while (!AtEnd)
        {
            var start = _i;
            if (Current.Is(";"))
            {
                _i++;
            }
            else if (IsWord("package"))
            {
                _i++;
                _package = ReadDottedName();
                if (Current.Is(";"))
                {
                    _i++;
                }
            }
            else if (IsWord("import"))
            {
                SkipStatement();
            }
            else
            {
                ReadDeclaration(owner: null, [], inCompanion: false);
            }

            if (_i == start)
            {
                _i++;
            }
        }
    }

    #region Declarations

    /// <summary>
    /// A declaration in a file (<paramref name="owner"/> null) or in the body of a type: a type, function, property,
    /// constructor or init block. Functions and properties outside types are left out.
    /// </summary>
    private void ReadDeclaration(ImportedType? owner, List<string> declaringTypes, bool inCompanion)
    {
        SkipAnnotations();
        var modifiers = ReadModifiers();
        if (IsWord("class") || IsWord("interface") || (IsWord("object") && Next.Kind == TokenKind.Identifier) || (IsWord("object") && modifiers.Contains("companion")))
        {
            if (modifiers.Contains("companion") && owner is not null)
            {
                ReadCompanion(owner, declaringTypes);
            }
            else
            {
                ReadType(modifiers, declaringTypes);
            }
        }
        else if (IsWord("fun"))
        {
            ReadFunction(owner, modifiers, inCompanion);
        }
        else if (IsWord("val") || IsWord("var"))
        {
            ReadProperty(owner, modifiers, inCompanion);
        }
        else if (IsWord("constructor") && Next.Is("(") && owner is not null)
        {
            ReadSecondaryConstructor(owner, modifiers);
        }
        else if (IsWord("init") && Next.Is("{"))
        {
            loss.InitBlocks++;
            _i = CLikeTokenizer.Matching(tokens, _i + 1) + 1;
        }
        else if (IsWord("typealias"))
        {
            loss.TypeAliases++;
            SkipStatement();
        }
    }

    private void ReadType(HashSet<string> modifiers, List<string> declaringTypes)
    {
        var keyword = Current.Text;
        _i++;
        var name = Current.Text;
        _i++;
        if (modifiers.Contains("annotation"))
        {
            loss.AnnotationClasses++;
            SkipTypeRest();
            return;
        }

        var type = new ImportedType
        {
            Namespace = _package,
            Name = name,
            DeclaringTypes = [.. declaringTypes],
            Kind = keyword == "interface" ? ImportedTypeKind.Interface
                : modifiers.Contains("enum") ? ImportedTypeKind.Enumeration
                : modifiers.Contains("abstract") || modifiers.Contains("sealed") ? ImportedTypeKind.AbstractClass
                : ImportedTypeKind.Class,
            Stereotype = keyword == "object" ? "object"
                : modifiers.Contains("data") ? "data"
                : modifiers.Contains("value") || modifiers.Contains("inline") ? "value"
                : modifiers.Contains("sealed") ? "sealed"
                : null,
        };
        if (Current.Is("<"))
        {
            type.TypeParameters = ReadTypeParameters();
        }

        // The primary constructor: [annotations and modifiers] [constructor] (parameters)
        var beforeConstructor = _i;
        SkipAnnotations();
        var constructorModifiers = ReadModifiers();
        var hasKeyword = IsWord("constructor");
        if (hasKeyword)
        {
            _i++;
        }

        if (Current.Is("(") && (hasKeyword || !StartsNewLineAt(_i)))
        {
            ReadPrimaryConstructor(type, Visibility(constructorModifiers), hasKeyword && constructorModifiers.Count > 0);
        }
        else
        {
            _i = beforeConstructor;
        }

        // Supertypes: Entity(), Payable, Repository<Order> by delegate
        if (Current.Is(":"))
        {
            _i++;
            while (!AtEnd)
            {
                SkipAnnotations();
                if (ReadTypeReference() is { Name: not null } reference)
                {
                    type.BaseTypes.Add(reference);
                }

                if (Current.Is("("))
                {
                    _i = CLikeTokenizer.Matching(tokens, _i) + 1;
                }

                if (IsWord("by"))
                {
                    _i++;
                    SkipExpression(stopAtComma: true, stopAtBrace: true);
                }

                if (!Current.Is(","))
                {
                    break;
                }

                _i++;
            }
        }

        if (IsWord("where"))
        {
            loss.TypeBounds++;
            while (!AtEnd && !Current.Is("{") && !StartsNewStatement())
            {
                _i++;
            }
        }

        types.Add(type);
        if (Current.Is("{"))
        {
            _i++;
            ReadBody(type, [.. declaringTypes, name]);
        }
    }

    private void ReadBody(ImportedType type, List<string> nested)
    {
        if (type.Kind == ImportedTypeKind.Enumeration)
        {
            ReadEnumEntries(type);
        }

        while (!AtEnd && !Current.Is("}"))
        {
            var start = _i;
            if (Current.Is(";") || Current.Is(","))
            {
                _i++;
                continue;
            }

            try
            {
                ReadDeclaration(type, nested, inCompanion: false);
            }
            catch (ArgumentOutOfRangeException)
            {
                _i = tokens.Count;
            }

            if (_i == start)
            {
                loss.Unreadable++;
                SkipMember();
            }
        }

        _i++;
    }

    /// <summary>The members of a companion object are the static members of its class.</summary>
    private void ReadCompanion(ImportedType owner, List<string> declaringTypes)
    {
        _i++;
        if (Current.Kind == TokenKind.Identifier && !StartsNewLineAt(_i))
        {
            _i++;
        }

        while (!AtEnd && !Current.Is("{") && !StartsNewStatement())
        {
            _i = Current.Is("(") ? CLikeTokenizer.Matching(tokens, _i) + 1 : _i + 1;
        }

        if (!Current.Is("{"))
        {
            return;
        }

        _i++;
        while (!AtEnd && !Current.Is("}"))
        {
            var start = _i;
            if (Current.Is(";"))
            {
                _i++;
                continue;
            }

            ReadDeclaration(owner, declaringTypes, inCompanion: true);
            if (_i == start)
            {
                loss.Unreadable++;
                SkipMember();
            }
        }

        _i++;
    }

    /// <summary>The constants of an enumeration, with the arguments they pass to its constructor: RED(0xFF0000).</summary>
    private void ReadEnumEntries(ImportedType type)
    {
        while (!AtEnd && !Current.Is("}") && !Current.Is(";"))
        {
            SkipAnnotations();
            if (Current.Kind != TokenKind.Identifier)
            {
                break;
            }

            var name = Current.Text;
            _i++;
            string? value = null;
            if (Current.Is("("))
            {
                var close = CLikeTokenizer.Matching(tokens, _i);
                value = CLikeTokenizer.Join(tokens.Skip(_i + 1).Take(close - _i - 1));
                _i = close + 1;
            }

            if (Current.Is("{"))
            {
                _i = CLikeTokenizer.Matching(tokens, _i) + 1;
            }

            type.Literals.Add(new ImportedLiteral(name, string.IsNullOrEmpty(value) ? null : value));
            if (!Current.Is(","))
            {
                break;
            }

            _i++;
        }

        if (Current.Is(";"))
        {
            _i++;
        }
    }

    #endregion

    #region Members

    /// <summary>
    /// The primary constructor's parameters: those declared with <c>val</c> or <c>var</c> are properties. It is an
    /// operation too when it has other parameters, or a visibility of its own.
    /// </summary>
    private void ReadPrimaryConstructor(ImportedType type, UmlVisibility visibility, bool hasOwnVisibility)
    {
        var parameters = ReadParameters();
        foreach (var parameter in parameters.Where(p => p.IsProperty))
        {
            type.Attributes.Add(new ImportedAttribute { Name = parameter.Name, Type = parameter.Type, Visibility = Visibility(parameter.Modifiers) });
        }

        if (parameters.Any(p => !p.IsProperty) || hasOwnVisibility)
        {
            var constructor = new ImportedOperation { Name = type.Name, Visibility = visibility };
            constructor.Parameters.AddRange(parameters.Select(p => new UmlParameter { Name = p.Name, Type = p.Type?.Text }));
            type.Operations.Add(constructor);
        }
    }

    private void ReadSecondaryConstructor(ImportedType owner, HashSet<string> modifiers)
    {
        _i++;
        var constructor = new ImportedOperation { Name = owner.Name, Visibility = Visibility(modifiers) };
        constructor.Parameters.AddRange(ReadParameters().Select(p => new UmlParameter { Name = p.Name, Type = p.Type?.Text }));

        // : this(…) or : super(…)
        if (Current.Is(":"))
        {
            _i += 2;
            if (Current.Is("("))
            {
                _i = CLikeTokenizer.Matching(tokens, _i) + 1;
            }
        }

        if (Current.Is("{"))
        {
            _i = CLikeTokenizer.Matching(tokens, _i) + 1;
        }

        owner.Operations.Add(constructor);
    }

    private void ReadFunction(ImportedType? owner, HashSet<string> modifiers, bool inCompanion)
    {
        _i++;
        var generic = Current.Is("<") ? $"<{string.Join(", ", ReadTypeParameters())}>" : string.Empty;

        // The name is the last word before the parameters; a receiver before it (String.) makes it an extension.
        var (name, extension) = ReadMemberName("(");
        if (name is null)
        {
            return;
        }

        var parameters = ReadParameters();
        string? returnType = null;
        if (Current.Is(":"))
        {
            _i++;
            returnType = ReadTypeReference()?.Text;
        }

        if (IsWord("where"))
        {
            loss.TypeBounds++;
            while (!AtEnd && !Current.Is("{") && !Current.Is("=") && !StartsNewStatement())
            {
                _i++;
            }
        }

        if (Current.Is("{"))
        {
            _i = CLikeTokenizer.Matching(tokens, _i) + 1;
        }
        else if (Current.Is("="))
        {
            _i++;
            SkipExpression();
        }

        if (owner is null || extension)
        {
            (extension ? ref loss.Extensions : ref loss.TopLevel)++;
            return;
        }

        var operation = new ImportedOperation
        {
            Name = name + generic,
            ReturnType = returnType,
            Visibility = Visibility(modifiers),
            IsStatic = inCompanion,
            IsAbstract = modifiers.Contains("abstract"),
        };
        operation.Parameters.AddRange(parameters.Select(p => new UmlParameter { Name = p.Name, Type = p.Type?.Text }));
        owner.Operations.Add(operation);
    }

    private void ReadProperty(ImportedType? owner, HashSet<string> modifiers, bool inCompanion)
    {
        _i++;
        if (Current.Is("<"))
        {
            SkipAngles();
        }

        var (name, extension) = ReadMemberName(":", "=", "by");
        TypeRef? propertyType = null;
        if (Current.Is(":"))
        {
            _i++;
            propertyType = ReadTypeReference();
        }

        string? value = null;
        if (Current.Is("="))
        {
            _i++;
            var start = _i;
            SkipExpression();
            value = CLikeTokenizer.Join(tokens.Skip(start).Take(_i - start));
        }
        else if (IsWord("by"))
        {
            _i++;
            SkipExpression();
        }

        SkipAccessors();
        if (name is null)
        {
            return;
        }

        if (owner is null || extension)
        {
            (extension ? ref loss.Extensions : ref loss.TopLevel)++;
            return;
        }

        owner.Attributes.Add(new ImportedAttribute
        {
            Name = name,
            Type = propertyType,
            Visibility = Visibility(modifiers),
            IsStatic = inCompanion,
            DefaultValue = value,
        });
    }

    /// <summary>
    /// The name of a function or property: the last word before one of <paramref name="ends"/> (or the end of the
    /// line), which is an extension when a receiver type and a dot come before it, e.g. <c>String.shout</c>.
    /// </summary>
    private (string? Name, bool Extension) ReadMemberName(params string[] ends)
    {
        var k = _i;
        var name = -1;
        var dotted = false;
        while (k < tokens.Count && !ends.Contains(At(k).Text) && (k == _i || !StartsNewLineAt(k)))
        {
            if (At(k).Kind == TokenKind.Identifier)
            {
                // A word follows a dot or comes first: in "String.shouted get()" the name is shouted, not get.
                if (name >= 0 && !At(k - 1).Is("."))
                {
                    break;
                }

                name = k;
                k++;
            }
            else if (At(k).Is("<"))
            {
                var depth = 0;
                do
                {
                    depth += At(k).Is("<") ? 1 : At(k).Is(">") ? -1 : 0;
                    k++;
                }
                while (k < tokens.Count && depth > 0);
            }
            else if (At(k).Is("."))
            {
                dotted = true;
                k++;
            }
            else if (At(k).Is("?"))
            {
                k++;
            }
            else
            {
                break;
            }
        }

        if (name < 0)
        {
            return (null, false);
        }

        _i = k;
        return (tokens[name].Text, dotted);
    }

    /// <summary>Getters and setters after a property: <c>get() = …</c>, <c>private set</c>, <c>set(value) { … }</c>.</summary>
    private void SkipAccessors()
    {
        while (!AtEnd)
        {
            var start = _i;
            SkipAnnotations(count: false);
            ReadModifiers();
            if (!(IsWord("get") || IsWord("set")) || Next.Is("."))
            {
                _i = start;
                return;
            }

            _i++;
            if (Current.Is("("))
            {
                _i = CLikeTokenizer.Matching(tokens, _i) + 1;
            }

            if (Current.Is(":"))
            {
                _i++;
                ReadTypeReference();
            }

            if (Current.Is("="))
            {
                loss.Accessors++;
                _i++;
                SkipExpression();
            }
            else if (Current.Is("{"))
            {
                loss.Accessors++;
                _i = CLikeTokenizer.Matching(tokens, _i) + 1;
            }
        }
    }

    /// <summary>A parameter list: <c>(val id: Long, name: String = "", vararg tags: String)</c>.</summary>
    private List<(string Name, TypeRef? Type, bool IsProperty, HashSet<string> Modifiers)> ReadParameters()
    {
        var parameters = new List<(string, TypeRef?, bool, HashSet<string>)>();
        if (!Current.Is("("))
        {
            return parameters;
        }

        var close = CLikeTokenizer.Matching(tokens, _i);
        _i++;
        while (_i < close)
        {
            SkipAnnotations();
            var modifiers = ReadModifiers();
            if (modifiers.Contains("vararg"))
            {
                loss.ParameterDetails++;
            }

            var isProperty = IsWord("val") || IsWord("var");
            if (isProperty)
            {
                _i++;
            }

            var name = "arg";
            if (Current.Kind == TokenKind.Identifier)
            {
                name = Current.Text;
                _i++;
            }

            TypeRef? parameterType = null;
            if (Current.Is(":"))
            {
                _i++;
                parameterType = ReadTypeReference();
            }

            if (Current.Is("="))
            {
                loss.ParameterDetails++;
            }

            // Up to the next parameter, over default values such as listOf(1, 2).
            while (_i < close && !Current.Is(","))
            {
                _i = Current.Is("(") || Current.Is("[") || Current.Is("{") ? CLikeTokenizer.Matching(tokens, _i) + 1 : _i + 1;
            }

            _i++;
            parameters.Add((name, parameterType, isProperty, modifiers));
        }

        _i = close + 1;
        return parameters;
    }

    private List<string> ReadTypeParameters()
    {
        var names = new List<string>();
        var depth = 0;
        while (!AtEnd)
        {
            if (Current.Is("<"))
            {
                depth++;
            }
            else if (Current.Is(">") && --depth == 0)
            {
                _i++;
                break;
            }
            else if (depth == 1 && Current.Kind == TokenKind.Identifier && (At(_i - 1).Is("<") || At(_i - 1).Is(",") ||
                     (At(_i - 1).Kind == TokenKind.Identifier && At(_i - 1).Text is "reified" or "in" or "out")) &&
                     Current.Text is not ("reified" or "in" or "out"))
            {
                names.Add(Current.Text);
            }
            else if (depth == 1 && Current.Is(":"))
            {
                loss.TypeBounds++;
            }

            _i++;
        }

        return names;
    }

    /// <summary>The modifiers before a declaration: a modifier word is followed by another word on the same line.</summary>
    private HashSet<string> ReadModifiers()
    {
        var modifiers = new HashSet<string>();
        while (Current.Kind == TokenKind.Identifier && !StartsNewLineAt(_i + 1) && Next.Kind == TokenKind.Identifier &&
               (Modifiers.Contains(Current.Text) || (IsWord("fun") && Next.Text == "interface")))
        {
            modifiers.Add(Current.Text);
            _i++;
        }

        return modifiers;
    }

    private static UmlVisibility Visibility(HashSet<string> modifiers) =>
        modifiers.Contains("private") ? UmlVisibility.Private
        : modifiers.Contains("protected") ? UmlVisibility.Protected
        : modifiers.Contains("internal") ? UmlVisibility.Package
        : UmlVisibility.Public;

    #endregion

    #region Type expressions

    private readonly record struct Parsed(string? Name = null, int Arity = 0, bool IsMany = false, bool IsOptional = false);

    /// <summary>A type: its text, and the type it refers to through nullable types and collections.</summary>
    private TypeRef? ReadTypeReference()
    {
        var start = _i;
        var parsed = ParseType();
        if (_i == start)
        {
            return null;
        }

        var text = TypeText(start, _i);
        return parsed.Name is null
            ? new TypeRef(text, IsOptional: parsed.IsOptional)
            : new TypeRef(text, parsed.Name, parsed.Arity, parsed.IsMany, parsed.IsOptional);
    }

    private Parsed ParseType()
    {
        while (Current.Is("@"))
        {
            SkipAnnotations(count: false);
        }

        if (IsWord("suspend"))
        {
            _i++;
        }

        Parsed parsed;
        if (Current.Is("("))
        {
            var close = CLikeTokenizer.Matching(tokens, _i);
            if (At(close + 1).Is("->"))
            {
                // A function type: (Int) -> Unit
                _i = close + 2;
                ParseType();
                return new Parsed();
            }

            _i++;
            parsed = ParseType();
            if (Current.Is(")"))
            {
                _i++;
            }
        }
        else if (Current.Kind == TokenKind.Identifier)
        {
            // A name, possibly qualified (java.util.UUID) or nested (Outer<T>.Inner), with type arguments.
            var name = Current.Text;
            _i++;
            var arguments = new List<Parsed>();
            while (true)
            {
                if (Current.Is("<") && !StartsNewLineAt(_i))
                {
                    arguments = ReadTypeArguments();
                }

                if (Current.Is(".") && Next.Kind == TokenKind.Identifier)
                {
                    name = Next.Text;
                    arguments = [];
                    _i += 2;
                    continue;
                }

                if (Current.Is(".") && Next.Is("("))
                {
                    // A function type with a receiver: String.(Int) -> Unit
                    _i = CLikeTokenizer.Matching(tokens, _i + 1) + 1;
                    if (Current.Is("->"))
                    {
                        _i++;
                        ParseType();
                    }

                    return new Parsed();
                }

                break;
            }

            parsed = Primitives.Contains(name) ? new Parsed()
                : Collections.Contains(name) && arguments.Count > 0 ? arguments[^1] with { IsMany = true, IsOptional = false }
                : new Parsed(name, arguments.Count);
        }
        else
        {
            return new Parsed();
        }

        if (Current.Is("?") && !StartsNewLineAt(_i))
        {
            _i++;
            parsed = parsed with { IsOptional = true };
        }

        return parsed;
    }

    /// <summary>Type arguments: <c>&lt;String, Order&gt;</c>, <c>&lt;out T&gt;</c>, <c>&lt;*&gt;</c>.</summary>
    private List<Parsed> ReadTypeArguments()
    {
        var arguments = new List<Parsed>();
        _i++;
        while (!AtEnd && !Current.Is(">"))
        {
            if (Current.Is(","))
            {
                _i++;
                continue;
            }

            if (Current.Is("*"))
            {
                _i++;
                arguments.Add(new Parsed());
                continue;
            }

            if ((IsWord("out") || IsWord("in")) && Next.Kind == TokenKind.Identifier)
            {
                _i++;
            }

            var start = _i;
            arguments.Add(ParseType());
            if (_i == start)
            {
                _i++;
            }
        }

        _i++;
        return arguments;
    }

    /// <summary>A type as written: <c>Map&lt;String, Order&gt;?</c>, <c>(Int) -&gt; Unit</c>.</summary>
    private string TypeText(int start, int end)
    {
        var text = new StringBuilder();
        for (var k = start; k < end; k++)
        {
            var token = tokens[k];
            if (k > start && NeedsSpace(tokens[k - 1], token))
            {
                text.Append(' ');
            }

            text.Append(token.Text);
        }

        return text.ToString();

        static bool NeedsSpace(Token previous, Token next) =>
            previous.Is(",") || previous.Is("->") || next.Is("->") ||
            (previous.Kind == TokenKind.Identifier && previous.Text is "suspend" or "out" or "in") ||
            (previous.Kind != TokenKind.Symbol && next.Kind != TokenKind.Symbol);
    }

    #endregion

    #region Skipping

    /// <summary>Annotations: <c>@Serializable</c>, <c>@field:Json(name = "id")</c>, <c>@[A B]</c>.</summary>
    private void SkipAnnotations(bool count = true)
    {
        while (Current.Is("@"))
        {
            if (count)
            {
                loss.Annotations++;
            }

            _i++;
            if (Current.Is("["))
            {
                _i = CLikeTokenizer.Matching(tokens, _i) + 1;
                continue;
            }

            if (Current.Kind == TokenKind.Identifier && Next.Is(":"))
            {
                // A use-site target: field:, get:, param:, file:
                _i += 2;
            }

            ReadDottedName();
            if (Current.Is("<"))
            {
                SkipAngles();
            }

            if (Current.Is("(") && !StartsNewLineAt(_i))
            {
                _i = CLikeTokenizer.Matching(tokens, _i) + 1;
            }
        }
    }

    /// <summary>The rest of a type that is left out: its primary constructor, supertypes and body.</summary>
    private void SkipTypeRest()
    {
        while (!AtEnd && !Current.Is("{") && !StartsNewStatement())
        {
            _i = Current.Is("(") || Current.Is("[") ? CLikeTokenizer.Matching(tokens, _i) + 1 : _i + 1;
        }

        if (Current.Is("{"))
        {
            _i = CLikeTokenizer.Matching(tokens, _i) + 1;
        }
    }

    private string ReadDottedName()
    {
        var name = new StringBuilder();
        if (Current.Kind == TokenKind.Identifier)
        {
            name.Append(Current.Text);
            _i++;
        }

        while (Current.Is(".") && Next.Kind == TokenKind.Identifier)
        {
            name.Append('.').Append(Next.Text);
            _i += 2;
        }

        return name.ToString();
    }

    private void SkipAngles()
    {
        var depth = 0;
        while (!AtEnd)
        {
            if (Current.Is("<"))
            {
                depth++;
            }
            else if (Current.Is(">") && --depth == 0)
            {
                _i++;
                return;
            }
            else if (Current.Is("(") || Current.Is("[") || Current.Is("{"))
            {
                _i = CLikeTokenizer.Matching(tokens, _i) + 1;
                continue;
            }

            _i++;
        }
    }

    /// <summary>A member that could not be read: up to its end, including a body.</summary>
    private void SkipMember()
    {
        var start = _i;
        SkipExpression();
        if (Current.Is("{"))
        {
            _i = CLikeTokenizer.Matching(tokens, _i) + 1;
        }

        if (Current.Is(";"))
        {
            _i++;
        }

        if (_i == start)
        {
            _i++;
        }
    }

    private void SkipStatement()
    {
        SkipExpression();
        if (Current.Is(";"))
        {
            _i++;
        }
    }

    /// <summary>
    /// An expression (or the rest of a statement), over brackets and lambdas, up to <c>;</c>, a closing bracket, a
    /// comma when <paramref name="stopAtComma"/>, a brace when <paramref name="stopAtBrace"/>, or a line break where the
    /// declaration ends.
    /// </summary>
    private void SkipExpression(bool stopAtComma = false, bool stopAtBrace = false)
    {
        var start = _i;
        while (!AtEnd)
        {
            if (_i > start && StartsNewStatement())
            {
                return;
            }

            if (Current.Is(";") || Current.Is(")") || Current.Is("]") || Current.Is("}") ||
                (stopAtComma && Current.Is(",")) || (stopAtBrace && Current.Is("{")))
            {
                return;
            }

            _i = Current.Is("(") || Current.Is("[") || Current.Is("{") ? CLikeTokenizer.Matching(tokens, _i) + 1 : _i + 1;
        }
    }

    #endregion

    #region Tokens

    private Token At(int index) => index >= 0 && index < tokens.Count ? tokens[index] : new Token(TokenKind.Symbol, "", 0);

    private bool IsWord(string word) => Current.Kind == TokenKind.Identifier && Current.Text == word;

    /// <summary>The token at <paramref name="index"/> is on a later line than the one before it.</summary>
    private bool StartsNewLineAt(int index) =>
        index > 0 && index < tokens.Count && tokens[index].Line > KotlinTokenizer.EndLine(tokens[index - 1]);

    /// <summary>The current token starts a new declaration: it is on a new line, and the code does not go on.</summary>
    private bool StartsNewStatement()
    {
        if (!StartsNewLineAt(_i))
        {
            return false;
        }

        var previous = At(_i - 1);
        return !(previous.Kind != TokenKind.String && ContinueAfter.Contains(previous.Text)) && !ContinueWith.Contains(Current.Text);
    }

    #endregion
}
