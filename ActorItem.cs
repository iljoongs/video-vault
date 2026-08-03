using System.ComponentModel;
using System.Text.Json.Serialization;

namespace VideoVault;

/// <summary>
/// 배우 마스터 목록(`actors.json`)의 항목 하나. 이름, 100x100 썸네일 경로, 부가 정보(출생년도/키/신체정보)를 가진다.
/// </summary>
public class ActorItem : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string? _thumbnailPath;
    private int? _birthYear;
    private int? _height;
    private string _bodyInfo = string.Empty;

    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }
    }

    /// <summary>100x100 이내로 리사이즈된 썸네일 이미지 경로. 미지정 시 null. 원본은 별도로 보관하지 않는다.</summary>
    public string? ThumbnailPath
    {
        get => _thumbnailPath;
        set
        {
            if (_thumbnailPath != value)
            {
                _thumbnailPath = value;
                OnPropertyChanged(nameof(ThumbnailPath));
                OnPropertyChanged(nameof(HasThumbnail));
            }
        }
    }

    /// <summary>출생년도. 미지정 시 null.</summary>
    public int? BirthYear
    {
        get => _birthYear;
        set
        {
            if (_birthYear != value)
            {
                _birthYear = value;
                OnPropertyChanged(nameof(BirthYear));
            }
        }
    }

    /// <summary>키(cm). 미지정 시 null.</summary>
    public int? Height
    {
        get => _height;
        set
        {
            if (_height != value)
            {
                _height = value;
                OnPropertyChanged(nameof(Height));
            }
        }
    }

    /// <summary>신체정보 (자유 텍스트, 예: "B90 W58 H86"). 기본값 빈 문자열.</summary>
    public string BodyInfo
    {
        get => _bodyInfo;
        set
        {
            if (_bodyInfo != value)
            {
                _bodyInfo = value;
                OnPropertyChanged(nameof(BodyInfo));
            }
        }
    }

    /// <summary>
    /// 이 배우가 출연한 것으로 등록된 품번(관리 리스트 파일명에서 확장자를 뺀 값) 목록. "작품 추가" 창에서 사용자가
    /// 직접 추가하며, 현재 관리 리스트에 실제로 그 파일이 있는지 여부와 무관하게 유지된다(있으면 진한 파란색,
    /// 없으면 연한 파란색으로 배우 관리 창에서 구분해서 보여준다).
    /// </summary>
    /// <remarks>Tags/Actors와 마찬가지로 private set이므로 역직렬화를 위해 <see cref="JsonIncludeAttribute"/>가 필요하다.</remarks>
    [JsonInclude]
    public List<string> Credits { get; private set; } = new();

    [JsonIgnore]
    public bool HasThumbnail => !string.IsNullOrEmpty(ThumbnailPath);

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetCredits(List<string> credits)
    {
        Credits = credits;
        OnPropertyChanged(nameof(Credits));
    }

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
