using System.Text;

namespace UmlWizard.CodeGeneration;

/// <summary>Writes indented source lines; files end with a single line break.</summary>
/// <param name="indentUnit">One level of indentation.</param>
/// <param name="braceOnOwnLine">Opening braces go on a line of their own (C#), not at the end of the header.</param>
public sealed class CodeWriter(string indentUnit = "    ", bool braceOnOwnLine = false)
{
    private readonly StringBuilder _text = new();
    private int _depth;
    private bool _pendingBlank;

    // True right after a block or indentation starts, where a blank line would look odd.
    private bool _justOpened = true;

    /// <summary>Writes a line at the current indentation; an empty line is written as a blank line.</summary>
    public CodeWriter Line(string text = "")
    {
        if (text.Length == 0)
        {
            return Blank();
        }

        if (_pendingBlank)
        {
            _text.Append('\n');
            _pendingBlank = false;
        }

        for (var i = 0; i < _depth; i++)
        {
            _text.Append(indentUnit);
        }

        _text.Append(text).Append('\n');
        _justOpened = false;
        return this;
    }

    /// <summary>
    /// Writes a label one level further out than the current lines, e.g. <c>public:</c> in a C++ class; what follows
    /// is treated as the start of a block.
    /// </summary>
    public CodeWriter Label(string text)
    {
        _depth--;
        Line(text);
        _depth++;
        Opened();
        return this;
    }

    /// <summary>
    /// Separates what follows by one blank line: several calls give one blank line, and none is written at the start
    /// of the file, of a block or at its end.
    /// </summary>
    public CodeWriter Blank()
    {
        _pendingBlank = !_justOpened;
        return this;
    }

    /// <summary>Indents the lines written until the result is disposed.</summary>
    public IDisposable Indent()
    {
        _depth++;
        Opened();
        return new Closer(this, string.Empty);
    }

    /// <summary>
    /// Writes <paramref name="header"/> followed by <paramref name="open"/> (e.g. "{"; empty for none) and indents until
    /// disposed, then writes <paramref name="close"/> (e.g. "}" or "end").
    /// </summary>
    public IDisposable Block(string header, string open = "{", string close = "}")
    {
        if (braceOnOwnLine && open.Length > 0 && header.Length > 0)
        {
            Line(header);
            Line(open);
        }
        else
        {
            Line(open.Length == 0 ? header : header.Length == 0 ? open : header + " " + open);
        }

        _depth++;
        Opened();
        return new Closer(this, close);
    }

    public override string ToString() => _text.ToString();

    private void Opened()
    {
        _pendingBlank = false;
        _justOpened = true;
    }

    private sealed class Closer(CodeWriter writer, string close) : IDisposable
    {
        public void Dispose()
        {
            writer._depth--;
            writer._pendingBlank = false;
            if (close.Length > 0)
            {
                writer.Line(close);
            }
        }
    }
}
