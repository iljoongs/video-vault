using System.Collections;
using System.Globalization;
using System.Windows.Data;

namespace VideoVault;

/// <summary>
/// 태그 목록(<see cref="ManagedVideoItem.Tags"/>)을 태그 마스터 목록과 같은 순서(이름 기준 오름차순,
/// <c>TagManagerWindow</c>가 보여주는 순서와 동일)로 정렬해서 보여준다. <see cref="ManagedVideoItem.Tags"/>
/// 자체는 태그가 추가된 순서를 그대로 담고 있어(추가/삭제 시 순서가 뒤섞임), 관리 리스트의 "태그" 컬럼처럼
/// 여러 항목을 나란히 볼 때 태그 위치가 항목마다 들쭉날쭉해 보이는 문제가 있었다.
/// </summary>
public class TagsOrderConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not IEnumerable tags)
        {
            return value;
        }

        return tags.Cast<string>().OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
