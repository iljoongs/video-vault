using System.IO;

namespace VideoVault;

/// <summary>
/// 관리 리스트 / 태그 마스터 목록의 기본 저장 위치를 관리한다.
/// </summary>
public static class AppPaths
{
    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoVault");

    public static string LibraryPath => Path.Combine(AppDataDir, "library.json");

    /// <summary>"제거"된(활성 목록에서 빠진) 관리 리스트 항목을 별도로 저장하는 파일. 데이터는 보존되어 재사용 가능하다.</summary>
    public static string RemovedLibraryPath => Path.Combine(AppDataDir, "removed.json");

    public static string TagsPath => Path.Combine(AppDataDir, "tags.json");

    public static string SettingsPath => Path.Combine(AppDataDir, "settings.json");

    public static string ActorsPath => Path.Combine(AppDataDir, "actors.json");

    public static string ActorsThumbnailDir => Path.Combine(AppDataDir, "actresses");

    public static void EnsureAppDataDirectory() => Directory.CreateDirectory(AppDataDir);

    public static void EnsureActorsThumbnailDirectory() => Directory.CreateDirectory(ActorsThumbnailDir);
}
