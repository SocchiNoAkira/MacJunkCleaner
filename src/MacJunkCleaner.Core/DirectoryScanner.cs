using System.Diagnostics;

namespace MacJunkCleaner.Core;

public sealed record ScanProgress(int FoldersVisited, int CandidatesFound, string CurrentDirectory);

public sealed class ScanOutcome
{
    public required string RootPath { get; init; }

    public required IReadOnlyList<ScanItem> Items { get; init; }

    public int FoldersVisited { get; init; }

    public int ErrorCount { get; init; }

    public TimeSpan Duration { get; init; }

    public IEnumerable<ScanItem> Deletable => Items.Where(i => i.IsDeletable);

    public long DeletableBytes => Items.Where(i => i.IsDeletable).Sum(i => i.SizeBytes);
}

/// <summary>
/// 递归扫描器。使用显式栈做深度优先遍历，遇到符号链接 / junction 一律不进入，
/// 遇到 macOS 元数据目录则整体作为一个条目（不展开），避免清单被噪音淹没。
/// </summary>
public sealed class DirectoryScanner
{
    private const string SystemVolumeInformation = "System Volume Information";

    public ScanOutcome Scan(
        string rootPath,
        CleanupOptions options,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var items = new List<ScanItem>();
        var foldersVisited = 0;
        var errors = 0;

        var stack = new Stack<string>();
        stack.Push(rootPath);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = stack.Pop();
            foldersVisited++;

            if (foldersVisited % 64 == 0)
                progress?.Report(new ScanProgress(foldersVisited, items.Count, current));

            IEnumerator<FileSystemInfo> enumerator;
            try
            {
                enumerator = new DirectoryInfo(current)
                    .EnumerateFileSystemInfos("*", FsEnumeration.Entries)
                    .GetEnumerator();
            }
            catch
            {
                errors++;
                continue;
            }

            using (enumerator)
            {
                while (true)
                {
                    FileSystemInfo entry;
                    try
                    {
                        if (!enumerator.MoveNext()) break;
                        entry = enumerator.Current;
                    }
                    catch
                    {
                        errors++;
                        break;
                    }

                    try
                    {
                        HandleEntry(entry, stack, items, options);
                    }
                    catch
                    {
                        errors++;
                    }
                }
            }
        }

        progress?.Report(new ScanProgress(foldersVisited, items.Count, rootPath));
        stopwatch.Stop();

        return new ScanOutcome
        {
            RootPath = rootPath,
            Items = items,
            FoldersVisited = foldersVisited,
            ErrorCount = errors,
            Duration = stopwatch.Elapsed,
        };
    }

    private static void HandleEntry(
        FileSystemInfo entry,
        Stack<string> pendingDirectories,
        List<ScanItem> items,
        CleanupOptions options)
    {
        var isDirectory = entry is DirectoryInfo;
        var isLink = FsEnumeration.IsLink(entry);

        if (isDirectory)
        {
            if (string.Equals(entry.Name, SystemVolumeInformation, StringComparison.OrdinalIgnoreCase))
                return;

            if (!MacJunkClassifier.TryMatch(entry.Name, true, out var kind))
            {
                if (!isLink) pendingDirectories.Push(entry.FullName);
                return;
            }

            if (isLink)
            {
                items.Add(CreateItem(entry, kind, true, 0, 0, "符号链接 / junction，不进入也不删除"));
                return;
            }

            var (fileCount, size) = MeasureDirectory(entry.FullName);
            items.Add(CreateItem(entry, kind, true, size, fileCount, null));
            return;
        }

        if (!MacJunkClassifier.TryMatch(entry.Name, false, out var fileKind))
            return;

        if (isLink)
        {
            items.Add(CreateItem(entry, fileKind, false, 0, 0, "符号链接，不删除"));
            return;
        }

        string? skipReason = null;
        if (options.VerifyFileHeaders && MacJunkClassifier.RequiresHeaderCheck(fileKind))
            skipReason = VerifyHeader(entry.FullName, fileKind);

        long length = 0;
        try { length = ((FileInfo)entry).Length; } catch { }

        items.Add(CreateItem(entry, fileKind, false, length, 0, skipReason));
    }

    private static string? VerifyHeader(string path, JunkKind kind)
    {
        var header = AppleFileHeaderReader.TryRead(path, MacJunkClassifier.HeaderLength(kind));
        if (header is null) return "无法读取文件头（可能被占用或无权限）";

        return kind switch
        {
            JunkKind.DsStore when !MacJunkClassifier.HasDsStoreHeader(header)
                => "文件头不是 .DS_Store 格式，按用户文件保留",
            JunkKind.AppleDouble when !MacJunkClassifier.HasAppleDoubleHeader(header)
                => "文件头不是 AppleDouble 格式，按用户文件保留",
            _ => null,
        };
    }

    private static (int FileCount, long Size) MeasureDirectory(string path)
    {
        var fileCount = 0;
        long size = 0;
        var stack = new Stack<string>();
        stack.Push(path);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            try
            {
                foreach (var child in new DirectoryInfo(current).EnumerateFileSystemInfos("*", FsEnumeration.Entries))
                {
                    if (child is DirectoryInfo directory)
                    {
                        if (!FsEnumeration.IsLink(directory)) stack.Push(directory.FullName);
                    }
                    else
                    {
                        fileCount++;
                        try { size += ((FileInfo)child).Length; } catch { }
                    }
                }
            }
            catch
            {
                // 无权限的子目录忽略，仍按已知部分统计
            }
        }

        return (fileCount, size);
    }

    private static ScanItem CreateItem(
        FileSystemInfo entry,
        JunkKind kind,
        bool isDirectory,
        long size,
        int fileCount,
        string? skipReason) => new()
    {
        FullPath = entry.FullName,
        Name = entry.Name,
        Kind = kind,
        IsDirectory = isDirectory,
        SizeBytes = size,
        FileCount = fileCount,
        SkipReason = skipReason,
    };
}
