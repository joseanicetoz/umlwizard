using UmlWizard.CodeImport;
using UmlWizard.Core.Model;

namespace Acme.UmlWizard.PhpImport;

/// <summary>A declaration parser for one PHP file. PHP keywords are case-insensitive; names of variables are not.</summary>
internal sealed class PhpParser(List<Token> tokens, List<ImportedType> types, PhpCodeImporter.Losses loss)
{
    private static readonly HashSet<string> Modifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "public", "protected", "private", "static", "abstract", "final", "readonly", "var",
    };

    /// <summary>Types that are not classes of the code: they never become associations or inheritance.</summary>
    private static readonly HashSet<string> BuiltInTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "int", "float", "string", "bool", "array", "iterable", "callable", "object", "mixed", "void", "never", "null",
        "false", "true", "self", "static", "parent", "integer", "double", "boolean", "resource",
    };

    private string _namespace = string.Empty;
    private int _i;

    private Token Current => _i < tokens.Count ? tokens[_i] : new Token(TokenKind.Symbol, "", 0);

    private Token Next => _i + 1 < tokens.Count ? tokens[_i + 1] : new Token(TokenKind.Symbol, "", 0);

    private bool AtEnd => _i >= tokens.Count;

    public void ParseFile()
    {
        while (!AtEnd)
        {
            if (IsKeyword("namespace") && (Next.Kind == TokenKind.Identifier || Next.Is("{")))
            {
                // namespace App\Models;  or  namespace App\Models { … }  (the braces are read like any other)
                _i++;
                _namespace = Current.Kind == TokenKind.Identifier ? Current.Text.TrimStart('\\') : string.Empty;
                _i += Current.Kind == TokenKind.Identifier ? 1 : 0;
            }
            else if (IsKeyword("use") && Next.Kind == TokenKind.Identifier)
            {
                // Imports: the importer finds types by their simple name, so it does not need them.
                SkipPast(";");
            }
            else if (IsKeyword("function") && (Next.Kind == TokenKind.Identifier || Next.Is("&")))
            {
                loss.Functions++;
                _i++;
                SkipMethodRest();
            }
            else if (!TryTypeDeclaration())
            {
                _i++;
            }
        }
    }

    /// <summary>Reads a class, interface, trait or enum at the current token (after its attributes and modifiers), if there is one.</summary>
    private bool TryTypeDeclaration()
    {
        var start = _i;
        var attributes = loss.Attributes;
        var modifiers = ReadModifiers();
        var keyword = Current.Text.ToLowerInvariant();
        var anonymous = start > 0 && (tokens[start - 1].Is("new") || tokens[start - 1].Is("::") || tokens[start - 1].Is(">"));
        if (keyword is not ("class" or "interface" or "trait" or "enum") || Next.Kind != TokenKind.Identifier || anonymous)
        {
            // Not a declaration (e.g. new class { … } or Order::class): read on from the next token.
            _i = start;
            loss.Attributes = attributes;
            return false;
        }

        _i++;
        var type = new ImportedType
        {
            Namespace = _namespace,
            Name = Current.Text,
            Kind = keyword switch
            {
                "interface" or "trait" => ImportedTypeKind.Interface,
                "enum" => ImportedTypeKind.Enumeration,
                _ when modifiers.Contains("abstract") => ImportedTypeKind.AbstractClass,
                _ => ImportedTypeKind.Class,
            },
            Stereotype = keyword == "trait" ? "trait" : null,
            TaggedValues = TagsFor(modifiers),                             // final class, readonly class
        };
        _i++;

        // extends and implements, up to the body; an enum's backing type (enum Status: string) is skipped.
        while (!AtEnd && !Current.Is("{"))
        {
            if (IsKeyword("extends") || IsKeyword("implements"))
            {
                _i++;
                ReadNames(type.BaseTypes);
            }
            else
            {
                _i++;
            }
        }

        types.Add(type);
        _i++;
        ReadBody(type);
        return true;
    }

    private void ReadBody(ImportedType type)
    {
        while (!AtEnd && !Current.Is("}"))
        {
            var start = _i;
            if (Current.Is(";"))
            {
                _i++;
                continue;
            }

            if (IsKeyword("use"))
            {
                ReadTraitUse(type);
            }
            else if (IsKeyword("case") && type.Kind == ImportedTypeKind.Enumeration)
            {
                ReadCase(type);
            }
            else
            {
                ReadMember(type);
            }

            if (_i == start)
            {
                // Not a declaration we know: skip it.
                loss.Unreadable++;
                SkipStatement();
            }
        }

        _i++;
    }

    /// <summary><c>use Timestamps, SoftDeletes;</c> — the class realizes the traits, which are shown as interfaces.</summary>
    private void ReadTraitUse(ImportedType type)
    {
        _i++;
        ReadNames(type.BaseTypes);
        if (Current.Is("{"))
        {
            loss.TraitRules++;
            _i = CLikeTokenizer.Matching(tokens, _i) + 1;
        }
        else if (Current.Is(";"))
        {
            _i++;
        }
    }

    /// <summary><c>case Paid;</c> or, in a backed enum, <c>case Paid = 'paid';</c></summary>
    private void ReadCase(ImportedType type)
    {
        _i++;
        var name = Current.Text;
        _i++;
        string? value = null;
        if (Current.Is("="))
        {
            _i++;
            value = ReadExpression();
        }

        type.Literals.Add(new ImportedLiteral(name, value));
        _i += Current.Is(";") ? 1 : 0;
    }

    private void ReadMember(ImportedType type)
    {
        var modifiers = ReadModifiers();
        var visibility = modifiers.Contains("private") ? UmlVisibility.Private
            : modifiers.Contains("protected") ? UmlVisibility.Protected
            : UmlVisibility.Public;                                          // PHP members are public unless declared otherwise
        var isStatic = modifiers.Contains("static");

        if (IsKeyword("const"))
        {
            ReadConstants(type, visibility);
        }
        else if (IsKeyword("function"))
        {
            ReadMethod(type, visibility, isStatic, modifiers.Contains("abstract"), TagsFor(modifiers));
        }
        else if (modifiers.Count > 0)
        {
            ReadProperties(type, visibility, isStatic, modifiers);
        }
    }

    /// <summary><c>const LIMIT = 10, NAME = 'x';</c> and, from PHP 8.3, <c>const int LIMIT = 10;</c></summary>
    private void ReadConstants(ImportedType type, UmlVisibility visibility)
    {
        _i++;
        TypeRef? constantType = Current.Kind == TokenKind.Identifier && Next.Kind == TokenKind.Identifier ? ReadType() : null;
        while (Current.Kind == TokenKind.Identifier)
        {
            var name = Current.Text;
            _i++;
            string? value = null;
            if (Current.Is("="))
            {
                _i++;
                value = ReadExpression();
            }

            type.Attributes.Add(new ImportedAttribute { Name = name, Type = constantType, Visibility = visibility, IsStatic = true, DefaultValue = value });
            _i += Current.Is(",") ? 1 : 0;
        }

        _i += Current.Is(";") ? 1 : 0;
    }

    /// <summary><c>public ?Customer $customer = null, $other;</c> A property without a type has none.</summary>
    private void ReadProperties(ImportedType type, UmlVisibility visibility, bool isStatic, HashSet<string> modifiers)
    {
        TypeRef? propertyType = IsVariable ? null : ReadType();
        while (IsVariable)
        {
            var name = Current.Text[1..];
            _i++;
            string? value = null;
            if (Current.Is("="))
            {
                _i++;
                value = ReadExpression();
            }

            if (Current.Is("{"))
            {
                // PHP 8.4 property hooks: { get => …; set { … } }
                loss.Hooks++;
                _i = CLikeTokenizer.Matching(tokens, _i) + 1;
            }

            loss.ArrayProperties += propertyType?.Text.TrimStart('?').ToLowerInvariant() is "array" or "iterable" ? 1 : 0;
            type.Attributes.Add(new ImportedAttribute
            {
                Name = name, Type = propertyType, Visibility = visibility, IsStatic = isStatic, DefaultValue = value, TaggedValues = TagsFor(modifiers),
            });
            _i += Current.Is(",") ? 1 : 0;
        }

        _i += Current.Is(";") ? 1 : 0;
    }

    /// <summary><c>public function total(): float { … }</c>; <c>__construct</c> is the constructor.</summary>
    private void ReadMethod(ImportedType type, UmlVisibility visibility, bool isStatic, bool isAbstract, List<TaggedValue> taggedValues)
    {
        _i++;
        _i += Current.Is("&") ? 1 : 0;
        var name = Current.Text;
        _i++;
        var isConstructor = name.Equals("__construct", StringComparison.OrdinalIgnoreCase);
        var operation = new ImportedOperation
        {
            Name = isConstructor ? type.Name : name,
            Visibility = visibility,
            IsStatic = isStatic,
            IsAbstract = isAbstract,
            TaggedValues = taggedValues,                                   // final function
        };
        if (Current.Is("("))
        {
            operation.Parameters.AddRange(ReadParameters(type, isConstructor));
        }

        if (Current.Is(":") && !isConstructor)
        {
            _i++;
            operation.ReturnType = ReadType().Text;
        }

        SkipMethodRest();
        type.Operations.Add(operation);
    }

    /// <summary>
    /// The parameters, up to the closing parenthesis. In a constructor, a parameter with a visibility or
    /// <c>readonly</c> (constructor promotion) also declares a property.
    /// </summary>
    private List<UmlParameter> ReadParameters(ImportedType type, bool isConstructor)
    {
        var parameters = new List<UmlParameter>();
        var close = CLikeTokenizer.Matching(tokens, _i);
        _i++;
        while (_i < close)
        {
            var modifiers = ReadModifiers();
            TypeRef? parameterType = IsVariable || Current.Is("&") || Current.Is("...") ? null : ReadType();
            if (Current.Is("&") || Current.Is("..."))
            {
                loss.ParameterDetails++;
                _i++;
            }

            if (IsVariable)
            {
                var name = Current.Text[1..];
                parameters.Add(new UmlParameter { Name = name, Type = parameterType?.Text });
                if (isConstructor && modifiers.Count > 0)
                {
                    type.Attributes.Add(new ImportedAttribute
                    {
                        Name = name,
                        Type = parameterType,
                        Visibility = modifiers.Contains("private") ? UmlVisibility.Private
                            : modifiers.Contains("protected") ? UmlVisibility.Protected
                            : UmlVisibility.Public,
                        TaggedValues = TagsFor(modifiers),                 // public readonly int $id
                    });
                }

                _i++;
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
        }

        _i = close + 1;
        return parameters;
    }

    /// <summary>A list of type names: <c>Entity</c>, <c>Payable, \JsonSerializable</c>.</summary>
    private void ReadNames(List<TypeRef> references)
    {
        while (Current.Kind == TokenKind.Identifier)
        {
            references.Add(Reference(Current.Text, Current.Text, optional: false));
            _i++;
            if (!Current.Is(","))
            {
                break;
            }

            _i++;
        }
    }

    /// <summary>A type: <c>int</c>, <c>?Customer</c>, <c>\App\Models\Order</c>, <c>int|string</c>, <c>Countable&amp;Iterator</c>.</summary>
    private TypeRef ReadType()
    {
        var text = string.Empty;
        var names = new List<string>();
        var nullable = false;
        var expectName = true;   // a name follows ?, |, & and ( — the name after a type is the member's
        while (!AtEnd)
        {
            if (Current.Is("?"))
            {
                nullable = true;
            }
            else if (Current.Is("(") || Current.Is("|") || (Current.Is("&") && Next.Kind == TokenKind.Identifier && !Next.Text.StartsWith('$')))
            {
                // Union, intersection and DNF types: (A&B)|null
                expectName = true;
            }
            else if (Current.Is(")") && !expectName)
            {
                // The end of a group of a DNF type
            }
            else if (Current.Kind == TokenKind.Identifier && !IsVariable && expectName)
            {
                names.Add(Current.Text);
                expectName = false;
            }
            else
            {
                break;
            }

            text += Current.Text;
            _i++;
        }

        var types = names.Where(n => !n.Equals("null", StringComparison.OrdinalIgnoreCase)).ToList();
        var optional = nullable || types.Count < names.Count;
        return types.Count == 1 ? Reference(text, types[0], optional) : new TypeRef(text, IsOptional: optional);
    }

    /// <summary>
    /// A type as used: its simple name finds it among the imported types (<c>\App\Models\Order</c> → <c>Order</c>);
    /// built-in types have none.
    /// </summary>
    private static TypeRef Reference(string text, string name, bool optional)
    {
        var simple = name[(name.LastIndexOf('\\') + 1)..];
        return BuiltInTypes.Contains(simple) ? new TypeRef(text, IsOptional: optional) : new TypeRef(text, simple, IsOptional: optional);
    }

    /// <summary>
    /// What UML has no place for among the modifiers, as tagged values: <c>final</c> and <c>readonly</c> become
    /// <c>php.final = true</c> and <c>php.readonly = true</c>, which the PHP code generator sample can read back.
    /// </summary>
    private static List<TaggedValue> TagsFor(HashSet<string> modifiers) =>
    [
        .. modifiers.Contains("final") ? [new TaggedValue { Name = PhpCodeImporter.FinalTag, Value = "true" }] : Array.Empty<TaggedValue>(),
        .. modifiers.Contains("readonly") ? [new TaggedValue { Name = PhpCodeImporter.ReadonlyTag, Value = "true" }] : Array.Empty<TaggedValue>(),
    ];

    /// <summary>Modifiers, skipping attributes (<c>#[…]</c>) and asymmetric visibility (<c>private(set)</c>).</summary>
    private HashSet<string> ReadModifiers()
    {
        var modifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (!AtEnd)
        {
            if (Current.Is("#") && Next.Is("["))
            {
                loss.Attributes++;
                _i = CLikeTokenizer.Matching(tokens, _i + 1) + 1;
            }
            else if (Current.Kind == TokenKind.Identifier && Modifiers.Contains(Current.Text))
            {
                modifiers.Add(Current.Text.ToLowerInvariant());
                _i++;
                if (Current.Is("(") && Next.Text.Equals("set", StringComparison.OrdinalIgnoreCase))
                {
                    _i = CLikeTokenizer.Matching(tokens, _i) + 1;
                }
            }
            else
            {
                break;
            }
        }

        return modifiers;
    }

    /// <summary>An initial value as written, up to the <c>,</c>, <c>;</c> or <c>{</c> that ends it.</summary>
    private string ReadExpression()
    {
        var start = _i;
        while (!AtEnd && !Current.Is(",") && !Current.Is(";") && !Current.Is("{") && !Current.Is("}"))
        {
            _i = Current.Is("(") || Current.Is("[") ? CLikeTokenizer.Matching(tokens, _i) + 1 : _i + 1;
        }

        return CLikeTokenizer.Join(tokens.Skip(start).Take(_i - start));
    }

    /// <summary>After a function's name: its parameters and return type, then its body or <c>;</c>.</summary>
    private void SkipMethodRest()
    {
        while (!AtEnd && !Current.Is("{") && !Current.Is(";"))
        {
            _i = Current.Is("(") ? CLikeTokenizer.Matching(tokens, _i) + 1 : _i + 1;
        }

        _i = Current.Is("{") ? CLikeTokenizer.Matching(tokens, _i) + 1 : _i + 1;
    }

    private void SkipStatement()
    {
        while (!AtEnd && !Current.Is(";") && !Current.Is("{") && !Current.Is("}"))
        {
            _i++;
        }

        if (Current.Is("{"))
        {
            _i = CLikeTokenizer.Matching(tokens, _i) + 1;
        }
        else if (Current.Is(";"))
        {
            _i++;
        }
    }

    private void SkipPast(string text)
    {
        while (!AtEnd && !Current.Is(text))
        {
            _i++;
        }

        _i++;
    }

    private bool IsVariable => Current.Kind == TokenKind.Identifier && Current.Text.StartsWith('$');

    private bool IsKeyword(string keyword) => Current.Kind == TokenKind.Identifier && Current.Text.Equals(keyword, StringComparison.OrdinalIgnoreCase);
}
