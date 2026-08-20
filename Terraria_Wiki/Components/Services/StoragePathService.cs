namespace Terraria_Wiki.Services;

public enum StorageLocationMode
{
    Default,
    Application,
    Custom
}

public sealed class StoragePathService
{
    private const string LocationModeKey = "DataStorageLocationMode";
    private const string CustomPathKey = "DataStorageCustomPath";

    public string DefaultPath => FileSystem.AppDataDirectory;
    public string ApplicationPath => AppContext.BaseDirectory;

    public StorageLocationMode LocationMode
    {
        get => Enum.TryParse<StorageLocationMode>(
            Preferences.Default.Get(LocationModeKey, StorageLocationMode.Default.ToString()),
            out var mode) ? mode : StorageLocationMode.Default;
    }

    public string CustomPath => Preferences.Default.Get(CustomPathKey, string.Empty);

    public string RootPath => ResolvePath(LocationMode, CustomPath);

    public string ResolvePath(StorageLocationMode mode, string? customPath = null)
    {
        return mode switch
        {
            StorageLocationMode.Application => ApplicationPath,
            StorageLocationMode.Custom when !string.IsNullOrWhiteSpace(customPath) => Path.GetFullPath(customPath),
            _ => DefaultPath
        };
    }

    public async Task MigrateAsync(StorageLocationMode mode, string? customPath = null)
    {
        var sourcePath = Path.GetFullPath(RootPath);
        var targetPath = Path.GetFullPath(ResolvePath(mode, customPath));

        if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            SaveLocation(mode, customPath);
            return;
        }

        if (IsNestedPath(targetPath, sourcePath))
            throw new InvalidOperationException("目标数据目录不能位于当前数据目录内部。");

        await Task.Run(() => CopyDirectory(sourcePath, targetPath));
        SaveLocation(mode, customPath);
    }

    public void SaveLocation(StorageLocationMode mode, string? customPath = null)
    {
        Preferences.Default.Set(LocationModeKey, mode.ToString());
        Preferences.Default.Set(CustomPathKey, mode == StorageLocationMode.Custom ? customPath ?? string.Empty : string.Empty);
    }

    private static bool IsNestedPath(string path, string parent)
    {
        var relative = Path.GetRelativePath(parent, path);
        return relative != "." && relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}") && !Path.IsPathRooted(relative);
    }

    private static void CopyDirectory(string sourcePath, string targetPath)
    {
        if (!Directory.Exists(sourcePath))
            Directory.CreateDirectory(sourcePath);

        Directory.CreateDirectory(targetPath);
        foreach (var directory in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourcePath, directory);
            Directory.CreateDirectory(Path.Combine(targetPath, relative));
        }

        foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourcePath, file);
            var targetFile = Path.Combine(targetPath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(file, targetFile, true);
        }
    }
}
