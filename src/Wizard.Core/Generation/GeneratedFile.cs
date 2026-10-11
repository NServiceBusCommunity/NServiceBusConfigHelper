/// <summary>One file of the generated solution. Text is written as UTF-8 with lf line endings.</summary>
/// <param name="Path">Relative to the solution directory, with forward slashes.</param>
/// <param name="Bom">Verified files are UTF-8 with a byte order mark (Verify's convention).</param>
public sealed record GeneratedFile(string Path, string Text, bool Bom = false)
{
    static UTF8Encoding withBom = new(encoderShouldEmitUTF8Identifier: true);
    static UTF8Encoding withoutBom = new(encoderShouldEmitUTF8Identifier: false);

    public byte[] ToBytes()
    {
        var text = Text.Replace("\r\n", "\n");
        if (Bom)
        {
            return [.. withBom.GetPreamble(), .. withBom.GetBytes(text)];
        }

        return withoutBom.GetBytes(text);
    }
}
