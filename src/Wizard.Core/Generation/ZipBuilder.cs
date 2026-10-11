/// <summary>
/// Zips the generated files under a folder named after the solution. Runs in the browser.
/// Entries get a fixed timestamp so the same input always produces the same bytes.
/// </summary>
public static class ZipBuilder
{
    static DateTimeOffset timestamp = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static byte[] Build(string rootFolder, IEnumerable<GeneratedFile> files)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files.OrderBy(_ => _.Path, StringComparer.Ordinal))
            {
                var entry = archive.CreateEntry($"{rootFolder}/{file.Path}", CompressionLevel.Optimal);
                entry.LastWriteTime = timestamp;
                using var entryStream = entry.Open();
                entryStream.Write(file.ToBytes());
            }
        }

        return stream.ToArray();
    }
}
