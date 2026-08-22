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

    /// <summary>
    /// 품번은 시리즈 하나에만 속해야 하는데, Credits를 수동으로 관리하다 보면 같은 품번이 둘 이상의 시리즈에
    /// 동시에 남아있는 경우가 생길 수 있다(예: 파일의 Series를 다른 시리즈로 바꿨는데 예전 시리즈의 Credits에는
    /// 옛 지정이 그대로 남아있던 경우). 이를 정리한다(2026-08-16 추가) — 우선순위는 다음과 같다:
    /// ① 그 품번의 실제 파일이 있고 <see cref="ManagedVideoItem.Series"/>가 지정되어 있으면, 그 시리즈가
    ///    "최근에 업데이트된" 것으로 보고 그 시리즈만 남기고 나머지에서 제거한다.
    /// ② 실제 파일이 없거나 Series가 비어있어 판단 근거가 없으면(예: 두 시리즈 모두 파일 없이 수동 등록만 된
    ///    경우), 이름 기준 오름차순으로 가장 앞선 시리즈를 남기고 나머지에서 제거한다(결정적 기본 규칙).
    /// </summary>
    public static void RemoveDuplicateCreditsAcrossSeries(IEnumerable<SeriesItem> masterSeries, IEnumerable<ManagedVideoItem> managedItems)
    {
        var codeToItemSeries = managedItems
            .Where(m => !string.IsNullOrEmpty(m.Series))
            .GroupBy(m => Path.GetFileNameWithoutExtension(m.FileName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Series, StringComparer.OrdinalIgnoreCase);

        var codeToSeriesList = new Dictionary<string, List<SeriesItem>>(StringComparer.OrdinalIgnoreCase);
        foreach (var series in masterSeries)
        {
            foreach (var code in series.Credits)
            {
                if (!codeToSeriesList.TryGetValue(code, out var seriesWithCode))
                {
                    seriesWithCode = new List<SeriesItem>();
                    codeToSeriesList[code] = seriesWithCode;
                }

                seriesWithCode.Add(series);
            }
        }

        foreach (var (code, seriesWithCode) in codeToSeriesList)
        {
            if (seriesWithCode.Count < 2)
            {
                continue;
            }

            var winner = codeToItemSeries.TryGetValue(code, out var itemSeriesName)
                ? seriesWithCode.FirstOrDefault(s => string.Equals(s.Name, itemSeriesName, StringComparison.OrdinalIgnoreCase))
                    ?? seriesWithCode.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).First()
                : seriesWithCode.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).First();

            foreach (var loser in seriesWithCode.Where(s => !ReferenceEquals(s, winner)))
            {
                loser.SetCredits(loser.Credits.Where(c => !string.Equals(c, code, StringComparison.OrdinalIgnoreCase)).ToList());
            }
        }
    }

    /// <summary>
    /// 항목이 완전삭제될 때(관리 데이터 자체가 사라질 때) 호출한다. 이 파일에 지정된 시리즈의 Credits에서
    /// 이 파일의 품번을 제거해서, 실제로는 더 이상 존재하지 않는 파일의 품번이 "파일 없음"(연한 색) 상태로
    /// 시리즈 관리 창에 고아처럼 남지 않도록 한다(2026-08-16 추가, <see cref="ActorCreditSync.OnFileDeleted"/>와
    /// 동일한 목적). 소프트 삭제("제거", 복구 가능)는 항목이 관리 리스트에 그대로 남아있으므로 대상이 아니다.
    /// </summary>
    public static void OnFileDeleted(ManagedVideoItem item, IEnumerable<SeriesItem> masterSeries)
    {
        if (string.IsNullOrEmpty(item.Series))
        {
            return;
        }

        var series = masterSeries.FirstOrDefault(s => string.Equals(s.Name, item.Series, StringComparison.OrdinalIgnoreCase));
        var code = Path.GetFileNameWithoutExtension(item.FileName);
        if (series is null || !series.Credits.Any(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var updatedCredits = series.Credits.Where(c => !string.Equals(c, code, StringComparison.OrdinalIgnoreCase)).ToList();
        series.SetCredits(updatedCredits);
    }
}
