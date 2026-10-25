namespace LotusLMS.Api;

/// <summary>Persists the generated device identity across logins and application restarts.</summary>
internal static class LotusLmsDeviceIdentity
{
    public static string GetOrCreate(string directory)
    {
        var fullDirectory = Path.GetFullPath(directory);
        Directory.CreateDirectory(fullDirectory);
        using var identityLock = AcquireLock(Path.Combine(fullDirectory, "device-uuid.lock"));
        var path = Path.Combine(fullDirectory, "device-uuid.txt");
        if (File.Exists(path))
        {
            return Read(path);
        }

        // Publish a complete file while holding the shared lock for this identity.
        var temporaryPath = Path.Combine(fullDirectory, $"device-uuid.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, Guid.NewGuid().ToString("D"));
            File.Move(temporaryPath, path, overwrite: false);

            return Read(path);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static FileStream AcquireLock(string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < 200)
            {
                Thread.Sleep(25);
            }
        }
    }

    private static string Read(string path)
    {
        var value = File.ReadAllText(path).Trim();
        if (!Guid.TryParse(value, out var uuid))
        {
            throw new InvalidDataException("The saved LotusLMS device UUID is invalid.");
        }

        return uuid.ToString("D");
    }
}
