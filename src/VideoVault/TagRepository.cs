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

    /// <summary>
    /// 구버전 형식(문자열 배열, 예: <c>["코미디", "액션"]</c>)과 현재 형식(<see cref="TagItem"/> 객체 배열)을
    /// 둘 다 읽을 수 있다(2026-08-27 `TagItem`/Credits 도입 시 마이그레이션 — 구버전 파일이 있는 사용자도
    /// 그대로 이어서 쓸 수 있어야 한다). 배열의 첫 원소가 JSON 문자열이면 구버전으로 판단해 각 이름을
    /// <c>Credits</c>가 빈 <see cref="TagItem"/>으로 변환한다. 빈 배열(<c>[]</c>)은 어느 쪽으로 봐도 결과가
    /// 같으므로 구분할 필요 없이 바로 아래 분기로 넘어간다.
    /// </summary>
    public static List<TagItem> Load(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.ValueKind == JsonValueKind.Array &&
            doc.RootElement.GetArrayLength() > 0 &&
            doc.RootElement[0].ValueKind == JsonValueKind.String)
        {
            var legacyNames = JsonSerializer.Deserialize<List<string>>(json, Options) ?? new List<string>();
            return legacyNames.Select(name => new TagItem { Name = name }).ToList();
        }

        return JsonSerializer.Deserialize<List<TagItem>>(json, Options) ?? new List<TagItem>();
    }

    public static void Save(string path, IEnumerable<TagItem> tags)
    {
        var json = JsonSerializer.Serialize(tags, Options);
        File.WriteAllText(path, json);
    }
}
