using System.ComponentModel;
using System.IO;
using System.Text.Json.Serialization;

namespace VideoVault;

/// <summary>
/// 관리 리스트(영속 데이터)의 항목 하나. JSON으로 직렬화된다.
/// </summary>
public class ManagedVideoItem : INotifyPropertyChanged
{
    private int _playCount;
    private string? _thumbnailPath;
    private string? _thumbnailOriginalPath;
    private string _fileName = string.Empty;
    private string _fullPath = string.Empty;
    private string _memo = string.Empty;
    private string _code = string.Empty;
    private string _releaseDate = string.Empty;
    private bool _isArchived;

    public string FileName
    {
        get => _fileName;
        set
        {
            if (_fileName != value)
            {
                _fileName = value;
                OnPropertyChanged(nameof(FileName));
            }
        }
    }

    public long SizeBytes { get; set; }
    public DateTime ModifiedDate { get; set; }

    public string FullPath
    {
        get => _fullPath;
        set
        {
            if (_fullPath != value)
            {
                _fullPath = value;
                OnPropertyChanged(nameof(FullPath));
                OnPropertyChanged(nameof(FolderName));
            }
        }
    }

    public int PlayCount
    {
        get => _playCount;
        set
        {
            if (_playCount != value)
            {
                _playCount = value;
                OnPropertyChanged(nameof(PlayCount));
            }
        }
    }

    /// <summary>태그 마스터 목록에 존재하는 태그만 참조한다 (UI에서 강제).</summary>
    /// <remarks>
    /// setter가 private이라 System.Text.Json 기본 규칙으로는 역직렬화 시 무시된다.
    /// <see cref="JsonIncludeAttribute"/>로 명시적으로 포함시키지 않으면 library.json을 다시 불러올 때
    /// 태그가 항상 빈 목록으로 초기화되는 버그가 생긴다 (실제 발생했던 버그).
    /// </remarks>
    [JsonInclude]
    public List<string> Tags { get; private set; } = new();

    /// <summary>배우 마스터 목록에 존재하는 이름만 참조한다 (UI에서 강제). 여러 명 지정 가능.</summary>
    /// <remarks>Tags와 마찬가지로 private set이므로 역직렬화를 위해 <see cref="JsonIncludeAttribute"/>가 필요하다.</remarks>
    [JsonInclude]
    public List<string> Actors { get; private set; } = new();

    public string Memo
    {
        get => _memo;
        set
        {
            if (_memo != value)
            {
                _memo = value;
                OnPropertyChanged(nameof(Memo));
            }
        }
    }

    /// <summary>
    /// 파일 코드(예: 비디오 코드). 기본값은 빈 문자열이며, 속성 창에서 <see cref="DeriveCode"/>로
    /// 자동 제안값을 채워주되(파일명의 첫 "-" 이전 부분, 없으면 폴더명), 사용자가 직접 수정할 수 있다.
    /// 파일명을 바꾸면(속성 창) 이 제안값도 다시 계산되어 반영된다.
    /// </summary>
    public string Code
    {
        get => _code;
        set
        {
            if (_code != value)
            {
                _code = value;
                OnPropertyChanged(nameof(Code));
            }
        }
    }

    /// <summary>
    /// 출시일 (자유 텍스트, 예: "2023-06-06"). 형식을 강제하지 않으며 속성 창에서 코드 옆에 편집한다.
    /// </summary>
    public string ReleaseDate
    {
        get => _releaseDate;
        set
        {
            if (_releaseDate != value)
            {
                _releaseDate = value;
                OnPropertyChanged(nameof(ReleaseDate));
            }
        }
    }

    /// <summary>
    /// 관리 리스트 화면에서 "제거"되었거나 파일을 더 이상 찾을 수 없어 보관 상태로 전환된 항목인지 여부.
    /// true여도 데이터(재생횟수/태그/배우/메모/썸네일)는 그대로 보존되며, 화면(리스트/필터/정렬)에는 노출되지 않는다.
    /// 나중에 같은 파일명이 다시 관리 리스트에 추가되면 이 데이터를 재사용할지 사용자에게 물어본다.
    /// </summary>
    public bool IsArchived
    {
        get => _isArchived;
        set
        {
            if (_isArchived != value)
            {
                _isArchived = value;
                OnPropertyChanged(nameof(IsArchived));
            }
        }
    }

    /// <summary>아이콘 보기에서 사용할 썸네일 이미지 파일 경로. 미지정 시 null.</summary>
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

    /// <summary>썸네일용(320x240)과 별도로 보관하는, 리사이즈 전 원본 이미지 파일 경로. 미지정 시 null.</summary>
    public string? ThumbnailOriginalPath
    {
        get => _thumbnailOriginalPath;
        set
        {
            if (_thumbnailOriginalPath != value)
            {
                _thumbnailOriginalPath = value;
                OnPropertyChanged(nameof(ThumbnailOriginalPath));
            }
        }
    }

    [JsonIgnore]
    public bool HasThumbnail => !string.IsNullOrEmpty(ThumbnailPath);

    [JsonIgnore]
    public string SizeDisplay => FormatUtil.FormatSize(SizeBytes);

    /// <summary>파일이 들어있는 폴더의 마지막 폴더명만(전체 경로가 아님). 예: "...\쿠로카와 사리나\a.mp4" → "쿠로카와 사리나".</summary>
    [JsonIgnore]
    public string FolderName => GetLastFolderName(FullPath);

    [JsonIgnore]
    public string TagsSortKey => Tags.Count == 0
        ? string.Empty
        : string.Join(",", Tags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase));

    [JsonIgnore]
    public string ActorsDisplay => string.Join(", ", Actors);

    [JsonIgnore]
    public string ActorsSortKey => Actors.Count == 0
        ? string.Empty
        : string.Join(",", Actors.OrderBy(a => a, StringComparer.OrdinalIgnoreCase));

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetTags(List<string> tags)
    {
        Tags = tags;
        OnPropertyChanged(nameof(Tags));
        OnPropertyChanged(nameof(TagsSortKey));
    }

    public void SetActors(List<string> actors)
    {
        Actors = actors;
        OnPropertyChanged(nameof(Actors));
        OnPropertyChanged(nameof(ActorsDisplay));
        OnPropertyChanged(nameof(ActorsSortKey));
    }

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public static ManagedVideoItem FromFolderItem(VideoFileItem file) => new()
    {
        FileName = file.FileName,
        SizeBytes = file.SizeBytes,
        ModifiedDate = file.ModifiedDate,
        FullPath = file.FullPath,
    };

    /// <summary>
    /// 파일명에서 첫 번째 "-" 이전 부분을 코드로 추출한다. "-"가 없으면 <paramref name="fullPath"/>의
    /// 마지막 폴더명(<see cref="FolderName"/>과 같은 규칙)을 대신 사용한다.
    /// </summary>
    public static string DeriveCode(string fileName, string fullPath)
    {
        var dashIndex = fileName.IndexOf('-');
        return dashIndex >= 0 ? fileName[..dashIndex].Trim() : GetLastFolderName(fullPath);
    }

    private static string GetLastFolderName(string fullPath)
    {
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory))
        {
            return string.Empty;
        }

        var name = Path.GetFileName(directory);
        return string.IsNullOrEmpty(name) ? directory : name;
    }
}
