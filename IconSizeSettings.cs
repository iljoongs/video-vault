using System.ComponentModel;

namespace VideoVault;

public enum IconSize
{
    ExtraLarge,
    Large,
    Normal,
    Small,
}

/// <summary>
/// 관리 리스트 아이콘 보기의 카드 크기 프리셋. 아이콘 보기 DataTemplate이 이 싱글턴 인스턴스의
/// 속성에 직접 바인딩하므로(<see cref="Current"/>), <see cref="Apply"/>로 프리셋을 바꾸면
/// PropertyChanged를 통해 이미 그려진 모든 카드에 즉시 반영된다.
/// </summary>
public class IconSizeSettings : INotifyPropertyChanged
{
    public static readonly IconSizeSettings Current = new();

    private double _cardWidth;
    private double _cardHeight;
    private double _thumbnailWidth;
    private double _thumbnailHeight;
    private double _fallbackIconFontSize;
    private double _fileNameFontSize;
    private double _playCountFontSize;
    private double _tagFontSize;
    private IconSize _preset;

    private IconSizeSettings()
    {
        Apply(IconSize.Normal);
    }

    /// <summary>마지막으로 <see cref="Apply"/>에 넘긴 프리셋. 설정 저장 시 이 값을 읽어 문자열로 기록한다.</summary>
    public IconSize Preset => _preset;

    public double CardWidth
    {
        get => _cardWidth;
        private set { _cardWidth = value; OnPropertyChanged(nameof(CardWidth)); }
    }

    public double CardHeight
    {
        get => _cardHeight;
        private set { _cardHeight = value; OnPropertyChanged(nameof(CardHeight)); }
    }

    public double ThumbnailWidth
    {
        get => _thumbnailWidth;
        private set { _thumbnailWidth = value; OnPropertyChanged(nameof(ThumbnailWidth)); }
    }

    public double ThumbnailHeight
    {
        get => _thumbnailHeight;
        private set { _thumbnailHeight = value; OnPropertyChanged(nameof(ThumbnailHeight)); }
    }

    public double FallbackIconFontSize
    {
        get => _fallbackIconFontSize;
        private set { _fallbackIconFontSize = value; OnPropertyChanged(nameof(FallbackIconFontSize)); }
    }

    public double FileNameFontSize
    {
        get => _fileNameFontSize;
        private set { _fileNameFontSize = value; OnPropertyChanged(nameof(FileNameFontSize)); }
    }

    public double PlayCountFontSize
    {
        get => _playCountFontSize;
        private set { _playCountFontSize = value; OnPropertyChanged(nameof(PlayCountFontSize)); }
    }

    public double TagFontSize
    {
        get => _tagFontSize;
        private set { _tagFontSize = value; OnPropertyChanged(nameof(TagFontSize)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Apply(IconSize size)
    {
        _preset = size;
        (CardWidth, CardHeight, ThumbnailWidth, ThumbnailHeight, FallbackIconFontSize, FileNameFontSize, PlayCountFontSize, TagFontSize) = size switch
        {
            IconSize.ExtraLarge => (270.0, 380.0, 240.0, 175.0, 64.0, 15.0, 12.0, 11.0),
            IconSize.Large => (210.0, 300.0, 180.0, 131.0, 48.0, 13.0, 11.0, 10.0),
            IconSize.Normal => (160.0, 225.0, 130.0, 95.0, 36.0, 11.0, 10.0, 9.0),
            IconSize.Small => (110.0, 155.0, 85.0, 62.0, 24.0, 9.0, 8.0, 8.0),
            _ => (160.0, 225.0, 130.0, 95.0, 36.0, 11.0, 10.0, 9.0),
        };
    }

    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
