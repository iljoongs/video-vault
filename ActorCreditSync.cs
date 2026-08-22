using System.IO;

namespace VideoVault;

/// <summary>
/// 관리 리스트 항목(파일)의 Actors와 배우 마스터 목록의 Credits(품번 목록)를 서로 동기화한다.
/// 한쪽이 바뀌면(파일명 변경, 속성 창에서 배우 제거 등) 다른 쪽도 그에 맞춰 갱신해서 두 데이터가
/// 어긋나지 않도록 한다. (반대 방향인 "작품 추가 → Actors 갱신"은 <see cref="ActorManagerWindow"/>가
/// 직접 처리한다 — 이 클래스는 파일명 변경/배우 제거처럼 여러 창에서 공유되는 동기화 로직만 담당한다.)
/// </summary>
public static class ActorCreditSync
{
    /// <summary>
    /// 파일명이 바뀌었을 때 호출한다.
    /// 1) 이 파일에 지정된 배우들 중 옛 품번을 Credits로 갖고 있던 배우는 새 품번으로 값을 갱신한다.
    /// 2) 새 품번이 이미 다른 배우의 Credits에 등록되어 있다면(=그 배우의 작품으로 알려져 있다면), 그 배우를
    ///    이 파일의 Actors에 추가한다 — 관리 데이터에 없던 파일이 나중에 그 품번으로 rename된 경우를 위함이다.
    /// </summary>
    public static void OnFileRenamed(ManagedVideoItem item, string oldFileName, IEnumerable<ActorItem> masterActors)
    {
        var oldCode = Path.GetFileNameWithoutExtension(oldFileName);
        var newCode = Path.GetFileNameWithoutExtension(item.FileName);

        if (string.Equals(oldCode, newCode, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // 이 파일에 이미 지정된 배우만 대상으로 한다 — 우연히 같은 문자열을 Credits로 가진, 이 파일과 무관한
        // 다른 배우의 데이터까지 건드리지 않기 위함이다.
        foreach (var actorName in item.Actors)
        {
            var actor = masterActors.FirstOrDefault(a => string.Equals(a.Name, actorName, StringComparison.OrdinalIgnoreCase));
            if (actor is null)
            {
                continue;
            }

            var index = actor.Credits.FindIndex(c => string.Equals(c, oldCode, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                var updatedCredits = new List<string>(actor.Credits) { [index] = newCode };
                actor.SetCredits(updatedCredits);
            }
        }

        var matchingActors = masterActors.Where(a =>
            a.Credits.Any(c => string.Equals(c, newCode, StringComparison.OrdinalIgnoreCase)) &&
            !item.Actors.Any(existing => string.Equals(existing, a.Name, StringComparison.OrdinalIgnoreCase)));

        foreach (var actor in matchingActors)
        {
            var updatedActors = new List<string>(item.Actors) { actor.Name };
            item.SetActors(updatedActors);
        }
    }

    /// <summary>
    /// 속성 창 등에서 이 파일의 Actors 목록에서 배우가 제거됐을 때 호출한다. 그 배우의 Credits에 이 파일의
    /// 품번이 있으면 함께 제거한다(이 파일이 더 이상 그 배우의 작품으로 지정돼 있지 않다는 뜻이므로).
    /// </summary>
    public static void OnActorRemovedFromItem(ManagedVideoItem item, string removedActorName, IEnumerable<ActorItem> masterActors)
    {
        var actor = masterActors.FirstOrDefault(a => string.Equals(a.Name, removedActorName, StringComparison.OrdinalIgnoreCase));
        if (actor is null)
        {
            return;
        }

        var code = Path.GetFileNameWithoutExtension(item.FileName);
        if (!actor.Credits.Any(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var updatedCredits = actor.Credits.Where(c => !string.Equals(c, code, StringComparison.OrdinalIgnoreCase)).ToList();
        actor.SetCredits(updatedCredits);
    }

    /// <summary>
    /// 항목이 완전삭제될 때(관리 데이터 자체가 사라질 때) 호출한다. 이 파일에 태깅된 모든 배우의 Credits에서
    /// 이 파일의 품번을 제거해서, 실제로는 더 이상 존재하지 않는 파일의 품번이 "파일 없음"(연한 색) 상태로
    /// 배우 관리 창에 고아처럼 남지 않도록 한다(2026-08-16 추가). 소프트 삭제("제거", 복구 가능)는 항목이
    /// 관리 리스트에 그대로 남아있으므로 대상이 아니다 — 완전삭제(되돌릴 수 없음)에서만 호출한다.
    /// </summary>
    public static void OnFileDeleted(ManagedVideoItem item, IEnumerable<ActorItem> masterActors)
    {
        foreach (var actorName in item.Actors)
        {
            OnActorRemovedFromItem(item, actorName, masterActors);
        }
    }
}
