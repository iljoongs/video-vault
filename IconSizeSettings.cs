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

    /// <summary>카드 테두리의 `Border.Padding`(5, 위/아래 합 10) + `Border.BorderThickness`(1, 합 2)
    /// — "아이콘만 보기"에서 카드 높이를 썸네일 높이에 정확히 맞추려면 이 여백만큼 썸네일 높이에 더해야 한다
    /// (`MainWindow.xaml`의 아이콘 보기 카드 `Border` 참고). 가로 폭에는 쓰이지 않는다(<see cref="RecomputeCardSize"/> 참고).</summary>
    private const double CardChromeSize = 12.0;

    private double _cardWidth;
    private double _cardHeight;
    private double _baseCardWidth;
    private double _baseCardHeight;
    private double _thumbnailWidth;
    private double _thumbnailHeight;
    private double _fallbackIconFontSize;
    private double _fileNameFontSize;
    private double _playCountFontSize;
    private double _tagFontSize;
    private IconSize _preset;
    private bool _iconOnly;

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
        (_baseCardWidth, _baseCardHeight, ThumbnailWidth, ThumbnailHeight, FallbackIconFontSize, FileNameFontSize, PlayCountFontSize, TagFontSize) = size switch
        {
            IconSize.ExtraLarge => (270.0, 285.0, 240.0, 175.0, 64.0, 15.0, 12.0, 11.0),
            IconSize.Large => (210.0, 235.0, 180.0, 131.0, 48.0, 13.0, 11.0, 10.0),
            IconSize.Normal => (160.0, 225.0, 130.0, 95.0, 36.0, 11.0, 10.0, 9.0),
            IconSize.Small => (110.0, 155.0, 85.0, 62.0, 24.0, 9.0, 8.0, 8.0),
            _ => (160.0, 225.0, 130.0, 95.0, 36.0, 11.0, 10.0, 9.0),
        };
        RecomputeCardSize();
    }

    /// <summary>"아이콘만 보기" 켜짐 여부(<see cref="IconCardFieldsSettings.IconOnly"/>와 함께 토글됨)를 반영해
    /// 카드 크기를 다시 계산한다(2026-08-16 추가) — 켜지면 세로 높이만 현재 프리셋의 썸네일 높이에 딱 맞게
    /// (<see cref="CardChromeSize"/>만 더해서) 줄이고, 꺼지면 프리셋 본래의 높이로 되돌린다. **가로 폭은 켜짐/꺼짐과
    /// 무관하게 항상 프리셋 본래의 `_baseCardWidth`를 쓴다**(2026-08-16 수정 — 원래는 폭도 썸네일 폭에 맞춰 줄였으나,
    /// 그러면 "아이콘만 보기"를 켜고 끌 때 한 행에 들어가는 카드 개수가 달라져 그리드가 크게 흔들렸다). 프리셋을
    /// 전환해도(<see cref="Apply"/>) 이 값은 유지된다 — 어떤 프리셋에서든 "아이콘만 보기"가 동일하게 적용되도록
    /// 하기 위함이다.</summary>
    public void SetIconOnly(bool iconOnly)
    {
        _iconOnly = iconOnly;
        RecomputeCardSize();
    }

    private void RecomputeCardSize()
    {
        CardWidth = _baseCardWidth;
        CardHeight = _iconOnly ? ThumbnailHeight + CardChromeSize : _baseCardHeight;
    }

    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
