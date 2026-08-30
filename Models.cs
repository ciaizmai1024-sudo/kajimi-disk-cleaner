using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.IO;

namespace DiskCleaner;

public static class SizeText
{
    public static string Format(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var i = 0;
        while (value >= 1024 && i < units.Length - 1) { value /= 1024; i++; }
        return $"{value:0.##} {units[i]}";
    }
}

public sealed class DirectoryNode : INotifyPropertyChanged
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public ObservableCollection<DirectoryNode> Children { get; } = new();
    public long Size { get; set; }
    public long FileCount { get; set; }
    public string SizeDisplay => SizeText.Format(Size);
    public string Detail => $"{SizeDisplay}  ·  {FileCount:N0} 个文件";
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() { PropertyChanged?.Invoke(this, new(nameof(SizeDisplay))); PropertyChanged?.Invoke(this, new(nameof(Detail))); }
}

public sealed class LargeFileItem : INotifyPropertyChanged
{
    private bool _selected;
    public bool IsSelected { get => _selected; set { _selected = value; Changed(); } }
    public string Name { get; init; } = "";
    public string FullPath { get; init; } = "";
    public long Size { get; init; }
    public string SizeDisplay => SizeText.Format(Size);
    public DateTime Modified { get; init; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new(n));
}

public sealed class CleanupItem : INotifyPropertyChanged
{
    private bool _selected = true;
    private long _size;
    private string _status = "等待分析";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public List<string> Paths { get; init; } = new();
    public string? SearchPattern { get; init; }
    public int? OlderThanDays { get; init; }
    public bool IsRecycleBin { get; init; }
    public bool IsSelected { get => _selected; set { _selected = value; Changed(); } }
    public long Size { get => _size; set { _size = value; Changed(); Changed(nameof(SizeDisplay)); } }
    public string SizeDisplay => SizeText.Format(Size);
    public string Status { get => _status; set { _status = value; Changed(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new(n));
}

public sealed record ScanProgress(long Bytes, long Files, long Directories, string CurrentPath);
