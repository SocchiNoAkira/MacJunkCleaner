namespace MacJunkCleaner.Core;

public static class AppleFileHeaderReader
{
    /// <summary>
    /// 读取文件头部若干字节用于魔数校验。
    /// 返回 null 表示无法读取（被占用 / 无权限），此时调用方应跳过该文件。
    /// </summary>
    public static byte[]? TryRead(string path, int count)
    {
        if (count <= 0) return Array.Empty<byte>();

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                128,
                FileOptions.SequentialScan);

            var buffer = new byte[count];
            var total = 0;
            while (total < count)
            {
                var read = stream.Read(buffer, total, count - total);
                if (read <= 0) break;
                total += read;
            }

            if (total == 0) return Array.Empty<byte>();
            if (total < count) Array.Resize(ref buffer, total);
            return buffer;
        }
        catch
        {
            return null;
        }
    }
}
