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
    public string? FolderFilter { get; set; }
    public string? SeriesFilter { get; set; }
    public List<string> SelectedTags { get; set; } = new();
    public List<string> SelectedActors { get; set; } = new();
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

    /// <summary>주요 창(폴더 목록/속성/배우 관리/태그 관리)이 마지막으로 열려 있던 화면 위치. 키는 창 클래스
    /// 이름(예: "PropertiesWindow"), 값은 [Left, Top]. <see cref="WindowPositionMemory"/> 참고.</summary>
    public Dictionary<string, double[]> WindowPositions { get; set; } = new();

    /// <summary>메인 창의 마지막 크기/위치(2026-08-07 추가). 창을 닫을 때 저장하고 다음 실행 시 복원한다
    /// (최대화 상태로 닫았으면 최대화 이전의 "정상" 크기/위치를 저장 — <see cref="Window.RestoreBounds"/>).
    /// 값이 없거나(구버전 settings.json) 화면 밖으로 판정되면 XAML에 정의된 기본 크기/`CenterScreen`을 그대로 쓴다.</summary>
    public double? MainWindowWidth { get; set; }
    public double? MainWindowHeight { get; set; }
    public double? MainWindowLeft { get; set; }
    public double? MainWindowTop { get; set; }

    /// <summary>아이콘 보기 카드 크기 프리셋(2026-08-12 추가). <see cref="IconSize"/> enum 이름을 문자열로 저장하며,
    /// 값이 없거나 인식할 수 없으면(구버전 settings.json 등) "Normal"이 적용된다.</summary>
    public string? IconSizePreset { get; set; }

    /// <summary>아이콘 보기 카드에 표시할 정보(2026-08-12 추가) — 기본값은 재생횟수/태그 켜짐, 크기/시리즈 꺼짐
    /// (예전부터의 카드 모습과 동일, <see cref="IconCardFieldsSettings"/> 참고).</summary>
    public bool IconShowSize { get; set; }
    public bool IconShowPlayCount { get; set; } = true;
    public bool IconShowTags { get; set; } = true;
    public bool IconShowSeries { get; set; }

    /// <summary>"아이콘만 보기"(2026-08-16 추가) — 켜면 아이콘 보기 카드가 썸네일만 보여주고 텍스트 정보(품번/크기/
    /// 재생횟수/태그/시리즈)는 모두 숨기며, 카드 크기도 썸네일 크기에 딱 맞게 줄어든다. <see cref="IconCardFieldsSettings.IconOnly"/> 참고.</summary>
    public bool IconOnlyMode { get; set; }

    /// <summary>배우 관리 창에서 썸네일/작품 리스트 창(오른쪽 패널)의 마지막 가로 크기(px, 2026-08-27 추가).
    /// <see cref="ActorManagerWindow.RememberedRightPanelWidth"/> 참고.</summary>
    public double? ActorManagerRightPanelWidth { get; set; }

    /// <summary>속성/태그 관리/배우 관리/시리즈 관리 창이 마지막으로 열려 있던 창 크기(2026-08-29 추가,
    /// 사용자 요청). 위치(Left/Top)는 <see cref="WindowPositions"/>에 저장되므로 여기서는 크기(Width/Height)만
    /// 다룬다. 키는 창 클래스 이름(예: "PropertiesWindow"), 값은 [Width, Height]. <see cref="WindowSizeMemory"/> 참고.</summary>
    public Dictionary<string, double[]> WindowSizes { get; set; } = new();

    /// <summary>썸네일 뷰어 왼쪽의 빠른 보기 패널(2026-08-31 추가, 2026-08-31 "모든 파일" 제거 + additive 방식으로
    /// 재설계 + "일반 파일"/"썸네일 파일" 추가 + "썸네일 파일"→"썸네일 있음" 이름 변경/"썸네일 없음" 추가, 사용자 요청)
    /// — "일반 파일"/"삭제 파일"/"신규 파일"/"썸네일 있음"/"썸네일 없음" 체크박스 상태. 다섯 다 서로 독립적인
    /// OR 토글이며, "일반 파일"만 기본값이 켜짐(나머지는 꺼짐). <see cref="MainWindow.FilterManagedItem"/> 참고.</summary>
    public bool QuickFilterNormalFiles { get; set; } = true;
    public bool QuickFilterDeletedFiles { get; set; }
    public bool QuickFilterNewFiles { get; set; }
    public bool QuickFilterThumbnailFiles { get; set; }
    public bool QuickFilterNoThumbnailFiles { get; set; }
}
