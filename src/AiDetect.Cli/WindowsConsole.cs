using System.Runtime.InteropServices;

namespace AiDetect.Cli;

/// <summary>Включение ANSI-цветов в консоли Windows (Virtual Terminal Processing).</summary>
internal static partial class WindowsConsole
{
    private const int StdOutputHandle = -11;
    private const uint EnableVirtualTerminalProcessing = 0x0004;

    public static bool TryEnableColors()
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            var handle = GetStdHandle(StdOutputHandle);
            return GetConsoleMode(handle, out var mode) && SetConsoleMode(handle, mode | EnableVirtualTerminalProcessing);
        }
        catch (Exception)
        {
            return false;
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GetStdHandle(int nStdHandle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
}
