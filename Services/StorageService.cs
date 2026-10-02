using System;
using System.IO;

namespace sopfiy.Services;

public static class StorageService
{
    public static string AppDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AuraStream"
    );

    public static string LegacyDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "sopfiy"
    );

    static StorageService()
    {
        EnsureMigrated();
    }

    public static void EnsureMigrated()
    {
        try
        {
            if (!Directory.Exists(AppDirectory))
            {
                Directory.CreateDirectory(AppDirectory);
            }

            if (Directory.Exists(LegacyDirectory))
            {
                var filesToMigrate = new[] { "playlists.json", "auth_session.json" };
                foreach (var fileName in filesToMigrate)
                {
                    var sourcePath = Path.Combine(LegacyDirectory, fileName);
                    var destPath = Path.Combine(AppDirectory, fileName);

                    if (File.Exists(sourcePath) && !File.Exists(destPath))
                    {
                        File.Copy(sourcePath, destPath, overwrite: false);
                    }
                }
            }
        }
        catch
        {
            // Fallback gracefully without interrupting app launch
        }
    }

    public static string GetFilePath(string fileName)
    {
        EnsureMigrated();
        return Path.Combine(AppDirectory, fileName);
    }
}
