using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VideoVault;

/// <summary>
/// 원본 이미지를 동영상 파일과 같은 폴더에 원본 그대로, 그리고 320x240 이내로 리사이즈한 썸네일용으로 각각 저장한다.
/// </summary>
public static class ThumbnailHelper
{
    /// <summary>썸네일의 최대 가로/세로 크기. 비율을 유지하며 이 범위 안에 맞추므로 실제 결과 크기는 이보다 작을 수 있다.</summary>
    public const int ThumbnailWidth = 320;
    public const int ThumbnailHeight = 240;

    public readonly record struct Result(string ThumbnailPath, string OriginalPath);

    /// <summary>"파일 없이 추가"된 항목(실제 폴더가 없음)의 썸네일을 저장할 고정 폴더.</summary>
    public const string PlaceholderThumbnailDir = @"E:\happy\thumbnail";

    /// <summary>
    /// <paramref name="sourceImagePath"/> 이미지를
    /// (1) 원본 그대로 <paramref name="videoFullPath"/>와 같은 폴더에 "{동영상 파일명}.original{확장자}"로 복사하고,
    /// (2) 가로세로 비율을 유지한 채 320x240 이내로 리사이즈해(둘 중 더 많이 축소되는 쪽에 맞춤) 같은 폴더에 "{동영상 파일명}.thumbnail.jpg"로 저장한 뒤,
    /// (3) 두 파일로의 복사가 끝난 <paramref name="sourceImagePath"/> 원본은 삭제한다
    /// (드래그 앤 드롭/다운로드로 만들어진 임시 파일이 남지 않도록 하기 위함이기도 하다).
    /// </summary>
    public static Result CreateThumbnail(string sourceImagePath, string videoFullPath)
    {
        var videoDir = Path.GetDirectoryName(videoFullPath)
            ?? throw new InvalidOperationException("동영상 파일의 폴더 경로를 확인할 수 없습니다.");
        var videoNameNoExt = Path.GetFileNameWithoutExtension(videoFullPath);

        return CreateThumbnailInDirectory(sourceImagePath, videoDir, videoNameNoExt);
    }

    /// <summary>
    /// "파일 없이 추가"된 항목은 실제 폴더가 없으므로(<see cref="ManagedVideoItem.FullPath"/>가 파일명뿐이거나
    /// 비어있음), 동영상과 같은 폴더 대신 <see cref="PlaceholderThumbnailDir"/>에 저장한다(2026-08-06 추가).
    /// </summary>
    public static Result CreatePlaceholderThumbnail(string sourceImagePath, string itemFileName)
    {
        Directory.CreateDirectory(PlaceholderThumbnailDir);
        var nameNoExt = Path.GetFileNameWithoutExtension(itemFileName);
        return CreateThumbnailInDirectory(sourceImagePath, PlaceholderThumbnailDir, nameNoExt);
    }

    private static Result CreateThumbnailInDirectory(string sourceImagePath, string videoDir, string videoNameNoExt)
    {
        var sourceExtension = Path.GetExtension(sourceImagePath);
        if (string.IsNullOrEmpty(sourceExtension))
        {
            sourceExtension = ".jpg";
        }

        var sourceFullPath = Path.GetFullPath(sourceImagePath);
        var originalPath = Path.Combine(videoDir, $"{videoNameNoExt}.original{sourceExtension}");
        var isSourceSameAsOriginal = string.Equals(sourceFullPath, Path.GetFullPath(originalPath), StringComparison.OrdinalIgnoreCase);

        if (!isSourceSameAsOriginal)
        {
            File.Copy(sourceImagePath, originalPath, overwrite: true);
        }

        var decoder = BitmapDecoder.Create(new Uri(sourceImagePath), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var source = decoder.Frames[0];

        var scale = Math.Min((double)ThumbnailWidth / source.PixelWidth, (double)ThumbnailHeight / source.PixelHeight);
        var resized = new TransformedBitmap(source, new ScaleTransform(scale, scale));

        var thumbnailPath = Path.Combine(videoDir, $"{videoNameNoExt}.thumbnail.jpg");

        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(resized));

        using (var stream = new FileStream(thumbnailPath, FileMode.Create, FileAccess.Write))
        {
            encoder.Save(stream);
        }

        var isSourceSameAsThumbnail = string.Equals(sourceFullPath, Path.GetFullPath(thumbnailPath), StringComparison.OrdinalIgnoreCase);
        if (!isSourceSameAsOriginal && !isSourceSameAsThumbnail)
        {
            TryDeleteSource(sourceImagePath);
        }

        return new Result(thumbnailPath, originalPath);
    }

    /// <summary>배우 썸네일 최대 가로/세로 크기.</summary>
    public const int ActorThumbnailWidth = 100;
    public const int ActorThumbnailHeight = 100;

    /// <summary>
    /// <paramref name="sourceImagePath"/> 이미지를 100x100 이내로 리사이즈해 <paramref name="destJpgPath"/>에 저장한다.
    /// 동영상 썸네일과 달리 원본은 별도로 보관하지 않고, 저장이 끝나면 소스 파일을 정리한다.
    /// </summary>
    public static string CreateActorThumbnail(string sourceImagePath, string destJpgPath)
    {
        var decoder = BitmapDecoder.Create(new Uri(sourceImagePath), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var source = decoder.Frames[0];

        var scale = Math.Min((double)ActorThumbnailWidth / source.PixelWidth, (double)ActorThumbnailHeight / source.PixelHeight);
        var resized = new TransformedBitmap(source, new ScaleTransform(scale, scale));

        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(resized));

        using (var stream = new FileStream(destJpgPath, FileMode.Create, FileAccess.Write))
        {
            encoder.Save(stream);
        }

        var sourceFullPath = Path.GetFullPath(sourceImagePath);
        var isSourceSameAsDest = string.Equals(sourceFullPath, Path.GetFullPath(destJpgPath), StringComparison.OrdinalIgnoreCase);
        if (!isSourceSameAsDest)
        {
            TryDeleteSource(sourceImagePath);
        }

        return destJpgPath;
    }

    private static void TryDeleteSource(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // 원본/썸네일 저장은 이미 성공했으므로, 소스 삭제 실패(권한 등)는 전체 동작을 실패로 취급하지 않는다.
        }
    }
}
