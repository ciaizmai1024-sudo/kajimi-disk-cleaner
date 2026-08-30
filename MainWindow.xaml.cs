using Microsoft.VisualBasic.FileIO;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DiskCleaner;

public partial class MainWindow : Window
{
    private readonly DiskScanner _scanner = new();
    private readonly CleanupService _cleanup = new();
    private readonly UpdateService _updates = new();
    private readonly ObservableCollection<LargeFileItem> _largeFiles = new();
    private readonly ObservableCollection<CleanupItem> _cleanupItems;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _cleanupCts;

    public MainWindow()
    {
        InitializeComponent();
        LargeFilesGrid.ItemsSource = _largeFiles;
        _cleanupItems = new(CleanupService.CreateDefaultItems());
        CleanupList.ItemsSource = _cleanupItems;
        LoadDrives();
        var admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        AdminStatus.Text = admin ? "● 已使用管理员权限" : "○ 普通权限（部分系统文件仅可扫描）";
        AdminButton.Visibility = admin ? Visibility.Collapsed : Visibility.Visible;
        Loaded += async (_, _) =>
        {
            await AnalyzeCleanupAsync();
            await CheckForUpdatesAsync(false);
        };
    }

    private void LoadDrives()
    {
        var drives = DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => new DriveChoice(d)).ToList();
        DriveBox.ItemsSource = drives;
        DriveBox.SelectedItem = drives.FirstOrDefault(d => d.Drive.Name.Equals("C:\\", StringComparison.OrdinalIgnoreCase)) ?? drives.FirstOrDefault();
    }

    private void DriveBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DriveBox.SelectedItem is not DriveChoice choice) return;
        TotalText.Text = SizeText.Format(choice.Drive.TotalSize);
        FreeText.Text = SizeText.Format(choice.Drive.AvailableFreeSpace);
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (DriveBox.SelectedItem is not DriveChoice choice) return;
        _scanCts?.Cancel(); _scanCts = new();
        SetScanning(true); DirectoryTree.ItemsSource = null; _largeFiles.Clear(); ScanSizeText.Text = "正在扫描…";
        var progress = new Progress<ScanProgress>(p =>
        {
            ScanSizeText.Text = SizeText.Format(p.Bytes);
            ScanStatusText.Text = $"已扫描 {p.Directories:N0} 个目录、{p.Files:N0} 个文件  ·  {p.CurrentPath}";
        });
        try
        {
            var threshold = long.Parse(((ComboBoxItem)ThresholdBox.SelectedItem).Tag.ToString()!);
            var result = await _scanner.ScanAsync(choice.Drive.RootDirectory.FullName, threshold, progress, _scanCts.Token);
            DirectoryTree.ItemsSource = new[] { result.Root };
            foreach (var f in result.LargeFiles) _largeFiles.Add(f);
            ScanSizeText.Text = SizeText.Format(result.Root.Size);
            LargeSummaryText.Text = $"共发现 {_largeFiles.Count:N0} 个大文件，合计 {SizeText.Format(_largeFiles.Sum(x => x.Size))}";
            ScanStatusText.Text = $"扫描完成：{result.Root.FileCount:N0} 个文件。展开箭头可逐层查看每个目录。";
        }
        catch (OperationCanceledException) { ScanStatusText.Text = "扫描已停止"; }
        catch (Exception ex) { MessageBox.Show($"扫描过程中发生错误：{ex.Message}", "卡吉米磁盘清理助手", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { SetScanning(false); }
    }

    private void SetScanning(bool value) { ScanButton.IsEnabled = !value; CancelScanButton.IsEnabled = value; DriveBox.IsEnabled = !value; ScanProgress.Visibility = value ? Visibility.Visible : Visibility.Collapsed; }
    private void CancelScanButton_Click(object sender, RoutedEventArgs e) => _scanCts?.Cancel();

    private void OpenDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (DirectoryTree.SelectedItem is DirectoryNode node) OpenExplorer(node.FullPath);
    }

    private void DeleteDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (DirectoryTree.SelectedItem is not DirectoryNode node) { MessageBox.Show("请先在目录树中选择一个目录。", "提示"); return; }
        if (Path.GetPathRoot(node.FullPath)?.TrimEnd('\\').Equals(node.FullPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) == true)
        { MessageBox.Show("磁盘根目录不可整体清理，请选择它下面的目录。", "提示"); return; }
        if (MessageBox.Show($"将整个目录移入回收站？\n\n{node.FullPath}\n{node.Detail}", "确认手动清理", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { FileSystem.DeleteDirectory(node.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin); MessageBox.Show("目录已移入回收站。", "完成"); }
        catch (Exception ex) { MessageBox.Show($"处理失败：{ex.Message}", "提示", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void SelectAllLarge_Click(object sender, RoutedEventArgs e) { var select = _largeFiles.Any(x => !x.IsSelected); foreach (var f in _largeFiles) f.IsSelected = select; }
    private void OpenLargeLocation_Click(object sender, RoutedEventArgs e) { if (LargeFilesGrid.SelectedItem is LargeFileItem f) OpenExplorer(f.FullPath, true); }

    private void DeleteLarge_Click(object sender, RoutedEventArgs e)
    {
        var selected = _largeFiles.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0) { MessageBox.Show("请先勾选要清理的文件。", "提示"); return; }
        if (MessageBox.Show($"将 {selected.Count} 个文件（{SizeText.Format(selected.Sum(x => x.Size))}）移入回收站？", "确认手动清理", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var failed = 0;
        foreach (var f in selected) try { FileSystem.DeleteFile(f.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin); _largeFiles.Remove(f); } catch { failed++; }
        LargeSummaryText.Text = failed == 0 ? "所选文件已移入回收站" : $"清理完成，{failed} 个文件处理失败";
        RefreshDriveInfo();
    }

    private async void AnalyzeCleanup_Click(object sender, RoutedEventArgs e) => await AnalyzeCleanupAsync();
    private async Task AnalyzeCleanupAsync()
    {
        _cleanupCts?.Cancel(); _cleanupCts = new(); CleanButton.IsEnabled = false; CleanupSummaryText.Text = "正在分析…";
        try
        {
            await _cleanup.AnalyzeAsync(_cleanupItems, new Progress<string>(s => CleanupSummaryText.Text = $"正在分析：{s}"), _cleanupCts.Token);
            CleanupSummaryText.Text = $"预计可释放 {SizeText.Format(_cleanupItems.Where(x => x.IsSelected).Sum(x => x.Size))}";
        }
        catch (OperationCanceledException) { }
        finally { CleanButton.IsEnabled = true; }
    }

    private async void CleanButton_Click(object sender, RoutedEventArgs e)
    {
        var chosen = _cleanupItems.Where(x => x.IsSelected && x.Size > 0).ToList();
        if (chosen.Count == 0) { MessageBox.Show("当前勾选项目没有可清理内容。", "提示"); return; }
        if (MessageBox.Show($"将永久清理 {chosen.Count} 个缓存项目，预计释放 {SizeText.Format(chosen.Sum(x => x.Size))}。\n正在使用的文件会自动跳过。", "确认一键清理", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        CleanButton.IsEnabled = false; _cleanupCts = new();
        try
        {
            var result = await _cleanup.CleanAsync(chosen, new Progress<string>(s => CleanupSummaryText.Text = $"正在清理：{s}"), _cleanupCts.Token);
            CleanupSummaryText.Text = $"已释放 {SizeText.Format(result.Freed)}，删除 {result.Deleted:N0} 个文件，跳过 {result.Failed:N0} 个";
            RefreshDriveInfo();
            await AnalyzeCleanupAsync();
        }
        catch (OperationCanceledException) { }
        finally { CleanButton.IsEnabled = true; }
    }

    private void AdminButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch { }
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(true);

    private async Task CheckForUpdatesAsync(bool showWhenCurrent)
    {
        UpdateButton.IsEnabled = false;
        UpdateButton.Content = "检查中…";
        try
        {
            var info = await _updates.CheckAsync();
            if (info.Available)
            {
                if (MessageBox.Show(info.Message + "\n\n现在打开 GitHub 下载页面？", "发现新版本", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo(info.ReleaseUrl) { UseShellExecute = true });
            }
            else if (showWhenCurrent)
                MessageBox.Show(info.Message, "检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            if (showWhenCurrent) MessageBox.Show($"版本检查遇到问题：{ex.Message}", "检查更新", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { UpdateButton.Content = "检查更新"; UpdateButton.IsEnabled = true; }
    }

    private void ContactButton_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("mailto:ciaizmai1024@gmail.com?subject=卡吉米磁盘清理助手反馈") { UseShellExecute = true }); } catch { }
    }

    private static void OpenExplorer(string path, bool select = false)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", select ? $"/select,\"{path}\"" : $"\"{path}\"") { UseShellExecute = true }); } catch { }
    }
    private void RefreshDriveInfo() { if (DriveBox.SelectedItem is DriveChoice c) { var d = new DriveInfo(c.Drive.Name); TotalText.Text = SizeText.Format(d.TotalSize); FreeText.Text = SizeText.Format(d.AvailableFreeSpace); } }

    public async Task ExportScreenshotsAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        WindowState = WindowState.Normal;
        Width = 1180; Height = 760;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(1800);

        Tabs.SelectedIndex = 0;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        SaveVisual(Path.Combine(folder, "space-analysis.png"));

        Tabs.SelectedIndex = 2;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Task.Delay(250);
        SaveVisual(Path.Combine(folder, "cleanup-items.png"));
    }

    private void SaveVisual(string path)
    {
        var width = Math.Max(1, (int)Math.Ceiling(ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}

public sealed class DriveChoice
{
    public DriveInfo Drive { get; }
    public string Label => $"{Drive.Name}  {Drive.VolumeLabel}  （可用 {SizeText.Format(Drive.AvailableFreeSpace)}）";
    public DriveChoice(DriveInfo drive) => Drive = drive;
}
