# 시리즈 관리

> [메인 지시서](../CLAUDE.md)의 하위 문서. 시리즈 마스터 목록(`series.json`)과 `SeriesManagerWindow`, 그리고 시리즈의 소속 작품 목록(Credits)과 관리 리스트 간의 상호 동기화를 다룬다. 속성 창에서 시리즈를 항목에 지정하는 UI는 [속성 관리](properties-management.md), 배우는 [배우 관리](actor-management.md)(`AddCreditWindow`를 이 문서와 공유) 참고.

**관련 파일**: `SeriesItem.cs`, `SeriesRepository.cs`, `SeriesManagerWindow.xaml`/`.xaml.cs`, `SeriesCreditSync.cs`

## 시리즈 마스터 목록 관리 (`SeriesManagerWindow`)

배우와 별개로, 관리 리스트 항목이 속한 **시리즈**(예: 같은 기획/레이블 묶음)를 관리하는 마스터 목록이다(2026-08-09 추가, **구현 완료**). 배우와 달리 한 항목에는 **시리즈를 하나만** 지정할 수 있다(콤보박스 단일 선택, 태그/배우처럼 여러 개 체크하는 방식이 아님).

- **데이터 모델(`SeriesItem`)**: `Name`(문자열) + `Credits`(`List<string>`, 이 시리즈에 속한 것으로 등록된 품번 목록, `ActorItem.Credits`와 완전히 동일한 패턴 — `private set` + `[JsonInclude]` + `SetCredits(...)`). 배우와 달리 썸네일/출생년도 같은 부가 정보는 없다.
- **저장 위치**: `%LOCALAPPDATA%\VideoVault\series.json`(`AppPaths.SeriesPath`, `SeriesRepository`). 관리 리스트/태그/배우와 동일하게 프로그램 시작 시 자동 로딩되고, 파일이 없으면 빈 목록으로 시작한다.
- **자동 저장**: 시리즈 추가/삭제/Credits 변경 시 500ms debounce 후 `series.json`에 자동 저장(관리 리스트/태그/배우와 동일한 정책, `SeriesItem.PropertyChanged`를 구독). Ctrl+S로도 즉시 저장된다(`SaveAllNow`에 포함).
- **관리 리스트 항목과의 관계**: `ManagedVideoItem.Series`는 시리즈 마스터 목록의 이름 하나만 참조하는 단일 문자열(기본값 빈 문자열)이다 — `Code`/`ReleaseDate`와 마찬가지로 public setter라 `[JsonInclude]`가 필요 없다.
- **시리즈 관리 화면(`SeriesManagerWindow`, 기본 크기 450x700)**: 메인창 "도구 > 시리즈 관리" 메뉴 또는 관리 리스트 툴바의 "시리즈 관리" 버튼("배우 관리" 버튼 옆)으로 연다. 배우 관리 창처럼 아이콘 그리드가 아니라 태그 관리 창과 비슷한 **단순 리스트** 형태다(썸네일이 없으므로).
  - **목록**: 창 맨 위에 시리즈 이름/작품수를 보여주는 **테이블(`ListView` + `GridView`)**(2026-08-16 변경, **구현 완료** — 예전에는 이름만 나열하는 단순 `ListBox`였다). 컬럼은 "시리즈 이름"(`Name`)과 "작품수"(`Credits.Count`) 두 개이며, `ListView`는 `ListBox`를 상속하므로 `SelectedItem`/`ScrollIntoView` 등 기존 코드는 그대로 유지된다(약 10줄 높이로 보이도록 `Height="220"` 고정). 이름 기준 오름차순 정렬(`ICollectionView` + `SortDescription`, 태그/배우 관리 창과 동일한 패턴)은 그대로 유지되며, "작품수" 컬럼은 `Credits`가 바뀔 때마다 바인딩이 자동으로 다시 계산되어 즉시 갱신된다.
    - **컬럼 크기(2026-08-16 추가, 구현 완료)**: "작품수" 컬럼(`CreditCountColumn`)은 고정 폭을 주지 않아(`Width` 미지정 = Auto) 글자 수에 맞춰 자동으로 좁게 유지된다. "시리즈 이름" 컬럼(`SeriesNameColumn`)이 나머지 공간을 채우도록 코드에서 `Width`를 계산해 지정해서, 결과적으로 "작품수" 컬럼이 항상 테이블 오른쪽 끝에 붙어 보인다(`GridView` 자체는 Star 크기 조절을 지원하지 않아 XAML만으로는 불가능). `SeriesListBox.SizeChanged`와 "작품수" 컬럼의 `ActualWidth` 변경(`INotifyPropertyChanged`, 자릿수가 바뀌는 경우 등) 양쪽에서 `UpdateSeriesNameColumnWidth()`를 호출해 창 크기 변경 후에도 유지된다.
  - **목록 아래 입력줄: 텍스트박스 하나 + "추가"/"변경"/"삭제" 버튼(이 순서)**(2026-08-27 재설계, **구현 완료**, 사용자 요청) — 예전에는 "시리즈 추가"/"시리즈 삭제" 버튼만 있고 **이름 변경 기능 자체가 없었다**("요구사항에 없어 범위 밖"으로 명시적으로 제외돼 있었음). 이번에 배우/태그 관리 창과 같은 형태(텍스트박스 하나 + 추가/변경/삭제)로 맞추면서 **이름 변경이 신규로 추가됐다**. 맨 아래 "닫기" 버튼도 삭제되었다(제목표시줄 X, Alt+F4로 여전히 닫을 수 있음).
    - **추가**: `SeriesBox`에 새 이름을 입력하고 "추가"를 누르면 목록에 추가된다(추가된 항목을 바로 선택+스크롤).
    - **변경(이름 변경, 2026-08-27 신규)**: 목록에서 바꿀 시리즈를 먼저 선택하고, `SeriesBox`에 새 이름을 입력한 뒤 "변경"을 누른다(`SeriesBox`는 자동으로 채워지지 않는다 — 추가와 같은 텍스트박스를 공유할 뿐). 다른 시리즈와 이름이 중복되면 막는다. 이름을 바꾸면 이 시리즈가 지정된 모든 관리 리스트 항목의 `Series`에도 새 이름이 반영된다(참조 무결성 유지 — 배우/태그 이름 변경과 동일한 원칙, `RenameSeries_Click`). 시리즈는 썸네일 파일이 없으므로 [배우 관리](actor-management.md)의 `RenameActorAndSync`처럼 파일 rename까지 처리할 필요는 없다. 이름이 바뀌면 정렬 위치도 바뀌어야 하므로 `_seriesView.Refresh()`를 호출한다.
    - **삭제**: 목록에서 시리즈를 선택하고 "삭제"를 누르면(확인 대화상자 후) 마스터 목록에서 삭제되고, 이 시리즈가 지정된 모든 관리 리스트 항목의 `Series`도 빈 문자열로 되돌아간다(참조 무결성 유지 — 태그/배우 삭제와 동일한 원칙). `SeriesBox` 내용과는 무관하다.
  - **창 크기/위치를 닫기 전 상태로 기억한다**(2026-08-29 추가, 구현 완료, 사용자 요청) — 위치(Left/Top)는 `WindowPositionMemory`, 크기(Width/Height)는 `WindowSizeMemory`(둘 다 [배우 관리](actor-management.md)/[태그 관리](tag-management.md)/[속성 관리](properties-management.md)와 공유하는 공용 저장소)가 창 클래스 이름을 키로 기억한다. 생성자에서 `WindowSizeMemory.TryGetSize(nameof(SeriesManagerWindow), ...)`로 값이 있으면 적용하고, `Closed`에서 `WindowSizeMemory.Remember(...)`로 저장한다(최대화 상태로 닫았으면 `RestoreBounds` 사용). 프로그램을 재시작해도 유지된다(`settings.json`의 `AppSettings.WindowSizes`) → [공통 관리](common-management.md)의 "창 관리 정책" 규칙 4 참고.
  - **품번(이 시리즈에 속한 작품 목록 = `SeriesItem.Credits`)**: 입력줄 밑에 "품번" 라벨과 그 옆(오른쪽 정렬) **"추가"** 버튼이 한 줄에 있고(2026-08-27 변경, 사용자 요청 — 예전에는 "작품 추가" 버튼이 라벨과 별도의 줄에 혼자 있었다. 라벨은 "Credits"에서 이미 "품번"이었고 바뀐 적 없음, 버튼만 "작품 추가"→"추가"로 이름이 짧아지고 라벨 옆 오른쪽 정렬로 옮겨짐 — [배우 관리](actor-management.md)의 "Credits"→"품번" 라벨 변경과 같은 날 작업이지만 이 창은 라벨이 이미 "품번"이었다는 점이 다르다), 그 아래 품번 칩 목록이 있다 — **[배우 관리](actor-management.md)의 Credits 패널과 완전히 동일한 로직/스타일**(진한 색=관리 리스트에 실제 파일 있음, 연한 색=아직 없음; **품번 기준 오름차순 정렬**, 2026-08-09 추가; 칩 좌클릭 시 일치하는 항목의 속성 창 열기, 우클릭 시 Credits에서 제거; 선택 시 관리 리스트에서 이 시리즈로 지정된 파일들의 품번을 자동 병합; "추가" 버튼은 배우 관리 창과 같은 `AddCreditWindow`를 그대로 재사용 — [배우 관리](actor-management.md) 참고; **마우스를 올리면 썸네일 팝업 미리보기**, 아래 참고). **우클릭 삭제 시, 이 품번과 일치하는 실제 파일이 아직 이 시리즈를 `Series`로 갖고 있으면 그 필드도 함께 비운다**(2026-08-09 버그 수정) — 그렇지 않으면 자동 병합이 삭제 직후 곧바로 되살려서 "삭제가 안 되는" 버그가 있었다(배우 Credits 삭제와 동일한 원인/수정). 유일한 차이는 "추가"로 새 품번을 추가했을 때 자동 태깅 동작이다 — 배우는 다중 선택이라 항상 추가하지만, 시리즈는 단일 값이므로 **매칭되는 파일의 `Series`가 비어있을 때만** 이 시리즈 이름으로 채운다(이미 다른 시리즈가 지정된 파일은 조용히 건드리지 않는다, `UpdateManagedItemSeriesForCredit`). **품번 칩 목록에 둥근 테두리가 있다**(2026-08-27 추가, 사용자 요청, 배우/태그 관리 창도 동일) — `ScrollViewer`를 회색 `CornerRadius="8"` 둥근 `Border`(배경 없음)로 감쌌고, `ScrollViewer`에 `Padding="6"`도 줘서 칩이 모서리에 바로 닿지 않게 했다.
    - **시리즈 간 중복 품번 정리**(2026-08-16 추가, **구현 완료**) — 품번은 시리즈 하나에만 속해야 하는데, Credits를 수동으로 관리하다 보면(작품 추가/드래그 앤 드롭) 같은 품번이 둘 이상의 시리즈 Credits에 동시에 남는 경우가 생길 수 있다(예: 파일의 `Series`를 다른 시리즈로 바꿨는데 예전 시리즈의 Credits에는 옛 지정이 고아로 남아있던 경우). `SeriesCreditSync.RemoveDuplicateCreditsAcrossSeries(masterSeries, managedItems)`가 이를 정리하며, `SeriesManagerWindow.RefreshCreditsPanel()`이 (선택 여부와 무관하게) 매번 호출해서 창을 열자마자도 기존에 쌓인 중복이 즉시 해소된다. **우선순위 규칙**: ① 그 품번의 실제 파일이 있고 `ManagedVideoItem.Series`가 지정되어 있으면 "가장 최근에 업데이트된" 것으로 보고 그 시리즈만 남기고 나머지 시리즈의 Credits에서 제거한다. ② 실제 파일이 없거나 `Series`가 비어있어 판단 근거가 없으면(예: 두 시리즈 모두 파일 없이 수동 등록만 된 경우), 이름 기준 오름차순으로 가장 앞선 시리즈를 남기는 결정적 기본 규칙을 쓴다. 실제 데이터("dandy-915"가 "Specialized Nureses"/"Semem Collection" 두 곳에 중복— 둘 다 실제 파일 없음)로 검증했고, 규칙 ②에 따라 이름이 앞선 "Semem Collection"에만 남고 나머지에서 제거됨을 확인했다.
    - **"추가" 시 다른 시리즈의 기존 등록을 이 시리즈로 이전**(2026-08-16 추가, **구현 완료**) — 위 항목의 수동적인 정리와 달리, 품번 "추가"(대화상자 또는 Credits 목록에 텍스트 드래그 앤 드롭, 둘 다 `AddCreditToSeries`를 거친다)는 **사용자의 명시적 의사표시**이므로 사후 정리 규칙(이름순 등)보다 항상 우선한다. 추가하려는 품번이 다른 시리즈의 Credits에 이미 있으면 **먼저 확인 대화상자**("'{품번}' 품번은 이미 '{기존 시리즈}' 시리즈에 등록되어 있습니다. 기존 시리즈에서 제거하고 '{이 시리즈}'(으)로 옮기시겠습니까?", 예/아니오)를 띄운다 — 다른 시리즈의 등록을 실수로 뺏어오는 것을 막기 위함이다. "예"를 선택해야 그 시리즈의 Credits에서 즉시 제거하고, 그 품번과 일치하는 실제 파일의 `Series`가 그 기존 시리즈를 가리키고 있었다면 이 시리즈로 함께 옮긴다(파일의 `Series` 필드까지 옮기지 않으면, 나중에 기존 시리즈를 다시 열었을 때 `SyncCreditsFromManagedItems`가 그 파일의 `Series`를 근거로 방금 제거한 품번을 도로 되살리는 문제가 있었다). "아니오"를 선택하면 아무것도 바꾸지 않고 추가 자체를 취소한다. 이미 **같은** 시리즈에 등록된 품번을 다시 추가하려 하면(다른 시리즈와 무관) 예전과 동일하게 확인 없이 "이미 추가된 품번입니다" 안내만 표시한다.
    - **품번 칩에 마우스를 올리면 썸네일 팝업**(2026-08-16 추가, **구현 완료**, [배우 관리](actor-management.md) 창도 동일) — 관리 리스트에서 일치하는 파일에 썸네일이 있으면, 칩 위에 마우스를 올리는 즉시(`CreditChip_MouseEnter`) 마우스 근처에 그 썸네일을 보여주는 `Popup`(`CreditThumbnailPopup`)이 뜨고, 마우스가 벗어나면(`CreditChip_MouseLeave`) 닫힌다. **크기는 아이콘 보기 "큰 아이콘" 프리셋의 썸네일 크기(180x131)로 고정**했다(현재 선택된 아이콘 크기 프리셋과는 무관 — 이 창들은 자체 아이콘 크기 설정이 없으므로 고정값을 그대로 썼다). `ImageLoadHelper.Load(path, decodePixelWidth: 180)`로 불필요하게 원본 해상도 그대로 디코딩하지 않는다([공통 관리](common-management.md)의 성능 절에 있는 것과 같은 디코딩 폭 제한 패턴). 일치하는 파일이 없거나 썸네일이 없으면 조용히 아무 반응도 하지 않는다(빈 팝업을 띄우지 않음).

## 상태바 (2026-08-29 추가, 2026-08-29 스타일 개선, 구현 완료, 사용자 요청)

`SeriesManagerWindow` 맨 아래에 카드 형태의 상태바(둥근 모서리 `CornerRadius="6"`, 옅은 회색 배경 `#FAFAFA` + 옅은 테두리 `#E0E0E0`)가 있다. **실제 사용자 결정이 필요 없는** 단순 안내/오류/성공 메시지는 대화상자(`MessageBox.Show`) 대신 이 상태바에 표시한다 — 예를 들어 "시리즈 이름을 입력하세요.", "이미 존재하는 시리즈입니다.", **"이미 추가된 품번입니다: '코드'"**(사용자가 명시적으로 지적한 케이스, 단 아래 예외 참고), "관리 리스트에 이 품번의 파일이 없습니다." 등. 시리즈/품번 추가·변경·삭제가 성공했을 때도 "'이름' 시리즈를 추가했습니다." 같은 메시지를 남긴다. 반대로 **실제 Yes/No 결정이 필요한 확인**은 그대로 `MessageBox.Show`(`MessageBoxButton.YesNo`)를 사용한다 — 시리즈 삭제 확인, 품번을 Credits에서 제거할지 확인뿐 아니라, 이 창 특유의 위 "'추가' 시 다른 시리즈의 기존 등록을 이 시리즈로 이전" 확인("중복 품번 확인" 대화상자)도 여기 해당한다 — 겉보기엔 "중복 품번"이라 상태바 대상처럼 보이지만 실제로 다른 시리즈에서 옮겨올지를 사용자가 결정해야 하므로 대화상자를 유지했다(같은 품번이 **이미 이 시리즈**에 있는 단순 중복 케이스만 상태바 메시지로 처리됨).

- **종류별 아이콘/색 구분**(2026-08-29 추가, 사용자 요청 — "적당히 이쁘게 꾸며줘", 페이드아웃 등 애니메이션은 명시적으로 제외): 성공(`StatusType.Success`) "✓" 초록(`#2E7D32`), 경고(`StatusType.Warning`, 입력 누락·이미 존재함·중복 품번 등) "⚠" 주황(`#B26A00`), 정보(`StatusType.Info`, "관리 리스트에 이 품번의 파일이 없습니다" 같은 단순 안내) "ℹ" 파랑(`#1565C0`), 오류(`StatusType.Error`, 예외 발생) "✕" 빨강(`#C62828`). `SetStatus(string message, StatusType type)` private 헬퍼가 아이콘(`StatusIcon`)과 텍스트(`StatusText`) 둘 다 갱신하며, 메시지는 다음 갱신 전까지 자동으로 사라지지 않는다.

[태그 관리](tag-management.md)/[배우 관리](actor-management.md)도 동일한 패턴(구조·스타일·종류 구분 모두)을 공유한다.

## 화면 노출

- **속성 창(`PropertiesWindow`)**: "배우" 항목 바로 밑에 "시리즈:" 콤보박스가 있다(2026-08-09 추가). 마스터 목록 이름 + 맨 앞의 "(없음)" 항목(시리즈 미지정을 나타내는 센티널, `PropertiesWindow.NoSeriesLabel`)으로 채워지며, "확인" 클릭 시(다른 필드와 동일하게 커밋 시점에만 반영) 선택값이 "(없음)"이면 `_item.Series`를 빈 문자열로, 아니면 선택된 이름으로 설정한다. 태그/배우처럼 실시간 반영이 아니라 코드/출시일과 같은 "커밋 시점 반영" 패턴이다. 마스터 목록에서 이미 삭제된 시리즈 이름을 항목이 그대로 갖고 있으면(다른 곳에서 삭제된 경우) 목록에 함께 포함시켜 데이터가 조용히 사라지지 않게 한다(`RefreshSeriesComboBox`). → [속성 관리](properties-management.md) 참고.
- **시리즈 필터(관리 리스트)**: 필터 영역 맨 왼쪽의 단일 선택 콤보박스로 시리즈별 필터링이 가능하다 — [동영상 파일 관리](video-file-management.md) 참고.
- 관리 리스트 컬럼/하단 상세정보 패널에는 아직 노출하지 않는다(요구사항에 없어 범위 밖 — 향후 필요 시 "배우" 컬럼과 같은 패턴으로 추가 가능).

## 파일명 변경 시 Credits 동기화 (`SeriesCreditSync.cs`)

(2026-08-09 추가, 구현 완료): `RenameHelper.TryRenameManagedItemTo`/`TryEditFullPath`(파일명이 실제로 바뀌는 두 rename/이동 경로, [동영상 파일 관리](video-file-management.md) 소유)가 성공하면 `SeriesCreditSync.OnFileRenamed`를 호출해, 이 파일이 지정된 시리즈의 Credits에서 **옛 품번을 새 품번으로 갱신**한다. **버그 수정**: 예전에는 이 동기화가 없어서, 파일명이 바뀌어 품번이 달라지면(예: F2 이름변경, 속성 창 파일명 수정) 새 품번은 다른 곳(`SyncCreditsFromManagedItems`, 시리즈 선택 시 자동 병합)에서 알아서 추가되는데 **옛 품번은 아무도 지우지 않아 Credits에 고아로 남는** 버그가 있었다(배우 쪽은 `ActorCreditSync.OnFileRenamed`가 처음부터 이 역할을 했으나, 시리즈 기능이 나중에 추가되면서 이 연동이 빠져 있었다). [배우 관리](actor-management.md)의 `ActorCreditSync`와 마찬가지로 새 품번이 이미 다른(아직 파일 없는) 시리즈의 Credits와 일치하고 이 파일에 시리즈가 비어있으면 그 시리즈를 자동으로 지정하는 역방향 동작도 포함한다. `PropertiesWindow`도 rename 성공 시 `RefreshSeriesComboBox()`를 호출해(배우의 `SyncSelectedActorsFromItem()`과 동일한 패턴) 이 자동 지정을 화면에 즉시 반영한다.

`SeriesCreditSync.OnFileDeleted(item, masterSeries)`(2026-08-16 추가, 구현 완료): [동영상 파일 관리](video-file-management.md)의 "완전삭제"(`MainWindow.PermanentlyDeleteManaged_Click`)에서 항목을 `_managedItems`에서 제거하기 직전에 호출한다. 이 파일에 `Series`가 지정되어 있으면 그 시리즈의 Credits에서 이 파일의 품번을 제거한다 — [배우 관리](actor-management.md)의 `ActorCreditSync.OnFileDeleted`와 동일한 목적으로, 완전삭제 후 그 품번이 "파일 없음"(연한 색) 상태로 시리즈 관리 창에 고아처럼 남지 않도록 한다. **"제거"(소프트 삭제, 복구 가능)는 대상이 아니다.**

## "시리즈 동기화" 버튼

(`MainWindow.SyncSeries_Click`, 2026-08-10 추가, 같은 날 "속성" 버튼 밑 줄로 위치 변경, 구현 완료): 관리 리스트 툴바 5번째 줄("속성"/"태그 관리"/"배우 관리"/"시리즈 관리" 줄 바로 밑), "배우 동기화" 버튼 오른쪽에 있다(도구 메뉴에도 동일 항목 있음). 지금까지의 동기화는 모두 "관리 리스트 → 시리즈 Credits" 방향(선택 시 자동 병합, rename 시 갱신)이었는데, 이 버튼은 **반대 방향**("시리즈 Credits → 관리 리스트")을 수동으로 일괄 실행한다 — 모든 시리즈의 Credits(품번)를 훑어서, 그 품번과 일치하지만 아직 `Series`가 비어있는 관리 리스트 항목에 그 시리즈를 채워 넣는다. **이미 다른 시리즈가 지정된 항목은 절대 덮어쓰지 않고 충돌로 건너뛴 뒤 결과 대화상자에 별도로 보고한다**(사용자가 직접 지정한 값을 임의로 바꾸지 않기 위함 — 배우/시리즈 Credits 우클릭 삭제 등 다른 곳과 동일한 원칙). 결과 대화상자는 업데이트된 항목 수와 목록(최대 20개 표시 후 "...외 N건 더"), 충돌 건수와 목록을 보여준다. 실행 전 확인 대화상자는 없다 — 기존 값을 덮어쓰지 않는 안전한 연산이므로.

`PropertiesWindow`/`ActorManagerWindow` 생성자는 `masterSeries`(`ObservableCollection<SeriesItem>`)를 추가로 받는다(2026-08-09 변경) — 배우 관리 창의 Credits 칩에서도 속성 창을 열 수 있어야 하므로(그래야 그 창의 시리즈 콤보박스도 정상 동작), `ActorManagerWindow`가 내부적으로 여는 `PropertiesWindow`에도 `masterSeries`를 그대로 전달해야 한다. `MainWindow`/`SeriesManagerWindow`가 `PropertiesWindow`/`ActorManagerWindow`를 여는 모든 지점이 이 매개변수를 함께 넘긴다.

## 데이터 모델 (시리즈 마스터 목록, `series.json`)

시리즈 마스터 목록은 `SeriesItem` 객체 배열 형태로 저장한다(2026-08-09 추가).

```json
[
    { "Name": "SS 시리즈", "Credits": ["SSNI-123", "SSNI-456"] },
    { "Name": "미분류 기획", "Credits": [] }
]
```

관리 리스트 항목의 `Series` 필드는 이 마스터 목록의 `Name` 값 하나만 참조하도록 유지한다(마스터 목록에 없는 값이나 여러 개를 동시에 갖지 않도록 속성 창의 콤보박스가 강제) — 위 절 참고. `Credits`(품번 목록)는 배우의 `Credits`와 같은 패턴이지만 자동 태깅은 단방향으로만 동작한다(해당 항목의 `Series`가 비어있을 때만 채움) — 위 절 참고.
