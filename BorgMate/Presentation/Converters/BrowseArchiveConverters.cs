namespace BorgMate.Presentation.Converters;

public static class BrowseArchiveConverters
{
    public static FileIconConverter FileIcon { get; } = FileIconConverter.Instance;
    public static FileChangeFontWeightConverter ChangeFontWeight { get; } = FileChangeFontWeightConverter.Instance;
    public static FileChangeForegroundConverter ChangeForeground { get; } = FileChangeForegroundConverter.Instance;

    public static ToggleActiveForegroundConverter ToggleActiveForeground { get; } =
        ToggleActiveForegroundConverter.Instance;

    public static DepthToMarginConverter DepthToMargin { get; } = DepthToMarginConverter.Instance;
}