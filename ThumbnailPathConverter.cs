using System.Globalization;
using System.Windows.Data;

namespace VideoVault;

/// <summary>
/// 썸네일 파일 경로(문자열)를 <see cref="ImageLoadHelper.Load"/>로 즉시 전부 읽어들인 이미지로 변환한다.
/// XAML의 암시적 문자열→ImageSource 변환은 파일을 계속 열어둬 이후 같은 경로 덮어쓰기가 실패하므로 사용하지 않는다.
/// </summary>
public class ThumbnailPathConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ImageLoadHelper.Load(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
