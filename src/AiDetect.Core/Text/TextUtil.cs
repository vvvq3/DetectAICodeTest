namespace AiDetect.Core.Text;

/// <summary>Общие текстовые утилиты для всех языков.</summary>
public static class TextUtil
{
    public static bool ContainsEmoji(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            int cp = char.IsSurrogatePair(s, i) ? char.ConvertToUtf32(s, i) : s[i];
            if (cp is >= 0x1F300 and <= 0x1FAFF or >= 0x2600 and <= 0x27BF or 0x2B50 or 0x2B55 or 0x2705 or 0x274C or 0x2714 or 0x2716)
            {
                return true;
            }

            if (char.IsSurrogatePair(s, i))
            {
                i++;
            }
        }

        return false;
    }
}
