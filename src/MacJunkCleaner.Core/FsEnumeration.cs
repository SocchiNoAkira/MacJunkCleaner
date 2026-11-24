namespace MacJunkCleaner.Core;

internal static class FsEnumeration
{
    /// <summary>
    /// 关键：AttributesToSkip 默认为 Hidden | System，而 mac 拷来的 .DS_Store 常带隐藏属性，
    /// 用默认值会静默跳过全部目标。这里必须显式清零。
    /// </summary>
    internal static readonly EnumerationOptions Entries = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = (FileAttributes)0,
        ReturnSpecialDirectories = false,
        MatchType = MatchType.Simple,
        MatchCasing = MatchCasing.PlatformDefault,
    };

    internal static bool IsLink(FileSystemInfo info)
        => (info.Attributes & FileAttributes.ReparsePoint) != 0;

    internal static string Describe(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "无权限（拒绝访问）",
        DirectoryNotFoundException => "目录已不存在（可能被其它程序删除）",
        FileNotFoundException => "文件已不存在（可能被其它程序删除）",
        IOException io => $"IO 错误：{io.Message}",
        _ => $"{ex.GetType().Name}：{ex.Message}",
    };
}
