using System.Collections;
using System.Text.RegularExpressions;

namespace VideoVault;

/// <summary>
/// 관리 리스트를 파일명 기준으로 정렬할 때, 문자와 숫자가 섞인 이름을 사람이 기대하는 순서로 비교한다
/// (예: "abp-2.mp4"가 "abp-10.mp4"보다 앞에 온다). 기본 문자열(사전식) 비교로는 "1" &lt; "2"라서
/// "abp-10.mp4"가 "abp-2.mp4"보다 앞에 오는 문제가 있었다.
/// </summary>
public class FileNameNaturalComparer : IComparer
{
    public static readonly FileNameNaturalComparer Ascending = new(1);
    public static readonly FileNameNaturalComparer Descending = new(-1);

    private static readonly Regex NumberRunRegex = new(@"(\d+)", RegexOptions.Compiled);

    private readonly int _direction;

    private FileNameNaturalComparer(int direction) => _direction = direction;

    public int Compare(object? x, object? y) =>
        _direction * CompareNatural((x as ManagedVideoItem)?.FileName, (y as ManagedVideoItem)?.FileName);

    private static int CompareNatural(string? a, string? b)
    {
        a ??= string.Empty;
        b ??= string.Empty;

        var partsA = NumberRunRegex.Split(a);
        var partsB = NumberRunRegex.Split(b);

        var length = Math.Min(partsA.Length, partsB.Length);
        for (var i = 0; i < length; i++)
        {
            var partA = partsA[i];
            var partB = partsB[i];
            var bothDigits = partA.Length > 0 && partB.Length > 0 && char.IsDigit(partA[0]) && char.IsDigit(partB[0]);

            var cmp = bothDigits
                ? CompareDigitRuns(partA, partB)
                : string.Compare(partA, partB, StringComparison.CurrentCulture);

            if (cmp != 0)
            {
                return cmp;
            }
        }

        return partsA.Length.CompareTo(partsB.Length);
    }

    /// <summary>같은 자릿수 비교가 아니라 값 비교를 위해 앞의 "0"을 뗀 뒤 길이 → 값 순으로 비교한다
    /// (오버플로 걱정 없이 임의 자릿수의 숫자를 다룰 수 있다).</summary>
    private static int CompareDigitRuns(string a, string b)
    {
        var trimmedA = a.TrimStart('0');
        var trimmedB = b.TrimStart('0');

        if (trimmedA.Length != trimmedB.Length)
        {
            return trimmedA.Length.CompareTo(trimmedB.Length);
        }

        var cmp = string.CompareOrdinal(trimmedA, trimmedB);
        return cmp != 0 ? cmp : a.Length.CompareTo(b.Length);
    }
}
