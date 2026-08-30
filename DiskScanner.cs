using System.IO;

namespace DiskCleaner;

public sealed class DiskScanner
{
    private const FileAttributes SkipAttributes = FileAttributes.ReparsePoint;

    public async Task<(DirectoryNode Root, List<LargeFileItem> LargeFiles)> ScanAsync(
        string rootPath, long largeThreshold, IProgress<ScanProgress> progress, CancellationToken token)
    {
        return await Task.Run(() =>
        {
            var root = new DirectoryNode { Name = rootPath, FullPath = rootPath };
            var stack = new Stack<(DirectoryNode Node, bool Visited)>();
            stack.Push((root, false));
            var large = new List<LargeFileItem>();
            long bytes = 0, files = 0, directories = 0, ticks = 0;

            while (stack.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                var (node, visited) = stack.Pop();
                if (visited)
                {
                    foreach (var child in node.Children) { node.Size += child.Size; node.FileCount += child.FileCount; }
                    node.Refresh();
                    continue;
                }

                stack.Push((node, true));
                directories++;
                try
                {
                    foreach (var path in Directory.EnumerateFileSystemEntries(node.FullPath))
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            var attr = File.GetAttributes(path);
                            if ((attr & FileAttributes.Directory) != 0)
                            {
                                if ((attr & SkipAttributes) != 0) continue;
                                var child = new DirectoryNode { Name = Path.GetFileName(path), FullPath = path };
                                node.Children.Add(child);
                                stack.Push((child, false));
                            }
                            else
                            {
                                var info = new FileInfo(path);
                                var length = info.Length;
                                node.Size += length; node.FileCount++; bytes += length; files++;
                                if (length >= largeThreshold)
                                    large.Add(new LargeFileItem { Name = info.Name, FullPath = info.FullName, Size = length, Modified = info.LastWriteTime });
                            }
                        }
                        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { }
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { }

                if (++ticks % 120 == 0) progress.Report(new(bytes, files, directories, node.FullPath));
            }
            progress.Report(new(bytes, files, directories, rootPath));
            SortTree(root);
            return (root, large.OrderByDescending(x => x.Size).ToList());
        }, token);
    }

    private static void SortTree(DirectoryNode node)
    {
        var sorted = node.Children.OrderByDescending(x => x.Size).ToList();
        node.Children.Clear();
        foreach (var child in sorted) { SortTree(child); node.Children.Add(child); }
    }
}
