using System.IO;
using System.Text.Json;

namespace VideoVault;

/// <summary>
/// 관리 리스트의 JSON 파일 읽기/쓰기를 담당한다. UI 코드와 분리되어 있다.
/// </summary>
public static class ManagedListRepository
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    public static List<ManagedVideoItem> Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<ManagedVideoItem>>(json, Options) ?? new List<ManagedVideoItem>();
    }

    public static void Save(string path, IEnumerable<ManagedVideoItem> items)
    {
        var json = JsonSerializer.Serialize(items, Options);
        File.WriteAllText(path, json);
    }
}
