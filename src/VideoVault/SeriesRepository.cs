using System.IO;
using System.Text.Json;

namespace VideoVault;

/// <summary>
/// 시리즈 마스터 목록(`series.json`)의 읽기/쓰기를 담당한다. 관리 리스트/태그/배우 저장소와 분리되어 있다.
/// </summary>
public static class SeriesRepository
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    public static List<SeriesItem> Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<SeriesItem>>(json, Options) ?? new List<SeriesItem>();
    }

    public static void Save(string path, IEnumerable<SeriesItem> series)
    {
        var json = JsonSerializer.Serialize(series, Options);
        File.WriteAllText(path, json);
    }
}
