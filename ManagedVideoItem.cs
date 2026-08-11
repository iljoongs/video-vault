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
    private string _series = string.Empty;
    private bool _isValid = true;
    private bool _isExist = true;
    private bool _isPlaceholder;

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
    /// 이 항목이 속한 시리즈(시리즈 마스터 목록의 이름 하나만 참조, 자유 입력 아님). 기본값은 빈 문자열이며
    /// 속성 창의 콤보박스에서 선택한다 — 배우/태그와 달리 여러 개가 아니라 단일 선택이다(2026-08-08 추가).
    /// </summary>
    public string Series
    {
        get => _series;
        set
        {
            if (_series != value)
            {
                _series = value;
                OnPropertyChanged(nameof(Series));
            }
        }
    }

    /// <summary>
    /// 이 항목이 관리 리스트에서 "유효한"(사용자가 계속 관리하길 원하는) 상태인지 여부(2026-08-06 추가,
    /// 예전 `IsArchived`를 대체 — 의미가 반대이므로 값도 반대로 저장된다: 예전 `IsArchived = true` ↔ 지금
    /// `IsValid = false`). false면 사용자가 "제거"했거나(또는 "파일 없이 추가"로 아직 실제 파일을 못 구한
    /// 상태) — true여도 데이터(재생횟수/태그/배우/메모/썸네일)는 그대로 보존된다. 화면(리스트/필터/정렬)에는
    /// 기본적으로 노출되지 않으며 "제거된 항목도 표시" 체크박스로 볼 수 있다. <see cref="IsExist"/>(파일이
    /// 실제로 존재하는지)와는 독립적인 축이다 — 파일이 없어졌다고 자동으로 `IsValid`가 바뀌지는 않는다
    /// ([파일 존재 여부](#오류-처리) 참고).
    /// </summary>
    public bool IsValid
    {
        get => _isValid;
        set
        {
            if (_isValid != value)
            {
                _isValid = value;
                OnPropertyChanged(nameof(IsValid));
            }
        }
    }

    /// <summary>
    /// `FullPath`가 가리키는 실제 파일이 디스크에 존재하는지 여부(2026-08-06 추가). 시작 시(그리고 향후
    /// 재확인 시) 모든 항목에 대해 `File.Exists(FullPath)`로 다시 판단해 갱신된다 — "파일 없이 추가"된
    /// 항목(<see cref="IsPlaceholder"/>)은 항상 false로 취급한다(실제 폴더가 없는 경로이므로). `IsValid`와
    /// 독립적인 축이라, 파일이 일시적으로(외장 드라이브 분리 등) 없어져도 관리 리스트에서 사라지지 않고
    /// 계속 활성 상태로 남아있으며, 화면에서 다르게(예: 경고 색) 표시하는 용도로만 쓰인다.
    /// </summary>
    public bool IsExist
    {
        get => _isExist;
        set
        {
            if (_isExist != value)
            {
                _isExist = value;
                OnPropertyChanged(nameof(IsExist));
            }
        }
    }

    /// <summary>
    /// 실제 동영상 파일 없이(아직 구하지 못한 작품 등을 미리 기록해두기 위해) 수동으로 추가된 항목인지
    /// 여부(2026-08-06 추가). true면 시작 시 파일 존재 여부 확인(`ReconcileMissingFiles`)에서 제외되어
    /// 파일이 없다는 이유로 자동 제거되지 않으며, 속성 창에서 실제 파일이 있어야 동작하는 기능(재생/파일
    /// 이동·변경/썸네일)이 비활성화된다 — [관리 리스트] "파일 없이 추가" 참고.
    /// </summary>
    public bool IsPlaceholder
    {
        get => _isPlaceholder;
        set
        {
            if (_isPlaceholder != value)
            {
                _isPlaceholder = value;
                OnPropertyChanged(nameof(IsPlaceholder));
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
