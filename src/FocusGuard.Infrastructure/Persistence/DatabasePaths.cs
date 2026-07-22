namespace FocusGuard.Infrastructure.Persistence;

public static class DatabasePaths
{
    public static string DefaultDatabasePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusGuard", "focusguard.db");
}

