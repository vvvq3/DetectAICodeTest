using System.Text.RegularExpressions;

namespace AiDetect.Core.Diff;

/// <summary>Разбор унифицированного диффа: какие строки нового файла добавлены, сколько удалено.</summary>
public static partial class UnifiedDiff
{
    [GeneratedRegex(@"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@")]
    private static partial Regex HunkHeader();

    public sealed record DiffStats(HashSet<int> AddedLines, int Added, int Removed, List<string> AddedText, List<string> RemovedText);

    public static DiffStats Parse(string? diff)
    {
        var added = new HashSet<int>();
        var addedText = new List<string>();
        var removedText = new List<string>();
        int removed = 0;
        if (string.IsNullOrEmpty(diff))
        {
            return new DiffStats(added, 0, 0, addedText, removedText);
        }

        int newLine = 0;
        bool inHunk = false;
        foreach (var rawLine in diff.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var m = HunkHeader().Match(line);
            if (m.Success)
            {
                newLine = int.Parse(m.Groups[3].Value);
                inHunk = true;
                continue;
            }

            if (!inHunk)
            {
                continue;
            }

            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                inHunk = false;
                continue;
            }

            if (line.StartsWith('+'))
            {
                added.Add(newLine);
                addedText.Add(line[1..]);
                newLine++;
            }
            else if (line.StartsWith('-'))
            {
                removed++;
                removedText.Add(line[1..]);
            }
            else if (line.StartsWith('\\'))
            {
                // "\ No newline at end of file"
            }
            else
            {
                newLine++;
            }
        }

        return new DiffStats(added, added.Count, removed, addedText, removedText);
    }

    /// <summary>
    /// Восстанавливает «новую сторону» файла из диффа, если полный текст файла недоступен.
    /// Строки вне ханков заменяются пустыми, чтобы номера строк совпадали с исходными.
    /// </summary>
    public static string ReconstructNewSide(string diff)
    {
        var result = new List<string>();
        int newLine = 0;
        bool inHunk = false;
        foreach (var rawLine in diff.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var m = HunkHeader().Match(line);
            if (m.Success)
            {
                int start = int.Parse(m.Groups[3].Value);
                while (result.Count < start - 1)
                {
                    result.Add("");
                }

                newLine = start;
                inHunk = true;
                continue;
            }

            if (!inHunk || line.StartsWith('\\'))
            {
                continue;
            }

            if (line.StartsWith('-'))
            {
                continue;
            }

            result.Add(line.Length > 0 ? line[1..] : "");
            newLine++;
        }

        return string.Join('\n', result);
    }
}
