namespace BorgMate.Infrastructure.Platform;

internal static class StringHelpers
{
    public const string AppName = "BorgMate";

    public static string EscapeXml(string value)
    {
        return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("\"", "&quot;").Replace("'", "&apos;").Replace("\n", "&#10;");
    }

    public static string EscapeShell(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ");
    }
}