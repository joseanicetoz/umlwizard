using System.Text.RegularExpressions;
using UmlWizard.CodeImport;
using UmlWizard.Core.Model;

namespace UmlWizard.CodeImport.Ruby;

/// <summary>
/// Reads Ruby classes and modules. Modules that only hold other classes and modules are namespaces; modules with
/// methods or constants are mixins, shown as interfaces with the stereotype «module», which <c>include</c>,
/// <c>extend</c> and <c>prepend</c> realize. Attributes come from <c>attr_accessor</c> (and <c>attr_reader</c>,
/// <c>attr_writer</c>), from instance variables the methods assign and from constants; operations from <c>def</c>,
/// where <c>initialize</c> is the constructor and a method that raises <c>NotImplementedError</c> is abstract.
/// </summary>
public sealed class RubyCodeImporter : ICodeImporter
{
    public string Id => "Ruby";

    public string DisplayName => "Ruby";

    public IReadOnlyList<string> FileExtensions { get; } = [".rb"];

    public CodeImportParseResult Parse(IReadOnlyList<SourceFile> files)
    {
        var loss = new Losses();
        var types = new List<ImportedType>();
        foreach (var file in files)
        {
            new RubyReader(types, loss).Read(file.Text);
        }

        var notes = new List<string>();
        if (types.Count > 0)
        {
            notes.Add("Ruby declares no types, so attributes and parameters have none, and classes are linked only by inheritance " +
                "and include, extend or prepend, not by associations.");
        }

        CodeImporter.Add(notes, loss.Metaprogramming, "method defined at run time (define_method, method_missing, …) was left out",
            "methods defined at run time (define_method, method_missing, …) were left out");
        CodeImporter.Add(notes, loss.DefaultArguments, "parameter lost its default value", "parameters lost their default values");
        CodeImporter.Add(notes, loss.UnclosedBlocks, "file ended with unclosed blocks (a missing end); its declarations were read as far as possible",
            "files ended with unclosed blocks (a missing end); their declarations were read as far as possible");
        return new CodeImportParseResult(types, notes);
    }

    internal sealed class Losses
    {
        public int Metaprogramming, DefaultArguments, UnclosedBlocks;
    }
}

/// <summary>Reads the classes and modules of one Ruby file, statement by statement.</summary>
internal sealed partial class RubyReader(List<ImportedType> types, RubyCodeImporter.Losses loss)
{
    private enum FrameKind
    {
        Class,
        Module,
        SingletonClass,
        Method,
        Block,
    }

    /// <summary>An open <c>class</c>, <c>module</c>, <c>class &lt;&lt; self</c>, <c>def</c> or other block, closed by <c>end</c>.</summary>
    private sealed class Frame(FrameKind kind, ImportedType? type = null)
    {
        public FrameKind Kind { get; } = kind;

        /// <summary>The class or module; for a method, the class it belongs to.</summary>
        public ImportedType? Type { get; } = type;

        public UmlVisibility Visibility { get; set; } = UmlVisibility.Public;

        /// <summary>For a module after <c>module_function</c>: its methods are module (static) methods.</summary>
        public bool ModuleFunctions { get; set; }

        public ImportedOperation? Operation { get; init; }
    }

    private readonly List<Frame> _stack = [];

    /// <summary>Instance variables assigned by each type's methods; they become private attributes.</summary>
    private readonly Dictionary<ImportedType, List<(string Name, bool IsStatic)>> _instanceVariables = [];

    /// <summary>Modules that have members; the others are only namespaces.</summary>
    private readonly HashSet<ImportedType> _modules = [];

    /// <summary>Every module, and those that hold classes or modules (namespaces, unless they also have members).</summary>
    private readonly List<ImportedType> _allModules = [];

    private readonly HashSet<ImportedType> _namespaces = [];

    /// <summary>Methods named in <c>private :a, :b</c> and the like, applied when the file has been read.</summary>
    private readonly List<(ImportedType Type, string Name, UmlVisibility Visibility)> _laterVisibility = [];

    public void Read(string text)
    {
        foreach (var statement in Statements(text))
        {
            ReadStatement(statement);
        }

        if (_stack.Count > 0)
        {
            loss.UnclosedBlocks++;
        }

        foreach (var (type, name, visibility) in _laterVisibility)
        {
            foreach (var operation in type.Operations.Where(o => o.Name == name))
            {
                operation.Visibility = visibility;
            }
        }

        // Instance variables of class methods belong to the class itself: they are static.
        foreach (var (type, variables) in _instanceVariables)
        {
            foreach (var (name, isStatic) in variables.DistinctBy(v => v.Name).Where(v => type.Attributes.All(a => a.Name != v.Name)))
            {
                type.Attributes.Add(new ImportedAttribute { Name = name, Visibility = UmlVisibility.Private, IsStatic = isStatic });
            }
        }

        // A module with members is a mixin; so is an empty one that holds nothing (e.g. an interface without operations).
        types.AddRange(_allModules.Where(m => _modules.Contains(m) || !_namespaces.Contains(m)));
    }

    private ImportedType? CurrentType => _stack.LastOrDefault(f => f.Kind is FrameKind.Class or FrameKind.Module)?.Type;

    /// <summary>The frame that holds the current statement directly, if it is a class, module or singleton class body.</summary>
    private Frame? Body => _stack.Count > 0 && _stack[^1].Kind is FrameKind.Class or FrameKind.Module or FrameKind.SingletonClass ? _stack[^1] : null;

    private void ReadStatement(Statement statement)
    {
        var code = statement.Code;
        var words = Words().Matches(code).Select(m => m.Value).ToList();
        if (words.Count == 0)
        {
            return;
        }

        var first = words[0];
        var rest = code[(code.IndexOf(first, StringComparison.Ordinal) + first.Length)..].Trim();
        switch (first)
        {
            case "class" when rest.StartsWith("<<", StringComparison.Ordinal):
                _stack.Add(new Frame(FrameKind.SingletonClass, CurrentType));
                return;

            case "class":
                OpenClass(rest);
                return;

            case "module":
                OpenModule(rest);
                return;

            case "def":
                OpenMethod(rest, Body?.Visibility ?? UmlVisibility.Public);
                CloseEnds(words.Skip(1));
                return;

            case "private" or "protected" or "public" when Body is { } body:
                var visibility = first switch
                {
                    "private" => UmlVisibility.Private,
                    "protected" => UmlVisibility.Protected,
                    _ => UmlVisibility.Public,
                };
                if (rest.Length == 0)
                {
                    body.Visibility = visibility;
                }
                else if (rest.StartsWith("def ", StringComparison.Ordinal))
                {
                    OpenMethod(rest[4..], visibility);
                    CloseEnds(words.Skip(2));
                }
                else if (CurrentType is { } owner)
                {
                    foreach (Match symbol in Symbol().Matches(rest))
                    {
                        _laterVisibility.Add((owner, symbol.Groups[1].Value, visibility));
                    }
                }

                return;

            case "module_function" when Body is { Kind: FrameKind.Module } module:
                module.ModuleFunctions = true;
                return;

            case "attr_accessor" or "attr_reader" or "attr_writer" when Body is { Type: { } type } body:
                foreach (Match symbol in Symbol().Matches(rest))
                {
                    type.Attributes.Add(new ImportedAttribute { Name = symbol.Groups[1].Value, Visibility = UmlVisibility.Public, IsStatic = body.Kind == FrameKind.SingletonClass });
                }

                MarkModule(type);
                return;

            case "include" or "extend" or "prepend" when Body is { Type: { } type }:
                foreach (var name in rest.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    if (ConstantPath().Match(name) is { Success: true } path)
                    {
                        type.BaseTypes.Add(new TypeRef(path.Value, path.Value.Split("::")[^1]));
                    }
                }

                return;

            case "define_method" or "method_missing" or "define_singleton_method" or "attr_accessor_with_default":
                loss.Metaprogramming++;
                break;
        }

        // Constants and class variables in a class or module body; instance variables in its methods.
        if (Body is { Type: { } bodyType } && ConstantAssignment().Match(statement.Original) is { Success: true } constant)
        {
            var value = constant.Groups[2].Value.Trim();
            if (StructNew().Match(value) is { Success: true } structNew)
            {
                OpenStruct(constant.Groups[1].Value, structNew.Groups[1].Value, opensBlock: value.EndsWith(" do", StringComparison.Ordinal) || value.EndsWith("do |", StringComparison.Ordinal));
                return;
            }

            bodyType.Attributes.Add(new ImportedAttribute { Name = constant.Groups[1].Value, Visibility = UmlVisibility.Public, IsStatic = true, DefaultValue = value });
            MarkModule(bodyType);
        }
        else if (Body is { Type: { } varType } && ClassVariable().Match(statement.Original) is { Success: true } classVariable)
        {
            varType.Attributes.Add(new ImportedAttribute
            {
                Name = classVariable.Groups[1].Value,
                Visibility = UmlVisibility.Private,
                IsStatic = true,
                DefaultValue = classVariable.Groups[2].Value.Trim(),
            });
            MarkModule(varType);
        }
        else if (_stack.LastOrDefault(f => f.Kind == FrameKind.Method) is { Type: { } owner, Operation: { } operation })
        {
            foreach (Match variable in InstanceVariable().Matches(code))
            {
                (_instanceVariables.TryGetValue(owner, out var list) ? list : _instanceVariables[owner] = []).Add((variable.Groups[1].Value, operation.IsStatic));
            }

            if (code.Contains("NotImplementedError", StringComparison.Ordinal) && words.Contains("raise") && IsAbstractRaise(statement.Original))
            {
                operation.IsAbstract = true;
                if (owner.Kind == ImportedTypeKind.Class)
                {
                    owner.Kind = ImportedTypeKind.AbstractClass;
                }
            }
        }

        // Blocks that end with 'end': if, unless, while, until, case, begin and for as statements (also after '='),
        // and do … end blocks.
        if (first is "if" or "unless" or "while" or "until" or "case" or "begin" or "for" || OpensAfterAssignment().IsMatch(code))
        {
            _stack.Add(new Frame(FrameKind.Block));
        }

        foreach (var word in words.Skip(1))
        {
            if (word == "do")
            {
                _stack.Add(new Frame(FrameKind.Block));
            }
        }

        CloseEnds(words);
    }

    private void OpenClass(string rest)
    {
        var match = ClassHeader().Match(rest);
        if (!match.Success)
        {
            _stack.Add(new Frame(FrameKind.Block));
            return;
        }

        var path = match.Groups[1].Value.Split("::");
        var type = new ImportedType
        {
            Namespace = Namespace(path[..^1]),
            Name = path[^1],
            DeclaringTypes = _stack.Where(f => f.Kind == FrameKind.Class).Select(f => f.Type!.Name).ToList(),
        };
        if (match.Groups[2].Success)
        {
            var baseName = match.Groups[2].Value;
            type.BaseTypes.Add(new TypeRef(baseName, baseName.Split("::")[^1]));
        }

        MarkContainer();
        types.Add(type);
        _stack.Add(new Frame(FrameKind.Class, type));
    }

    private void OpenModule(string rest)
    {
        var match = ConstantPath().Match(rest);
        if (!match.Success)
        {
            _stack.Add(new Frame(FrameKind.Block));
            return;
        }

        var path = match.Value.Split("::");
        var type = new ImportedType
        {
            Namespace = Namespace(path[..^1]),
            Name = path[^1],
            DeclaringTypes = _stack.Where(f => f.Kind == FrameKind.Class).Select(f => f.Type!.Name).ToList(),
            Kind = ImportedTypeKind.Interface,
            Stereotype = "module",
        };
        MarkContainer();
        _allModules.Add(type);
        _stack.Add(new Frame(FrameKind.Module, type));
    }

    /// <summary><c>Point = Struct.new(:x, :y)</c>: a class «struct» with those attributes.</summary>
    private void OpenStruct(string name, string members, bool opensBlock)
    {
        var type = new ImportedType
        {
            Namespace = Namespace([]),
            Name = name,
            DeclaringTypes = _stack.Where(f => f.Kind == FrameKind.Class).Select(f => f.Type!.Name).ToList(),
            Stereotype = "struct",
        };
        foreach (Match symbol in Symbol().Matches(members))
        {
            type.Attributes.Add(new ImportedAttribute { Name = symbol.Groups[1].Value, Visibility = UmlVisibility.Public });
        }

        MarkContainer();
        types.Add(type);
        if (opensBlock)
        {
            _stack.Add(new Frame(FrameKind.Class, type));
        }
    }

    private void OpenMethod(string signature, UmlVisibility visibility)
    {
        var match = MethodHeader().Match(signature);
        if (!match.Success || CurrentType is not { } owner)
        {
            // A method outside any class (or one we cannot read): its body still ends with 'end'.
            _stack.Add(new Frame(FrameKind.Method));
            return;
        }

        var body = Body;
        var isStatic = match.Groups[1].Success || body?.Kind == FrameKind.SingletonClass || body?.ModuleFunctions == true;
        var name = match.Groups[2].Value;
        var operation = new ImportedOperation
        {
            Name = name == "initialize" && !isStatic ? owner.Name : name,
            Visibility = name == "initialize" ? UmlVisibility.Public : visibility,
            IsStatic = isStatic,
        };
        foreach (var parameter in SplitParameters(match.Groups[3].Success ? match.Groups[3].Value : match.Groups[4].Value))
        {
            operation.Parameters.Add(new UmlParameter { Name = parameter });
        }

        owner.Operations.Add(operation);
        MarkModule(owner);

        // An endless method (def area = width * height) has no body to close.
        var after = signature[(match.Index + match.Length)..].TrimStart();
        if (!(after.StartsWith('=') && !after.StartsWith("==", StringComparison.Ordinal)))
        {
            _stack.Add(new Frame(FrameKind.Method, owner) { Operation = operation });
        }
    }

    private IEnumerable<string> SplitParameters(string list)
    {
        var depth = 0;
        var start = 0;
        var parts = new List<string>();
        for (var i = 0; i <= list.Length; i++)
        {
            if (i == list.Length || (list[i] == ',' && depth == 0))
            {
                parts.Add(list[start..i].Trim());
                start = i + 1;
            }
            else if (list[i] is '(' or '[' or '{')
            {
                depth++;
            }
            else if (list[i] is ')' or ']' or '}')
            {
                depth--;
            }
        }

        foreach (var part in parts.Where(p => p.Length > 0))
        {
            var name = part.TrimStart('*', '&');
            if (name.Contains('=') || (name.Contains(':') && name.IndexOf(':') < name.Length - 1))
            {
                loss.DefaultArguments++;
            }

            name = name.Split('=', ':')[0].Trim();
            if (name.Length > 0)
            {
                yield return name;
            }
        }
    }

    /// <summary>Closes a frame for each <c>end</c> keyword of the statement.</summary>
    private void CloseEnds(IEnumerable<string> words)
    {
        foreach (var word in words)
        {
            if (word == "end" && _stack.Count > 0)
            {
                _stack.RemoveAt(_stack.Count - 1);
            }
        }
    }

    /// <summary>The module the current statement is directly in holds a class or module: it is (also) a namespace.</summary>
    private void MarkContainer()
    {
        if (_stack.Count > 0 && _stack[^1] is { Kind: FrameKind.Module, Type: { } module })
        {
            _namespaces.Add(module);
        }
    }

    /// <summary>
    /// <c>raise NotImplementedError</c> marks an abstract method when it says so ("must implement", "subclass",
    /// "abstract") or says nothing; "Order#total is not implemented" is a stub of a concrete method.
    /// </summary>
    private static bool IsAbstractRaise(string statement)
    {
        var message = statement[(statement.IndexOf("NotImplementedError", StringComparison.Ordinal) + "NotImplementedError".Length)..].Trim();
        return message.Length == 0 || AbstractMessage().IsMatch(message);
    }

    /// <summary>A module that has methods, attributes or constants is shown as a type, not only as a namespace.</summary>
    private void MarkModule(ImportedType type)
    {
        if (type.Kind == ImportedTypeKind.Interface && type.Stereotype == "module")
        {
            _modules.Add(type);
        }
    }

    /// <summary>The namespace of a declaration: the enclosing modules, and the <c>A::</c> part of its own name.</summary>
    private string Namespace(IEnumerable<string> own) =>
        string.Join("::", _stack.Where(f => f.Kind == FrameKind.Module).Select(f => f.Type!.Name).Concat(own));

    #region Statements

    private sealed record Statement(string Code, string Original);

    /// <summary>
    /// The statements of the source: comments, <c>=begin</c> … <c>=end</c> blocks and heredoc bodies are left out, lines
    /// are split at <c>;</c>, and in <see cref="Statement.Code"/> the contents of strings are blanked out so that words
    /// in them are not taken for keywords.
    /// </summary>
    private static IEnumerable<Statement> Statements(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        string? heredoc = null;
        var inComment = false;
        foreach (var line in lines)
        {
            if (inComment)
            {
                inComment = !line.StartsWith("=end", StringComparison.Ordinal);
                continue;
            }

            if (line.StartsWith("=begin", StringComparison.Ordinal))
            {
                inComment = true;
                continue;
            }

            if (heredoc is not null)
            {
                if (line.Trim() == heredoc)
                {
                    heredoc = null;
                }

                continue;
            }

            if (line.StartsWith("__END__", StringComparison.Ordinal))
            {
                yield break;
            }

            var (code, original) = Strip(line);
            if (Heredoc().Match(code) is { Success: true } start)
            {
                heredoc = start.Groups[1].Value.Trim('\'', '"');
            }

            var codeParts = code.Split(';');
            var originalParts = original.Split(';');
            for (var i = 0; i < codeParts.Length; i++)
            {
                if (codeParts[i].Trim().Length > 0)
                {
                    yield return new Statement(codeParts[i].Trim(), i < originalParts.Length && codeParts.Length == originalParts.Length ? originalParts[i].Trim() : original.Trim());
                }
            }
        }
    }

    /// <summary>A line without its comment; in the first copy the contents of strings are blanked out.</summary>
    private static (string Code, string Original) Strip(string line)
    {
        var code = new System.Text.StringBuilder(line.Length);
        char? quote = null;
        var end = line.Length;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quote is null)
            {
                if (c == '#')
                {
                    end = i;
                    break;
                }

                if (c is '"' or '\'' or '`')
                {
                    quote = c;
                }

                code.Append(c);
            }
            else
            {
                if (c == '\\')
                {
                    i++;
                    continue;
                }

                if (c == quote)
                {
                    quote = null;
                    code.Append(c);
                }
            }
        }

        return (code.ToString(), line[..end]);
    }

    #endregion

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*[?!]?")]
    private static partial Regex Words();

    [GeneratedRegex(@"^([A-Z][A-Za-z0-9_]*(?:::[A-Z][A-Za-z0-9_]*)*)(?:\s*<\s*([A-Z][A-Za-z0-9_]*(?:::[A-Z][A-Za-z0-9_]*)*))?")]
    private static partial Regex ClassHeader();

    [GeneratedRegex(@"^:{0,2}[A-Z][A-Za-z0-9_]*(?:::[A-Z][A-Za-z0-9_]*)*")]
    private static partial Regex ConstantPath();

    [GeneratedRegex(@"^(self\.)?([A-Za-z_][A-Za-z0-9_]*[?!=]?|\[\]=?|[+\-*/%<>=!~^&|]+)\s*(?:\((.*?)\)|([^=\n]*))?")]
    private static partial Regex MethodHeader();

    [GeneratedRegex(@":([A-Za-z_][A-Za-z0-9_]*[?!=]?)")]
    private static partial Regex Symbol();

    [GeneratedRegex(@"^([A-Z][A-Za-z0-9_]*)\s*=(?!=)\s*(.+)$")]
    private static partial Regex ConstantAssignment();

    [GeneratedRegex(@"^Struct\.new\((.*?)\)")]
    private static partial Regex StructNew();

    [GeneratedRegex(@"^@@([A-Za-z_][A-Za-z0-9_]*)\s*=(?!=)\s*(.*)$")]
    private static partial Regex ClassVariable();

    [GeneratedRegex(@"(?<![@\w])@([A-Za-z_][A-Za-z0-9_]*)\s*(?:\|\|)?=(?![=~])")]
    private static partial Regex InstanceVariable();

    [GeneratedRegex(@"=\s*(if|unless|case|begin|while|until)\b")]
    private static partial Regex OpensAfterAssignment();

    [GeneratedRegex(@"must implement|\bsubclass|\babstract\b", RegexOptions.IgnoreCase)]
    private static partial Regex AbstractMessage();

    [GeneratedRegex(@"<<[~-]?(['""]?[A-Z_][A-Z0-9_]*['""]?)")]
    private static partial Regex Heredoc();
}
