namespace FocusGuard.Infrastructure.Persistence;

public static class DatabasePaths
{
    public static string ApplicationDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusGuard");

    public static string DefaultDatabasePath => Path.Combine(ApplicationDataDirectory, "focusguard.db");
    public static string DefaultExtensionHeartbeatPath => Path.Combine(ApplicationDataDirectory, "extension-heartbeat.txt");
}

