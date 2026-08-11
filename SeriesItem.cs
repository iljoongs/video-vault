using System.ComponentModel;
using System.Text.Json.Serialization;

namespace VideoVault;

/// <summary>
/// 시리즈 마스터 목록(`series.json`)의 항목 하나. 이름과, 이 시리즈에 속한 것으로 등록된 품번 목록(Credits)을 가진다.
/// </summary>
public class SeriesItem : INotifyPropertyChanged
{
    private string _name = string.Empty;

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

    /// <summary>
    /// 이 시리즈에 속한 것으로 등록된 품번(관리 리스트 파일명에서 확장자를 뺀 값) 목록. "작품 추가" 창에서 사용자가
    /// 직접 추가하며, 현재 관리 리스트에 실제로 그 파일이 있는지 여부와 무관하게 유지된다(있으면 진한 색,
    /// 없으면 연한 색으로 시리즈 관리 창에서 구분해서 보여준다) — `ActorItem.Credits`와 동일한 패턴.
    /// </summary>
    /// <remarks>Tags/Actors/ActorItem.Credits와 마찬가지로 private set이므로 역직렬화를 위해 <see cref="JsonIncludeAttribute"/>가 필요하다.</remarks>
    [JsonInclude]
    public List<string> Credits { get; private set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetCredits(List<string> credits)
    {
        Credits = credits;
        OnPropertyChanged(nameof(Credits));
    }

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
