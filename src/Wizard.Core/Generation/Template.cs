/// <summary>
/// Renders the code and paths held in the registry. They are string literals, so nothing may leak the
/// line endings of the file holding them: every template is normalized to lf here.
/// </summary>
public static class Template
{
    /// <summary>A line ending with this is kept only on an endpoint that receives, such as routing a send back to itself.</summary>
    const string receivingMarker = " //:receiving";

    public static IEnumerable<string> Lines(string text) =>
        text.Replace("\r\n", "\n").Trim('\n').Split('\n');

    /// <summary>Replaces each of <see cref="Plan.Tokens"/>, and resolves the lines marked for a receiving endpoint.</summary>
    public static string Render(string template, Plan plan)
    {
        var lines = new List<string>();
        foreach (var line in Lines(template))
        {
            if (!line.EndsWith(receivingMarker, StringComparison.Ordinal))
            {
                lines.Add(line);
                continue;
            }

            if (plan.Receives)
            {
                lines.Add(line[..^receivingMarker.Length]);
            }
        }

        var text = string.Join('\n', lines);
        foreach (var (token, value) in plan.Tokens)
        {
            text = text.Replace(token, value);
        }

        return text;
    }

    /// <summary>Prefixes every non-empty line, for nesting rendered code inside a block.</summary>
    public static string Indent(string text, string indent)
    {
        string IndentLine(string line)
        {
            if (line.Length == 0)
            {
                return line;
            }

            return indent + line;
        }

        return string.Join('\n', text.Split('\n').Select(IndentLine));
    }
}
