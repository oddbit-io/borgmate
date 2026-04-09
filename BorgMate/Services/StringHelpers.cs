namespace BorgMate.Services;

internal static class StringHelpers
{
    public const string AppName = "BorgMate";

    public static string EscapeXml(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
             .Replace("\"", "&quot;").Replace("'", "&apos;").Replace("\n", "&#10;");

    public static string EscapeShell(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ");
}
