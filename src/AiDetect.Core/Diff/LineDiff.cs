using System.Text;
using DiffPlex;
using DiffPlex.Model;

namespace AiDetect.Core.Diff;

/// <summary>Построение унифицированного диффа по двум версиям файла (когда GitLab не отдал дифф для большого файла).</summary>
public static class LineDiff
{
    public static string BuildUnifiedDiff(string? oldText, string newText, int context = 3)
    {
        var differ = new Differ();
        var result = differ.CreateLineDiffs(oldText ?? "", newText, ignoreWhitespace: false, ignoreCase: false);
        var oldLines = result.PiecesOld;
        var newLines = result.PiecesNew;
        var sb = new StringBuilder();
        foreach (DiffBlock block in result.DiffBlocks)
        {
            int oldStart = Math.Max(0, block.DeleteStartA - context);
            int newStart = Math.Max(0, block.InsertStartB - context);
            int preContext = block.InsertStartB - newStart;
            int deleteEnd = block.DeleteStartA + block.DeleteCountA;
            int insertEnd = block.InsertStartB + block.InsertCountB;
            int postContext = Math.Min(context, newLines.Count - insertEnd);
            int oldCount = preContext + block.DeleteCountA + postContext;
            int newCount = preContext + block.InsertCountB + postContext;
            sb.Append("@@ -").Append(oldStart + 1).Append(',').Append(oldCount).Append(" +").Append(newStart + 1).Append(',').Append(newCount).Append(" @@\n");
            for (int i = newStart; i < block.InsertStartB; i++)
            {
                sb.Append(' ').Append(newLines[i]).Append('\n');
            }

            for (int i = block.DeleteStartA; i < deleteEnd && i < oldLines.Count; i++)
            {
                sb.Append('-').Append(oldLines[i]).Append('\n');
            }

            for (int i = block.InsertStartB; i < insertEnd && i < newLines.Count; i++)
            {
                sb.Append('+').Append(newLines[i]).Append('\n');
            }

            for (int i = insertEnd; i < insertEnd + postContext; i++)
            {
                sb.Append(' ').Append(newLines[i]).Append('\n');
            }
        }

        return sb.ToString();
    }
}
