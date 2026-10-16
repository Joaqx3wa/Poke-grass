using Velopack;
using Velopack.Sources;
using System.Reflection;

namespace PokeGrass;

internal sealed record AppUpdateCheck(UpdateManager Manager, UpdateInfo? Update, bool IsInstalled);

internal static class AppUpdateService
{
    public const string RepositoryUrl = "https://github.com/Joaqx3wa/Poke-grass";
    public static string InstalledVersion => typeof(App).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(App).Assembly.GetName().Version?.ToString(3)
        ?? "desconocida";

    public static async Task<AppUpdateCheck> CheckAsync()
    {
        var manager = new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: true));
        if (!manager.IsInstalled) return new AppUpdateCheck(manager, null, false);
        return new AppUpdateCheck(manager, await manager.CheckForUpdatesAsync(), true);
    }

    public static Task DownloadAsync(UpdateManager manager, UpdateInfo update, Action<int> progress,
        CancellationToken cancellationToken = default) => manager.DownloadUpdatesAsync(update, progress, cancellationToken);

    public static void ApplyAndRestart(UpdateManager manager, UpdateInfo update) =>
        manager.ApplyUpdatesAndRestart(update.TargetFullRelease);

}
