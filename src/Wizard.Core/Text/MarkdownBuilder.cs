/// <summary>Appends markdown blocks separated by exactly one blank line, with lf line endings.</summary>
public sealed class MarkdownBuilder
{
    readonly StringBuilder builder = new();

    void Block(string text)
    {
        if (builder.Length > 0)
        {
            builder.Append('\n');
        }

        builder.Append(text.Replace("\r\n", "\n").Trim('\n'));
        builder.Append('\n');
    }

    public void Heading(int level, string text) =>
        Block($"{new string('#', level)} {text}");

    public void Paragraph(string text) =>
        Block(text);

    public void Bullets(IEnumerable<string> items) =>
        Block(string.Join('\n', items.Select(_ => $" * {_}")));

    public void Numbered(IEnumerable<string> items) =>
        Block(string.Join('\n', items.Select((item, index) => $"{index + 1}. {item}")));

    public void Code(string code, string language = "") =>
        Block($"```{language}\n{code.Replace("\r\n", "\n").Trim('\n')}\n```");

    /// <summary>Markdown that is already formatted, such as content copied from Verify's docs.</summary>
    public void Raw(string markdown) =>
        Block(markdown);

    public override string ToString() =>
        builder.ToString();
}
