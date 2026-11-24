namespace MacJunkCleaner.Core;

/// <summary>
/// macOS 在 Windows 上留下的无用文件/目录类型。
/// </summary>
public enum JunkKind
{
    /// <summary>.DS_Store，Finder 的目录显示属性缓存。</summary>
    DsStore = 0,

    /// <summary>._xxx，AppleDouble 资源分支伴生文件。</summary>
    AppleDouble = 1,

    /// <summary>macOS 专用的外观文件，如 .localized、Icon\r、.VolumeIcon.icns。</summary>
    MacAppearance = 2,

    /// <summary>macOS 专用索引/版本目录，如 .Spotlight-V100、.fseventsd。</summary>
    MacIndexDirectory = 3,

    /// <summary>.Trashes，macOS 回收站目录（内部可能是真实文件，默认单独提示）。</summary>
    MacTrash = 4,
}

public static class JunkKindInfo
{
    public static readonly JunkKind[] All =
    [
        JunkKind.DsStore,
        JunkKind.AppleDouble,
        JunkKind.MacAppearance,
        JunkKind.MacIndexDirectory,
        JunkKind.MacTrash,
    ];

    public static string Display(JunkKind kind) => kind switch
    {
        JunkKind.DsStore => ".DS_Store",
        JunkKind.AppleDouble => "._* (AppleDouble)",
        JunkKind.MacAppearance => "macOS 外观文件",
        JunkKind.MacIndexDirectory => "macOS 索引目录",
        JunkKind.MacTrash => ".Trashes",
        _ => kind.ToString(),
    };

    public static string Hint(JunkKind kind) => kind switch
    {
        JunkKind.DsStore => "Finder 目录缓存，Windows 下无任何作用",
        JunkKind.AppleDouble => "资源分支副本，将校验文件头 00 05 16 07",
        JunkKind.MacAppearance => ".localized / Icon\\r / .VolumeIcon.icns 等",
        JunkKind.MacIndexDirectory => ".Spotlight-V100 / .fseventsd / .DocumentRevisions-V100 等",
        JunkKind.MacTrash => "注意：可能包含你在 mac 上删除过的真实文件",
        _ => string.Empty,
    };
}
