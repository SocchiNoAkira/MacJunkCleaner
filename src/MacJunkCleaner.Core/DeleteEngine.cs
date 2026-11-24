using System.Collections.Concurrent;
using System.Diagnostics;

namespace MacJunkCleaner.Core;

public sealed record DeleteProgress(int Completed, int Total);

public sealed record DeleteFailure(string Path, string Message);

public sealed class DeleteReport
{
    public int DeletedFiles { get; init; }

    public int DeletedDirectories { get; init; }

    public long FreedBytes { get; init; }

    public TimeSpan Duration { get; init; }

    public bool Cancelled { get; init; }

    public IReadOnlyList<DeleteFailure> Failures { get; init; } = Array.Empty<DeleteFailure>();

    public int FailedCount => Failures.Count;
}

/// <summary>
/// 永久删除执行器。单个条目失败不中断整体流程，全部记入失败清单。
/// </summary>
public sealed class DeleteEngine
{
    public DeleteReport Execute(
        IReadOnlyList<ScanItem> items,
        CleanupOptions options,
        IProgress<DeleteProgress>? progress = null,
        CleanupLogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var targets = items.Where(i => i.IsDeletable).ToList();
        var files = targets.Where(t => !t.IsDirectory).ToList();
        var directories = targets.Where(t => t.IsDirectory).ToList();

        var failures = new ConcurrentBag<DeleteFailure>();
        var deletedFiles = 0;
        var deletedDirectories = 0;
        long freedBytes = 0;
        var completed = 0;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, options.MaxDegreeOfParallelism),
            CancellationToken = cancellationToken,
        };

        try
        {
            Parallel.ForEach(files, parallelOptions, file =>
            {
                try
                {
                    DeleteFile(file.FullPath);
                    Interlocked.Increment(ref deletedFiles);
                    Interlocked.Add(ref freedBytes, file.SizeBytes);
                    logger?.Info($"[FILE] {file.FullPath}");
                }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                {
                    // 文件在扫描后被其它程序删掉了，视为已清理
                    Interlocked.Increment(ref deletedFiles);
                    logger?.Info($"[FILE-GONE] {file.FullPath}");
                }
                catch (Exception ex)
                {
                    var message = FsEnumeration.Describe(ex);
                    failures.Add(new DeleteFailure(file.FullPath, message));
                    logger?.Error($"[FILE-FAIL] {file.FullPath} :: {message}");
                }
                finally
                {
                    var done = Interlocked.Increment(ref completed);
                    if (done % 64 == 0) progress?.Report(new DeleteProgress(done, targets.Count));
                }
            });
        }
        catch (OperationCanceledException)
        {
            // 用户取消，已完成的删除结果照常汇总
        }

        foreach (var directory in directories)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                DeleteDirectory(directory.FullPath);
                deletedDirectories++;
                Interlocked.Add(ref freedBytes, directory.SizeBytes);
                logger?.Info($"[DIR ] {directory.FullPath} ({directory.FileCount} 个文件)");
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException)
            {
                deletedDirectories++;
                logger?.Info($"[DIR-GONE] {directory.FullPath}");
            }
            catch (Exception ex)
            {
                var message = FsEnumeration.Describe(ex);
                failures.Add(new DeleteFailure(directory.FullPath, message));
                logger?.Error($"[DIR-FAIL] {directory.FullPath} :: {message}");
            }

            progress?.Report(new DeleteProgress(Interlocked.Increment(ref completed), targets.Count));
        }

        stopwatch.Stop();
        progress?.Report(new DeleteProgress(completed, targets.Count));

        return new DeleteReport
        {
            DeletedFiles = deletedFiles,
            DeletedDirectories = deletedDirectories,
            FreedBytes = freedBytes,
            Duration = stopwatch.Elapsed,
            Cancelled = cancellationToken.IsCancellationRequested,
            Failures = failures.ToList(),
        };
    }

    private static void DeleteFile(string path)
    {
        var attributes = File.GetAttributes(path);
        var blocked = attributes & (FileAttributes.ReadOnly | FileAttributes.System);
        if (blocked != 0)
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly & ~FileAttributes.System);

        File.Delete(path);
    }

    private static void DeleteDirectory(string path)
    {
        ClearAttributesRecursive(path);
        Directory.Delete(path, recursive: true);
    }

    private static void ClearAttributesRecursive(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            try
            {
                var info = new DirectoryInfo(current);
                var attributes = info.Attributes;
                if ((attributes & (FileAttributes.ReadOnly | FileAttributes.System)) != 0)
                    info.Attributes = attributes & ~FileAttributes.ReadOnly & ~FileAttributes.System;

                foreach (var child in info.EnumerateFileSystemInfos("*", FsEnumeration.Entries))
                {
                    if (child is DirectoryInfo directory)
                    {
                        if (!FsEnumeration.IsLink(directory)) stack.Push(directory.FullName);
                        continue;
                    }

                    try
                    {
                        var childAttributes = child.Attributes;
                        if ((childAttributes & (FileAttributes.ReadOnly | FileAttributes.System)) != 0)
                            child.Attributes = childAttributes & ~FileAttributes.ReadOnly & ~FileAttributes.System;
                    }
                    catch
                    {
                        // 单个文件属性清理失败不影响后续
                    }
                }
            }
            catch
            {
                // 无权限的子目录直接跳过，交给 Directory.Delete 报错
            }
        }
    }
}
