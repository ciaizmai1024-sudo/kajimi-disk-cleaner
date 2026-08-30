using System.Runtime.InteropServices;
using System.IO;

namespace DiskCleaner;

public sealed class CleanupService
{
    public static List<CleanupItem> CreateDefaultItems()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var items = new List<CleanupItem>
        {
            new() { Name="用户临时文件", Description="应用程序产生的临时文件", Paths={ Path.GetTempPath() } },
            new() { Name="Windows 临时文件", Description="系统临时目录中可释放的内容（需要管理员权限）", Paths={ Path.Combine(windows,"Temp") } },
            new() { Name="Windows 更新缓存", Description="已下载的更新安装包（需要管理员权限）", Paths={ Path.Combine(windows,"SoftwareDistribution","Download") } },
            new() { Name="传递优化缓存", Description="Windows 更新的局域网分发缓存", Paths={ Path.Combine(windows,"SoftwareDistribution","DeliveryOptimization") } },
            new() { Name="系统错误报告", Description="Windows 崩溃与错误报告文件", Paths={ Path.Combine(ProgramData(),"Microsoft","Windows","WER") } },
            new() { Name="缩略图缓存", Description="资源管理器缩略图数据库，将自动重建", Paths={ Path.Combine(local,"Microsoft","Windows","Explorer") }, SearchPattern="thumbcache_*.db" },
            new() { Name="DirectX 着色器缓存", Description="显卡着色器缓存，将按需重建", Paths={ Path.Combine(local,"D3DSCache") } },
            new() { Name="浏览器缓存", Description="Edge、Chrome 和 Firefox 的网页缓存", Paths = BrowserCachePaths(local, roaming) },
            new() { Name="旧日志文件", Description="超过 14 天的 Windows 日志文本", Paths={ Path.Combine(windows,"Logs") }, SearchPattern="*.log", OlderThanDays=14 },
            new() { Name="回收站", Description="清空所有磁盘的回收站", IsRecycleBin=true, IsSelected=false },
        };
        return items;
    }

    public async Task AnalyzeAsync(IEnumerable<CleanupItem> items, IProgress<string> progress, CancellationToken token)
    {
        await Task.Run(() =>
        {
            foreach (var item in items)
            {
                token.ThrowIfCancellationRequested();
                item.Status = "正在分析…"; progress.Report(item.Name);
                long size = 0;
                if (item.IsRecycleBin)
                {
                    foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                        size += DirectorySize(Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin"), null, null, token);
                }
                else foreach (var path in item.Paths.Distinct(StringComparer.OrdinalIgnoreCase))
                    size += DirectorySize(path, item.SearchPattern, item.OlderThanDays, token);
                item.Size = size; item.Status = size == 0 ? "无需清理" : "可清理";
            }
        }, token);
    }

    public async Task<(long Freed, int Deleted, int Failed)> CleanAsync(IEnumerable<CleanupItem> items, IProgress<string> progress, CancellationToken token)
    {
        return await Task.Run(() =>
        {
            long freed = 0; int deleted = 0, failed = 0;
            foreach (var item in items.Where(x => x.IsSelected))
            {
                token.ThrowIfCancellationRequested();
                var itemFreedBefore = freed;
                item.Status = "正在清理…"; progress.Report(item.Name);
                if (item.IsRecycleBin)
                {
                    var before = item.Size;
                    var hr = SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4);
                    if (hr == 0) { freed += before; deleted++; item.Size = 0; item.Status = "已清理"; } else { failed++; item.Status = "部分失败"; }
                    continue;
                }

                foreach (var path in item.Paths.Distinct(StringComparer.OrdinalIgnoreCase))
                    DeleteContents(path, item.SearchPattern, item.OlderThanDays, token, ref freed, ref deleted, ref failed);
                item.Size = Math.Max(0, item.Size - (freed - itemFreedBefore));
                item.Status = failed == 0 ? "已清理" : "已清理（有文件被占用）";
            }
            return (freed, deleted, failed);
        }, token);
    }

    private static long DirectorySize(string path, string? pattern, int? olderDays, CancellationToken token)
    {
        if (!Directory.Exists(path)) return 0;
        long total = 0;
        var stack = new Stack<string>(); stack.Push(path);
        while (stack.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var dir = stack.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, pattern ?? "*", SearchOption.TopDirectoryOnly))
                    try { var f = new FileInfo(file); if (olderDays is null || f.LastWriteTime < DateTime.Now.AddDays(-olderDays.Value)) total += f.Length; } catch { }
                foreach (var sub in Directory.EnumerateDirectories(dir))
                    try { if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) == 0) stack.Push(sub); } catch { }
            }
            catch { }
        }
        return total;
    }

    private static void DeleteContents(string path, string? pattern, int? olderDays, CancellationToken token, ref long freed, ref int deleted, ref int failed)
    {
        if (!Directory.Exists(path)) return;
        var dirs = new Stack<(string Path, bool Visited)>(); dirs.Push((path, false));
        while (dirs.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var (dir, visited) = dirs.Pop();
            if (visited)
            {
                if (pattern is null && !dir.Equals(path, StringComparison.OrdinalIgnoreCase)) try { Directory.Delete(dir, false); } catch { }
                continue;
            }
            dirs.Push((dir, true));
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, pattern ?? "*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var f = new FileInfo(file);
                        if (olderDays is not null && f.LastWriteTime >= DateTime.Now.AddDays(-olderDays.Value)) continue;
                        var len = f.Length; f.IsReadOnly = false; f.Delete(); freed += len; deleted++;
                    }
                    catch { failed++; }
                }
                foreach (var sub in Directory.EnumerateDirectories(dir))
                    try { if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) == 0) dirs.Push((sub, false)); } catch { failed++; }
            }
            catch { failed++; }
        }
    }

    private static List<string> BrowserCachePaths(string local, string roaming)
    {
        var paths = new List<string>();
        foreach (var product in new[] { Path.Combine(local,"Google","Chrome","User Data"), Path.Combine(local,"Microsoft","Edge","User Data") })
        {
            if (!Directory.Exists(product)) continue;
            try
            {
                foreach (var profile in Directory.EnumerateDirectories(product).Where(p => Path.GetFileName(p) == "Default" || Path.GetFileName(p).StartsWith("Profile ")))
                { paths.Add(Path.Combine(profile,"Cache")); paths.Add(Path.Combine(profile,"Code Cache")); paths.Add(Path.Combine(profile,"GPUCache")); }
            } catch { }
        }
        var firefox = Path.Combine(local,"Mozilla","Firefox","Profiles");
        if (Directory.Exists(firefox)) try { foreach (var profile in Directory.EnumerateDirectories(firefox)) paths.Add(Path.Combine(profile,"cache2")); } catch { }
        return paths;
    }

    private static string ProgramData() => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint flags);
}
