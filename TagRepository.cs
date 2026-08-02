using System.IO;
using System.Text.Json;

namespace VideoVault;

/// <summary>
/// 태그 마스터 목록(`tags.json`)의 읽기/쓰기를 담당한다. 관리 리스트 저장소와 분리되어 있다.
/// </summary>
public static class TagRepository
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    public static List<string> Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<string>>(json, Options) ?? new List<string>();
    }

    public static void Save(string path, IEnumerable<string> tags)
    {
        var json = JsonSerializer.Serialize(tags, Options);
        File.WriteAllText(path, json);
    }
}
