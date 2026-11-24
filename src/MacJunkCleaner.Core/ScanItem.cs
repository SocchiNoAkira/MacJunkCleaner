namespace MacJunkCleaner.Core;

/// <summary>
/// 一次扫描中命中的候选条目（可能是可删除项，也可能是"像但被跳过"的条目）。
/// </summary>
public sealed class ScanItem
{
    public required string FullPath { get; init; }

    public required string Name { get; init; }

    public required JunkKind Kind { get; init; }

    /// <summary>该条目本身是目录（索引目录 / .Trashes）。</summary>
    public required bool IsDirectory { get; init; }

    /// <summary>文件大小；目录为其内部文件总大小。</summary>
    public long SizeBytes { get; init; }

    /// <summary>目录内部的文件数量；文件为 0。</summary>
    public int FileCount { get; init; }

    /// <summary>非空表示该条目不会删除，值为跳过原因。</summary>
    public string? SkipReason { get; init; }

    public bool IsDeletable => SkipReason is null;
}
