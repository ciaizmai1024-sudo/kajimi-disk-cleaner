using System.Windows;
using System.IO;

namespace DiskCleaner;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();
        window.Show();
        var index = Array.FindIndex(e.Args, x => x.Equals("--screenshots", StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            var folder = index + 1 < e.Args.Length ? e.Args[index + 1] : Path.Combine(AppContext.BaseDirectory, "screenshots");
            await window.ExportScreenshotsAsync(folder);
            window.Close();
            Shutdown();
        }
    }
}
