using System.IO;

namespace VideoVault;

/// <summary>
/// 관리 리스트 항목(파일)의 Tags와 태그 마스터 목록의 Credits(품번 목록)를 서로 동기화한다.
/// 한쪽이 바뀌면(파일명 변경, 속성 창에서 태그 제거 등) 다른 쪽도 그에 맞춰 갱신해서 두 데이터가
/// 어긋나지 않도록 한다 — <see cref="ActorCreditSync"/>와 완전히 동일한 목적/패턴이다(태그도 배우처럼
/// 한 파일에 여러 개가 동시에 붙을 수 있는 다대다 관계). (반대 방향인 "품번 추가 → Tags 갱신"은
/// <see cref="TagManagerWindow"/>가 직접 처리한다 — 이 클래스는 파일명 변경/태그 제거처럼 여러 창에서
/// 공유되는 동기화 로직만 담당한다.)
/// </summary>
/// <remarks>
/// 2026-09-13 추가 — 배우/시리즈에는 이미 <see cref="ActorCreditSync"/>/<see cref="SeriesCreditSync"/>가 있었지만
/// 태그에는 이에 대응하는 클래스가 아예 없어서, 파일명을 바꾸거나(품번 변경) 항목을 완전삭제해도 태그 Credits가
/// 따라가지 않고 예전 품번이 유령 참조로 남는 버그가 있었다(실사용 데이터 분석으로 발견 — 태그 Credits가
/// 참조하는 품번 중 상당수가 현재 관리 리스트의 어떤 파일과도 매칭되지 않았음). 배우/시리즈와 동일한 패턴으로
/// 이 클래스를 추가해 해결한다.
/// </remarks>
public static class TagCreditSync
{
    /// <summary>
    /// 파일명이 바뀌었을 때 호출한다.
    /// 1) 이 파일에 지정된 태그들 중 옛 품번을 Credits로 갖고 있던 태그는 새 품번으로 값을 갱신한다.
    /// 2) 새 품번이 이미 다른 태그의 Credits에 등록되어 있다면(=그 태그가 붙은 것으로 알려져 있다면), 그 태그를
    ///    이 파일의 Tags에 추가한다 — 관리 데이터에 없던 파일이 나중에 그 품번으로 rename된 경우를 위함이다.
    /// </summary>
    public static void OnFileRenamed(ManagedVideoItem item, string oldFileName, IEnumerable<TagItem> masterTags)
    {
        var oldCode = Path.GetFileNameWithoutExtension(oldFileName);
        var newCode = Path.GetFileNameWithoutExtension(item.FileName);

        if (string.Equals(oldCode, newCode, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // 이 파일에 이미 지정된 태그만 대상으로 한다 — 우연히 같은 문자열을 Credits로 가진, 이 파일과 무관한
        // 다른 태그의 데이터까지 건드리지 않기 위함이다.
        foreach (var tagName in item.Tags)
        {
            var tag = masterTags.FirstOrDefault(t => string.Equals(t.Name, tagName, StringComparison.OrdinalIgnoreCase));
            if (tag is null)
            {
                continue;
            }

            var index = tag.Credits.FindIndex(c => string.Equals(c, oldCode, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                var updatedCredits = new List<string>(tag.Credits) { [index] = newCode };
                tag.SetCredits(updatedCredits);
            }
        }

        var matchingTags = masterTags.Where(t =>
            t.Credits.Any(c => string.Equals(c, newCode, StringComparison.OrdinalIgnoreCase)) &&
            !item.Tags.Any(existing => string.Equals(existing, t.Name, StringComparison.OrdinalIgnoreCase)));

        foreach (var tag in matchingTags)
        {
            var updatedTags = new List<string>(item.Tags) { tag.Name };
            item.SetTags(updatedTags);
        }
    }

    /// <summary>
    /// 속성 창 등에서 이 파일의 Tags 목록에서 태그가 제거됐을 때 호출한다. 그 태그의 Credits에 이 파일의
    /// 품번이 있으면 함께 제거한다(이 파일이 더 이상 그 태그가 붙은 것으로 지정돼 있지 않다는 뜻이므로).
    /// </summary>
    public static void OnTagRemovedFromItem(ManagedVideoItem item, string removedTagName, IEnumerable<TagItem> masterTags)
    {
        var tag = masterTags.FirstOrDefault(t => string.Equals(t.Name, removedTagName, StringComparison.OrdinalIgnoreCase));
        if (tag is null)
        {
            return;
        }

        var code = Path.GetFileNameWithoutExtension(item.FileName);
        if (!tag.Credits.Any(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var updatedCredits = tag.Credits.Where(c => !string.Equals(c, code, StringComparison.OrdinalIgnoreCase)).ToList();
        tag.SetCredits(updatedCredits);
    }

    /// <summary>
    /// 항목이 완전삭제될 때(관리 데이터 자체가 사라질 때) 호출한다. 이 파일에 태깅된 모든 태그의 Credits에서
    /// 이 파일의 품번을 제거해서, 실제로는 더 이상 존재하지 않는 파일의 품번이 "파일 없음"(연한 색) 상태로
    /// 태그 관리 창에 고아처럼 남지 않도록 한다. 소프트 삭제("제거", 복구 가능)는 항목이 관리 리스트에 그대로
    /// 남아있으므로 대상이 아니다 — 완전삭제(되돌릴 수 없음)에서만 호출한다.
    /// </summary>
    public static void OnFileDeleted(ManagedVideoItem item, IEnumerable<TagItem> masterTags)
    {
        foreach (var tagName in item.Tags)
        {
            OnTagRemovedFromItem(item, tagName, masterTags);
        }
    }
}
