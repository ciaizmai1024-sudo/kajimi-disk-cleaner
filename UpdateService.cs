using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace DiskCleaner;

public sealed record UpdateInfo(bool Available, Version CurrentVersion, Version? LatestVersion, string ReleaseUrl, string Message);

public sealed class UpdateService
{
    public const string RepositoryUrl = "https://github.com/ciaizmai1024-sudo/kajimi-disk-cleaner";
    private const string LatestReleaseApi = "https://api.github.com/repos/ciaizmai1024-sudo/kajimi-disk-cleaner/releases/latest";

    public async Task<UpdateInfo> CheckAsync(CancellationToken token = default)
    {
        var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("KajimiDiskCleaner/" + current.ToString(3));
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        using var response = await client.GetAsync(LatestReleaseApi, token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new(false, current, null, RepositoryUrl + "/releases", "GitHub 上暂未发布正式版本。");
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        var root = json.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagValue) ? tagValue.GetString() ?? "" : "";
        var url = root.TryGetProperty("html_url", out var urlValue) ? urlValue.GetString() ?? RepositoryUrl + "/releases" : RepositoryUrl + "/releases";
        var normalized = tag.Trim().TrimStart('v', 'V');
        var latest = Version.TryParse(normalized, out var parsed) ? parsed : null;
        if (latest is null) return new(false, current, null, url, $"最新版本号格式未识别：{tag}");

        var available = latest > current;
        return new(available, current, latest, url,
            available ? $"发现新版本 v{latest.ToString(3)}，当前版本 v{current.ToString(3)}。" : $"当前已是最新版本 v{current.ToString(3)}。");
    }
}
