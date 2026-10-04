using System.Text.RegularExpressions;
using UmlWizard.Core.Model;
using UmlWizard.Core.Notation;
using UmlWizard.Core.Rendering;

namespace UmlWizard.PlantUml;

/// <summary>
/// Reads a PlantUML class diagram: classes, abstract classes, interfaces and enumerations (other keywords, such as
/// <c>entity</c> or <c>struct</c>, become classes with that stereotype) with their members, in UML notation
/// (<c>+name : Type</c>) or Java notation (<c>String name</c>), and the relationships between them. Packages are kept
/// as the tagged value <c>namespace</c>, as a code import keeps them.
/// </summary>
internal sealed partial class ClassDiagramReader(string name) : DiagramReader(name, DiagramType.Class)
{
    private readonly Dictionary<string, UmlClass> _byAlias = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _notes = new(StringComparer.Ordinal);
    private readonly List<string> _packages = [];
    private readonly List<bool> _braces = [];
    private string? _lastClass;
    private string? _lastRelationship;

    private ClassDiagramModel Model => (ClassDiagramModel)Document.Model;

    protected override bool ReadLine(List<string> lines, ref int i)
    {
        var line = lines[i];
        if (line == "}")
        {
            // The end of a package (or of a block that is not one, such as together { … }).
            if (_braces.Count > 0)
            {
                if (_braces[^1])
                {
                    _packages.RemoveAt(_packages.Count - 1);
                }

                _braces.RemoveAt(_braces.Count - 1);
            }

            return true;
        }

        if (PackageLine().Match(line) is { Success: true } package)
        {
            var packageName = package.Groups["display"].Success ? PlantUmlText.Unquote(package.Groups["display"].Value) : package.Groups["name"].Value;
            _packages.Add(packageName);
            _braces.Add(true);
            return true;
        }

        if (TogetherLine().IsMatch(line))
        {
            _braces.Add(false);
            return true;
        }

        if (ReadNote(lines, ref i) is { } note)
        {
            AddNote(note);
            return true;
        }

        if (DeclarationLine().Match(line) is { Success: true } declaration)
        {
            ReadDeclaration(declaration, lines, ref i);
            return true;
        }

        if (PlantUmlText.ParseRelation(line) is { } relation && ReadRelation(relation))
        {
            return true;
        }

        // Class : member, a member added outside the class's body.
        if (MemberLine().Match(line) is { Success: true } member && _byAlias.TryGetValue(member.Groups["class"].Value, out var cls))
        {
            AddMember(cls, member.Groups["member"].Value);
            return true;
        }

        return false;
    }

    protected override void Finish()
    {
        ClassDiagramLayout.Arrange(Document);
        PlaceNotes();
    }

    #region Classes

    private void ReadDeclaration(Match declaration, List<string> lines, ref int i)
    {
        var keyword = Regex.Replace(declaration.Groups["keyword"].Value.ToLowerInvariant(), @"\s+", " ");
        var rest = declaration.Groups["rest"].Value.Trim();
        var opensBody = rest.EndsWith('{');
        if (opensBody)
        {
            rest = rest[..^1].Trim();
        }

        var emptyBody = rest.EndsWith("{}", StringComparison.Ordinal);
        if (emptyBody)
        {
            rest = rest[..^2].Trim();
        }

        // extends and implements written in the declaration: class Order extends Entity implements Payable
        var bases = new List<(string Name, bool IsInterface)>();
        if (Heritage().Match(rest) is { Success: true } heritage)
        {
            rest = rest[..heritage.Index].Trim();
            bases.AddRange(Names(heritage.Groups["extends"].Value).Select(n => (n, keyword == "interface")));
            bases.AddRange(Names(heritage.Groups["implements"].Value).Select(n => (n, true)));
        }

        // A stereotype, and a colour, which is left out.
        string? stereotype = null;
        if (Stereotype().Match(rest) is { Success: true } s)
        {
            stereotype = s.Groups["text"].Value.Trim();
            rest = rest.Remove(s.Index, s.Length).Trim();
        }

        rest = Colors().Replace(rest, string.Empty).Trim();
        var (alias, display) = NameAndAlias(rest);
        var cls = GetOrAdd(alias, display);
        cls.Kind = keyword switch
        {
            "interface" or "protocol" => ClassifierKind.Interface,
            "enum" => ClassifierKind.Enumeration,
            "abstract" or "abstract class" => ClassifierKind.AbstractClass,
            _ => ClassifierKind.Class,
        };
        cls.Stereotype = stereotype ?? (keyword is "entity" or "annotation" or "struct" or "record" or "exception" or "metaclass" ? keyword : cls.Stereotype);
        _lastClass = cls.Id;

        foreach (var (baseName, isInterface) in bases)
        {
            var target = GetOrAdd(baseName, baseName);
            if (isInterface && target.Kind == ClassifierKind.Class && keyword != "interface")
            {
                target.Kind = ClassifierKind.Interface;
            }

            AddRelationship(isInterface && cls.Kind != ClassifierKind.Interface ? RelationshipKind.Realization : RelationshipKind.Inheritance, cls, target);
        }

        if (!opensBody)
        {
            return;
        }

        while (i + 1 < lines.Count && lines[i + 1] != "}")
        {
            var memberLine = lines[++i];
            if (!Separator().IsMatch(memberLine))
            {
                AddMember(cls, memberLine);
            }
        }

        i++;
    }

    private void AddMember(UmlClass cls, string text)
    {
        text = text.Trim();
        if (cls.Kind == ClassifierKind.Enumeration)
        {
            foreach (var literal in text.Split(',').Select(l => l.Trim()).Where(l => l.Length > 0))
            {
                var value = literal.IndexOf('=') is var equals && equals > 0 ? literal[(equals + 1)..].Trim()
                    : literal.IndexOf('(') is var open && open > 0 && literal.EndsWith(')') ? literal[(open + 1)..^1].Trim()
                    : null;
                var literalName = literal.Split('=', '(')[0].Trim();
                if (literalName.Length > 0)
                {
                    cls.Attributes.Add(new UmlAttribute { Name = literalName, DefaultValue = value });
                }
            }

            return;
        }

        var isStatic = false;
        var isAbstract = false;
        bool? isMethod = null;
        while (Modifier().Match(text) is { Success: true } modifier)
        {
            switch (modifier.Groups["name"].Value.ToLowerInvariant())
            {
                case "static" or "classifier":
                    isStatic = true;
                    break;
                case "abstract":
                    isAbstract = true;
                    break;
                case "field":
                    isMethod = false;
                    break;
                case "method":
                    isMethod = true;
                    break;
            }

            text = text.Remove(modifier.Index, modifier.Length).Trim();
        }

        // A web API endpoint, as the export writes it: getOrder(id : string) : Order <<GET /orders/{id}>>
        string? method = null;
        string? path = null;
        if (Endpoint().Match(text) is { Success: true } endpoint)
        {
            method = endpoint.Groups["method"].Value.ToUpperInvariant();
            path = endpoint.Groups["path"].Value.Trim();
            text = text[..endpoint.Index].Trim();
        }

        if (isMethod ?? text.Contains('('))
        {
            if (UmlNotation.ParseOperation(JavaToUml(text, isOperation: true)) is { } operation)
            {
                operation.IsStatic |= isStatic;
                operation.IsAbstract |= isAbstract;
                operation.HttpMethod = method;
                operation.Path = path;
                cls.Operations.Add(operation);
            }
        }
        else if (UmlNotation.ParseAttribute(JavaToUml(text, isOperation: false)) is { } attribute)
        {
            attribute.IsStatic |= isStatic;
            cls.Attributes.Add(attribute);
        }
    }

    /// <summary>
    /// A member in Java notation (<c>-String name</c>, <c>+int count(Order o)</c>) in UML notation (<c>-name : String</c>);
    /// members already in UML notation are returned as they are.
    /// </summary>
    private static string JavaToUml(string text, bool isOperation)
    {
        var visibility = text.Length > 0 && text[0] is '+' or '-' or '#' or '~' ? text[..1] : string.Empty;
        var body = text[visibility.Length..].Trim();
        if (isOperation)
        {
            var open = body.IndexOf('(');
            if (open < 0)
            {
                return text;
            }

            var close = body.LastIndexOf(')');
            var head = body[..open].Trim();
            var parameters = close > open ? body[(open + 1)..close] : body[(open + 1)..];
            var after = close > open ? body[(close + 1)..].Trim() : string.Empty;
            var words = head.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string? returnType = after.StartsWith(':') ? after[1..].Trim() : null;
            var operationName = head;
            if (words.Length >= 2 && returnType is null)
            {
                operationName = words[^1];
                returnType = string.Join(' ', words[..^1]);
            }

            var converted = string.Join(", ", SplitTopLevel(parameters).Select(p => JavaParameter(p.Trim())).Where(p => p.Length > 0));
            return $"{visibility}{operationName}({converted})" + (returnType is null ? string.Empty : $" : {returnType}");
        }

        // Attributes: split off the default value first.
        var equals = body.IndexOf('=');
        var declaration = equals >= 0 ? body[..equals].Trim() : body;
        var initial = equals >= 0 ? body[equals..] : string.Empty;
        if (declaration.Contains(':'))
        {
            return text;
        }

        var parts = declaration.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"{visibility}{parts[^1]} : {string.Join(' ', parts[..^1])} {initial}".TrimEnd() : text;
    }

    private static string JavaParameter(string parameter)
    {
        if (parameter.Length == 0 || parameter.Contains(':'))
        {
            return parameter;
        }

        var words = parameter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= 2 ? $"{words[^1]} : {string.Join(' ', words[..^1])}" : parameter;
    }

    private static IEnumerable<string> SplitTopLevel(string text)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            depth += text[i] is '<' or '(' or '[' ? 1 : text[i] is '>' or ')' or ']' ? -1 : 0;
            if (text[i] == ',' && depth == 0)
            {
                yield return text[start..i];
                start = i + 1;
            }
        }

        yield return text[start..];
    }

    /// <summary>The alias and display name of a declaration: <c>Order</c>, <c>"Order Line" as OL</c>, <c>OL as "Order Line"</c>, <c>Repository&lt;T&gt;</c>.</summary>
    private static (string Alias, string Display) NameAndAlias(string text)
    {
        if (DisplayAsAlias().Match(text) is { Success: true } a)
        {
            return (a.Groups["alias"].Value, PlantUmlText.Unquote(a.Groups["display"].Value));
        }

        if (AliasAsDisplay().Match(text) is { Success: true } b)
        {
            return (b.Groups["alias"].Value, PlantUmlText.Unquote(b.Groups["display"].Value));
        }

        var name = PlantUmlText.Unquote(text);
        var open = name.IndexOf('<');
        return (open > 0 ? name[..open].Trim() : name, name);
    }

    private static IEnumerable<string> Names(string text) =>
        text.Split(',').Select(n => PlantUmlText.Unquote(n)).Where(n => n.Length > 0);

    private UmlClass GetOrAdd(string alias, string display)
    {
        if (_byAlias.TryGetValue(alias, out var existing))
        {
            if (display != alias)
            {
                existing.Name = display;
            }

            return existing;
        }

        // A display name used as a reference: "Order Line" --> X
        var named = Model.Classes.FirstOrDefault(c => c.Name == display);
        if (named is not null)
        {
            _byAlias[alias] = named;
            return named;
        }

        var cls = new UmlClass { Name = display };
        if (_packages.Count > 0)
        {
            cls.TaggedValues.Set("namespace", string.Join('.', _packages));
        }

        Model.Classes.Add(cls);
        _byAlias[alias] = cls;
        return cls;
    }

    #endregion

    #region Relationships

    private bool ReadRelation(Relation relation)
    {
        // A dotted line between a note and an element attaches the note.
        foreach (var (note, other) in new[] { (relation.Left, relation.Right), (relation.Right, relation.Left) })
        {
            if (note.Kind == EndKind.Name && _notes.TryGetValue(note.Text, out var noteId))
            {
                if (Resolve(other) is { } attached)
                {
                    Model.NoteAttachments.Add(new NoteAttachment { NoteId = noteId, TargetId = attached.Id });
                }

                return true;
            }
        }

        if (Resolve(relation.Left) is not { } left || Resolve(relation.Right) is not { } right)
        {
            return false;
        }

        var arrow = relation.Arrow;
        var (kind, leftIsSource) = (arrow.LeftHead, arrow.RightHead) switch
        {
            (_, "|>") => (arrow.Dashed ? RelationshipKind.Realization : RelationshipKind.Inheritance, true),
            ("<|", _) => (arrow.Dashed ? RelationshipKind.Realization : RelationshipKind.Inheritance, false),
            ("*", _) => (RelationshipKind.Composition, true),
            (_, "*") => (RelationshipKind.Composition, false),
            ("o", _) => (RelationshipKind.Aggregation, true),
            (_, "o") => (RelationshipKind.Aggregation, false),
            ("" or "#" or "x" or "+" or "}", ">" or ">>") => (arrow.Dashed ? RelationshipKind.Dependency : RelationshipKind.DirectedAssociation, true),
            ("<" or "<<", "" or "#" or "x" or "+" or "{") => (arrow.Dashed ? RelationshipKind.Dependency : RelationshipKind.DirectedAssociation, false),
            _ => (RelationshipKind.Association, true),
        };

        // A realization of a class: the target is an interface.
        var target = leftIsSource ? right : left;
        if (kind == RelationshipKind.Realization && target.Kind == ClassifierKind.Class)
        {
            target.Kind = ClassifierKind.Interface;
        }

        var added = AddRelationship(kind, leftIsSource ? left : right, target, Label(relation.Label));
        added.SourceMultiplicity = leftIsSource ? relation.LeftMultiplicity : relation.RightMultiplicity;
        added.TargetMultiplicity = leftIsSource ? relation.RightMultiplicity : relation.LeftMultiplicity;
        return true;
    }

    private UmlClass? Resolve(End end) => end.Kind switch
    {
        EndKind.Name or EndKind.Quoted => GetOrAdd(end.Text, end.Text),
        _ => null,
    };

    private UmlRelationship AddRelationship(RelationshipKind kind, UmlClass source, UmlClass target, string? label = null)
    {
        var relationship = new UmlRelationship { Kind = kind, SourceId = source.Id, TargetId = target.Id, Label = label };
        Model.Relationships.Add(relationship);
        _lastRelationship = relationship.Id;
        return relationship;
    }

    /// <summary>A label without the arrow that shows which way to read it: <c>places &gt;</c> is <c>places</c>.</summary>
    private static string? Label(string? label)
    {
        var text = label?.Trim().TrimStart('<').TrimEnd('>').Trim();
        return string.IsNullOrEmpty(text) ? null : text.Replace("\\n", "\n");
    }

    #endregion

    private void AddNote(NoteSpec note)
    {
        var targets = new List<string>();
        if (note.Position == "on link" && _lastRelationship is not null)
        {
            targets.Add(_lastRelationship);
        }
        else if (note.ToLast && _lastClass is not null)
        {
            targets.Add(_lastClass);
        }

        targets.AddRange(note.Targets.Select(t => _byAlias.TryGetValue(t, out var c) ? c.Id : Model.Classes.FirstOrDefault(x => x.Name == t)?.Id).OfType<string>());
        var added = AddNote(note.Text, targets);
        if (note.Alias is { } alias)
        {
            _notes[alias] = added.Id;
        }
    }

    [GeneratedRegex(@"^(?<keyword>abstract\s+class|abstract|class|interface|enum|annotation|entity|struct|record|exception|metaclass|protocol|stereotype)\s+(?<rest>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex DeclarationLine();

    [GeneratedRegex(@"^(?:package|namespace)\s+(?:(?<display>""[^""]+"")(?:\s+as\s+\S+)?|(?<name>[^\s{<]+))[^{]*\{\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PackageLine();

    [GeneratedRegex(@"^together\s*\{\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TogetherLine();

    [GeneratedRegex(@"^(?<class>[A-Za-z_][\w.]*)\s*:\s*(?<member>.+)$")]
    private static partial Regex MemberLine();

    [GeneratedRegex(@"\s+(?=extends\s|implements\s)(?:extends\s+(?<extends>.+?))?\s*(?:implements\s+(?<implements>.+?))?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Heritage();

    [GeneratedRegex(@"<<\s*(?:\([^)]*\)\s*)?(?<text>[^>]*)>>")]
    private static partial Regex Stereotype();

    [GeneratedRegex(@"\s#{1,2}[\w\[\]#.:;,-]+")]
    private static partial Regex Colors();

    [GeneratedRegex(@"^(?<display>""[^""]+"")\s+as\s+(?<alias>[\w.]+)$")]
    private static partial Regex DisplayAsAlias();

    [GeneratedRegex(@"^(?<alias>[\w.]+)\s+as\s+(?<display>""[^""]+"")$")]
    private static partial Regex AliasAsDisplay();

    [GeneratedRegex(@"^(?:[-=_.]{2,}.*|[-=_.]{2,})$")]
    private static partial Regex Separator();

    [GeneratedRegex(@"\{(?<name>static|classifier|abstract|field|method)\}\s*", RegexOptions.IgnoreCase)]
    private static partial Regex Modifier();

    [GeneratedRegex(@"\s*<<\s*(?<method>GET|POST|PUT|PATCH|DELETE|HEAD|OPTIONS|TRACE)\b(?<path>[^>]*)>>\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Endpoint();
}
