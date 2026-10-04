using System.Text;
using UmlWizard.CodeImport;
using UmlWizard.Core.Model;

namespace UmlWizard.CodeImport.TypeScript;

/// <summary>
/// A declaration parser for one TypeScript file. Statements and members may end at a line break instead of a
/// semicolon, so a line break ends one unless the code clearly goes on (after an operator, or before one).
/// </summary>
internal sealed class TypeScriptParser(List<Token> tokens, string fileName, List<ImportedType> types, TypeScriptCodeImporter.Losses loss)
{
    private static readonly HashSet<string> MemberModifiers =
        ["public", "private", "protected", "static", "readonly", "abstract", "declare", "override", "accessor", "async"];

    /// <summary>Tokens after which a word is a name, not a modifier: <c>static: number</c>, <c>readonly()</c>.</summary>
    private static readonly HashSet<string> NotAfterModifier = ["(", ":", "=", ";", "?", "!", "<", "}", ",", ")"];

    /// <summary>Types that are not classes of the code.</summary>
    private static readonly HashSet<string> Primitives =
        ["string", "number", "boolean", "bigint", "symbol", "any", "unknown", "void", "never", "object", "this", "true", "false"];

    /// <summary>Generic types whose last type argument is the element of a collection (the value of a map).</summary>
    private static readonly HashSet<string> Collections =
    [
        "Array", "ReadonlyArray", "Set", "ReadonlySet", "WeakSet", "Map", "ReadonlyMap", "WeakMap", "Record", "Iterable",
        "IterableIterator", "AsyncIterable", "ArrayLike",
    ];

    /// <summary>Generic types that stand for their type argument.</summary>
    private static readonly HashSet<string> LookThrough = ["Partial", "Readonly", "Required", "NonNullable"];

    /// <summary>
    /// Tokens after which the code goes on on the next line. Not <c>&gt;</c>: a line that ends with one mostly ends
    /// with type arguments, e.g. <c>type Orders = Array&lt;Order&gt;</c>.
    /// </summary>
    private static readonly HashSet<string> ContinueAfter =
    [
        "=", "+", "-", "*", "/", "%", "&", "|", "^", "!", "~", "?", ":", ",", ".", "<", "=>", "(", "[", "{", "...",
        "new", "typeof", "in", "instanceof", "keyof", "extends", "implements", "from", "as", "satisfies",
    ];

    /// <summary>Tokens with which the code on a new line goes on from the previous one.</summary>
    private static readonly HashSet<string> ContinueWith =
    [
        ".", "?", ":", "+", "*", "/", "%", "&", "|", "^", "=", ">", "<", "=>", ",", ")", "]", "}", "as", "satisfies",
        "instanceof", "in", "from", "extends", "implements",
    ];

    private readonly List<string> _namespace = [];
    private int _i;

    private Token Current => At(_i);

    private Token Next => At(_i + 1);

    private bool AtEnd => _i >= tokens.Count;

    private string Namespace => string.Join('.', _namespace);

    public void ParseFile() => ParseStatements(inBlock: false);

    #region Statements

    private void ParseStatements(bool inBlock)
    {
        while (!AtEnd)
        {
            if (inBlock && Current.Is("}"))
            {
                _i++;
                return;
            }

            var start = _i;
            ParseStatement();
            if (_i == start)
            {
                _i++;
            }
        }
    }

    private void ParseStatement()
    {
        if (Current.Is(";"))
        {
            _i++;
            return;
        }

        if (Current.Is("{"))
        {
            _i = CLikeTokenizer.Matching(tokens, _i) + 1;
            return;
        }

        if (Current.Is("@"))
        {
            SkipDecorator();
            return;
        }

        if (IsWord("import") && !Next.Is("(") && !Next.Is("."))
        {
            SkipModuleClause();
            return;
        }

        if (IsWord("export"))
        {
            _i++;
            if (Current.Is("{") || Current.Is("*") || IsWord("import"))
            {
                SkipModuleClause();
                return;
            }

            if (Current.Is("=") || IsWord("as"))
            {
                SkipStatement();
                return;
            }

            if (IsWord("default"))
            {
                _i++;
                if (!(IsWord("class") || IsWord("abstract") || IsWord("interface") || IsWord("function") || IsWord("async")))
                {
                    SkipStatement();
                    return;
                }
            }
        }

        var isAbstract = false;
        while (true)
        {
            if (IsWord("declare"))
            {
                _i++;
            }
            else if (IsWord("abstract") && IsWordAt(_i + 1, "class"))
            {
                isAbstract = true;
                _i++;
            }
            else if (IsWord("const") && IsWordAt(_i + 1, "enum"))
            {
                _i++;
            }
            else
            {
                break;
            }
        }

        if (IsWord("class"))
        {
            ReadClass(isAbstract);
        }
        else if (IsWord("interface") && Next.Kind == TokenKind.Identifier)
        {
            ReadInterface();
        }
        else if (IsWord("enum") && Next.Kind == TokenKind.Identifier)
        {
            ReadEnum();
        }
        else if ((IsWord("namespace") || IsWord("module")) && Next.Kind is TokenKind.Identifier or TokenKind.String && !StartsNewLineAt(_i + 1))
        {
            ReadNamespace();
        }
        else if (IsWord("global") && Next.Is("{"))
        {
            _i += 2;
            ParseStatements(inBlock: true);
        }
        else if (IsWord("function") || (IsWord("async") && IsWordAt(_i + 1, "function")))
        {
            loss.Functions++;
            SkipFunction();
        }
        else if (IsWord("type") && Next.Kind == TokenKind.Identifier && !StartsNewLineAt(_i + 1))
        {
            loss.TypeAliases++;
            _i += 2;
            if (Current.Is("<"))
            {
                SkipAngles();
            }

            if (Current.Is("="))
            {
                _i++;
                ReadType();
            }

            SkipSemicolon();
        }
        else if (IsWord("const") || IsWord("let") || IsWord("var"))
        {
            loss.Functions++;
            SkipStatement();
        }
        else
        {
            SkipStatement();
        }
    }

    private void ReadNamespace()
    {
        _i++;
        string name;
        if (Current.Kind == TokenKind.String)
        {
            name = Current.Text.Trim('\'', '"', '`');
            _i++;
        }
        else
        {
            name = Current.Text;
            _i++;
            while (Current.Is(".") && Next.Kind == TokenKind.Identifier)
            {
                name += "." + Next.Text;
                _i += 2;
            }
        }

        if (!Current.Is("{"))
        {
            SkipSemicolon();
            return;
        }

        _i++;
        _namespace.Add(name);
        ParseStatements(inBlock: true);
        _namespace.RemoveAt(_namespace.Count - 1);
    }

    #endregion

    #region Types

    private void ReadClass(bool isAbstract)
    {
        _i++;
        var name = fileName;
        if (Current.Kind == TokenKind.Identifier && !IsWord("extends") && !IsWord("implements"))
        {
            name = Current.Text;
            _i++;
        }

        var type = new ImportedType
        {
            Namespace = Namespace,
            Name = name,
            Kind = isAbstract ? ImportedTypeKind.AbstractClass : ImportedTypeKind.Class,
        };
        if (Current.Is("<"))
        {
            type.TypeParameters = ReadTypeParameters();
        }

        // extends and implements, up to the body; a base class may be an expression, e.g. Mixin(Base).
        while (!AtEnd && !Current.Is("{"))
        {
            if (IsWord("extends") || IsWord("implements"))
            {
                _i++;
                while (true)
                {
                    if (ReadType() is { Name: not null } reference)
                    {
                        type.BaseTypes.Add(reference);
                    }

                    if (!Current.Is(","))
                    {
                        break;
                    }

                    _i++;
                }
            }
            else
            {
                _i = Current.Is("(") ? CLikeTokenizer.Matching(tokens, _i) + 1 : _i + 1;
            }
        }

        types.Add(type);
        _i++;
        ReadClassBody(type);
    }

    private void ReadClassBody(ImportedType type)
    {
        var overloaded = new HashSet<string>();
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
                ReadClassMember(type, overloaded);
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

    private void ReadClassMember(ImportedType type, HashSet<string> overloaded)
    {
        while (Current.Is("@"))
        {
            SkipDecorator();
        }

        if (IsWord("static") && Next.Is("{"))
        {
            loss.StaticBlocks++;
            _i = CLikeTokenizer.Matching(tokens, _i + 1) + 1;
            return;
        }

        var modifiers = ReadModifiers();
        var visibility = Visibility(modifiers);
        var isStatic = modifiers.Contains("static");
        if (Current.Is("*"))
        {
            _i++;
        }

        if ((IsWord("get") || IsWord("set")) && IsMemberNameAt(_i + 1))
        {
            _i++;
            ReadAccessor(type, visibility, isStatic, interfaceMember: false);
            return;
        }

        if (IsWord("constructor") && Next.Is("("))
        {
            _i++;
            ReadConstructor(type, visibility, overloaded);
            return;
        }

        if (ReadMemberName(ref visibility) is not { } name)
        {
            return;
        }

        var optional = Current.Is("?");
        if (Current.Is("?") || Current.Is("!"))
        {
            _i++;
        }

        if (Current.Is("(") || Current.Is("<"))
        {
            ReadMethod(type, name, visibility, isStatic, modifiers.Contains("abstract"), overloaded);
            return;
        }

        ReadProperty(type, name, visibility, isStatic, optional);
    }

    private void ReadInterface()
    {
        _i++;
        var type = new ImportedType { Namespace = Namespace, Name = Current.Text, Kind = ImportedTypeKind.Interface };
        _i++;
        if (Current.Is("<"))
        {
            type.TypeParameters = ReadTypeParameters();
        }

        if (IsWord("extends"))
        {
            _i++;
            while (true)
            {
                if (ReadType() is { Name: not null } reference)
                {
                    type.BaseTypes.Add(reference);
                }

                if (!Current.Is(","))
                {
                    break;
                }

                _i++;
            }
        }

        while (!AtEnd && !Current.Is("{"))
        {
            _i++;
        }

        types.Add(type);
        _i++;
        ReadTypeMembers(type);
    }

    /// <summary>The members of an interface: property and method signatures.</summary>
    private void ReadTypeMembers(ImportedType type)
    {
        var overloaded = new HashSet<string>();
        while (!AtEnd && !Current.Is("}"))
        {
            var start = _i;
            if (Current.Is(";") || Current.Is(","))
            {
                _i++;
                continue;
            }

            if (Current.Is("(") || Current.Is("<") || (IsWord("new") && Next.Is("(") || IsWord("new") && Next.Is("<")))
            {
                loss.CallSignatures++;
                SkipMember();
                continue;
            }

            ReadModifiers();
            var visibility = UmlVisibility.Public;
            if ((IsWord("get") || IsWord("set")) && IsMemberNameAt(_i + 1))
            {
                _i++;
                ReadAccessor(type, visibility, isStatic: false, interfaceMember: true);
            }
            else if (ReadMemberName(ref visibility) is { } name)
            {
                var optional = Current.Is("?");
                if (optional)
                {
                    _i++;
                }

                if (Current.Is("(") || Current.Is("<"))
                {
                    ReadMethod(type, name, UmlVisibility.Public, isStatic: false, isAbstract: false, overloaded);
                }
                else
                {
                    ReadProperty(type, name, UmlVisibility.Public, isStatic: false, optional);
                }
            }

            if (_i == start)
            {
                loss.Unreadable++;
                SkipMember();
            }
        }

        _i++;
    }

    private void ReadEnum()
    {
        _i++;
        var type = new ImportedType { Namespace = Namespace, Name = Current.Text, Kind = ImportedTypeKind.Enumeration };
        _i++;
        while (!AtEnd && !Current.Is("{"))
        {
            _i++;
        }

        _i++;
        types.Add(type);
        while (!AtEnd && !Current.Is("}"))
        {
            if (Current.Is(","))
            {
                _i++;
                continue;
            }

            var name = Current.Kind == TokenKind.String ? Current.Text[1..^1] : Current.Text;
            _i++;
            string? value = null;
            if (Current.Is("="))
            {
                _i++;
                var start = _i;
                SkipExpression(stopAtComma: true);
                value = CLikeTokenizer.Join(tokens.Skip(start).Take(_i - start));
            }

            type.Literals.Add(new ImportedLiteral(name, string.IsNullOrEmpty(value) ? null : value));
            if (!Current.Is(",") && !Current.Is("}"))
            {
                _i++;
            }
        }

        _i++;
    }

    #endregion

    #region Members

    /// <summary>The words before a member that are modifiers, not its name.</summary>
    private HashSet<string> ReadModifiers()
    {
        var modifiers = new HashSet<string>();
        while (Current.Kind == TokenKind.Identifier && MemberModifiers.Contains(Current.Text) &&
               !StartsNewLineAt(_i + 1) && !NotAfterModifier.Contains(Next.Text) && _i + 1 < tokens.Count)
        {
            modifiers.Add(Current.Text);
            _i++;
        }

        return modifiers;
    }

    /// <summary>
    /// The name of a member: a word, a string, a number, <c>#private</c> (which makes it private) or a [computed]
    /// name; null when there is none. An index signature <c>[key: string]: T</c> is skipped and reported.
    /// </summary>
    private string? ReadMemberName(ref UmlVisibility visibility)
    {
        if (Current.Is("["))
        {
            var close = CLikeTokenizer.Matching(tokens, _i);
            if (close > _i + 2 && At(_i + 1).Kind == TokenKind.Identifier && At(_i + 2).Is(":"))
            {
                loss.IndexSignatures++;
                _i = close + 1;
                if (Current.Is(":"))
                {
                    _i++;
                    ReadType();
                }

                SkipSemicolon();
                return null;
            }

            var computed = CLikeTokenizer.Join(tokens.Skip(_i + 1).Take(close - _i - 1));
            _i = close + 1;
            return $"[{computed}]";
        }

        if (Current.Kind is not (TokenKind.Identifier or TokenKind.String or TokenKind.Number))
        {
            return null;
        }

        var name = Current.Kind == TokenKind.String ? Current.Text[1..^1] : Current.Text;
        _i++;
        if (name.StartsWith('#'))
        {
            visibility = UmlVisibility.Private;
            name = name[1..];
        }

        return name;
    }

    private void ReadProperty(ImportedType type, string name, UmlVisibility visibility, bool isStatic, bool optional)
    {
        TypeRef? propertyType = null;
        if (Current.Is(":"))
        {
            _i++;
            propertyType = ReadType();
            if (optional && propertyType is not null)
            {
                propertyType = propertyType with { IsOptional = true };
            }
        }

        string? value = null;
        if (Current.Is("="))
        {
            _i++;
            var start = _i;
            SkipExpression();
            value = CLikeTokenizer.Join(tokens.Skip(start).Take(_i - start));
        }

        SkipSemicolon();
        type.Attributes.Add(new ImportedAttribute { Name = name, Type = propertyType, Visibility = visibility, IsStatic = isStatic, DefaultValue = value });
    }

    /// <summary><c>get total(): number { … }</c> and <c>set total(value: number) { … }</c> are a property.</summary>
    private void ReadAccessor(ImportedType type, UmlVisibility visibility, bool isStatic, bool interfaceMember)
    {
        if (ReadMemberName(ref visibility) is not { } name)
        {
            return;
        }

        TypeRef? propertyType = null;
        if (Current.Is("("))
        {
            var parameters = ReadParameters(type, constructor: false, out var parameterTypes);
            propertyType = parameters.Count > 0 ? parameterTypes[0] : null;
        }

        if (Current.Is(":"))
        {
            _i++;
            propertyType = ReadType() ?? propertyType;
        }

        if (Current.Is("{") && !interfaceMember)
        {
            _i = CLikeTokenizer.Matching(tokens, _i) + 1;
        }

        SkipSemicolon();
        type.Attributes.Add(new ImportedAttribute { Name = name, Type = propertyType, Visibility = visibility, IsStatic = isStatic });
    }

    private void ReadConstructor(ImportedType type, UmlVisibility visibility, HashSet<string> overloaded)
    {
        var operation = new ImportedOperation { Name = type.Name, Visibility = visibility };
        operation.Parameters.AddRange(ReadParameters(type, constructor: true, out _));
        if (EndOfSignature(overloaded, "constructor", isAbstract: false))
        {
            type.Operations.Add(operation);
        }
    }

    private void ReadMethod(ImportedType type, string name, UmlVisibility visibility, bool isStatic, bool isAbstract, HashSet<string> overloaded)
    {
        var generic = Current.Is("<") ? $"<{string.Join(", ", ReadTypeParameters())}>" : string.Empty;
        var operation = new ImportedOperation { Name = name + generic, Visibility = visibility, IsStatic = isStatic, IsAbstract = isAbstract };
        operation.Parameters.AddRange(ReadParameters(type, constructor: false, out _));
        if (Current.Is(":"))
        {
            _i++;
            operation.ReturnType = ReadType()?.Text;
        }

        if (EndOfSignature(overloaded, name, isAbstract))
        {
            type.Operations.Add(operation);
        }
    }

    /// <summary>
    /// Skips the body (or the end) of a method and tells whether to keep it: overload signatures are kept, and the
    /// implementation that follows them is not, as its signature is not meant to be called.
    /// </summary>
    private bool EndOfSignature(HashSet<string> overloaded, string name, bool isAbstract)
    {
        if (Current.Is("{"))
        {
            _i = CLikeTokenizer.Matching(tokens, _i) + 1;
            return !overloaded.Contains(name);
        }

        SkipSemicolon();
        if (!isAbstract)
        {
            overloaded.Add(name);
        }

        return true;
    }

    /// <summary>
    /// The parameters; in a constructor, a parameter with a visibility or <c>readonly</c> (a parameter property) also
    /// declares a property.
    /// </summary>
    private List<UmlParameter> ReadParameters(ImportedType type, bool constructor, out List<TypeRef?> parameterTypes)
    {
        var parameters = new List<UmlParameter>();
        parameterTypes = [];
        var close = CLikeTokenizer.Matching(tokens, _i);
        _i++;
        while (_i < close)
        {
            while (Current.Is("@"))
            {
                SkipDecorator();
            }

            var modifiers = ReadModifiers();
            if (Current.Is("..."))
            {
                loss.ParameterDetails++;
                _i++;
            }

            var name = "arg";
            if (Current.Kind == TokenKind.Identifier)
            {
                name = Current.Text;
                _i++;
            }
            else if (Current.Is("{") || Current.Is("["))
            {
                _i = CLikeTokenizer.Matching(tokens, _i) + 1;
            }

            if (Current.Is("?"))
            {
                loss.ParameterDetails++;
                _i++;
            }

            TypeRef? parameterType = null;
            if (Current.Is(":"))
            {
                _i++;
                parameterType = ReadType();
            }

            if (Current.Is("="))
            {
                loss.ParameterDetails++;
            }

            // Up to the next parameter, over default values such as [1, 2] or new Money(0, 'EUR').
            while (_i < close && !Current.Is(","))
            {
                _i = Current.Is("(") || Current.Is("[") || Current.Is("{") ? CLikeTokenizer.Matching(tokens, _i) + 1 : _i + 1;
            }

            _i++;
            if (name == "this")
            {
                // A type for "this", not a parameter.
                continue;
            }

            parameters.Add(new UmlParameter { Name = name, Type = parameterType?.Text });
            parameterTypes.Add(parameterType);
            if (constructor && modifiers.Overlaps(["public", "private", "protected", "readonly", "override"]))
            {
                type.Attributes.Add(new ImportedAttribute { Name = name, Type = parameterType, Visibility = Visibility(modifiers) });
            }
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
            else if (Current.Is("(") || Current.Is("[") || Current.Is("{"))
            {
                _i = CLikeTokenizer.Matching(tokens, _i) + 1;
                continue;
            }
            else if (depth == 1 && Current.Kind == TokenKind.Identifier && (At(_i - 1).Is("<") || At(_i - 1).Is(",")) && !IsWord("const") && !IsWord("in") && !IsWord("out"))
            {
                names.Add(Current.Text);
            }
            else if (depth == 1 && IsWord("extends"))
            {
                loss.TypeBounds++;
            }

            _i++;
        }

        return names;
    }

    private static UmlVisibility Visibility(HashSet<string> modifiers) =>
        modifiers.Contains("private") ? UmlVisibility.Private
        : modifiers.Contains("protected") ? UmlVisibility.Protected
        : UmlVisibility.Public;

    #endregion

    #region Type expressions

    /// <summary>What a type expression refers to.</summary>
    private readonly record struct Parsed(string? Name = null, int Arity = 0, bool IsMany = false, bool IsOptional = false, bool IsNullish = false);

    /// <summary>A type: its text, and the type it refers to through arrays, collections and <c>| null</c>.</summary>
    private TypeRef? ReadType()
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

    /// <summary>A union, or a conditional type <c>T extends U ? X : Y</c>.</summary>
    private Parsed ParseType()
    {
        var parsed = ParseUnion();
        if (IsWord("extends") && !StartsNewLineAt(_i))
        {
            _i++;
            ParseUnion();
            if (Current.Is("?"))
            {
                _i++;
                ParseType();
                if (Current.Is(":"))
                {
                    _i++;
                    ParseType();
                }
            }

            return new Parsed();
        }

        return parsed;
    }

    private Parsed ParseUnion()
    {
        if (Current.Is("|"))
        {
            _i++;
        }

        var members = new List<Parsed>();
        var optional = false;
        while (true)
        {
            var start = _i;
            var member = ParseIntersection();
            if (_i == start)
            {
                break;
            }

            if (member.IsNullish)
            {
                optional = true;
            }
            else
            {
                members.Add(member);
            }

            if (!Current.Is("|"))
            {
                break;
            }

            _i++;
        }

        return members.Count == 1 ? members[0] with { IsOptional = members[0].IsOptional || optional }
            : new Parsed(IsOptional: optional, IsNullish: members.Count == 0 && optional);
    }

    private Parsed ParseIntersection()
    {
        var first = ParsePostfix();
        var parts = 1;
        while (Current.Is("&") && !Next.Is("&"))
        {
            _i++;
            ParsePostfix();
            parts++;
        }

        return parts == 1 ? first : new Parsed();
    }

    /// <summary>A type followed by <c>[]</c> (an array) or <c>[K]</c> (an indexed access).</summary>
    private Parsed ParsePostfix()
    {
        var start = _i;
        var parsed = ParsePrimary();
        while (_i > start && Current.Is("[") && !StartsNewLineAt(_i))
        {
            if (Next.Is("]"))
            {
                _i += 2;
                parsed = parsed with { IsMany = true };
            }
            else
            {
                _i = CLikeTokenizer.Matching(tokens, _i) + 1;
                parsed = new Parsed();
            }
        }

        return parsed;
    }

    private Parsed ParsePrimary()
    {
        if (Current.Is("("))
        {
            var close = CLikeTokenizer.Matching(tokens, _i);
            if (At(close + 1).Is("=>"))
            {
                // A function type: (a: T) => R
                _i = close + 2;
                ParseType();
                return new Parsed();
            }

            _i++;
            var inner = ParseType();
            if (Current.Is(")"))
            {
                _i++;
            }

            return inner;
        }

        if (Current.Is("<") || IsWord("new") || (IsWord("abstract") && IsWordAt(_i + 1, "new")))
        {
            // A generic function type or a constructor type: <T>(a: T) => R, new (a: T) => R
            _i += IsWord("abstract") ? 2 : IsWord("new") ? 1 : 0;
            if (Current.Is("<"))
            {
                SkipAngles();
            }

            if (Current.Is("("))
            {
                _i = CLikeTokenizer.Matching(tokens, _i) + 1;
            }

            if (Current.Is("=>"))
            {
                _i++;
                ParseType();
            }

            return new Parsed();
        }

        if (Current.Is("{") || Current.Is("["))
        {
            // An object type or a tuple.
            _i = CLikeTokenizer.Matching(tokens, _i) + 1;
            return new Parsed();
        }

        if (Current.Kind is TokenKind.String or TokenKind.Number || (Current.Is("-") && Next.Kind == TokenKind.Number))
        {
            _i += Current.Is("-") ? 2 : 1;
            return new Parsed();
        }

        if (Current.Kind != TokenKind.Identifier)
        {
            return new Parsed();
        }

        switch (Current.Text)
        {
            case "typeof":
                _i++;
                if (IsWord("import") && Next.Is("("))
                {
                    _i = CLikeTokenizer.Matching(tokens, _i + 1) + 1;
                }

                SkipDottedName();
                return new Parsed();
            case "keyof" or "unique":
                _i++;
                ParsePostfix();
                return new Parsed();
            case "readonly":
                _i++;
                return ParsePostfix();
            case "infer":
                _i += 2;
                return new Parsed();
            case "asserts":
                _i += 2;
                if (IsWord("is"))
                {
                    _i++;
                    ParseType();
                }

                return new Parsed();
            case "import" when Next.Is("("):
                _i = CLikeTokenizer.Matching(tokens, _i + 1) + 1;
                break;
        }

        // A name, possibly qualified (ns.Order), with type arguments.
        var name = Current.Kind == TokenKind.Identifier ? Current.Text : string.Empty;
        if (Current.Kind == TokenKind.Identifier)
        {
            _i++;
        }

        while (Current.Is(".") && Next.Kind == TokenKind.Identifier)
        {
            name = Next.Text;
            _i += 2;
        }

        var arguments = new List<Parsed>();
        if (Current.Is("<") && !StartsNewLineAt(_i))
        {
            _i++;
            while (!AtEnd && !Current.Is(">"))
            {
                if (Current.Is(","))
                {
                    _i++;
                    continue;
                }

                var start = _i;
                arguments.Add(ParseType());
                if (_i == start)
                {
                    _i++;
                }
            }

            _i++;
        }

        if (IsWord("is") && !StartsNewLineAt(_i))
        {
            // A type predicate: value is Order
            _i++;
            ParseType();
            return new Parsed();
        }

        if (name is "null" or "undefined")
        {
            return new Parsed(IsNullish: true);
        }

        if (name.Length == 0 || Primitives.Contains(name))
        {
            return new Parsed();
        }

        if (Collections.Contains(name) && arguments.Count > 0)
        {
            var element = arguments[^1];
            return element with { IsMany = true, IsOptional = false };
        }

        if (LookThrough.Contains(name) && arguments.Count == 1)
        {
            return arguments[0];
        }

        return new Parsed(name, arguments.Count);
    }

    /// <summary>A type as written, with spaces where TypeScript is usually written with them: <c>Order[] | null</c>.</summary>
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
            previous.Is(",") || previous.Is(":") || previous.Is("{") || next.Is("}") ||
            previous.Is("|") || next.Is("|") || previous.Is("&") || next.Is("&") ||
            previous.Is("=>") || next.Is("=>") || previous.Is("?") || next.Is("?") ||
            (previous.Kind != TokenKind.Symbol && next.Kind != TokenKind.Symbol);
    }

    #endregion

    #region Skipping

    /// <summary>An import or export clause: up to its module name (<c>from './order'</c>), or its end.</summary>
    private void SkipModuleClause()
    {
        var start = _i;
        _i++;
        while (!AtEnd && !Current.Is(";"))
        {
            if (Current.Kind == TokenKind.String)
            {
                _i++;
                if (Current.Is(")"))
                {
                    _i++;
                }

                if ((IsWord("with") || IsWord("assert")) && Next.Is("{"))
                {
                    _i = CLikeTokenizer.Matching(tokens, _i + 1) + 1;
                }

                break;
            }

            if (_i > start + 1 && StartsNewStatement())
            {
                break;
            }

            _i = Current.Is("{") ? CLikeTokenizer.Matching(tokens, _i) + 1 : _i + 1;
        }

        SkipSemicolon();
    }

    /// <summary>A function outside a class: its signature and body, or the end of an overload signature.</summary>
    private void SkipFunction()
    {
        if (IsWord("async"))
        {
            _i++;
        }

        _i++;
        if (Current.Is("*"))
        {
            _i++;
        }

        if (Current.Kind == TokenKind.Identifier)
        {
            _i++;
        }

        if (Current.Is("<"))
        {
            SkipAngles();
        }

        if (Current.Is("("))
        {
            _i = CLikeTokenizer.Matching(tokens, _i) + 1;
        }

        if (Current.Is(":"))
        {
            _i++;
            ReadType();
        }

        if (Current.Is("{"))
        {
            _i = CLikeTokenizer.Matching(tokens, _i) + 1;
        }
        else
        {
            SkipSemicolon();
        }
    }

    /// <summary>A decorator: <c>@Injectable()</c>, <c>@Column({ type: 'text' })</c>.</summary>
    private void SkipDecorator()
    {
        loss.Decorators++;
        _i++;
        if (Current.Kind == TokenKind.Identifier)
        {
            _i++;
        }

        SkipDottedName();
        if (Current.Is("(") && !StartsNewLineAt(_i))
        {
            _i = CLikeTokenizer.Matching(tokens, _i) + 1;
        }
    }

    private void SkipDottedName()
    {
        if (Current.Kind == TokenKind.Identifier && !At(_i - 1).Is("."))
        {
            _i++;
        }

        while (Current.Is(".") && Next.Kind == TokenKind.Identifier)
        {
            _i += 2;
        }
    }

    /// <summary>A list of type parameters or arguments, from <c>&lt;</c> to its closing <c>&gt;</c>.</summary>
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
            else if (Current.Is(";"))
            {
                return;
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

        SkipSemicolon();
        if (_i == start)
        {
            _i++;
        }
    }

    private void SkipStatement()
    {
        SkipExpression();
        SkipSemicolon();
    }

    /// <summary>
    /// An expression (or the rest of a statement), over brackets, up to <c>;</c>, a closing bracket, a comma when
    /// <paramref name="stopAtComma"/>, or a line break where the statement ends.
    /// </summary>
    private void SkipExpression(bool stopAtComma = false)
    {
        var start = _i;
        while (!AtEnd)
        {
            if (_i > start && StartsNewStatement())
            {
                return;
            }

            if (Current.Is(";") || Current.Is(")") || Current.Is("]") || Current.Is("}") || (stopAtComma && Current.Is(",")))
            {
                return;
            }

            _i = Current.Is("(") || Current.Is("[") || Current.Is("{") ? CLikeTokenizer.Matching(tokens, _i) + 1 : _i + 1;
        }
    }

    private void SkipSemicolon()
    {
        if (Current.Is(";"))
        {
            _i++;
        }
    }

    #endregion

    #region Tokens

    private Token At(int index) => index >= 0 && index < tokens.Count ? tokens[index] : new Token(TokenKind.Symbol, "", 0);

    private bool IsWord(string word) => Current.Kind == TokenKind.Identifier && Current.Text == word;

    private bool IsWordAt(int index, string word) => At(index).Kind == TokenKind.Identifier && At(index).Text == word;

    /// <summary>The token at <paramref name="index"/> is on a later line than the one before it.</summary>
    private bool StartsNewLineAt(int index) =>
        index > 0 && index < tokens.Count && tokens[index].Line > TypeScriptTokenizer.EndLine(tokens[index - 1]);

    /// <summary>The current token starts a new statement: it is on a new line, and the code does not go on.</summary>
    private bool StartsNewStatement()
    {
        if (!StartsNewLineAt(_i))
        {
            return false;
        }

        var previous = At(_i - 1);
        return !(previous.Kind != TokenKind.String && ContinueAfter.Contains(previous.Text)) && !ContinueWith.Contains(Current.Text);
    }

    /// <summary>A member name follows at <paramref name="index"/> on the same line: <c>get total()</c>, not <c>get()</c>.</summary>
    private bool IsMemberNameAt(int index) =>
        !StartsNewLineAt(index) && (At(index).Kind is TokenKind.Identifier or TokenKind.String or TokenKind.Number || At(index).Is("["));

    #endregion
}
