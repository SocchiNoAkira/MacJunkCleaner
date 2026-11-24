namespace MacJunkCleaner.Core;

public sealed class CleanupOptions
{
    /// <summary>
    /// 是否对 .DS_Store / ._* 校验文件头魔数。
    /// 关闭后按文件名直接判定，速度更快但存在误删同名用户文件的风险。
    /// </summary>
    public bool VerifyFileHeaders { get; init; } = true;

    /// <summary>删除时的并行度。机械硬盘建议 1，SSD/NVMe 建议 4~8。</summary>
    public int MaxDegreeOfParallelism { get; init; } = 4;

    public static CleanupOptions Default => new();
}
