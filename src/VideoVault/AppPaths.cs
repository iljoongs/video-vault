using System.IO;

namespace VideoVault;

/// <summary>
/// 관리 리스트 / 태그 마스터 목록의 기본 저장 위치를 관리한다.
/// </summary>
public static class AppPaths
{
    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoVault");

    /// <summary>관리 리스트 전체(유효/제거된 항목 모두, `IsValid`로 구분)를 저장하는 유일한 파일.</summary>
    public static string LibraryPath => Path.Combine(AppDataDir, "library.json");

    /// <summary>
    /// (레거시) 예전 버전이 "제거"된 항목을 활성 목록과 별도 파일로 저장하던 경로(2026-08-06 이전).
    /// 지금은 모든 항목을 <see cref="LibraryPath"/> 하나에 `IsValid`로 구분해 저장하므로 더 이상 쓰지
    /// 않으며, 이 상수는 시작 시 한 번(`MainWindow.LoadInitialData`) 예전 파일을 발견해 병합·삭제하는
    /// 마이그레이션 용도로만 남아있다.
    /// </summary>
    public static string LegacyRemovedLibraryPath => Path.Combine(AppDataDir, "removed.json");

    public static string TagsPath => Path.Combine(AppDataDir, "tags.json");

    public static string SettingsPath => Path.Combine(AppDataDir, "settings.json");

    public static string ActorsPath => Path.Combine(AppDataDir, "actors.json");

    public static string ActorsThumbnailDir => Path.Combine(AppDataDir, "actresses");

    public static string SeriesPath => Path.Combine(AppDataDir, "series.json");

    public static void EnsureAppDataDirectory() => Directory.CreateDirectory(AppDataDir);

    public static void EnsureActorsThumbnailDirectory() => Directory.CreateDirectory(ActorsThumbnailDir);
}
