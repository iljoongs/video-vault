using System.IO;
using System.Windows.Media.Imaging;

namespace VideoVault;

/// <summary>
/// 로컬 파일 경로에서 <see cref="BitmapImage"/>를 즉시 전부 읽어들여(반환 즉시 파일 핸들 해제) 만든다.
/// </summary>
/// <remarks>
/// WPF의 <see cref="BitmapImage"/>는 기본 캐시 옵션(지연 로딩)으로 만들면 화면에 표시되는 동안 원본 파일을
/// 계속 열어둔다. 그 상태에서 같은 경로에 새 썸네일을 덮어쓰려 하면 "다른 프로세스가 파일을 사용 중"이라는
/// 오류가 난다(이 앱 자신이 UI에 표시하려고 열어둔 핸들과 충돌하는 것). <see cref="BitmapCacheOption.OnLoad"/>로
/// 로드 시점에 픽셀 데이터를 전부 메모리로 읽고 <see cref="System.Windows.Freezable.Freeze"/>하면 이 문제가 사라진다.
///
/// 별개로, WPF는 같은 URI로 다시 로드하면 디스크를 다시 읽지 않고 내부 이미지 캐시(URI 기준)에서 예전
/// 픽셀 데이터를 그대로 돌려주는 경우가 있다. 배우 썸네일처럼 "항상 같은 경로에 덮어쓰는" 파일을 다시
/// 로드하면 새로 만든 파일인데도 화면에는 예전 썸네일이 계속 보이는 버그가 이 때문에 생겼다.
/// <see cref="BitmapCreateOptions.IgnoreImageCache"/>로 이 캐시를 건너뛰고 항상 디스크에서 다시 읽는다.
/// </remarks>
public static class ImageLoadHelper
{
    /// <summary>
    /// <paramref name="decodePixelWidth"/>를 지정하면 그 너비로만 디코딩한다(세로는 원본 비율에 맞춰 자동 계산됨).
    /// 화면에 작게 표시될 이미지(예: 아이콘 보기 카드)를 원본 해상도 그대로 디코딩하는 비용을 줄이기 위한 것 —
    /// 지정하지 않으면 예전과 동일하게 원본 해상도 그대로 디코딩한다.
    /// </summary>
    public static BitmapImage? Load(string? path, int? decodePixelWidth = null)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        if (decodePixelWidth is { } width)
        {
            bitmap.DecodePixelWidth = width;
        }

        bitmap.UriSource = new Uri(path);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
