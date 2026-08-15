using System.Globalization;
using System.Windows.Data;

namespace VideoVault;

/// <summary>
/// 썸네일 파일 경로(문자열)를 <see cref="ImageLoadHelper.Load"/>로 즉시 전부 읽어들인 이미지로 변환한다.
/// XAML의 암시적 문자열→ImageSource 변환은 파일을 계속 열어둬 이후 같은 경로 덮어쓰기가 실패하므로 사용하지 않는다.
/// </summary>
/// <remarks>
/// <c>ConverterParameter</c>에 정수를 지정하면 그 너비로만 디코딩한다(<see cref="ImageLoadHelper.Load"/>의
/// <c>decodePixelWidth</c>) — 원본 해상도 그대로 표시할 필요가 없는 작은 이미지(아이콘 보기 카드 등)에서
/// 디코딩 비용을 줄이기 위한 것. 지정하지 않으면 예전과 동일하게 원본 해상도 그대로 디코딩한다.
/// </remarks>
public class ThumbnailPathConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var decodePixelWidth = parameter is string s && int.TryParse(s, out var width) ? width : (int?)null;
        return ImageLoadHelper.Load(value as string, decodePixelWidth);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
