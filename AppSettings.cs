namespace VideoVault;

/// <summary>
/// 프로그램 종료 시점의 UI 상태(보기 모드/정렬/필터/마지막 폴더)를 저장하는 모델. JSON으로 직렬화된다.
/// </summary>
public class AppSettings
{
    public bool IsIconView { get; set; }
    public string? SortProperty { get; set; }
    public bool SortAscending { get; set; } = true;
    public string? NameFilter { get; set; }
    public List<string> SelectedTags { get; set; } = new();
    public List<string> SelectedActors { get; set; } = new();
    public bool ShowRemovedItems { get; set; }
    public string? LastFolder { get; set; }

    /// <summary>리스트 보기에 표시할 컬럼 키 목록 (파일명은 항상 표시되므로 포함되지 않음).
    /// 기존 settings.json에 이 필드가 없어도(구버전 파일) 기본값이 그대로 적용된다.</summary>
    public List<string> VisibleColumns { get; set; } = new() { "Size", "PlayCount", "Tags" };

    /// <summary>리스트 보기 컬럼별 너비(파일명 포함). 키는 <see cref="VisibleColumns"/>와 같은 컬럼 키("FileName" 추가).
    /// 기존 settings.json에 이 필드가 없어도(구버전 파일) XAML에 정의된 기본 너비가 그대로 적용된다.</summary>
    public Dictionary<string, double> ColumnWidths { get; set; } = new();

    /// <summary>종료 시점에 관리 리스트에서 선택되어 있던 항목의 전체 경로(<see cref="ManagedVideoItem.FullPath"/>).
    /// 파일명이 아니라 경로로 매칭해, 같은 이름의 다른 파일과 혼동되지 않도록 한다. 다음 실행 시 이 경로의 항목을 찾아
    /// 선택하고 스크롤해서 보여준다. 찾지 못하면(파일이 없어졌거나 필터에 걸러짐) 조용히 무시한다.</summary>
    public string? SelectedItemPath { get; set; }
}
