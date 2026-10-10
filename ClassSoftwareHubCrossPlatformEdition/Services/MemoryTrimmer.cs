using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Threading;

namespace ClassSoftwareHubCrossPlatformEdition.Services;

/// <summary>
/// 内存回收（教学机 8G 内存，不能一路涨上去）。
///
/// 原来 Windows 版是「GC + <c>EmptyWorkingSet</c>」两步：后者是 Win32 独有的
/// （把闲置页丢进系统 standby list，进程工作集立刻降下来），跨平台没有、也不该有。
/// 现在只剩 .NET 自带的托管堆回收 —— 各系统行为一致，靠 <c>GC.Collect</c> 把对象还给运行时。
///
/// 只在「用户看不见」的时候收：收进托盘 / 最小化 / 切页停稳之后。节流 4 秒，避免连续切页时反复 GC 卡顿。
/// </summary>
public static class MemoryTrimmer
{
    private static DateTimeOffset _last = DateTimeOffset.MinValue;
    private static readonly object Gate = new();

    /// <summary>上一次回收把托管堆压下去多少（字节），界面上可以显示。</summary>
    public static long LastFreedBytes { get; private set; }

    /// <summary>累计回收次数（排查用）。</summary>
    public static int TrimCount { get; private set; }

    private static string LogPath => Path.Combine(SettingsStore.Dir, "memory.log");

    /// <summary>收一次。force = 用户主动触发，忽略节流。</summary>
    public static void Trim(bool force = false)
    {
        lock (Gate)
        {
            var now = DateTimeOffset.Now;
            if (!force && (now - _last).TotalMilliseconds < 4000) return;
            _last = now;
            TrimCount++;
        }

        try
        {
            var before = GC.GetTotalMemory(false);

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false, compacting: false);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false, compacting: false);

            var after = GC.GetTotalMemory(false);
            LastFreedBytes = Math.Max(0, before - after);

            Log($"第 {TrimCount} 次：托管堆 {before / 1048576.0:0.#}MB → {after / 1048576.0:0.#}MB (force={force})");
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[mem] 回收失败: " + ex.Message);
            Log("回收失败：" + ex.Message);
        }
    }

    /// <summary>等界面渲染停稳再收（切页动画没结束就 GC，纯浪费）。</summary>
    public static void TrimLater(int delayMs = 3000, bool force = false)
    {
        DispatcherTimer? timer = null;
        timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(delayMs),
            DispatcherPriority.Background,
            (_, _) =>
            {
                timer?.Stop();
                Trim(force);
            });
        timer.Start();
    }

    private static void Log(string message)
    {
        try
        {
            var dir = Core.AppPaths.EnsureDir();
            if (dir.Length == 0) return;
            File.AppendAllText(LogPath, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
        }
        catch { }
    }
}
