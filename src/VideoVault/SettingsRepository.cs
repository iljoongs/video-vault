using System.IO;
using System.Text.Json;

namespace VideoVault;

/// <summary>
/// 설정 파일(`settings.json`)의 읽기/쓰기를 담당한다. 다른 Repository와 분리되어 있다.
/// </summary>
public static class SettingsRepository
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    public static AppSettings Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
    }

    public static void Save(string path, AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, Options);
        File.WriteAllText(path, json);
    }
}
