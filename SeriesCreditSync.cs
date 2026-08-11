using System.IO;

namespace VideoVault;

/// <summary>
/// 관리 리스트 항목(파일)의 Series와 시리즈 마스터 목록의 Credits(품번 목록)를 서로 동기화한다.
/// 파일명이 바뀌면 그 파일이 속한 시리즈의 Credits도 그에 맞춰 갱신해서 두 데이터가 어긋나지 않도록 한다 —
/// <see cref="ActorCreditSync"/>와 동일한 목적이지만 Series는 단일 값이라 로직이 더 단순하다.
/// </summary>
public static class SeriesCreditSync
{
    /// <summary>
    /// 파일명이 바뀌었을 때 호출한다.
    /// 1) 이 파일이 지정된 시리즈가 옛 품번을 Credits로 갖고 있으면 새 품번으로 값을 갱신한다 — 이걸 안 하면
    ///    새 품번은 다른 경로(SyncCreditsFromManagedItems)로 다시 추가되는데 옛 품번은 아무도 지우지 않아
    ///    Credits에 옛 품번이 고아로 남는 버그가 있었다(2026-08-09 수정).
    /// 2) 새 품번이 이미 다른(아직 파일 없이 등록만 된) 시리즈의 Credits와 일치하고, 이 파일에 아직 시리즈가
    ///    지정되어 있지 않다면, 그 시리즈를 이 파일의 Series로 지정한다 — `ActorCreditSync`의 대응 동작과
    ///    같은 목적이지만, Series는 단일 값이므로 이미 다른 시리즈가 지정된 파일은 건드리지 않는다.
    /// </summary>
    public static void OnFileRenamed(ManagedVideoItem item, string oldFileName, IEnumerable<SeriesItem> masterSeries)
    {
        var oldCode = Path.GetFileNameWithoutExtension(oldFileName);
        var newCode = Path.GetFileNameWithoutExtension(item.FileName);

        if (string.Equals(oldCode, newCode, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.IsNullOrEmpty(item.Series))
        {
            var currentSeries = masterSeries.FirstOrDefault(s => string.Equals(s.Name, item.Series, StringComparison.OrdinalIgnoreCase));
            var index = currentSeries?.Credits.FindIndex(c => string.Equals(c, oldCode, StringComparison.OrdinalIgnoreCase)) ?? -1;
            if (currentSeries is not null && index >= 0)
            {
                var updatedCredits = new List<string>(currentSeries.Credits) { [index] = newCode };
                currentSeries.SetCredits(updatedCredits);
            }

            return;
        }

        var matchingSeries = masterSeries.FirstOrDefault(s =>
            s.Credits.Any(c => string.Equals(c, newCode, StringComparison.OrdinalIgnoreCase)));

        if (matchingSeries is not null)
        {
            item.Series = matchingSeries.Name;
        }
    }
}
