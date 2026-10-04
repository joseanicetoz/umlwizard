namespace UmlWizard.CodeGeneration;

/// <summary>
/// A type as written in the diagram, e.g. <c>int</c>, <c>List&lt;Order&gt;</c>, <c>Map&lt;string, int&gt;</c> or
/// <c>Order[]</c>, parsed so each language can spell it its own way.
/// </summary>
public sealed record TypeExpression(string Name, IReadOnlyList<TypeExpression> Arguments, int ArrayRank)
{
    /// <summary>The expression, or <c>null</c> for an empty or unreadable type (it is then used as written).</summary>
    public static TypeExpression? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var position = 0;
        var type = ParseType(text, ref position);
        SkipSpaces(text, ref position);
        return position == text.Length ? type : null;
    }

    /// <summary>Every type name in the expression, e.g. List, Order for <c>List&lt;Order&gt;</c>.</summary>
    public IEnumerable<string> Names() => Arguments.SelectMany(a => a.Names()).Prepend(Name);

    private static TypeExpression? ParseType(string text, ref int position)
    {
        SkipSpaces(text, ref position);
        var start = position;
        while (position < text.Length && (char.IsLetterOrDigit(text[position]) || text[position] is '_' or '.' or ':'))
        {
            position++;
        }

        if (position == start)
        {
            return null;
        }

        var name = text[start..position];
        var arguments = new List<TypeExpression>();
        SkipSpaces(text, ref position);
        if (position < text.Length && text[position] == '<')
        {
            do
            {
                position++;
                if (ParseType(text, ref position) is not { } argument)
                {
                    return null;
                }

                arguments.Add(argument);
                SkipSpaces(text, ref position);
            }
            while (position < text.Length && text[position] == ',');

            if (position >= text.Length || text[position] != '>')
            {
                return null;
            }

            position++;
        }

        var rank = 0;
        SkipSpaces(text, ref position);
        while (position + 1 < text.Length && text[position] == '[' && text[position + 1] == ']')
        {
            rank++;
            position += 2;
            SkipSpaces(text, ref position);
        }

        return new TypeExpression(name, arguments, rank);
    }

    private static void SkipSpaces(string text, ref int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position]))
        {
            position++;
        }
    }
}
