using System.IO;
using System.Text.Json;

namespace VideoVault;

/// <summary>
/// 배우 마스터 목록(`actors.json`)의 읽기/쓰기를 담당한다. 관리 리스트/태그 저장소와 분리되어 있다.
/// </summary>
public static class ActorRepository
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    public static List<ActorItem> Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<ActorItem>>(json, Options) ?? new List<ActorItem>();
    }

    public static void Save(string path, IEnumerable<ActorItem> actors)
    {
        var json = JsonSerializer.Serialize(actors, Options);
        File.WriteAllText(path, json);
    }
}
