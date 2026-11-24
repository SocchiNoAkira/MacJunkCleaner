using System.Text;

namespace MacJunkCleaner.Core;

public static class ReportWriter
{
    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double value = bytes;
        string[] units = ["KB", "MB", "GB", "TB"];
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    public static string BuildText(
        string rootPath,
        IReadOnlyList<ScanItem> items,
        DeleteReport? report,
        string? logPath,
        int scanErrorCount = 0)
    {
        var builder = new StringBuilder();
        builder.AppendLine("macOS 残留文件清理报告");
        builder.AppendLine(new string('=', 60));
        builder.AppendLine($"生成时间 : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"扫描目录 : {rootPath}");
        builder.AppendLine();

        builder.AppendLine("统计");
        builder.AppendLine(new string('-', 60));
        foreach (var kind in JunkKindInfo.All)
        {
            var group = items.Where(i => i.Kind == kind).ToList();
            var deletable = group.Where(i => i.IsDeletable).ToList();
            if (group.Count == 0) continue;
            builder.AppendLine(
                $"{JunkKindInfo.Display(kind),-24} 命中 {group.Count,6}  可删 {deletable.Count,6}  大小 {FormatSize(deletable.Sum(i => i.SizeBytes)),10}");
        }

        builder.AppendLine();

        if (report is not null)
        {
            builder.AppendLine("执行结果");
            builder.AppendLine(new string('-', 60));
            builder.AppendLine($"删除文件   : {report.DeletedFiles}");
            builder.AppendLine($"删除目录   : {report.DeletedDirectories}");
            builder.AppendLine($"释放空间   : {FormatSize(report.FreedBytes)}");
            builder.AppendLine($"失败条目   : {report.FailedCount}");
            builder.AppendLine($"耗时       : {report.Duration.TotalSeconds:0.##} 秒{(report.Cancelled ? "（用户已取消）" : string.Empty)}");
            builder.AppendLine();

            if (report.Failures.Count > 0)
            {
                builder.AppendLine("失败明细");
                builder.AppendLine(new string('-', 60));
                foreach (var failure in report.Failures)
                    builder.AppendLine($"{failure.Path}  ->  {failure.Message}");
                builder.AppendLine();
            }
        }

        foreach (var kind in JunkKindInfo.All)
        {
            var deleted = items.Where(i => i.Kind == kind && i.IsDeletable).ToList();
            if (deleted.Count == 0) continue;

            builder.AppendLine($"已清理 / 待清理：{JunkKindInfo.Display(kind)}");
            builder.AppendLine(new string('-', 60));
            foreach (var item in deleted.Take(2000))
                builder.AppendLine($"{FormatSize(item.SizeBytes),10}  {item.FullPath}");
            if (deleted.Count > 2000)
                builder.AppendLine($"…… 其余 {deleted.Count - 2000} 条已省略，详见日志文件");
            builder.AppendLine();
        }

        var skipped = items.Where(i => !i.IsDeletable).ToList();
        if (skipped.Count > 0)
        {
            builder.AppendLine("已跳过");
            builder.AppendLine(new string('-', 60));
            foreach (var item in skipped.Take(2000))
                builder.AppendLine($"{item.FullPath}  ->  {item.SkipReason}");
            if (skipped.Count > 2000)
                builder.AppendLine($"…… 其余 {skipped.Count - 2000} 条已省略");
            builder.AppendLine();
        }

        if (scanErrorCount > 0)
            builder.AppendLine($"扫描过程中有 {scanErrorCount} 个目录无法访问，已跳过。");

        if (!string.IsNullOrEmpty(logPath))
            builder.AppendLine($"日志文件 : {logPath}");

        return builder.ToString();
    }
}
