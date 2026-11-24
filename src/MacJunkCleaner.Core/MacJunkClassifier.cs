namespace MacJunkCleaner.Core;

/// <summary>
/// 纯函数判定逻辑：根据名称（配合文件头）决定一个条目是否属于 macOS 残留。
/// 判定采用白名单，绝不做"删除所有点开头文件"这类模糊匹配。
/// </summary>
public static class MacJunkClassifier
{
    public const string AppleDoublePrefix = "._";

    private static readonly HashSet<string> DsStoreNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".DS_Store",
    };

    private static readonly HashSet<string> AppearanceFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".localized",
        ".VolumeIcon.icns",
        ".background",
        ".apdisk",
        ".metadata_never_index",
        ".com.apple.timemachine.donotpresent",
        "Icon\r",
    };

    /// <summary>
    /// 这些名字在 macOS 上是目录而非文件（.localized 内含 xx.strings，.background 是 DMG 背景图目录）。
    /// </summary>
    private static readonly HashSet<string> AppearanceDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".localized",
        ".background",
    };

    private static readonly HashSet<string> IndexDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".Spotlight-V100",
        ".fseventsd",
        ".DocumentRevisions-V100",
        ".AppleDouble",
        ".TemporaryItems",
    };

    private static readonly HashSet<string> TrashDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".Trashes",
    };

    /// <summary>AppleDouble 文件头：00 05 16 07（旧版本为 00 05 16 00）。</summary>
    public static bool HasAppleDoubleHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < 4) return false;
        if (header[0] != 0x00 || header[1] != 0x05 || header[2] != 0x16) return false;
        return header[3] is 0x07 or 0x00;
    }

    /// <summary>.DS_Store 文件头：00 00 00 01 42 75 64 31（即 "Bud1"）。</summary>
    public static bool HasDsStoreHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < 8) return false;
        return header[0] == 0x00 && header[1] == 0x00 && header[2] == 0x00 && header[3] == 0x01
               && header[4] == 0x42 && header[5] == 0x75 && header[6] == 0x64 && header[7] == 0x31;
    }

    /// <summary>仅按名称判定所属类别（不看内容）。</summary>
    public static bool TryMatch(string name, bool isDirectory, out JunkKind kind)
    {
        kind = default;
        if (string.IsNullOrEmpty(name)) return false;

        if (isDirectory)
        {
            if (TrashDirectoryNames.Contains(name)) { kind = JunkKind.MacTrash; return true; }
            if (AppearanceDirectoryNames.Contains(name)) { kind = JunkKind.MacAppearance; return true; }
            if (IndexDirectoryNames.Contains(name)) { kind = JunkKind.MacIndexDirectory; return true; }
            return false;
        }

        if (DsStoreNames.Contains(name)) { kind = JunkKind.DsStore; return true; }
        if (name.StartsWith(AppleDoublePrefix, StringComparison.Ordinal)) { kind = JunkKind.AppleDouble; return true; }
        if (AppearanceFileNames.Contains(name)) { kind = JunkKind.MacAppearance; return true; }
        return false;
    }

    /// <summary>对该类别是否需要读取文件头做二次确认。</summary>
    public static bool RequiresHeaderCheck(JunkKind kind) => kind is JunkKind.DsStore or JunkKind.AppleDouble;

    public static int HeaderLength(JunkKind kind) => kind switch
    {
        JunkKind.DsStore => 8,
        JunkKind.AppleDouble => 4,
        _ => 0,
    };
}
