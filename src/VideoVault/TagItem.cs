using System.ComponentModel;
using System.Text.Json.Serialization;

namespace VideoVault;

/// <summary>
/// 태그 마스터 목록(`tags.json`)의 항목 하나. 이름과, 이 태그가 붙은 것으로 등록된 품번 목록(Credits)을 가진다
/// (2026-08-27 추가 — 이전에는 태그 마스터 목록이 단순 문자열 배열이라 이 목록을 저장할 공간이 없었다).
/// `SeriesItem`과 완전히 동일한 모양이다(썸네일/부가 정보가 없다는 점도 같음).
/// </summary>
public class TagItem : INotifyPropertyChanged
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
    /// 이 태그가 붙은 것으로 등록된 품번(관리 리스트 파일명에서 확장자를 뺀 값) 목록. "작품 추가" 창에서 사용자가
    /// 직접 추가하며, 현재 관리 리스트에 실제로 그 파일이 있는지 여부와 무관하게 유지된다(있으면 진한 색,
    /// 없으면 연한 색으로 태그 관리 창에서 구분해서 보여준다) — `ActorItem.Credits`와 동일한 패턴. 태그는 배우처럼
    /// 한 파일에 여러 개가 동시에 붙을 수 있으므로(다대다), 시리즈처럼 "다른 태그에서 이 품번을 뺏어온다"는
    /// 개념은 없다 — 여러 태그의 Credits에 같은 품번이 동시에 있는 것이 정상이다.
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
