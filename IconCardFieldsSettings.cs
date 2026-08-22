using System.ComponentModel;

namespace VideoVault;

/// <summary>
/// 아이콘 보기 카드에 크기/재생횟수/태그/시리즈 정보를 표시할지 여부. 아이콘 보기의 빈 공간을 우클릭하면
/// 뜨는 팝업에서 토글하며, 카드 DataTemplate이 이 싱글턴 인스턴스에 직접 바인딩되어 있어 변경 즉시
/// 이미 그려진 모든 카드에 반영된다(<see cref="IconSizeSettings"/>와 동일한 패턴).
/// </summary>
public class IconCardFieldsSettings : INotifyPropertyChanged
{
    public static readonly IconCardFieldsSettings Current = new();

    private bool _showSize;
    private bool _showPlayCount = true;
    private bool _showTags = true;
    private bool _showSeries;
    private bool _iconOnly;

    private IconCardFieldsSettings()
    {
    }

    public bool ShowSize
    {
        get => _showSize;
        set
        {
            if (_showSize != value)
            {
                _showSize = value;
                OnPropertyChanged(nameof(ShowSize));
            }
        }
    }

    public bool ShowPlayCount
    {
        get => _showPlayCount;
        set
        {
            if (_showPlayCount != value)
            {
                _showPlayCount = value;
                OnPropertyChanged(nameof(ShowPlayCount));
            }
        }
    }

    public bool ShowTags
    {
        get => _showTags;
        set
        {
            if (_showTags != value)
            {
                _showTags = value;
                OnPropertyChanged(nameof(ShowTags));
            }
        }
    }

    public bool ShowSeries
    {
        get => _showSeries;
        set
        {
            if (_showSeries != value)
            {
                _showSeries = value;
                OnPropertyChanged(nameof(ShowSeries));
            }
        }
    }

    /// <summary>"아이콘만 보기"(2026-08-16 추가) — 켜면 품번/크기/재생횟수/태그/시리즈 텍스트를 모두 숨겨 카드가
    /// 썸네일 이미지만 보여준다. 개별 Show* 토글과 달리 이 값이 켜지면 그 토글들의 상태와 무관하게 전부 숨겨진다
    /// (카드 DataTemplate의 각 텍스트 Style.Triggers가 이 값도 함께 검사). 카드 크기 자체를 썸네일 크기에 맞게
    /// 줄이는 것은 <see cref="IconSizeSettings.SetIconOnly"/>가 별도로 담당한다.</summary>
    public bool IconOnly
    {
        get => _iconOnly;
        set
        {
            if (_iconOnly != value)
            {
                _iconOnly = value;
                OnPropertyChanged(nameof(IconOnly));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
