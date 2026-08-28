# 태그 관리

> [메인 지시서](../CLAUDE.md)의 하위 문서. 태그 마스터 목록(`tags.json`)과 `TagManagerWindow`를 다룬다. 관리 리스트 항목에 태그를 붙이는 UI(체크박스)는 [속성 관리](properties-management.md), 관리 리스트에서 태그로 필터링/컬럼 표시하는 UI는 [동영상 파일 관리](video-file-management.md) 참고.

**관련 파일**: `TagItem.cs`, `TagRepository.cs`, `TagManagerWindow.xaml`/`.xaml.cs`

## 태그 마스터 목록 관리 (`tags.json`)

- **저장 위치**: 관리 리스트와 동일한 로컬 데이터 폴더에 별도 파일로 저장 (`%LOCALAPPDATA%\VideoVault\tags.json`)
- **자동 로딩**: 프로그램 시작 시 자동으로 불러오며, 파일이 없으면 빈 태그 목록으로 시작
- **자동 저장**: 태그 추가/이름변경/삭제, 품번(Credits) 변경 시 500ms debounce 후 `tags.json`에 자동 저장(관리 리스트/배우/시리즈와 동일한 정책 — [공통 관리](common-management.md)의 설정 관리 참고). `TagItem`이 객체가 되면서(아래 참고) 이름 변경은 더 이상 `ObservableCollection`의 `CollectionChanged`(Replace)를 거치지 않고 `TagItem.PropertyChanged`만 발생시키므로, `MainWindow`가 각 `TagItem`의 `PropertyChanged`도 직접 구독해서(`MasterTags_CollectionChanged` → `MasterTagItem_PropertyChanged`, 배우/시리즈 마스터 목록과 동일한 패턴) 이름 변경/Credits 변경 모두 자동 저장을 트리거하도록 했다(2026-08-27 변경).
- **태그 관리 화면(`TagManagerWindow`, 기본 크기 450x700)**: 태그 마스터 목록을 별도 창에서 관리. 위쪽에 태그 목록(`TagsListBox`), 그 아래 한 줄에 **입력 텍스트박스(`TagBox`) 하나 + "추가"/"변경"/"삭제" 버튼 3개(이 순서로)**, 그 아래 **품번(Credits) 패널**이 있다(2026-08-27 재설계, **구현 완료**, 사용자 요청) — 예전에는 "추가"용 텍스트박스와 "이름 변경"용 텍스트박스가 줄을 나눠 따로 있었고 버튼 라벨도 "선택 태그 이름 변경"/"선택 태그 삭제"로 길었으며 맨 아래 "닫기" 버튼도 있었는데, 텍스트박스 하나로 합치고 버튼 3개(추가/변경/삭제)만 남기는 것으로 정리됐다. **"닫기" 버튼은 삭제되었다** — 제목표시줄 X, Alt+F4로 여전히 닫을 수 있다(다른 창들의 버튼 간소화와 같은 패턴, [동영상 파일 관리](video-file-management.md)의 툴바 재배치 참고).
  - **목록은 항상 이름 기준 오름차순으로 정렬되어 표시된다**(2026-08-27 변경) — `CollectionViewSource.GetDefaultView(_masterTags)`에 `SortDescription(nameof(TagItem.Name), Ascending)`을 적용한 `ICollectionView`를 `TagsListBox.ItemsSource`로 사용한다. `TagsListBox`는 `DisplayMemberPath="Name"`으로 항목 이름만 보여준다(이전에는 항목 자체가 문자열이라 별도 지정이 필요 없었으나, `TagItem` 객체가 되면서 지정하지 않으면 `VideoVault.TagItem`이 그대로 찍힌다). 실제 저장 순서(`tags.json`의 배열 순서)는 바꾸지 않고 화면 표시만 정렬한다 — **구현 완료**.
  - **추가**: `TagBox`에 새 태그 이름을 입력하고 "추가"를 누르면 목록에 추가된다(목록 선택 여부와 무관). **추가된 태그를 목록에서 바로 선택하고 그 위치로 스크롤한다**(2026-08-02 추가, **구현 완료**) — 목록이 항상 정렬 상태라 새 태그가 끝이 아니라 임의 위치에 삽입될 수 있으므로, `TagsListBox.SelectedItem`/`ScrollIntoView`로 사용자가 방금 추가한 태그를 바로 눈으로 확인할 수 있게 한다.
  - **변경(이름 변경)**: 목록에서 바꿀 태그를 먼저 선택하고, `TagBox`에 새 이름을 입력한 뒤 "변경"을 누른다(`TagBox`는 자동으로 채워지지 않는다 — 추가와 같은 텍스트박스를 공유할 뿐, 선택한 태그의 현재 이름을 미리 채워주지는 않는다). 이름을 바꾸면(`tag.Name = newName`, `TagItem.PropertyChanged`가 알림) 이 태그를 사용 중인 관리 리스트의 모든 항목에도 변경된 이름이 반영된다(참조 무결성 유지). 이름이 바뀌면 정렬 위치도 바뀌어야 하므로 변경 후 `_tagsView.Refresh()`를 호출한다.
  - **삭제**: 목록에서 태그를 선택하고 "삭제"를 누르면(확인 대화상자 후) 마스터 목록에서 삭제되고, 이 태그를 사용 중인 관리 리스트 항목들에서도 해당 태그가 제거된다. `TagBox` 내용과는 무관하다.
- **태그 동일성 판정 규칙**: 앞뒤 공백을 제거하고 대소문자를 구분하지 않는다 (`" Action "`, `"action"`, `"ACTION"`은 모두 같은 태그로 취급되어 중복 추가가 거부된다). 현재 `TagManagerWindow`의 중복 검사가 이 규칙으로 이미 구현되어 있다.

## 품번(Credits) 패널 (`TagItem.Credits`, 2026-08-27 추가, 구현 완료, 사용자 요청)

목록 아래 입력줄 밑에 "품번" 라벨과 그 옆(오른쪽 정렬) "추가" 버튼이 한 줄에 있고, 그 아래 이 태그가 붙은 것으로 등록된 품번을 칩으로 나열한다. **[배우 관리](actor-management.md)의 Credits 패널과 완전히 동일한 로직/스타일을 그대로 재사용한다**(진한 색=관리 리스트에 실제 파일 있음, 연한 색=아직 없음; 품번 기준 오름차순 정렬; 칩 좌클릭 시 일치하는 항목의 속성 창 열기, 우클릭 시 Credits에서 제거; 마우스를 올리면 썸네일 팝업 미리보기; 드래그 앤 드롭으로 품번 추가). 배우 관리 창을 원형으로 삼은 이유는 태그도 배우처럼 **한 파일에 여러 개가 동시에 붙을 수 있는 다대다 관계**이기 때문이다 — 시리즈(단일 선택)와 달리 "다른 태그에서 이 품번을 뺏어온다"는 개념 자체가 없다(여러 태그의 Credits에 같은 품번이 동시에 있는 것이 정상).

- **테두리(2026-08-27 추가, 구현 완료, 사용자 요청)**: 품번 칩 목록을 감싼 `ScrollViewer`를 회색 `CornerRadius="8"` 둥근 `Border`(배경 없음)로 한 번 더 감쌌다 — 배우/시리즈 관리 창의 품번 영역은 테두리가 없지만, 태그 관리 창은 이 테두리로 영역 경계를 명확히 구분한다(세 창의 겉모습을 반드시 통일하지는 않기로 함). `ScrollViewer`에 `Padding="6"`도 함께 줘서 칩이 둥근 테두리 모서리에 바로 닿지 않게 여백을 뒀다.

- **자동 동기화(태그 선택 시)**: 태그를 선택할 때마다(`RefreshCreditsPanel` → `SyncCreditsFromManagedItems`) 관리 리스트에서 이 태그가 `Tags`로 지정된 파일들을 찾아 그 품번들을 Credits에 자동으로 병합한다(이미 있는 값은 건드리지 않음) — 관리 리스트에서 이미 이 태그를 붙여둔 파일이 있다면 "추가"를 따로 거치지 않아도 Credits에 반영된다. 실제 데이터("kr" 태그, 약 250개 품번)로 검증했다.
- **품번 추가 → 태그 자동 지정**: "추가" 버튼(`AddCreditWindow` 대화상자, 배우/시리즈 관리 창과 완전히 동일한 창을 재사용)이나 드래그 앤 드롭으로 새 품번을 추가하면, 관리 리스트에 그 품번의 실제 파일이 있는 경우 `UpdateManagedItemTagsForCredit`가 그 파일의 `Tags`에도 이 태그를 자동으로 추가한다(이미 지정돼 있으면 건드리지 않음).
- **칩 좌클릭 → 속성 창 열기**: 관리 리스트에서 일치하는 항목을 찾아 [속성 관리](properties-management.md)의 `PropertiesWindow`를 연다(이를 위해 `TagManagerWindow`가 배우/시리즈 관리 창과 마찬가지로 `masterActors`/`masterSeries`도 생성자에서 받도록 확장됐다). 일치하는 파일이 없으면(연한 색 칩) 안내 메시지만 표시한다.
- **칩 우클릭 → Credits에서 삭제**: 확인 대화상자 후 이 태그의 Credits에서 제거한다. 이 품번과 일치하는 실제 파일이 아직 이 태그를 `Tags`로 갖고 있으면 그 파일의 `Tags`에서도 함께 제거한다(배우/시리즈 관리 창과 동일한 이유의 버그 수정 — 안 하면 자동 병합이 곧바로 되살림).
- **품번 칩에 마우스를 올리면 썸네일 팝업**: 배우/시리즈 관리 창과 완전히 동일(180x131, `ImageLoadHelper.Load(path, 180)`).

## 데이터 모델 (태그 마스터 목록, `tags.json`)

태그 마스터 목록은 `TagItem` 객체 배열 형태로 저장한다(2026-08-27 변경 — 이전에는 문자열 배열이었다).

```json
[
    { "Name": "코미디", "Credits": ["abc-123"] },
    { "Name": "액션", "Credits": [] }
]
```

`TagItem`은 `Name`(문자열) + `Credits`(`List<string>`, 이 태그가 붙은 것으로 등록된 품번 목록)로 구성되며, `ActorItem`/`SeriesItem`과 완전히 동일한 모양이다(`private set` + `[JsonInclude]` + `SetCredits(...)`, 썸네일/부가 정보는 없음).

- **구버전(문자열 배열) 마이그레이션**(2026-08-27 추가, **구현 완료**): `TagRepository.Load`가 파일을 읽을 때 JSON 배열의 첫 원소가 문자열이면 구버전으로 판단해, 각 이름을 `Credits`가 빈 `TagItem`으로 자동 변환한다(`["액션", "코미디"]` → `[{"Name":"액션","Credits":[]}, {"Name":"코미디","Credits":[]}]`). 별도 안내나 백업 없이 조용히 변환되며, 다음 자동 저장(태그 추가/변경/삭제, Credits 변경 중 아무거나) 시점에 새 형식으로 디스크에 반영된다 — 그 전까지는 파일 자체는 구버전 그대로지만 메모리상 데이터는 이미 새 형식이라 동작에는 영향이 없다. 실제 사용자 데이터(태그 42개)로 검증: 마이그레이션 직후 "kr" 태그를 선택하자 관리 리스트에서 자동 동기화된 약 250개 품번이 정상적으로 채워졌고, 이후 창을 닫아 저장된 `tags.json`에서 새 형식과 채워진 Credits를 모두 확인했다.

관리 리스트 항목의 `Tags` 필드는 이 마스터 목록에 존재하는 값(`TagItem.Name`)만 참조하도록 유지한다(마스터 목록에 없는 태그가 항목에 들어가지 않도록 UI에서 강제). `ManagedVideoItem.Tags`(개별 항목이 가진 태그 이름 목록) 자체는 이번 변경으로 바뀌지 않았다 — 여전히 단순 `List<string>`이다. 마스터 목록(`TagItem`, 이름 + 이 태그를 쓰는 것으로 "알려진" 품번 목록)과 개별 항목의 태그 지정(`ManagedVideoItem.Tags`)은 서로 다른 개념이며, 위 "품번(Credits) 패널" 절이 그 동기화 규칙을 다룬다.

## 관리 리스트에서의 노출

- **속성 창의 태그 체크박스**: [속성 관리](properties-management.md) 참고 — 자유 입력이 아니라 마스터 목록에서 체크박스로 선택하는 방식이며, 여러 개 동시 선택 가능. 체크박스 목록은 `TagItem.Name`으로 만들어진다(`PropertiesWindow.BuildTagList`).
- **태그 필터/태그 컬럼**: [동영상 파일 관리](video-file-management.md) 참고 — 리스트 보기의 태그 컬럼은 개별 태그(chip) 형태로 표시되고, 필터 영역의 "태그 필터" 버튼으로 여러 태그를 선택해 필터링할 수 있다. 필터 체크박스 목록도 `TagItem.Name`으로 만들어진다(`MainWindow.ShowTagFilterPopup`).

## 이 영역의 컨벤션

- tags는 항상 문자열 배열(태그 목록)로 취급하며, 단일 문자열(콤마 구분 등)로 저장하지 않는다. 이건 마스터 목록의 `TagItem.Credits`와 개별 항목의 `ManagedVideoItem.Tags` 둘 다에 적용된다.
- 태그 마스터 목록(`tags.json`)과 관리 리스트(`library.json`)는 별도 파일/별도 Repository(`TagRepository`, `ManagedListRepository`)로 분리 관리한다.
- 태그 마스터 목록에서 태그 이름 변경/삭제 시 관리 리스트의 `Tags` 필드와 항상 동기화되도록 한다 (마스터 목록에 없는 태그가 관리 리스트에 남아있지 않도록 함).
- `ManagedVideoItem.Tags`는 setter가 `private`이라 `[JsonInclude]`가 반드시 필요하다 — [공통 관리](common-management.md)의 컨벤션 참고(실제로 이 필드가 이 문제로 저장은 되지만 불러오기에서 항상 빈 목록이 되는 버그가 있었다, 2026-07-31 수정). `TagItem.Credits`도 같은 이유로 `[JsonInclude]`가 필요하다.
