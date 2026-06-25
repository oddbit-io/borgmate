namespace BorgMate.Presentation.Converters;

public static class BrowseArchiveConverters
{
    public static FileIconConverter FileIcon { get; } = FileIconConverter.Instance;
    public static FileChangeFontWeightConverter ChangeFontWeight { get; } = FileChangeFontWeightConverter.Instance;
    public static FileChangeAddedConverter ChangeAdded { get; } = FileChangeAddedConverter.Instance;

    public static DepthToMarginConverter DepthToMargin { get; } = DepthToMarginConverter.Instance;
}
