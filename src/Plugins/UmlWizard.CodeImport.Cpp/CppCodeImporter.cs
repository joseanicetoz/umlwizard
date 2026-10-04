using UmlWizard.CodeImport;
using UmlWizard.Core.Model;

namespace UmlWizard.CodeImport.Cpp;

/// <summary>
/// Reads C++ declarations in headers and source files: classes, structs («struct»), unions («union») and
/// enumerations in their namespaces, with their data members, member functions and constructors. A class whose
/// functions are all pure virtual and that has no data members is an interface; one with a pure virtual function is
/// abstract. Out-of-class definitions of member functions are skipped: the declarations in the class are read.
/// </summary>
public sealed class CppCodeImporter : ICodeImporter
{
    public string Id => "Cpp";

    public string DisplayName => "C++";

    public IReadOnlyList<string> FileExtensions { get; } = [".h", ".hpp", ".hh", ".hxx", ".cpp", ".cc", ".cxx"];

    public CodeImportParseResult Parse(IReadOnlyList<SourceFile> files)
    {
        var loss = new Losses();
        var types = new List<ImportedType>();
        foreach (var file in files)
        {
            var tokens = CLikeTokenizer.Tokenize(file.Text, preprocessor: true, out var directives);
            loss.Directives += directives;
            new CppParser(tokens, types, loss).ParseFile();
        }

        return new CodeImportParseResult(types, loss.Notes());
    }

    internal sealed class Losses
    {
        public int Directives, Destructors, Operators, Friends, FreeDeclarations, Aliases, Specializations, DefaultArguments, Unreadable;

        public List<string> Notes()
        {
            var notes = new List<string>();
            if (Directives > 0)
            {
                notes.Add("Preprocessor directives (#include, #define, #if, …) were not run: code that macros generate or #if hides may be read differently.");
            }

            CodeImporter.Add(notes, Unreadable, "declaration could not be read and was skipped", "declarations could not be read and were skipped");
            CodeImporter.Add(notes, FreeDeclarations, "function or variable outside a class was left out", "functions and variables outside classes were left out");
            CodeImporter.Add(notes, Destructors, "destructor was left out", "destructors were left out");
            CodeImporter.Add(notes, Operators, "operator was left out", "operators were left out");
            CodeImporter.Add(notes, Friends, "friend declaration was left out", "friend declarations were left out");
            CodeImporter.Add(notes, Aliases, "type alias (typedef, using) was left out", "type aliases (typedef, using) were left out");
            CodeImporter.Add(notes, Specializations, "template specialization was left out", "template specializations were left out");
            CodeImporter.Add(notes, DefaultArguments, "parameter lost its default argument", "parameters lost their default arguments");
            return notes;
        }
    }
}

/// <summary>A declaration parser for one C++ file.</summary>
internal sealed class CppParser(List<Token> tokens, List<ImportedType> types, CppCodeImporter.Losses loss)
{
    private static readonly HashSet<string> Specifiers =
        ["virtual", "static", "inline", "explicit", "constexpr", "consteval", "constinit", "mutable", "extern", "thread_local"];

    private static readonly HashSet<string> Qualifiers = ["const", "volatile", "typename", "struct", "class", "enum", "union"];

    /// <summary>Templates whose first type argument is the element of a collection.</summary>
    private static readonly HashSet<string> Collections =
        ["vector", "list", "deque", "forward_list", "set", "multiset", "unordered_set", "unordered_multiset", "array", "span",
         "queue", "stack", "priority_queue", "initializer_list"];

    /// <summary>Templates whose second type argument is the element (the value of a map).</summary>
    private static readonly HashSet<string> Maps = ["map", "multimap", "unordered_map", "unordered_multimap"];

    /// <summary>Templates that hold at most one value of their first type argument.</summary>
    private static readonly HashSet<string> Optionals = ["unique_ptr", "shared_ptr", "weak_ptr", "optional", "observer_ptr"];

    private static readonly HashSet<string> BuiltIns =
        ["void", "bool", "char", "wchar_t", "char8_t", "char16_t", "char32_t", "short", "int", "long", "float", "double", "unsigned",
         "signed", "auto", "size_t", "ptrdiff_t", "int8_t", "int16_t", "int32_t", "int64_t", "uint8_t", "uint16_t", "uint32_t",
         "uint64_t", "string", "wstring", "string_view", "byte", "nullptr_t"];

    private int _i;

    private Token Current => _i < tokens.Count ? tokens[_i] : new Token(TokenKind.Symbol, "", 0);

    private bool AtEnd => _i >= tokens.Count;

    private Token At(int j) => j < tokens.Count ? tokens[j] : new Token(TokenKind.Symbol, "", 0);

    public void ParseFile()
    {
        while (!AtEnd)
        {
            ParseScope([]);
            if (!AtEnd)
            {
                _i++; // A stray '}'.
            }
        }
    }

    /// <summary>The declarations of a namespace (or the file), up to its closing brace.</summary>
    private void ParseScope(List<string> ns)
    {
        List<string>? template = null;
        while (!AtEnd && !Current.Is("}"))
        {
            var start = _i;
            if (Current.Is("namespace"))
            {
                _i++;
                var names = new List<string>();
                while (Current.Kind == TokenKind.Identifier || Current.Is("::") || Current.Is("inline"))
                {
                    if (Current.Kind == TokenKind.Identifier && !Current.Is("inline"))
                    {
                        names.Add(Current.Text);
                    }

                    _i++;
                }

                if (Current.Is("{"))
                {
                    _i++;
                    ParseScope([.. ns, .. names]);
                    _i++;
                }
                else
                {
                    SkipStatement(); // namespace alias
                }
            }
            else if (Current.Is("extern") && At(_i + 1).Kind == TokenKind.String && At(_i + 2).Is("{"))
            {
                _i += 3;
                ParseScope(ns);
                _i++;
            }
            else if (Current.Is("template"))
            {
                template = ReadTemplateParameters();
                continue;
            }
            else if ((Current.Is("class") || Current.Is("struct") || Current.Is("union")) && IsTypeDefinition())
            {
                ReadClass(ns, [], template);
            }
            else if (Current.Is("enum") && IsTypeDefinition())
            {
                ReadEnum(ns, []);
            }
            else if (Current.Is("using") || Current.Is("typedef"))
            {
                loss.Aliases += Current.Is("typedef") || At(_i + 2).Is("=") ? 1 : 0;
                SkipStatement();
            }
            else if (Current.Is(";"))
            {
                _i++;
            }
            else if (Current.Is("class") || Current.Is("struct") || Current.Is("union") || Current.Is("enum"))
            {
                // A forward declaration (class Customer;): the class is read where it is defined.
                SkipStatement();
            }
            else
            {
                // A function or variable outside any class, or the out-of-class definition of a member (A::f), which
                // the class declares already.
                var qualified = false;
                for (var j = _i; j < tokens.Count && !At(j).Is(";") && !At(j).Is("{") && !At(j).Is("("); j++)
                {
                    qualified |= At(j).Is("::") && At(j + 2).Is("(");
                }

                loss.FreeDeclarations += qualified || Current.Is("static_assert") ? 0 : 1;

                // A function definition: its parameters, then an initializer list (which may hold braces) and its body.
                var open = _i;
                while (open < tokens.Count && !At(open).Is("(") && !At(open).Is(";") && !At(open).Is("{") && !At(open).Is("="))
                {
                    open = At(open).Is("<") ? CLikeTokenizer.Matching(tokens, open) + 1 : open + 1;
                }

                if (At(open).Is("("))
                {
                    _i = CLikeTokenizer.Matching(tokens, open) + 1;
                    SkipFunctionRest();
                }
                else
                {
                    SkipStatement();
                }
            }

            if (_i == start)
            {
                _i++;
            }

            template = null;
        }
    }

    /// <summary>True when the class, struct, union or enum keyword starts a definition (with a body), not a use.</summary>
    private bool IsTypeDefinition()
    {
        for (var j = _i + 1; j < tokens.Count; j++)
        {
            if (At(j).Is("{"))
            {
                return true;
            }

            if (At(j).Is(";") || At(j).Is("(") || At(j).Is("=") || At(j).Is(")"))
            {
                return false;
            }
        }

        return false;
    }

    private void ReadClass(List<string> ns, List<string> declaringTypes, List<string>? template)
    {
        var keyword = Current.Text;
        _i++;

        // The name is the last word before the base list or body: class EXPORT Order final : public Entity {
        string? name = null;
        var specialization = false;
        while (!AtEnd && !Current.Is("{") && !Current.Is(":"))
        {
            if (Current.Is("[") && At(_i + 1).Is("["))
            {
                _i = CLikeTokenizer.Matching(tokens, _i) + 1;
                continue;
            }

            if (Current.Is("<"))
            {
                specialization = true;
                _i = CLikeTokenizer.Matching(tokens, _i) + 1;
                continue;
            }

            if (Current.Kind == TokenKind.Identifier && !Current.Is("final") && !Current.Is("alignas"))
            {
                name = Current.Text;
            }

            _i = Current.Is("(") ? CLikeTokenizer.Matching(tokens, _i) + 1 : _i + 1;
        }

        if (specialization || name is null)
        {
            loss.Specializations += specialization ? 1 : 0;
            SkipStatement();
            return;
        }

        var type = new ImportedType
        {
            Namespace = string.Join("::", ns),
            Name = name,
            DeclaringTypes = [.. declaringTypes],
            TypeParameters = template ?? [],
            Stereotype = keyword is "struct" or "union" ? keyword : null,
        };

        if (Current.Is(":"))
        {
            _i++;
            foreach (var baseTokens in SplitTopLevel(ReadUntilBody()))
            {
                var written = baseTokens.Where(t => !t.Is("public") && !t.Is("protected") && !t.Is("private") && !t.Is("virtual")).ToList();
                if (written.Count > 0)
                {
                    type.BaseTypes.Add(Reference(written));
                }
            }
        }

        _i++; // {
        var counts = ReadBody(type, keyword == "class" ? UmlVisibility.Private : UmlVisibility.Public, [.. declaringTypes, name], ns);
        _i++; // }
        SkipStatement(); // declarators after the body, then ;

        // An interface: no data members and only pure virtual functions, or none but a virtual destructor.
        if (type.Attributes.Count == 0 && counts.Pure == counts.Functions && (counts.Pure > 0 || counts.VirtualDestructor))
        {
            type.Kind = ImportedTypeKind.Interface;
            type.Stereotype = null;
        }
        else if (counts.Pure > 0)
        {
            type.Kind = ImportedTypeKind.AbstractClass;
        }

        types.Add(type);
    }

    private (int Functions, int Pure, bool VirtualDestructor) ReadBody(ImportedType type, UmlVisibility access, List<string> nested, List<string> ns)
    {
        var functions = 0;
        var pure = 0;
        var virtualDestructor = false;
        List<string>? template = null;
        while (!AtEnd && !Current.Is("}"))
        {
            var start = _i;
            if ((Current.Is("public") || Current.Is("protected") || Current.Is("private")) && At(_i + 1).Is(":"))
            {
                access = Current.Text switch
                {
                    "public" => UmlVisibility.Public,
                    "protected" => UmlVisibility.Protected,
                    _ => UmlVisibility.Private,
                };
                _i += 2;
                continue;
            }

            if (Current.Is("template"))
            {
                template = ReadTemplateParameters();
                continue;
            }

            if ((Current.Is("class") || Current.Is("struct") || Current.Is("union")) && IsTypeDefinition())
            {
                ReadClass(ns, nested, template);
            }
            else if (Current.Is("enum") && IsTypeDefinition())
            {
                ReadEnum(ns, nested);
            }
            else if (Current.Is("friend"))
            {
                loss.Friends++;
                SkipStatement();
            }
            else if (Current.Is("using") || Current.Is("typedef"))
            {
                loss.Aliases += Current.Is("typedef") || At(_i + 2).Is("=") ? 1 : 0;
                SkipStatement();
            }
            else if (Current.Is("static_assert"))
            {
                SkipStatement();
            }
            else if (Current.Is(";"))
            {
                _i++;
            }
            else
            {
                switch (ReadMember(type, access, template))
                {
                    case MemberKind.Function:
                        functions++;
                        break;
                    case MemberKind.PureFunction:
                        functions++;
                        pure++;
                        break;
                    case MemberKind.VirtualDestructor:
                        virtualDestructor = true;
                        break;
                }
            }

            if (_i == start)
            {
                loss.Unreadable++;
                SkipStatement();
            }

            template = null;
        }

        return (functions, pure, virtualDestructor);
    }

    private enum MemberKind
    {
        Other,
        Function,
        PureFunction,
        VirtualDestructor,
    }

    private MemberKind ReadMember(ImportedType type, UmlVisibility visibility, List<string>? template)
    {
        var isStatic = false;
        var isVirtual = false;
        while (Specifiers.Contains(Current.Text) || (Current.Is("[") && At(_i + 1).Is("[")))
        {
            isStatic |= Current.Is("static");
            isVirtual |= Current.Is("virtual");
            _i = Current.Is("[") ? CLikeTokenizer.Matching(tokens, _i) + 1 : _i + 1;
        }

        if (Current.Is("~"))
        {
            loss.Destructors++;
            SkipStatement();
            return isVirtual ? MemberKind.VirtualDestructor : MemberKind.Other;
        }

        // The type and name, up to the parameters, an initial value or the end of the declaration.
        var prefix = new List<Token>();
        while (!AtEnd && !Current.Is("(") && !Current.Is(";") && !Current.Is("=") && !Current.Is("{") && !Current.Is(",")
               && !Current.Is("[") && !(Current.Is(":") && prefix.Count > 0) && !Current.Is("}"))
        {
            if (Current.Is("operator"))
            {
                loss.Operators++;
                SkipStatement();
                return MemberKind.Other;
            }

            if (Current.Is("<"))
            {
                var close = CLikeTokenizer.Matching(tokens, _i);
                prefix.AddRange(tokens.Skip(_i).Take(close - _i + 1));
                _i = close + 1;
                continue;
            }

            prefix.Add(Current);
            _i++;
        }

        if (prefix.Count == 0 || prefix[^1].Kind != TokenKind.Identifier)
        {
            SkipStatement();
            return MemberKind.Other;
        }

        var name = prefix[^1].Text;
        var typeTokens = prefix.Take(prefix.Count - 1).ToList();
        var generic = template is { Count: > 0 } ? $"<{string.Join(", ", template)}>" : string.Empty;

        if (Current.Is("("))
        {
            var parameters = ReadParameters();
            var pureVirtual = SkipFunctionRest();
            if (typeTokens.Count == 0 && name == type.Name)
            {
                var constructor = new ImportedOperation { Name = name, Visibility = visibility };
                constructor.Parameters.AddRange(parameters);
                type.Operations.Add(constructor);
                return MemberKind.Other;
            }

            var operation = new ImportedOperation
            {
                Name = name + generic,
                ReturnType = CLikeTokenizer.Join(typeTokens.Where(t => !t.Is("typename"))),
                Visibility = visibility,
                IsStatic = isStatic,
                IsAbstract = isVirtual && pureVirtual,
            };
            operation.Parameters.AddRange(parameters);
            type.Operations.Add(operation);
            return pureVirtual ? MemberKind.PureFunction : MemberKind.Function;
        }

        // Data members: int a = 1, *b, c[4];
        var baseTokens = typeTokens.Where(t => !t.Is("*") && !t.Is("&")).ToList();
        while (true)
        {
            var written = new List<Token>(baseTokens);
            written.AddRange(typeTokens.Skip(baseTokens.Count).Where(t => t.Is("*") || t.Is("&")));
            var reference = Reference(written);
            while (Current.Is("["))
            {
                var close = CLikeTokenizer.Matching(tokens, _i);
                reference = reference with { Text = reference.Text + CLikeTokenizer.Join(tokens.Skip(_i).Take(close - _i + 1)), IsMany = true };
                _i = close + 1;
            }

            if (Current.Is(":"))
            {
                // A bit-field width.
                _i += 2;
            }

            string? value = null;
            if (Current.Is("=") || Current.Is("{"))
            {
                var start = Current.Is("=") ? _i + 1 : _i;
                if (Current.Is("{"))
                {
                    _i = CLikeTokenizer.Matching(tokens, _i) + 1;
                }
                else
                {
                    _i++;
                    SkipExpression();
                }

                value = CLikeTokenizer.Join(tokens.Skip(start).Take(_i - start));
            }

            type.Attributes.Add(new ImportedAttribute { Name = name, Type = reference, Visibility = visibility, IsStatic = isStatic, DefaultValue = value });
            if (!Current.Is(","))
            {
                break;
            }

            // The next declarator: its own pointer or reference marks, then its name.
            _i++;
            typeTokens = [.. baseTokens];
            while (Current.Is("*") || Current.Is("&"))
            {
                typeTokens.Add(Current);
                _i++;
            }

            if (Current.Kind != TokenKind.Identifier)
            {
                break;
            }

            name = Current.Text;
            _i++;
        }

        SkipStatement();
        return MemberKind.Other;
    }

    private List<UmlParameter> ReadParameters()
    {
        var close = CLikeTokenizer.Matching(tokens, _i);
        var inside = tokens.Skip(_i + 1).Take(close - _i - 1).ToList();
        _i = close + 1;
        var parameters = new List<UmlParameter>();
        foreach (var parameter in SplitTopLevel(inside))
        {
            var written = parameter;
            var equals = written.FindIndex(t => t.Is("="));
            if (equals >= 0)
            {
                loss.DefaultArguments++;
                written = written.Take(equals).ToList();
            }

            if (written.Count == 0 || (written.Count == 1 && written[0].Is("void")) || written[0].Is("..."))
            {
                continue;
            }

            var hasName = written.Count > 1 && written[^1].Kind == TokenKind.Identifier && !Qualifiers.Contains(written[^1].Text)
                && !BuiltIns.Contains(written[^1].Text);
            parameters.Add(new UmlParameter
            {
                Name = hasName ? written[^1].Text : $"arg{parameters.Count + 1}",
                Type = CLikeTokenizer.Join(hasName ? written.Take(written.Count - 1) : written),
            });
        }

        return parameters;
    }

    /// <summary>After a function's parameters: qualifiers, <c>= 0</c>, an initializer list and a body; true when pure virtual.</summary>
    private bool SkipFunctionRest()
    {
        var pure = false;
        while (!AtEnd && !Current.Is(";") && !Current.Is("{") && !Current.Is("}"))
        {
            if (Current.Is("=") && At(_i + 1).Is("0"))
            {
                pure = true;
            }

            if (Current.Is(":"))
            {
                // A constructor's initializer list: member(args) or member{args}, separated by commas, then the body.
                _i++;
                while (!AtEnd && !Current.Is("{") && !Current.Is(";"))
                {
                    while (!AtEnd && !Current.Is("(") && !Current.Is("{"))
                    {
                        _i = Current.Is("<") ? CLikeTokenizer.Matching(tokens, _i) + 1 : _i + 1;
                    }

                    _i = CLikeTokenizer.Matching(tokens, _i) + 1;
                    if (Current.Is(","))
                    {
                        _i++;
                    }
                    else
                    {
                        break;
                    }
                }

                continue;
            }

            _i = Current.Is("(") ? CLikeTokenizer.Matching(tokens, _i) + 1 : _i + 1;
        }

        if (Current.Is("{"))
        {
            _i = CLikeTokenizer.Matching(tokens, _i) + 1;
            if (Current.Is(";"))
            {
                _i++;
            }
        }
        else if (Current.Is(";"))
        {
            _i++;
        }

        return pure;
    }

    private void ReadEnum(List<string> ns, List<string> declaringTypes)
    {
        _i++;
        if (Current.Is("class") || Current.Is("struct"))
        {
            _i++;
        }

        var name = Current.Kind == TokenKind.Identifier ? Current.Text : null;
        while (!AtEnd && !Current.Is("{"))
        {
            _i++;
        }

        var close = CLikeTokenizer.Matching(tokens, _i);
        var body = tokens.Skip(_i + 1).Take(close - _i - 1).ToList();
        _i = close + 1;
        SkipStatement();
        if (name is null)
        {
            return;
        }

        var type = new ImportedType
        {
            Namespace = string.Join("::", ns),
            Name = name,
            DeclaringTypes = [.. declaringTypes],
            Kind = ImportedTypeKind.Enumeration,
        };
        foreach (var literal in SplitTopLevel(body).Where(l => l.Count > 0 && l[0].Kind == TokenKind.Identifier))
        {
            var equals = literal.FindIndex(t => t.Is("="));
            type.Literals.Add(new ImportedLiteral(literal[0].Text, equals < 0 ? null : CLikeTokenizer.Join(literal.Skip(equals + 1))));
        }

        types.Add(type);
    }

    /// <summary>The names of a template's type parameters: template&lt;typename T, class U = int, int N&gt;.</summary>
    private List<string> ReadTemplateParameters()
    {
        _i++;
        if (!Current.Is("<"))
        {
            return [];
        }

        var close = CLikeTokenizer.Matching(tokens, _i);
        var names = SplitTopLevel(tokens.Skip(_i + 1).Take(close - _i - 1).ToList())
            .Select(p => p.TakeWhile(t => !t.Is("=")).LastOrDefault(t => t.Kind == TokenKind.Identifier).Text)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();
        _i = close + 1;
        return names;
    }

    /// <summary>The tokens up to the body that follows (a base list), leaving the position on the brace.</summary>
    private List<Token> ReadUntilBody()
    {
        var start = _i;
        while (!AtEnd && !Current.Is("{"))
        {
            _i = Current.Is("<") ? CLikeTokenizer.Matching(tokens, _i) + 1 : _i + 1;
        }

        return tokens.Skip(start).Take(_i - start).ToList();
    }

    /// <summary>Splits at the commas that are not inside brackets or type arguments.</summary>
    private static List<List<Token>> SplitTopLevel(List<Token> list)
    {
        var parts = new List<List<Token>> { new() };
        var depth = 0;
        foreach (var token in list)
        {
            if (token.Is("(") || token.Is("[") || token.Is("{") || token.Is("<"))
            {
                depth++;
            }
            else if (token.Is(")") || token.Is("]") || token.Is("}") || token.Is(">"))
            {
                depth--;
            }
            else if (token.Is(",") && depth == 0)
            {
                parts.Add([]);
                continue;
            }

            parts[^1].Add(token);
        }

        return parts.Where(p => p.Count > 0).ToList();
    }

    /// <summary>A type as used, e.g. <c>std::vector&lt;std::unique_ptr&lt;Order&gt;&gt;</c> refers to many Orders.</summary>
    private static TypeRef Reference(List<Token> written)
    {
        var text = CLikeTokenizer.Join(written);
        var (name, arity, many, optional) = Analyze(written.Where(t => !Qualifiers.Contains(t.Text)).ToList());
        return new TypeRef(text, name, arity, many, optional);
    }

    private static (string? Name, int Arity, bool Many, bool Optional) Analyze(List<Token> written)
    {
        var optional = written.Count > 0 && written[^1].Is("*");
        written = written.Where(t => !t.Is("*") && !t.Is("&")).ToList();

        // The last name at the top level, and the type arguments that follow it.
        var open = -1;
        string? name = null;
        for (var j = 0; j < written.Count; j++)
        {
            if (written[j].Is("<"))
            {
                open = j;
                j = CLikeTokenizer.Matching(written, j);
            }
            else if (written[j].Kind == TokenKind.Identifier)
            {
                name = written[j].Text;
                open = -1;
            }
        }

        if (name is null || BuiltIns.Contains(name))
        {
            return (null, 0, false, optional);
        }

        var arguments = open < 0 ? [] : SplitTopLevel(written.Skip(open + 1).Take(CLikeTokenizer.Matching(written, open) - open - 1).ToList());
        if (Collections.Contains(name) && arguments.Count > 0)
        {
            var element = Analyze(arguments[0]);
            return (element.Name, element.Arity, true, false);
        }

        if (Maps.Contains(name) && arguments.Count > 1)
        {
            var element = Analyze(arguments[1]);
            return (element.Name, element.Arity, true, false);
        }

        if (Optionals.Contains(name) && arguments.Count > 0)
        {
            var element = Analyze(arguments[0]);
            return (element.Name, element.Arity, element.Many, true);
        }

        return (name, arguments.Count, false, optional);
    }

    private void SkipExpression()
    {
        while (!AtEnd && !Current.Is(",") && !Current.Is(";") && !Current.Is("}"))
        {
            _i = Current.Is("(") || Current.Is("{") || Current.Is("[") ? CLikeTokenizer.Matching(tokens, _i) + 1 : _i + 1;
        }
    }

    /// <summary>Skips to the end of the declaration: past its <c>;</c>, or past its body and an optional <c>;</c>.</summary>
    private void SkipStatement()
    {
        while (!AtEnd && !Current.Is(";") && !Current.Is("}"))
        {
            if (Current.Is("{"))
            {
                _i = CLikeTokenizer.Matching(tokens, _i) + 1;
                if (Current.Is(";"))
                {
                    _i++;
                }

                return;
            }

            _i = Current.Is("(") || Current.Is("[") ? CLikeTokenizer.Matching(tokens, _i) + 1 : _i + 1;
        }

        if (Current.Is(";"))
        {
            _i++;
        }
    }
}
