namespace MacJunkCleaner.Core;

public enum RootRisk
{
    Ok = 0,
    Warn = 1,
    Blocked = 2,
}

public readonly record struct RootValidation(RootRisk Risk, string? Message)
{
    public bool CanProceed => Risk != RootRisk.Blocked;
}

/// <summary>
/// 扫描根目录保护：避免用户误选系统目录造成大面积误删。
/// </summary>
public static class PathGuard
{
    public static RootValidation Validate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new RootValidation(RootRisk.Blocked, "请先选择一个目录。");

        string full;
        try
        {
            full = Path.GetFullPath(path.Trim().Trim('"'));
        }
        catch
        {
            return new RootValidation(RootRisk.Blocked, "路径格式无效。");
        }

        if (!Directory.Exists(full))
            return new RootValidation(RootRisk.Blocked, "目录不存在或不可访问。");

        var trimmed = TrimSeparator(full);

        if (IsDriveRoot(trimmed))
        {
            var systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            if (string.Equals(Path.GetPathRoot(trimmed), systemRoot, StringComparison.OrdinalIgnoreCase))
                return new RootValidation(RootRisk.Blocked, $"不允许清理系统盘根目录：{trimmed}");

            return new RootValidation(RootRisk.Warn,
                $"你选择的是驱动器根目录 {trimmed}，将扫描整块磁盘，可能耗时较长。确定继续吗？");
        }

        foreach (var system in DangerousPaths())
        {
            if (IsSameOrUnder(trimmed, system))
                return new RootValidation(RootRisk.Blocked, $"不允许清理系统目录：{system}");
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile) && string.Equals(trimmed, TrimSeparator(profile), StringComparison.OrdinalIgnoreCase))
            return new RootValidation(RootRisk.Warn,
                "你选择的是整个用户主目录，扫描范围很大且可能涉及软件配置。建议选择具体的数据目录。");

        return new RootValidation(RootRisk.Ok, null);
    }

    private static IEnumerable<string> DangerousPaths()
    {
        var list = new List<string>();
        void Add(Environment.SpecialFolder folder)
        {
            var p = Environment.GetFolderPath(folder);
            if (!string.IsNullOrWhiteSpace(p)) list.Add(TrimSeparator(p));
        }

        Add(Environment.SpecialFolder.Windows);
        Add(Environment.SpecialFolder.System);
        Add(Environment.SpecialFolder.ProgramFiles);
        Add(Environment.SpecialFolder.ProgramFilesX86);
        Add(Environment.SpecialFolder.CommonApplicationData);

        // 驱动器根目录不放进这里：它由 IsDriveRoot 单独判断，
        // 否则 C:\ 会把整个系统盘下的所有子目录都算成"系统目录"。
        return list.Where(p => p.Length > 0);
    }

    private static string TrimSeparator(string path)
    {
        if (path.Length <= 3) return path; // 保留 "C:\"
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsDriveRoot(string path)
        => string.Equals(Path.GetPathRoot(path)?.TrimEnd(Path.DirectorySeparatorChar), path, StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrUnder(string path, string parent)
    {
        if (parent.Length == 0) return false;
        if (string.Equals(path, parent, StringComparison.OrdinalIgnoreCase)) return true;
        var prefix = parent.EndsWith(Path.DirectorySeparatorChar) ? parent : parent + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
