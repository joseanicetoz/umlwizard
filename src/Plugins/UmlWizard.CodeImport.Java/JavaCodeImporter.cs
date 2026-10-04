using UmlWizard.CodeImport;
using UmlWizard.Core.Model;

namespace UmlWizard.CodeImport.Java;

/// <summary>
/// Reads Java declarations: classes, abstract classes, interfaces, enumerations and records («record») with their
/// fields, methods and constructors, in their packages. Method bodies and initializers are skipped.
/// </summary>
public sealed class JavaCodeImporter : ICodeImporter
{
    public string Id => "Java";

    public string DisplayName => "Java";

    public IReadOnlyList<string> FileExtensions { get; } = [".java"];

    public CodeImportParseResult Parse(IReadOnlyList<SourceFile> files)
    {
        var loss = new Losses();
        var types = new List<ImportedType>();
        foreach (var file in files)
        {
            new JavaParser(CLikeTokenizer.Tokenize(file.Text), types, loss).ParseFile();
        }

        return new CodeImportParseResult(types, loss.Notes());
    }

    internal sealed class Losses
    {
        public int Annotations, Initializers, TypeBounds, AnnotationTypes, Unreadable;

        public List<string> Notes()
        {
            var notes = new List<string>();
            CodeImporter.Add(notes, Unreadable, "declaration could not be read and was skipped", "declarations could not be read and were skipped");
            CodeImporter.Add(notes, Annotations, "annotation (e.g. @Override) was left out", "annotations (e.g. @Override) were left out");
            CodeImporter.Add(notes, Initializers, "initializer block was left out", "initializer blocks were left out");
            CodeImporter.Add(notes, TypeBounds, "bound of a type parameter (extends …) was left out", "bounds of type parameters (extends …) were left out");
            CodeImporter.Add(notes, AnnotationTypes, "annotation type (@interface) was left out", "annotation types (@interface) were left out");
            return notes;
        }
    }
}

/// <summary>A declaration parser for one Java file.</summary>
internal sealed class JavaParser(List<Token> tokens, List<ImportedType> types, JavaCodeImporter.Losses loss)
{
    private static readonly HashSet<string> Modifiers =
        ["public", "protected", "private", "static", "abstract", "final", "sealed", "non-sealed", "strictfp", "default",
         "transient", "volatile", "synchronized", "native"];

    private static readonly HashSet<string> TypeKeywords = ["class", "interface", "enum", "record"];

    /// <summary>Generic types whose last type argument is the element of a collection (the value of a map).</summary>
    private static readonly HashSet<string> Collections =
    [
        "Iterable", "Collection", "List", "ArrayList", "LinkedList", "Set", "HashSet", "LinkedHashSet", "TreeSet", "SortedSet",
        "NavigableSet", "Queue", "Deque", "ArrayDeque", "PriorityQueue", "Stream", "Map", "HashMap", "LinkedHashMap", "TreeMap",
        "SortedMap", "NavigableMap", "ConcurrentMap", "ConcurrentHashMap", "CopyOnWriteArrayList",
    ];

    private string _package = string.Empty;
    private int _i;

    private Token Current => _i < tokens.Count ? tokens[_i] : new Token(TokenKind.Symbol, "", 0);

    private bool AtEnd => _i >= tokens.Count;

    public void ParseFile()
    {
        while (!AtEnd)
        {
            if (Current.Is("package"))
            {
                _i++;
                _package = ReadUntil(";");
            }
            else if (Current.Is("import"))
            {
                SkipPast(";");
            }
            else if (!TryTypeDeclaration([]))
            {
                _i++;
            }
        }
    }

    /// <summary>Reads a type declaration at the current token (after its annotations and modifiers), if there is one.</summary>
    private bool TryTypeDeclaration(List<string> declaringTypes)
    {
        var start = _i;
        var annotations = loss.Annotations;
        var modifiers = ReadModifiers();
        if (Current.Is("@") && _i + 1 < tokens.Count && tokens[_i + 1].Is("interface"))
        {
            loss.AnnotationTypes++;
            _i += 2;
            SkipPast("{", block: true);
            return true;
        }

        if (!TypeKeywords.Contains(Current.Text) || _i + 1 >= tokens.Count || tokens[_i + 1].Kind != TokenKind.Identifier)
        {
            // Not a type: the member is read again, and its annotations counted then.
            _i = start;
            loss.Annotations = annotations;
            return false;
        }

        var keyword = Current.Text;
        _i++;
        var type = new ImportedType
        {
            Namespace = _package,
            Name = Current.Text,
            DeclaringTypes = [.. declaringTypes],
            Kind = keyword switch
            {
                "interface" => ImportedTypeKind.Interface,
                "enum" => ImportedTypeKind.Enumeration,
                _ when modifiers.Contains("abstract") => ImportedTypeKind.AbstractClass,
                _ => ImportedTypeKind.Class,
            },
            Stereotype = keyword == "record" ? "record" : null,
        };
        _i++;
        if (Current.Is("<"))
        {
            type.TypeParameters = ReadTypeParameters();
        }

        if (keyword == "record" && Current.Is("("))
        {
            foreach (var (name, reference) in ReadParameters())
            {
                type.Attributes.Add(new ImportedAttribute { Name = name, Type = reference, Visibility = UmlVisibility.Private });
            }
        }

        // extends, implements and permits, up to the body.
        while (!AtEnd && !Current.Is("{"))
        {
            if (Current.Is("extends") || Current.Is("implements"))
            {
                _i++;
                do
                {
                    if (Current.Is(","))
                    {
                        _i++;
                    }

                    type.BaseTypes.Add(ReadType().Reference);
                }
                while (Current.Is(","));
            }
            else
            {
                _i++;
            }
        }

        types.Add(type);
        _i++;
        ReadBody(type, [.. declaringTypes, type.Name]);
        return true;
    }

    private void ReadBody(ImportedType type, List<string> nested)
    {
        var inInterface = type.Kind == ImportedTypeKind.Interface;
        if (type.Kind == ImportedTypeKind.Enumeration)
        {
            ReadEnumConstants(type);
        }

        while (!AtEnd && !Current.Is("}"))
        {
            var start = _i;
            if (Current.Is(";"))
            {
                _i++;
                continue;
            }

            if (Current.Is("{") || (Current.Is("static") && _i + 1 < tokens.Count && tokens[_i + 1].Is("{")))
            {
                loss.Initializers++;
                SkipPast("{", block: true);
                continue;
            }

            if (TryTypeDeclaration(nested))
            {
                continue;
            }

            try
            {
                ReadMember(type, inInterface);
            }
            catch (ArgumentOutOfRangeException)
            {
                _i = tokens.Count;
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

    private void ReadEnumConstants(ImportedType type)
    {
        while (!AtEnd && !Current.Is(";") && !Current.Is("}"))
        {
            ReadModifiers();
            if (Current.Kind == TokenKind.Identifier)
            {
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
            }

            if (Current.Is(","))
            {
                _i++;
            }
            else if (!Current.Is(";") && !Current.Is("}"))
            {
                _i++;
            }
        }

        if (Current.Is(";"))
        {
            _i++;
        }
    }

    private void ReadMember(ImportedType type, bool inInterface)
    {
        var modifiers = ReadModifiers();
        var visibility = modifiers.Contains("public") ? UmlVisibility.Public
            : modifiers.Contains("protected") ? UmlVisibility.Protected
            : modifiers.Contains("private") ? UmlVisibility.Private
            : inInterface ? UmlVisibility.Public : UmlVisibility.Package;
        var isStatic = modifiers.Contains("static");

        var generic = string.Empty;
        if (Current.Is("<"))
        {
            generic = $"<{string.Join(", ", ReadTypeParameters())}>";
        }

        // A constructor: the class name followed by its parameters.
        if (Current.Is(type.Name) && _i + 1 < tokens.Count && tokens[_i + 1].Is("("))
        {
            _i++;
            var constructor = new ImportedOperation { Name = type.Name, Visibility = visibility };
            constructor.Parameters.AddRange(ReadParameters().Select(p => new UmlParameter { Name = p.Name, Type = p.Type.Text }));
            SkipMethodRest();
            type.Operations.Add(constructor);
            return;
        }

        if (Current.Kind != TokenKind.Identifier)
        {
            return;
        }

        var (text, reference) = ReadType();
        if (Current.Kind != TokenKind.Identifier)
        {
            return;
        }

        var name = Current.Text;
        _i++;
        if (Current.Is("("))
        {
            var operation = new ImportedOperation
            {
                Name = name + generic,
                ReturnType = text,
                Visibility = visibility,
                IsStatic = isStatic,
                IsAbstract = modifiers.Contains("abstract"),
            };
            operation.Parameters.AddRange(ReadParameters().Select(p => new UmlParameter { Name = p.Name, Type = p.Type.Text }));
            SkipMethodRest();
            type.Operations.Add(operation);
            return;
        }

        // Fields: int a = 1, b[];  Interface fields are constants.
        while (true)
        {
            var fieldText = text;
            var fieldReference = reference;
            while (Current.Is("["))
            {
                _i += 2;
                fieldText += "[]";
                fieldReference = fieldReference with { Text = fieldText, IsMany = true };
            }

            string? value = null;
            if (Current.Is("="))
            {
                _i++;
                var start = _i;
                SkipExpression();
                value = CLikeTokenizer.Join(tokens.Skip(start).Take(_i - start));
            }

            type.Attributes.Add(new ImportedAttribute
            {
                Name = name,
                Type = fieldReference,
                Visibility = visibility,
                IsStatic = isStatic || inInterface,
                DefaultValue = value,
            });

            if (!Current.Is(",") || _i + 1 >= tokens.Count)
            {
                break;
            }

            _i++;
            name = Current.Text;
            _i++;
        }

        if (Current.Is(";"))
        {
            _i++;
        }
    }

    /// <summary>A type: <c>java.util.List&lt;Order&gt;[]</c>, <c>int</c>, <c>Map.Entry&lt;K, V&gt;</c>.</summary>
    private (string Text, TypeRef Reference) ReadType()
    {
        var start = _i;
        while (Current.Is("@"))
        {
            SkipAnnotation();
        }

        var parts = new List<(string Name, List<TypeRef> Arguments)>();
        while (Current.Kind == TokenKind.Identifier || Current.Is("?"))
        {
            var partName = Current.Text;
            _i++;
            var arguments = new List<TypeRef>();
            if (Current.Is("<"))
            {
                _i++;
                while (!AtEnd && !Current.Is(">"))
                {
                    if (Current.Is(",") || Current.Is("extends") || Current.Is("super"))
                    {
                        _i++;
                        continue;
                    }

                    arguments.Add(ReadType().Reference);
                }

                _i++;
            }

            parts.Add((partName, arguments));
            if (!Current.Is("."))
            {
                break;
            }

            _i++;
        }

        var many = false;
        while (Current.Is("[") || Current.Is("..."))
        {
            many = true;
            _i += Current.Is("...") ? 1 : 2;
        }

        var text = CLikeTokenizer.Join(tokens.Skip(start).Take(_i - start).Where(t => !t.Is("@")));
        if (parts.Count == 0)
        {
            return (text, new TypeRef(text));
        }

        var (name, args) = parts[^1];
        if (Collections.Contains(name) && args.Count > 0)
        {
            var element = args[^1];
            return (text, element with { Text = text, IsMany = true });
        }

        if (name == "Optional" && args.Count == 1)
        {
            return (text, args[0] with { Text = text, IsOptional = true });
        }

        var reference = IsPrimitive(name) ? new TypeRef(text) : new TypeRef(text, name, args.Count, many);
        return (text, reference);
    }

    private List<(string Name, TypeRef Type)> ReadParameters()
    {
        var parameters = new List<(string, TypeRef)>();
        var close = CLikeTokenizer.Matching(tokens, _i);
        _i++;
        while (_i < close)
        {
            ReadModifiers();
            var (_, reference) = ReadType();
            if (Current.Kind == TokenKind.Identifier)
            {
                parameters.Add((Current.Text, reference));
                _i++;
            }

            while (_i < close && !Current.Is(","))
            {
                _i++;
            }

            _i++;
        }

        _i = close + 1;
        return parameters;
    }

    private List<string> ReadTypeParameters()
    {
        var close = CLikeTokenizer.Matching(tokens, _i);
        var names = new List<string>();
        var depth = 0;
        for (var j = _i; j <= close; j++)
        {
            if (tokens[j].Is("<"))
            {
                depth++;
            }
            else if (tokens[j].Is(">"))
            {
                depth--;
            }
            else if (depth == 1 && tokens[j].Kind == TokenKind.Identifier && (tokens[j - 1].Is("<") || tokens[j - 1].Is(",")))
            {
                names.Add(tokens[j].Text);
            }
            else if (depth == 1 && tokens[j].Is("extends"))
            {
                loss.TypeBounds++;
            }
        }

        _i = close + 1;
        return names;
    }

    private HashSet<string> ReadModifiers()
    {
        var modifiers = new HashSet<string>();
        while (!AtEnd)
        {
            if (Current.Is("@") && !(_i + 1 < tokens.Count && tokens[_i + 1].Is("interface")))
            {
                loss.Annotations++;
                SkipAnnotation();
            }
            else if (Modifiers.Contains(Current.Text) || (Current.Is("non") && _i + 2 < tokens.Count && tokens[_i + 1].Is("-")))
            {
                modifiers.Add(Current.Text);
                _i += Current.Is("non") ? 3 : 1;
            }
            else
            {
                break;
            }
        }

        return modifiers;
    }

    /// <summary>An annotation: <c>@Override</c>, <c>@javax.persistence.Entity(name = "orders")</c>.</summary>
    private void SkipAnnotation()
    {
        _i += 2; // @ and the first part of the name
        while (Current.Is(".") && _i + 1 < tokens.Count && tokens[_i + 1].Kind == TokenKind.Identifier)
        {
            _i += 2;
        }

        if (Current.Is("("))
        {
            _i = CLikeTokenizer.Matching(tokens, _i) + 1;
        }
    }

    /// <summary>After a method's parameters: <c>throws …</c>, then its body, <c>;</c> or <c>default …;</c>.</summary>
    private void SkipMethodRest()
    {
        while (!AtEnd && !Current.Is("{") && !Current.Is(";"))
        {
            _i++;
        }

        _i = Current.Is("{") ? CLikeTokenizer.Matching(tokens, _i) + 1 : _i + 1;
    }

    /// <summary>An initial value, up to the <c>,</c> or <c>;</c> that ends it.</summary>
    private void SkipExpression()
    {
        while (!AtEnd && !Current.Is(",") && !Current.Is(";") && !Current.Is("}"))
        {
            _i = Current.Is("(") || Current.Is("{") || Current.Is("[") ? CLikeTokenizer.Matching(tokens, _i) + 1 : _i + 1;
        }
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

    private void SkipPast(string text, bool block = false)
    {
        while (!AtEnd && !Current.Is(text))
        {
            _i++;
        }

        _i = block && !AtEnd ? CLikeTokenizer.Matching(tokens, _i) + 1 : _i + 1;
    }

    private string ReadUntil(string end)
    {
        var start = _i;
        while (!AtEnd && !Current.Is(end))
        {
            _i++;
        }

        var text = CLikeTokenizer.Join(tokens.Skip(start).Take(_i - start));
        _i++;
        return text;
    }

    private static bool IsPrimitive(string name) =>
        name is "int" or "long" or "short" or "byte" or "char" or "boolean" or "float" or "double" or "void" or "var" or "?"
            or "String" or "Object" or "Integer" or "Long" or "Short" or "Byte" or "Character" or "Boolean" or "Float" or "Double";
}
