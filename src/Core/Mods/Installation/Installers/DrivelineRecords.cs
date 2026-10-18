namespace Core.Mods.Installation.Installers;

internal static class DrivelineRecords
{
    public static IReadOnlyList<string> Parse(string contents)
    {
        var records = new List<string>();
        var lines = new List<string>();
        var indent = 0;
        using var reader = new StringReader(contents);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                FinishRecord();
                continue;
            }

            if (lines.Count == 0)
            {
                var header = line.TrimStart();
                if (!header.StartsWith("RECORD ", StringComparison.Ordinal) &&
                    !header.StartsWith("RECORD\t", StringComparison.Ordinal) &&
                    !header.StartsWith("RECORDGROUP ", StringComparison.Ordinal) &&
                    !header.StartsWith("RECORDGROUP\t", StringComparison.Ordinal)) continue;
                indent = line.Length - header.Length;
            }

            // Remove only actual indentation; a less-indented body line must not lose text.
            var remove = 0;
            while (remove < indent && remove < line.Length && char.IsWhiteSpace(line[remove])) remove++;
            lines.Add(line[remove..].TrimEnd());
        }
        FinishRecord();
        return records;

        void FinishRecord()
        {
            if (lines.Count == 0) return;
            records.Add(string.Join(Environment.NewLine, lines));
            lines.Clear();
        }
    }
}
