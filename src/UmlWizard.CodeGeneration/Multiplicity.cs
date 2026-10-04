namespace UmlWizard.CodeGeneration;

/// <summary>A multiplicity such as <c>1</c>, <c>0..1</c>, <c>*</c> or <c>1..*</c>.</summary>
public readonly record struct Multiplicity(int Lower, int? Upper)
{
    /// <summary>More than one: the end is a collection.</summary>
    public bool IsMany => Upper is null or > 1;

    /// <summary>Zero or one.</summary>
    public bool IsOptional => Lower == 0 && Upper == 1;

    /// <summary>The multiplicity; exactly one when it is empty or unreadable.</summary>
    public static Multiplicity Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new(1, 1);
        }

        var parts = text.Split("..", StringSplitOptions.TrimEntries);
        var lower = Bound(parts[0]);
        var upper = parts.Length > 1 ? Bound(parts[1]) : lower;
        return new(lower ?? 0, upper);

        static int? Bound(string part) => int.TryParse(part, out var value) ? value : null;
    }
}
