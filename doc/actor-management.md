# 배우 관리

> [메인 지시서](../CLAUDE.md)의 하위 문서. 배우 마스터 목록(`actors.json`)과 `ActorManagerWindow`, 그리고 배우의 출연작 목록(Credits)과 관리 리스트 간의 상호 동기화를 다룬다. 속성 창에서 배우를 항목에 태깅하는 UI는 [속성 관리](properties-management.md), 시리즈는 [시리즈 관리](series-management.md)(같은 `AddCreditWindow`를 공유) 참고.

**관련 파일**: `ActorItem.cs`, `ActorRepository.cs`, `ActorManagerWindow.xaml`/`.xaml.cs`, `ActorInfoWindow.xaml`/`.xaml.cs`, `AddCreditWindow.xaml`/`.xaml.cs`, `ActorCreditSync.cs`

## 배우 마스터 목록 관리 (`ActorManagerWindow`)

태그와 별개로, 배우는 이름뿐 아니라 100x100 썸네일 이미지를 함께 갖는 마스터 목록으로 관리한다 — **구현 완료**.

- **데이터 모델(`ActorItem`)**: `Name`(문자열) + `ThumbnailPath`(문자열?, 100x100 리사이즈본 경로, 미지정 시 null) + `BirthYear`(int?, 출생년도) + `Height`(int?, 키/cm) + `BodyInfo`(문자열, 신체정보 자유 텍스트, 기본값 빈 문자열) + `Credits`(`List<string>`, 이 배우가 출연한 것으로 등록된 품번 목록, 2026-08-02 추가) + 계산 속성 `HasThumbnail`. `INotifyPropertyChanged` 구현. `Name`/`ThumbnailPath`/`BirthYear`/`Height`/`BodyInfo`는 모두 public setter라 `[JsonInclude]`가 필요 없지만, `Credits`는 `ManagedVideoItem.Tags`/`Actors`와 동일한 이유로 `private set` + `[JsonInclude]` + `SetCredits(...)` 메서드 패턴을 쓴다 — [공통 관리](common-management.md)의 컨벤션 참고.
- **저장 위치**: `%LOCALAPPDATA%\VideoVault\actors.json`. 관리 리스트/태그와 동일하게 프로그램 시작 시 자동 로딩되고, 파일이 없으면 빈 목록으로 시작한다.
- **자동 저장**: 배우 추가/이름변경/삭제/썸네일 변경 시 500ms debounce 후 `actors.json`에 자동 저장 (관리 리스트/태그와 동일한 정책, `ActorItem.PropertyChanged`를 구독해 썸네일 변경도 감지). Ctrl+S로도 즉시 저장된다.
- **관리 리스트 항목과의 관계**: `ManagedVideoItem.Actors`는 배우 이름의 목록(`List<string>`)으로, 태그와 마찬가지로 배우 마스터 목록에 존재하는 이름만 참조한다(자유 입력 아님). **한 항목에 배우를 여러 명 지정할 수 있다.**
- **배우 관리 화면(`ActorManagerWindow`, 기본 크기 1000x1000)**: 메인창 "도구 > 배우 관리" 메뉴 또는 관리 리스트 툴바의 "배우 관리" 버튼(태그 관리 버튼 옆)으로 연다.
  - **목록은 아이콘 보기로만 표시된다** (리스트 보기 없음): `WrapPanel` 기반 그리드에 각 배우의 썸네일(90x90)과 이름을 보여준다 — **구현 완료**. **이름 밑에 작품 수(숫자만)가 표시된다**(2026-08-12 추가, **구현 완료**) — `{Binding Credits.Count}`로 바인딩하며, 라벨 없이 숫자만 보여준다(예: "3"). `Credits`가 바뀌면(작품 추가/삭제, 관리 리스트와의 자동 동기화 등) `ActorItem.PropertyChanged`가 `Credits`를 알리므로 그리드도 자동으로 갱신된다. 카드 높이(`Border`)는 이 줄을 위해 140→155로 늘렸다.
  - **목록은 항상 이름 기준 오름차순으로 정렬되어 표시된다** — `TagManagerWindow`와 동일하게 `ICollectionView` + `SortDescription(nameof(ActorItem.Name), Ascending)`을 사용하며, `actors.json`의 실제 저장 순서는 바꾸지 않는다 — **구현 완료**.
  - **추가**: 새 배우 이름 입력 후 목록에 추가 (동일성 판정은 태그와 동일하게 앞뒤 공백 제거 + 대소문자 무시). **추가된 배우를 목록에서 바로 선택하고 그 위치로 스크롤한다**(2026-08-02 추가, **구현 완료**) — 태그 관리와 동일한 패턴(`ActorsListBox.SelectedItem`/`ScrollIntoView`), 이름 기준 정렬이라 새 배우가 임의 위치에 삽입되므로 직접 찾지 않아도 바로 보이게 한다.
  - **이름 변경**: 배우 이름을 바꾸면 이 배우가 지정된 모든 관리 리스트 항목의 `Actors`에도 새 이름이 반영된다(참조 무결성 유지, 태그 이름 변경과 동일한 패턴). 썸네일 파일이 있으면 새 이름 기준 파일명으로 함께 rename을 시도한다(부가 정리, 실패해도 이름 변경 자체는 유지 — `RenameHelper`의 썸네일 rename과 동일한 원칙). 이름 변경 후 정렬 위치 갱신을 위해 `_actorsView.Refresh()`를 호출한다.
  - **삭제**: 배우를 마스터 목록에서 삭제하면 이 배우를 사용 중인 관리 리스트 항목들의 `Actors`에서도 제거되고, 배우의 썸네일 파일도 함께 삭제를 시도한다(부가 정리, 실패해도 무시).
  - **썸네일 지정/삭제**: 창 오른쪽에 선택된 배우의 썸네일 뷰어(**200x150 고정**, 왼쪽 정렬 — 아래 "오른쪽 패널 크기 조절" 참고, `Stretch="Uniform"`이라 사진을 자르지 않고 비율 그대로 전부 보여준다 — 박스와 사진의 가로세로 비율이 다르면 위아래/좌우로 빈 여백이 생긴다. 2026-08-27 수정, 사용자 피드백 — 원래 `UniformToFill`이었는데 200x150이 가로로 긴 박스라 세로로 긴 인물 사진의 위/아래가 잘려 나가 "원본 사진과 다르다"는 문제가 있었다. 품번 칩 위에 뜨는 썸네일 미리보기 팝업(`CreditThumbnailImage`, 180x131)도 이미 같은 `Uniform` 방식이라 그쪽과 일관성도 맞춘 것)가 있고, 그 위에 "썸네일 추가"/"썸네일 삭제" 버튼이 나란히 있다. 추가는 파일 선택 대화상자 또는 드래그 앤 드롭(`DragDropImageHelper` 재사용, 인터넷 이미지 드래그도 동일하게 지원 — [공통 관리](common-management.md) 참고)으로, 삭제는 썸네일 파일을 디스크에서 지우고 `ThumbnailPath`를 null로 되돌린다(즉시 반영, 썸네일이 없으면 안내만 표시). 동영상 썸네일 뷰어와 달리 **원본을 클릭해서 크게 보는 기능은 없다** (원본 자체를 보관하지 않으므로).
  - **오른쪽 패널 크기 조절**(2026-08-27 추가, **구현 완료**, 사용자 요청): 왼쪽 배우 그리드와 오른쪽 패널(썸네일/배우 정보/Credits) 사이에 세로로 긴 `GridSplitter`가 있어, 좌우로 드래그하면 오른쪽 패널 전체 가로 폭이 조절된다(150~600px, `RightPanelColumn`). **썸네일 뷰어 자체의 크기는 이 폭과 무관하게 고정이다**(2026-08-27 같은 날 수정 — 처음엔 `HorizontalAlignment="Stretch"`로 패널 폭에 맞춰 썸네일도 함께 늘어나게 했는데, 배우를 바꾸거나 패널을 늘렸다 줄였다 할 때마다 썸네일 크기까지 덩달아 바뀌는 게 오히려 산만하다는 피드백을 받고 `Width="200" Height="150" HorizontalAlignment="Left"`로 되돌렸다 — 패널이 넓어지면 썸네일 오른쪽에 남는 공간이 그냥 빈 여백으로 남는다). **마지막 패널 폭은 창을 닫을 때 기억해뒀다가 다시 열면 그대로 복원된다** — `ActorManagerWindow.RememberedRightPanelWidth`(정적 프로퍼티, 앱 실행 중 메모리에 보관)를 `MainWindow`가 시작 시 `AppSettings.ActorManagerRightPanelWidth`에서 불러와 채우고 종료 시 다시 그 값을 저장한다(`WindowPositionMemory`와 같은 패턴이나 창이 하나뿐이라 Dictionary 없이 정적 프로퍼티 하나로 충분). 세로 크기는 이 패널 자체가 아니라 아래 Credits 목록만 조절 대상이다(다음 항목 참고).
  - **썸네일 정보 밑에 배우 정보 표시**: 오른쪽 패널의 썸네일 뷰어 아래에 선택된 배우의 이름/출생년도/키/신체정보를 읽기 전용으로 보여준다(값이 없으면 "-"). 선택이 바뀔 때마다 `RefreshActorInfoPanel`이 갱신한다 — **구현 완료**.
  - **우클릭 → "배우 정보 수정"**: 목록의 배우를 우클릭하면 `ActorItemContextMenu`(다른 우클릭 메뉴들과 동일하게 `Window.Resources`에 선언 후 `ItemContainerStyle`에서 `StaticResource`로 참조하는 패턴)가 뜨고, "배우 정보 수정"을 클릭하면 `ActorInfoWindow`가 열린다. 이 창에서 이름/출생년도/키/신체정보를 편집할 수 있다(출생년도/키/신체정보는 비워두면 삭제됨). 이름을 바꾸면 대화상자 자체는 다른 배우와의 이름 중복만 검사하고, 실제 rename 동기화(썸네일 파일 rename + 관리 리스트 항목들의 `Actors` 참조 갱신)는 "선택 배우 이름 변경" 버튼과 공유하는 `RenameActorAndSync`가 처리한다(중복 로직 방지) — **구현 완료**.
  - **Credits(이 배우가 출연한 작품 목록, 2026-08-02 추가, 구현 완료)**: 썸네일 정보/배우 정보 밑에 **"Credits"** 라벨과 그 옆(오른쪽 정렬) **"작품 추가"** 버튼이 있고, 그 아래 이 배우의 `ActorItem.Credits`(품번 문자열 목록)를 태그 칩과 같은 모양(둥근 배경, 패딩)의 칩으로 나열한다. **품번 기준 오름차순으로 정렬되어 표시된다**(2026-08-09 추가, `OrderBy(code, StringComparer.OrdinalIgnoreCase)`) — `Credits`(저장 순서)는 그대로 두고 화면 표시만 정렬한다.
    - **색상 구분**: 관리 리스트(활성/제거됨 모두)에 같은 품번(파일명, 확장자 제외)의 파일이 실제로 있으면 **진한 파란색**(`#3D6FB4`, 흰 글자), 없으면 **연한 파란색**(`#CFE2F3`, 짙은 글자)으로 표시된다 — "알려진 작품이지만 아직 관리 리스트에 없는 파일"과 "이미 보유한 파일"을 한눈에 구분하기 위함.
    - **자동 동기화(배우 선택 시)**: 배우를 선택할 때마다(`RefreshCreditsPanel` → `SyncCreditsFromManagedItems`) 관리 리스트에서 이 배우가 `Actors`로 지정된 파일들을 찾아 그 품번들을 Credits에 자동으로 병합한다(이미 있는 값은 건드리지 않음) — 관리 리스트에서 이미 이 배우를 태깅해둔 파일이 있다면 "작품 추가"를 따로 거치지 않아도 Credits에 반영된다.
    - **작품 추가 → 배우 자동 태깅**: 아래 "작품 추가 창"으로 새 품번을 추가하면, 관리 리스트에 그 품번의 실제 파일이 있는 경우 `UpdateManagedItemActorsForCredit`가 그 파일의 `Actors`에도 이 배우를 자동으로 추가한다(이미 지정돼 있으면 건드리지 않음).
    - **칩 좌클릭 → 속성 창 열기**: Credits 칩을 클릭하면 관리 리스트에서 일치하는 항목을 찾아 [속성 관리](properties-management.md)의 `PropertiesWindow`를 연다. 일치하는 파일이 없으면(연한 파란색 칩) 안내 메시지만 표시한다.
    - **칩 우클릭 → Credits에서 삭제**: 확인 대화상자 후 이 배우의 Credits에서 제거한다. **이 품번과 일치하는 실제 파일이 아직 이 배우를 `Actors`로 갖고 있으면 그 파일의 `Actors`에서도 함께 제거한다**(2026-08-09 버그 수정, **구현 완료**) — 이걸 안 하면 곧바로 `RefreshCreditsPanel`의 `SyncCreditsFromManagedItems`가 그 파일을 근거로 품번을 다시 채워 넣어서, 사용자 입장에서는 "삭제가 안 되는" 것처럼 보이는 실제 버그가 있었다(우클릭 삭제 직후 같은 칩이 그대로 남아있음). 확인 대화상자 문구에도 "실제 파일에 이 배우가 지정되어 있다면 그 지정도 함께 해제됩니다"를 명시한다.
    - 배우 이름 변경/삭제 시에는 Credits 자체를 별도로 옮기지 않는다(Credits는 배우별 데이터이므로 `ActorItem`과 함께 그대로 유지/삭제됨).
    - **품번 칩에 마우스를 올리면 썸네일 팝업이 뜬다**(2026-08-16 추가, **구현 완료**) — [시리즈 관리](series-management.md)의 "품번 칩에 마우스를 올리면 썸네일 팝업" 참고(두 창이 완전히 동일한 로직을 쓴다). 배우는 여러 명이 같은 품번을 공유할 수 있어(다중 출연) 시리즈와 달리 **중복 정리 대상은 아니다** — 이 항목만 시리즈 관리 창과 다른 점이다.
    - **드래그 앤 드롭으로 품번 추가**(2026-08-15 추가, **구현 완료**): "작품 추가" 대화상자를 열지 않고도, 브라우저 등에서 선택한 텍스트를 Credits 칩 목록(`CreditsList`) 영역으로 바로 끌어다 놓으면 그 텍스트가 품번으로 즉시 추가된다. `AddCreditWindow`와 동일한 정규화 규칙(소문자, 앞뒤 공백 제거)을 따르고, 같은 `AddCreditToActor`(시리즈 관리 창은 `AddCreditToSeries`)를 재사용해 중복 검사·관리 리스트 자동 태깅·화면 갱신이 "작품 추가" 버튼 경로와 완전히 동일하게 적용된다.
      - **드롭 영역/세로 크기(2026-08-22, 2026-08-27 변경, 구현 완료)**: 처음에는 `MinHeight="30"`(내용에 딱 맞게 줄어듦) → `Height="300"` 고정(시리즈 관리 창처럼 눈에 띄는 고정 영역)을 거쳐, 최종적으로 **시리즈 관리 창과 동일하게 `*` 행에 넣어 남는 세로 공간을 전부 채우도록** 바뀌었다(2026-08-27, 사용자 요청 — "작품 리스트를 현재 창 기준으로 세로 크기를 최대로"). 오른쪽 패널 전체를 `StackPanel`에서 2행짜리 `Grid`(`RowDefinition Height="Auto"`+`Height="*"`)로 바꿔, 썸네일/배우 정보/Credits 헤더는 Row 0(내용에 맞는 높이)에, `CreditsList`를 감싼 `ScrollViewer`는 Row 1(`*`, 창 크기에 따라 자동으로 남는 공간 전부)에 배치했다 — 예전엔 오른쪽 패널이 고정폭 `StackPanel`이라 `*` 행 자체를 쓸 수 없었지만, `Grid`로 바꾸면서 가능해졌다. 테두리/배경은 여전히 넣지 않는다(시리즈 관리 창과 겉모습을 맞춤). 이벤트 핸들러(`CreditsList_DragOver`/`CreditsList_Drop`)는 `sender`를 쓰지 않고 `e.Data`만 참조하므로 호스트를 옮겨도 로직 변경이 필요 없었다.

## 작품 추가 창 (`AddCreditWindow`)

(2026-08-02 추가, 2026-08-09 연속 추가 방식으로 개편, 구현 완료): "작품 추가" 버튼으로 여는 대화상자(기본 크기 520x420). **배우 관리/시리즈 관리 창이 공유하는 완전히 동일한 대화상자다** — 시리즈 관리 창(`SeriesManagerWindow`)에서도 같은 창을 그대로 재사용한다, [시리즈 관리](series-management.md) 참고.

- 품번을 직접 입력할 수 있는 텍스트 상자가 있고, **바로 옆에 "추가" 버튼**이 있다(2026-08-09 추가).
- **입력한 품번은 항상 소문자로, 앞뒤 공백은 제거하고 저장한다**(2026-08-09 추가, `AddCurrentCode`) — `CodeBox.Text.Trim().ToLowerInvariant()`.
- **"추가" 버튼을 클릭하거나 입력란에서 Enter를 누르면 그 즉시 반영되고 창은 닫히지 않는다**(2026-08-09 변경, 구현 완료 — 예전에는 "확인" 버튼으로 값 하나만 확정하고 창이 닫혔다). 반영 후 입력란이 즉시 비워지고 포커스가 그대로 유지되어, 여러 품번을 연달아 입력해서 한 번에 여러 개를 추가할 수 있다. 생성자가 `Action<string> onAddCode` 콜백을 받아 각 추가 시점마다 즉시 호출하는 방식으로 구현되어 있다 — 호출자(`ActorManagerWindow.AddCreditToActor`/`SeriesManagerWindow.AddCreditToSeries`)가 이 콜백 안에서 중복 검사 + Credits 반영 + 동기화 + 화면 갱신을 수행한다. 창 맨 아래에는 "닫기" 버튼 하나만 있다(예전의 "확인"/"취소" 두 버튼을 대체 — 추가는 이미 즉시 반영되므로 "취소"로 되돌릴 대상이 없다).
- 입력할 때마다(`TextChanged`) 관리 리스트에서 부분 일치하는 항목을 찾아 **썸네일 + 품번**으로 미리보기 목록을 보여준다(최대 50개, 품번 기준 오름차순). 미리보기 항목을 더블클릭하면 그 품번이 입력란에 채워진다(추가되지는 않음 — "추가" 버튼/Enter를 눌러야 실제로 추가됨).
- 관리 리스트에 없는 품번도 자유롭게 입력해서 추가할 수 있다(미리보기는 어디까지나 참고용이며 입력을 제한하지 않음).

## 배우 Credits ↔ 관리 리스트 상호 동기화 (`ActorCreditSync.cs`)

(2026-08-02 추가, 구현 완료): 위에서 설명한 개별 동기화 지점들을 한 클래스에 모아둔 공용 로직이다.

- `OnFileRenamed(item, oldFileName, masterActors)`: 파일명이 바뀌면(`RenameHelper.TryRenameManagedItemTo`/`TryEditFullPath` 성공 시 호출) ① 이 파일에 이미 지정된 배우들 중 옛 품번을 Credits로 갖고 있던 배우는 새 품번으로 값을 갱신하고, ② 새 품번이 이미 다른(아직 파일 없이 "연한 파란색"으로만 있던) 배우의 Credits와 일치하면 그 배우를 이 파일의 `Actors`에 자동으로 추가한다. 우연히 같은 문자열을 Credits로 가진, 이 파일과 무관한 배우의 데이터까지 건드리지 않도록 ①은 이 파일에 이미 지정된 배우만 대상으로 한다. `RenameHelper`(파일 이름 변경/이동 로직 자체)는 [동영상 파일 관리](video-file-management.md) 소유이며, `masterActors`를 매개변수로 받아 이 동기화를 호출한다 — `MainWindow`의 F2/우클릭 "이름변경"도 같은 경로를 타므로 어느 진입점으로 rename해도 동일하게 동기화된다.
- `OnActorRemovedFromItem(item, removedActorName, masterActors)`: [속성 관리](properties-management.md)의 `PropertiesWindow`에서 배우 칩을 제거하고 커밋하면, 그 배우의 Credits에서 이 파일의 품번을 제거한다.
- `OnFileDeleted(item, masterActors)`(2026-08-16 추가, 구현 완료): [동영상 파일 관리](video-file-management.md)의 "완전삭제"(`MainWindow.PermanentlyDeleteManaged_Click`)에서 항목을 `_managedItems`에서 제거하기 직전에 호출한다. 이 파일에 태깅되어 있던 모든 배우에 대해 내부적으로 `OnActorRemovedFromItem`을 호출해서, 각 배우의 Credits에서 이 파일의 품번을 제거한다 — 완전삭제는 항목 자체가 사라지므로, 정리하지 않으면 그 품번이 실제로는 존재하지 않는 파일인 채로 "파일 없음"(연한 색) 상태로 배우 관리 창에 고아처럼 남는다. **"제거"(소프트 삭제, `IsValid = false`, 복구 가능)는 대상이 아니다** — 항목이 관리 리스트에 그대로 남아있어 Credits와 어긋나지 않기 때문이다.

## "배우 동기화" 버튼

(`MainWindow.SyncActors_Click`, 2026-08-10 추가, 같은 날 "속성" 버튼 밑 줄로 위치 변경, 구현 완료): 관리 리스트 툴바 5번째 줄("속성"/"태그 관리"/"배우 관리"/"시리즈 관리" 줄 바로 밑), "시리즈 동기화" 버튼 왼쪽에 있다(도구 메뉴에도 동일 항목 있음). "시리즈 동기화"와 같은 방향의 일괄 동기화("Credits → 관리 리스트")를 배우에 대해 수행한다 — 모든 배우의 Credits(품번)를 훑어서, 그 품번과 일치하지만 아직 그 배우가 `Actors`에 지정되어 있지 않은 관리 리스트 항목에 배우를 추가한다. **배우는 시리즈와 달리 여러 명을 동시에 지정할 수 있는 목록이라 "충돌"이라는 개념이 없다** — 기존에 지정된 다른 배우를 덮어쓰거나 지우지 않고 그냥 없는 것만 추가한다(따라서 결과 대화상자에는 업데이트 목록만 있고 시리즈 동기화와 달리 충돌 섹션이 없다). 결과 대화상자 문구 조립은 `MainWindow.BuildSyncResultMessage`를 시리즈 동기화와 공유한다(충돌 목록을 빈 리스트로 넘기면 자동으로 그 섹션이 생략됨).

## 배우 썸네일 저장 방식(`ThumbnailHelper.CreateActorThumbnail`)

동영상 썸네일과 달리 원본 이미지는 별도로 보관하지 않고, 가로세로 비율을 유지한 채 **100x100 이내로 리사이즈한 결과만** 저장한다(동영상 썸네일의 320x240 로직과 같은 방식으로 축소 비율을 계산하되 크기만 다름). 저장 위치는 `%LOCALAPPDATA%\VideoVault\actresses\{배우명}.jpg`이며(`AppPaths.ActorsThumbnailDir`), 파일명에 쓸 수 없는 문자는 `_`로 치환한다. 소스로 쓰인 원본 파일은 저장이 끝나면 삭제한다(드래그 앤 드롭 임시 파일 정리 포함, 동영상 썸네일과 동일한 원칙). 파일 잠금 버그·이미지 캐시 무효화 등 공통 인프라는 [공통 관리](common-management.md)의 "썸네일 관리" 참고.

## 화면 노출

- **속성 창(`PropertiesWindow`)**: "배우" 항목이 콤보박스(배우 마스터 목록에서 선택) + "추가" 버튼으로 되어 있다. 추가하면 아래에 칩(chip) 형태로 표시되고, 칩의 "✕"를 클릭하면 제거된다 — 여러 명을 반복해서 추가/제거할 수 있다. 상세는 [속성 관리](properties-management.md) 참고.
- **관리 리스트 하단 "선택된 항목 상세 정보" 패널**: 패널 오른쪽에 선택된 항목에 지정된 배우들의 100x100 썸네일이 표시된다. **배우가 여러 명이면 오른쪽 정렬로 순서대로(왼쪽→오른쪽) 나열**된다. 썸네일이 없는 배우는 기본 아이콘(👤)으로 표시된다. → [동영상 파일 관리](video-file-management.md) 참고.
- **리스트 보기 "배우" 컬럼**: 여러 배우 이름을 쉼표로 이어붙인 문자열(`ManagedVideoItem.ActorsDisplay`)로 표시한다 (태그 컬럼처럼 칩 형태는 아님).

## 하위 호환

예전 버전은 배우를 단일 문자열 필드 `Actor`로 저장했다. 이 필드는 새 버전에서 더 이상 사용하지 않으며(`Actors` 목록으로 대체), 예전 `library.json`을 불러와도 `Actor` 필드는 무시되고 `Actors`는 빈 목록으로 시작한다(당시 실제 사용자 데이터의 `Actor` 값이 모두 빈 문자열이었음을 확인 후 결정 — 별도 마이그레이션 로직 없음).

## 데이터 모델 (배우 마스터 목록, `actors.json`)

배우 마스터 목록은 `ActorItem` 객체 배열 형태로 저장한다.

```json
[
    { "Name": "이즈미 리온", "ThumbnailPath": "C:\\Users\\...\\VideoVault\\actresses\\이즈미 리온.jpg", "BirthYear": 1994, "Height": 158, "BodyInfo": "B83 W58 H85", "Credits": ["SSNI-123", "SSNI-456"] },
    { "Name": "쿠로카와 사리나", "ThumbnailPath": null, "BirthYear": null, "Height": null, "BodyInfo": "", "Credits": [] }
]
```

관리 리스트 항목의 `Actors` 필드는 이 마스터 목록의 `Name` 값만 참조하도록 유지한다 (마스터 목록에 없는 배우가 항목에 들어가지 않도록 UI에서 강제). 썸네일 이미지 파일 자체는 `%LOCALAPPDATA%\VideoVault\actresses\` 폴더에 `{배우명}.jpg`로 저장된다 — 위 "배우 썸네일 저장 방식" 참고. `Credits`(품번 목록)는 관리 리스트의 `Actors`와 `ActorCreditSync`로 상호 동기화된다 — 위 절 참고.

## 이 영역의 컨벤션

- 배우 썸네일은 동영상 썸네일과 별도 로직(`ThumbnailHelper.CreateActorThumbnail`)을 쓴다 — 동영상 파일 옆이 아니라 `%LOCALAPPDATA%\VideoVault\actresses\{배우명}.jpg`에 100x100으로, 원본 보관 없이 저장한다.
- `ActorItem.Credits`는 `ManagedVideoItem.Tags`/`Actors`와 동일한 이유로 `private set` + `[JsonInclude]` + `SetCredits(...)` 메서드 패턴을 쓴다.
