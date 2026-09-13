using System.Text;

namespace CCReimagined.Core.Codegen;

/// <summary>
/// An indentation-aware sink for generated source. The old tool concatenated strings with
/// embedded "\n" and then ran a brace-counting re-indenter over the result; tracking depth
/// as the code is written removes that whole pass and the mistakes it could make.
/// </summary>
public sealed class CodeWriter
{
    private readonly StringBuilder _sb = new();
    private readonly string _indentUnit;
    private int _depth;
    private bool _atLineStart = true;

    public CodeWriter(string indentUnit = "    ") => _indentUnit = indentUnit;

    public int Depth => _depth;

    public CodeWriter Line(string text = "")
    {
        if (text.Length == 0)
        {
            _sb.Append('\n');
            _atLineStart = true;
            return this;
        }

        WriteIndentIfNeeded();
        _sb.Append(text).Append('\n');
        _atLineStart = true;
        return this;
    }

    /// <summary>Writes a multi-line block, re-indenting every line to the current depth.</summary>
    public CodeWriter Lines(string block)
    {
        foreach (var line in block.Replace("\r\n", "\n").Split('\n'))
            Line(line);

        return this;
    }

    public CodeWriter Blank()
    {
        // Collapse runs of blank lines so the output never has gaps of two or more.
        if (_sb.Length > 0 && !_sb.ToString().EndsWith("\n\n", StringComparison.Ordinal))
            _sb.Append('\n');

        _atLineStart = true;
        return this;
    }

    public CodeWriter Append(string text)
    {
        WriteIndentIfNeeded();
        _sb.Append(text);
        _atLineStart = false;
        return this;
    }

    public CodeWriter Indent()
    {
        _depth++;
        return this;
    }

    public CodeWriter Outdent()
    {
        _depth = Math.Max(0, _depth - 1);
        return this;
    }

    /// <summary>Opens a braced block; dispose (or use the <c>using</c> form) to close it.</summary>
    public Block Open(string header)
    {
        Line(header);
        Line("{");
        Indent();
        return new Block(this, "}");
    }

    /// <summary>Opens a braced block closed with a trailing token, e.g. <c>};</c>.</summary>
    public Block Open(string header, string closer)
    {
        Line(header);
        Line("{");
        Indent();
        return new Block(this, closer);
    }

    /// <summary>
    /// Emits a C# raw string literal holding SQL, indented so the closing delimiter lines
    /// up and the runtime value carries no leading whitespace.
    /// </summary>
    public CodeWriter RawStringLiteral(string declaration, string content)
    {
        Line($"{declaration} =");
        Indent();
        Line("\"\"\"");

        foreach (var line in content.Replace("\r\n", "\n").TrimEnd().Split('\n'))
            Line(line);

        Line("\"\"\";");
        Outdent();
        return this;
    }

    /// <summary>Emits a <c>///</c> summary comment, wrapping at a readable width.</summary>
    public CodeWriter DocComment(params string[] lines)
    {
        Line("/// <summary>");
        foreach (var line in lines)
            Line($"/// {line}");
        Line("/// </summary>");
        return this;
    }

    private void WriteIndentIfNeeded()
    {
        if (!_atLineStart)
            return;

        for (var i = 0; i < _depth; i++)
            _sb.Append(_indentUnit);

        _atLineStart = false;
    }

    public override string ToString() => _sb.ToString();

    public readonly struct Block : IDisposable
    {
        private readonly CodeWriter _writer;
        private readonly string _closer;

        internal Block(CodeWriter writer, string closer)
        {
            _writer = writer;
            _closer = closer;
        }

        public void Dispose()
        {
            _writer.Outdent();
            _writer.Line(_closer);
        }
    }
}
