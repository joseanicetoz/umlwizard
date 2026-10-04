using System.Text.RegularExpressions;
using UmlWizard.Core.Model;

namespace UmlWizard.PlantUml;

/// <summary>The diagrams read from a PlantUML text, and what could not be read.</summary>
public sealed record PlantUmlImportResult(IReadOnlyList<DiagramDocument> Documents, IReadOnlyList<string> Warnings);

/// <summary>
/// Reads PlantUML (the text between <c>@startuml</c> and <c>@enduml</c>) into diagrams, one per <c>@startuml</c>, laid
/// out automatically: class, sequence, state, activity, use case, component and deployment diagrams. The type of each
/// diagram is recognised from its content, as PlantUML does; its title, if any, names it. What UML Wizard has no place
/// for (styling, combined fragments, composite states, …) is left out and reported.
/// </summary>
public static partial class PlantUmlImporter
{
    /// <summary>The file extensions PlantUML files usually have.</summary>
    public static IReadOnlyList<string> FilePatterns { get; } = ["*.puml", "*.plantuml", "*.pu", "*.iuml", "*.wsd", "*.txt"];

    /// <exception cref="IOException">The file cannot be read.</exception>
    /// <exception cref="InvalidDataException">The file holds no diagram UML Wizard can read.</exception>
    public static PlantUmlImportResult ImportFile(string path) =>
        Import(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));

    /// <exception cref="InvalidDataException">The text holds no diagram UML Wizard can read.</exception>
    public static PlantUmlImportResult Import(string text, string fallbackName)
    {
        var blocks = PlantUmlText.Diagrams(text).Where(b => b.Lines.Count > 0).ToList();
        var documents = new List<DiagramDocument>();
        var warnings = new List<string>();
        for (var i = 0; i < blocks.Count; i++)
        {
            var (name, lines) = blocks[i];
            var title = Title(lines);
            var diagramName = title ?? name ?? (blocks.Count > 1 ? $"{fallbackName} {i + 1}" : fallbackName);
            var prefix = blocks.Count > 1 ? $"{diagramName}: " : string.Empty;
            if (Detect(lines) is not { } type)
            {
                warnings.Add($"{prefix}the kind of diagram was not recognised; it was not imported. UML Wizard reads class, sequence, state, activity, use case, component and deployment diagrams.");
                continue;
            }

            DiagramReader reader = type switch
            {
                DiagramType.Class => new ClassDiagramReader(diagramName),
                DiagramType.Sequence => new SequenceDiagramReader(diagramName),
                DiagramType.StateMachine => new StateDiagramReader(diagramName),
                DiagramType.Activity => new ActivityDiagramReader(diagramName),
                _ => new DescriptionDiagramReader(diagramName, type),
            };
            var (document, readerWarnings) = reader.Read(lines);
            documents.Add(document);
            warnings.AddRange(readerWarnings.Select(w => prefix + w));
        }

        if (documents.Count == 0)
        {
            throw new InvalidDataException(warnings.Count > 0 ? warnings[0] : "The file holds no PlantUML diagram (@startuml … @enduml).");
        }

        return new PlantUmlImportResult(documents, warnings);
    }

    /// <summary>The diagram's title: <c>title Checkout</c>, or the lines of a <c>title … end title</c> block.</summary>
    private static string? Title(List<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var match = TitleLine().Match(lines[i]);
            if (!match.Success)
            {
                continue;
            }

            var text = match.Groups["text"].Value.Trim();
            if (text.Length > 0)
            {
                return PlantUmlText.Unquote(text).Replace("\n", " ");
            }

            var block = lines.Skip(i + 1).TakeWhile(l => !EndTitle().IsMatch(l)).ToList();
            return block.Count > 0 ? string.Join(" ", block).Trim() : null;
        }

        return null;
    }

    /// <summary>
    /// The kind of a diagram, recognised from its content as PlantUML does; null when it is none UML Wizard reads.
    /// The strongest signs come first: an activity's actions, a state machine's [*], a sequence diagram's participants.
    /// </summary>
    internal static DiagramType? Detect(List<string> lines)
    {
        var relations = lines.Select(PlantUmlText.ParseRelation).OfType<Relation>().ToList();
        bool Any(Regex pattern) => lines.Any(pattern.IsMatch);
        bool AnyEnd(params EndKind[] kinds) => relations.Any(r => kinds.Contains(r.Left.Kind) || kinds.Contains(r.Right.Kind));

        if (Any(ActivityLine()) || AnyEnd(EndKind.ActivityStartEnd))
        {
            return DiagramType.Activity;
        }

        if (Any(StateLine()) || AnyEnd(EndKind.StartEnd))
        {
            return DiagramType.StateMachine;
        }

        if (Any(SequenceLine()))
        {
            return DiagramType.Sequence;
        }

        var descriptionElements = Any(UseCaseLine()) || AnyEnd(EndKind.UseCase, EndKind.Actor) || Any(DeploymentLine()) || Any(ComponentLine()) ||
                                  AnyEnd(EndKind.Component, EndKind.Interface);
        if (Any(ClassLine()) && !descriptionElements || relations.Any(r => IsClassArrow(r.Arrow)) && !descriptionElements)
        {
            return DiagramType.Class;
        }

        if (Any(UseCaseLine()) || AnyEnd(EndKind.UseCase, EndKind.Actor))
        {
            return DiagramType.UseCase;
        }

        if (Any(DeploymentLine()))
        {
            return DiagramType.Deployment;
        }

        if (Any(ComponentLine()) || AnyEnd(EndKind.Component, EndKind.Interface))
        {
            return DiagramType.Component;
        }

        if (Any(ClassLine()))
        {
            return DiagramType.Class;
        }

        // Only actors and messages between them: a sequence diagram.
        if (lines.Any(SequenceReaderPatterns.IsMessage))
        {
            return DiagramType.Sequence;
        }

        return null;
    }

    private static bool IsClassArrow(Arrow arrow) =>
        arrow.LeftHead is "<|" or "*" or "o" || arrow.RightHead is "|>" or "*" or "o";

    [GeneratedRegex(@"^title(?:\s+(?<text>.*))?$", RegexOptions.IgnoreCase)]
    private static partial Regex TitleLine();

    [GeneratedRegex(@"^end\s*title$", RegexOptions.IgnoreCase)]
    private static partial Regex EndTitle();

    [GeneratedRegex(@"^(?:start|stop|kill|detach|fork|repeat|end\s+fork|endif|endwhile|split)$|^:.*[;|<>/\]}]$|^if\s*\(.*\)\s*(?:then|is)|^while\s*\(|^repeat\s*:|^switch\s*\(|^\|[^|]+\|$", RegexOptions.IgnoreCase)]
    private static partial Regex ActivityLine();

    [GeneratedRegex(@"^state\s+|^\[\*\]", RegexOptions.IgnoreCase)]
    private static partial Regex StateLine();

    [GeneratedRegex(@"^(?:participant|boundary|control|collections|queue)\s|^(?:autonumber|activate|deactivate|destroy|return)\b|^(?:alt|loop|opt|par|critical|break|group)\b|^==.*==$|^\.\.\.$|^\|\|\d*\|\|$|^ref\s+over\b|^(?:note|hnote|rnote)\s+over\b", RegexOptions.IgnoreCase)]
    private static partial Regex SequenceLine();

    [GeneratedRegex(@"^(?:abstract\s+class|abstract|class|enum|annotation|struct|record|exception|metaclass|protocol|entity)\s+[^\s]|^interface\s+\S+.*\{", RegexOptions.IgnoreCase)]
    private static partial Regex ClassLine();

    [GeneratedRegex(@"^usecase\s|^\([^)*]+\)(?:\s+as\s+\S+)?\s*$|^:[^:]+:(?:\s+as\s+\S+)?\s*$|^actor\s.*\{|^rectangle\s", RegexOptions.IgnoreCase)]
    private static partial Regex UseCaseLine();

    [GeneratedRegex(@"^(?:node|artifact|device|cloud|frame|database|storage|stack|file)\s", RegexOptions.IgnoreCase)]
    private static partial Regex DeploymentLine();

    [GeneratedRegex(@"^component\s|^\[[^\]*][^\]]*\](?:\s+as\s+\S+)?|^\(\)\s|^port(?:in|out)?\s", RegexOptions.IgnoreCase)]
    private static partial Regex ComponentLine();
}

/// <summary>A note as the PlantUML text gives it: its text, and what it is attached to.</summary>
/// <param name="Alias">The name a floating note is given (<c>note as N</c>), to be linked later (<c>N .. Order</c>).</param>
/// <param name="Targets">The names or aliases it is attached to (<c>of Order</c>, <c>over A, B</c>).</param>
/// <param name="Position">left, right, top, bottom, over, across, link (the last relationship) or floating.</param>
/// <param name="ToLast">Attached to the last element or message, as <c>note left</c> without <c>of</c> is.</param>
internal sealed record NoteSpec(string Text, string? Alias, IReadOnlyList<string> Targets, string Position, bool ToLast);

/// <summary>
/// What the readers of every kind of diagram share: the line loop, notes in all their forms, directives that only
/// style the drawing (<c>skinparam</c>, <c>hide</c>, …), which are skipped, and the report of lines not understood.
/// </summary>
internal abstract partial class DiagramReader(string name, DiagramType type)
{
    private readonly List<string> _unread = [];
    private readonly Dictionary<string, (string One, string Many, int Count)> _losses = [];

    protected DiagramDocument Document { get; } = DiagramDocument.Create(name, type);

    protected List<string> Warnings { get; } = [];

    /// <summary>PlantUML's <c>left to right direction</c>, for the layout.</summary>
    protected bool LeftToRight { get; private set; }

    public (DiagramDocument Document, List<string> Warnings) Read(List<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (!ReadLine(lines, ref i) && !ReadCommon(lines, ref i))
            {
                _unread.Add(lines[i]);
            }
        }

        Finish();
        foreach (var (one, many, count) in _losses.Values)
        {
            Warnings.Add($"{count} {(count == 1 ? one : many)}.");
        }

        if (_unread.Count > 0)
        {
            var shown = string.Join(", ", _unread.Take(3).Select(l => $"\"{(l.Length > 60 ? l[..60] + "…" : l)}\""));
            Warnings.Add($"{_unread.Count} {(_unread.Count == 1 ? "line was" : "lines were")} not understood and left out: {shown}{(_unread.Count > 3 ? ", …" : string.Empty)}.");
        }

        return (Document, Warnings);
    }

    /// <summary>Reads the line at <paramref name="i"/> (and the lines of a block it starts); false when it is not one of this reader's.</summary>
    protected abstract bool ReadLine(List<string> lines, ref int i);

    /// <summary>Lays the diagram out and places its notes.</summary>
    protected abstract void Finish();

    /// <summary>Counts something the diagram has no place for; each kind is reported once, with its count.</summary>
    protected void Lose(string key, string one, string many)
    {
        var count = _losses.TryGetValue(key, out var current) ? current.Count : 0;
        _losses[key] = (one, many, count + 1);
    }

    /// <summary>Directives every kind of diagram may have, which change how PlantUML draws it, not what it shows.</summary>
    private bool ReadCommon(List<string> lines, ref int i)
    {
        var line = lines[i];
        if (TitleOrStyling().IsMatch(line))
        {
            return true;
        }

        if (DirectionLine().Match(line) is { Success: true } direction)
        {
            LeftToRight = direction.Groups[1].Value.StartsWith("left", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        if (BlockStart().Match(line) is { Success: true } block)
        {
            // skinparam { … }, legend … end legend, header … end header: skipped whole.
            var end = block.Groups["kind"].Value.ToLowerInvariant() switch
            {
                "skinparam" or "style" => "}",
                var kind => "end" + kind,
            };
            while (i + 1 < lines.Count && !string.Equals(lines[i].Replace(" ", string.Empty), end, StringComparison.OrdinalIgnoreCase) &&
                   !(end == "}" && lines[i] == "}"))
            {
                i++;
            }

            return true;
        }

        if (line.StartsWith("!include", StringComparison.OrdinalIgnoreCase))
        {
            Lose("include", "included file (!include) was not read", "included files (!include) were not read");
            return true;
        }

        return line.StartsWith('!');
    }

    #region Notes

    /// <summary>Reads a note at <paramref name="i"/>, with the lines of its block; null when the line is not a note.</summary>
    protected static NoteSpec? ReadNote(List<string> lines, ref int i)
    {
        var match = NoteLine().Match(lines[i]);
        if (!match.Success)
        {
            return null;
        }

        var rest = match.Groups["rest"].Value.Trim();
        var floating = match.Groups["floating"].Success;
        string? text = null;
        string? alias = null;
        var targets = new List<string>();
        var position = "floating";
        var toLast = false;

        if (QuotedNote().Match(rest) is { Success: true } quoted)
        {
            return new NoteSpec(PlantUmlText.Unquote(quoted.Groups["text"].Value), quoted.Groups["alias"].Value, [], "floating", false);
        }

        if (AliasNote().Match(rest) is { Success: true } aliased)
        {
            alias = aliased.Groups["alias"].Value;
        }
        else if (PositionedNote().Match(rest) is { Success: true } positioned)
        {
            position = positioned.Groups["position"].Value.ToLowerInvariant();
            var of = positioned.Groups["targets"].Value.Trim();
            if (position is "over" || of.Length > 0)
            {
                targets.AddRange(of.Split(',').Select(t => PlantUmlText.Unquote(StripColor(t))).Where(t => t.Length > 0));
            }
            else
            {
                toLast = position is not ("across" or "floating") && !floating;
            }

            if (positioned.Groups["text"].Success)
            {
                text = positioned.Groups["text"].Value.Trim();
            }

            if (floating)
            {
                position = "floating";
            }
        }
        else if (rest.Length > 0 && !rest.StartsWith(':'))
        {
            return null;
        }
        else if (rest.StartsWith(':'))
        {
            text = rest[1..].Trim();
            toLast = !floating;
        }

        if (text is null)
        {
            // A block, up to end note.
            var block = new List<string>();
            while (i + 1 < lines.Count && !EndNote().IsMatch(lines[i + 1]))
            {
                block.Add(lines[++i]);
            }

            i++;
            text = string.Join("\n", block);
        }
        else
        {
            text = text.Replace("\\n", "\n");
        }

        return new NoteSpec(text, alias, targets, position, toLast);
    }

    /// <summary>Adds a note to the diagram, attached to the elements with the given ids.</summary>
    protected NoteElement AddNote(string text, IEnumerable<string> targetIds)
    {
        var note = new NoteElement { Text = text };
        Document.Model.Notes.Add(note);
        foreach (var target in targetIds.Distinct())
        {
            Document.Model.NoteAttachments.Add(new NoteAttachment { NoteId = note.Id, TargetId = target });
        }

        return note;
    }

    /// <summary>
    /// Places the notes: to the right of the first element each one is attached to, else below the diagram. The
    /// diagram's elements must be laid out.
    /// </summary>
    protected void PlaceNotes()
    {
        var model = Document.Model;
        if (model.Notes.Count == 0)
        {
            return;
        }

        using var renderer = Core.Rendering.DiagramRenderer.Create(Document.DiagramType);
        renderer.UpdateLayout(Document);
        var content = renderer.GetContentBounds(Document);
        var below = (double)content.Bottom + 40;
        var belowX = (double)content.Left;
        var taken = new List<(double X, double Y)>();
        foreach (var note in model.Notes)
        {
            var layout = Document.Layout.Find(note.Id) ?? Document.Layout.Add(new ElementLayout { ElementId = note.Id });
            var lines = note.Text.Split('\n');
            layout.Width = Math.Clamp(lines.Max(l => l.Length) * 7.5 + 24, NoteElement.DefaultWidth, 320);
            layout.Height = Math.Max(NoteElement.DefaultHeight, lines.Length * 18 + 24);
            var target = model.NoteAttachments.Where(a => a.NoteId == note.Id)
                .Select(a => renderer.GetElementBounds(Document, a.TargetId))
                .FirstOrDefault(b => b is not null);
            if (target is { } bounds && Document.DiagramType != DiagramType.Sequence)
            {
                (layout.X, layout.Y) = (bounds.Right + 40, bounds.Top);
                while (taken.Any(t => Math.Abs(t.X - layout.X) < 40 && Math.Abs(t.Y - layout.Y) < layout.Height))
                {
                    layout.Y += layout.Height + 10;
                }
            }
            else if (Document.DiagramType != DiagramType.Sequence || layout.Y == 0)
            {
                (layout.X, layout.Y) = (belowX, below);
                belowX += layout.Width + 30;
            }

            taken.Add((layout.X, layout.Y));
        }
    }

    private static string StripColor(string text) => ColorSuffix().Replace(text, string.Empty).Trim();

    #endregion

    [GeneratedRegex(@"^(?:title\b|skinparam\s+\S+\s+\S|hide\b|show\b|scale\b|caption\b|header\b.+|footer\b.+|autonumber\b|set\s+namespaceseparator\b|allowmixing\b|allow_mixing\b|newpage\b|mainframe\b|page\s|top\s+to\s+bottom\s+direction|left\s+to\s+right\s+direction)", RegexOptions.IgnoreCase)]
    private static partial Regex TitleOrStyling();

    [GeneratedRegex(@"^(left\s+to\s+right|top\s+to\s+bottom)\s+direction$", RegexOptions.IgnoreCase)]
    private static partial Regex DirectionLine();

    [GeneratedRegex(@"^(?<kind>skinparam|style|legend|header|footer|title)\b[^{]*(?:\{)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex BlockStart();

    [GeneratedRegex(@"^(?:(?<floating>floating)\s+)?[hr]?note\b(?<rest>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex NoteLine();

    [GeneratedRegex(@"^(?<text>""[^""]*"")\s+as\s+(?<alias>\w+)(?:\s+#\S+)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex QuotedNote();

    [GeneratedRegex(@"^as\s+(?<alias>\w+)(?:\s+#\S+)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex AliasNote();

    [GeneratedRegex(@"^(?<position>left|right|top|bottom|over|across|on\s+link)(?:\s+of)?(?<targets>(?:\s+[^:#]+?)?)(?:\s+#\S+)?\s*(?::\s*(?<text>.*))?$", RegexOptions.IgnoreCase)]
    private static partial Regex PositionedNote();

    [GeneratedRegex(@"^end\s*[hr]?note$", RegexOptions.IgnoreCase)]
    private static partial Regex EndNote();

    [GeneratedRegex(@"\s*#\S+$")]
    private static partial Regex ColorSuffix();
}
