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

    [JsonIgnore]
    public bool HasThumbnail => !string.IsNullOrEmpty(ThumbnailPath);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
