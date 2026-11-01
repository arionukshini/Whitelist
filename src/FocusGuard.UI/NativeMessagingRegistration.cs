using FocusGuard.Enforcement;
using Microsoft.Win32;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FocusGuard.UI;

internal static class NativeMessagingRegistration
{
    public const string HostName = "com.whitelistlockin.host";

    public static bool RegisterIfHostIsInstalled()
    {
        var hostPath = Path.Combine(AppContext.BaseDirectory, "NativeHost", "WhitelistLockIn.NativeHost.exe");
        if (!File.Exists(hostPath)) return false;
        Register(hostPath);
        return true;
    }

    public static void RegisterInstalledHost()
    {
        var hostPath = Path.Combine(AppContext.BaseDirectory, "NativeHost", "WhitelistLockIn.NativeHost.exe");
        if (!File.Exists(hostPath)) throw new FileNotFoundException("The browser bridge host is missing.", hostPath);
        Register(hostPath);
    }

    private static void Register(string hostPath)
    {
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "NativeMessagingHost.json");
        var manifest = new HostManifest(
            HostName,
            "Whitelist Lock In browser session bridge",
            Path.GetFullPath(hostPath),
            "stdio",
            [$"chrome-extension://{WebsiteAllowlistServer.ExtensionId}/"]);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));

        RegisterBrowser(@"Software\Google\Chrome\NativeMessagingHosts\" + HostName, manifestPath);
        RegisterBrowser(@"Software\Microsoft\Edge\NativeMessagingHosts\" + HostName, manifestPath);
    }

    private static void RegisterBrowser(string keyPath, string manifestPath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true)
            ?? throw new IOException($"Could not register the browser bridge at {keyPath}.");
        key.SetValue(string.Empty, manifestPath, RegistryValueKind.String);
    }

    private sealed record HostManifest(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("allowed_origins")] IReadOnlyList<string> AllowedOrigins);
}
