# 공통 관리

> [메인 지시서](../CLAUDE.md)의 하위 문서. 특정 도메인(동영상/속성/태그/배우/시리즈)에 속하지 않고 앱 전체에 걸쳐 적용되는 것들 — 설정 저장, 오류 처리, 성능, 확장성, 창 관리 정책, 썸네일 공통 인프라, 전역 컨벤션, 로그, 테스트 — 을 모은다.

**관련 파일**: `App.xaml`/`.xaml.cs`, `ImageLoadHelper.cs`, `WindowsIconHelper.cs`, `ThumbnailPathConverter.cs`, `ThumbnailHelper.cs`, `OriginalImageWindow.xaml`/`.xaml.cs`, `DragDropImageHelper.cs`, `AppPaths.cs`, `AppSettings.cs`, `SettingsRepository.cs`, `SingleInstanceWindow.cs`, `WindowPositionMemory.cs`, `WindowSnapHelper.cs`, `FormatUtil.cs`

## 썸네일 관리

`ThumbnailPath`(320x240 리사이즈본 경로)와 `ThumbnailOriginalPath`(리사이즈 전 원본 경로)를 함께 저장한다. 지정하지 않으면 기본 아이콘(🎬)을 표시한다. 이 절은 동영상/배우 썸네일이 공유하는 인프라를 다룬다 — 동영상 썸네일 저장 규칙은 [동영상 파일 관리](video-file-management.md), 배우 썸네일 저장 규칙(100x100, 원본 미보관)은 [배우 관리](actor-management.md) 참고.

### 썸네일 파일 잠금 문제 (이미 표시 중인 썸네일 덮어쓰기)

**버그(2026-08-01 수정)**: 이미 썸네일이 지정된 항목(동영상/배우 모두)에 새 썸네일을 지정하면 "다른 프로세스가 파일을 사용 중입니다" 오류로 실패하는 문제가 있었다.

- **원인**: WPF의 `BitmapImage`는 기본 캐시 옵션(지연 로딩)으로 만들면 화면에 표시되는 동안 원본 파일을 계속 열어둔다. 메인창 썸네일 뷰어(선택된 항목을 계속 따라가며 표시)나 아이콘 보기, `PropertiesWindow`/`ActorManagerWindow`의 썸네일 뷰어가 이미 그 썸네일 파일을 열어 표시하고 있는 상태에서, `ThumbnailHelper.CreateThumbnail`/`CreateActorThumbnail`이 **같은 경로**(`{파일명}.thumbnail.jpg` 등, 항상 같은 이름으로 저장하므로)에 새 파일을 쓰려고 하면 이 앱 자신이 열어둔 핸들과 충돌해서 실패한다.
- **수정**: `ImageLoadHelper.Load(path)`가 `BitmapCacheOption.OnLoad`로 로드 시점에 픽셀 데이터를 전부 메모리로 읽어들이고 `Freeze()`해서, 반환 즉시 파일 핸들을 놓도록 만들었다. 코드에서 직접 `new BitmapImage(new Uri(path))`로 이미지를 만들던 곳(`PropertiesWindow`/`ActorManagerWindow`/`OriginalImageWindow`)은 모두 이 헬퍼로 교체했고, XAML에서 `Image.Source`를 `{Binding ThumbnailPath}`로 바인딩하던 곳(메인창의 썸네일 뷰어/아이콘 보기/선택 항목 배우 썸네일, `ActorManagerWindow`의 아이콘 목록)은 암시적 문자열→`ImageSource` 변환 대신 `ThumbnailPathConverter`(내부적으로 `ImageLoadHelper.Load` 호출)를 통하도록 바꿨다.
- **검증**: 격리된 콘솔 테스트로 예전 방식(`new BitmapImage(new Uri(path))`)이 실제로 파일을 잠가 덮어쓰기가 `IOException`으로 실패하는 것과, `ImageLoadHelper.Load`로 로드한 뒤에는 같은 파일을 즉시 덮어쓸 수 있는 것을 모두 확인함.
- **컨벤션(향후 적용)**: 로컬 파일 경로에서 이미지를 표시해야 하는 새 코드는 항상 `ImageLoadHelper.Load`(코드) 또는 `ThumbnailPathConverter`(XAML 바인딩)를 사용한다. `new BitmapImage(new Uri(...))`를 캐시 옵션 없이 직접 쓰지 않는다.

**버그 2: 썸네일을 바꿔도 예전 이미지가 계속 보임 (2026-08-01 수정)**. 위 파일 잠금 문제를 고치고 나니, `ActorManagerWindow`에서 배우 썸네일을 다시 지정하면(같은 `{배우명}.jpg` 경로에 덮어씀) 파일 자체는 새 내용으로 바뀌었는데도 화면에는 예전 썸네일 이미지가 그대로 보이는 별개의 버그가 드러났다.

- **원인**: WPF는 `BitmapImage`를 같은 `UriSource`로 다시 로드할 때 디스크를 다시 읽지 않고 프로세스 내부의 이미지 캐시(URI 기준)에서 예전에 디코딩해둔 픽셀 데이터를 그대로 돌려주는 경우가 있다. 배우 썸네일처럼 매번 같은 경로(`{배우명}.jpg`)에 덮어쓰는 파일에서 이 캐시 재사용이 그대로 드러난다 (동영상 썸네일은 파일명이 자주 안 겹쳐서 상대적으로 덜 눈에 띄지만 이론적으로는 같은 문제를 겪을 수 있다).
- **수정**: `ImageLoadHelper.Load`에서 `BitmapImage.CreateOptions`에 `BitmapCreateOptions.IgnoreImageCache`를 추가로 지정해, 항상 디스크에서 다시 디코딩하도록 강제했다.
- **검증**: 격리 테스트로, 같은 경로에 서로 다른 크기(40x40 → 90x90)의 이미지를 순서대로 저장한 뒤 두 번째 `ImageLoadHelper.Load` 호출 결과가 실제로 새 크기(90x90)를 반환하는지 확인함 (수정 전이었다면 캐시된 첫 번째 이미지의 크기가 반환되었을 것).

### 썸네일 뷰어 3종

"썸네일 뷰어"는 아래 세 가지로 구분된다 — **구현 완료**.

1. **메인창 뷰어** (`MainWindow`, 관리 리스트 영역 상단 오른쪽): 320x240 미리보기만 있고, 위에 안내 텍스트나 버튼은 없다. 관리 리스트에서 선택된 항목을 따라간다(`UpdateSelectedItemDetails`에서 `DataContext` 동기화).
   - **클릭**하면 [원본창](#원본창-originalimagewindow)이 열린다.
   - **이미지 파일을 드래그 앤 드롭**하면 새 썸네일로 지정된다 (이 창에는 "썸네일 추가" 버튼이 없으므로 드래그 앤 드롭이 유일한 추가 수단이다).
2. **속성창 뷰어** (`PropertiesWindow` 맨 아래, [속성 관리](properties-management.md) 참고): 뷰어 위에 현재 썸네일 경로(없으면 "지정된 썸네일 없음") 텍스트와 "썸네일 추가" 버튼이 있고, 그 아래 320x240 뷰어가 **가운데 정렬**로 놓인다.
   - **클릭**하면 원본창이 열린다.
   - **"썸네일 추가" 버튼** 또는 **드래그 앤 드롭**으로 새 썸네일을 지정할 수 있다.
3. **원본창** (`OriginalImageWindow`): 위 두 뷰어 중 하나를 클릭하면 뜨는 별도 창. `ThumbnailOriginalPath`의 원본(리사이즈 전) 이미지를 크게 보여준다. **아무 곳이나 클릭하면 닫힌다** (`Window_MouseLeftButtonUp` → `Close()`). 항목에 원본 이미지가 없으면(`ThumbnailOriginalPath`가 null이거나 파일이 없으면) 아무 반응도 하지 않는다.

(아이콘 보기 카드의 작은 썸네일 영역은 위 "뷰어"와 별개의 UI였고, 클릭 시 파일 선택 대화상자로 썸네일을 지정하는 기능(`ThumbnailArea_Click`)이 있었으나 2026-08-12 제거되었다 — 이제 이 영역은 클릭에 반응하지 않는다.)

### 썸네일 생성 방식 (`ThumbnailHelper.CreateThumbnail`)

이미지를 새로 지정하면(버튼/드래그 앤 드롭 어느 경로든) 동영상 파일과 같은 폴더에 **원본 파일**과 **썸네일용 파일**을 각각 저장한다:

1. **원본**: 리사이즈하지 않고 그대로 `"{동영상 파일명(확장자 제외)}.original{원본 확장자}"`로 복사한다 (예: `movie.mp4` + `photo.png` → `movie.original.png`). 소스가 이미 이 경로 자신이면(원본을 다시 소스로 드래그하는 경우 등) 복사를 건너뛴다.
2. **썸네일**: 원본을 **320x240 이내로, 가로세로 비율을 유지하며** 리사이즈해 `"{동영상 파일명}.thumbnail.jpg"`로 저장한다. `min(320/원본가로, 240/원본세로)` 배율을 가로/세로에 동일하게 적용하므로(`TransformedBitmap` + `ScaleTransform`), 결과가 정확히 320x240일 필요는 없다 — 예: 2:1 비율 원본은 320x160, 1:2 비율 원본은 120x240이 된다. 뷰어는 어차피 `Stretch="Uniform"`이라 다양한 크기의 썸네일도 320x240 박스 안에 그대로 맞춰 보인다.
3. **소스 정리**: 원본/썸네일 두 파일로 복사가 끝나면, 소스로 쓰인 원래 파일(`sourceImagePath`)은 삭제한다 (`TryDeleteSource`, 실패해도 전체 동작은 성공으로 취급 — 삭제는 부가 정리일 뿐 핵심 결과가 아니므로). 사용자가 직접 고른 파일이 지워지는 것뿐 아니라, 아래 "인터넷 이미지 드래그 앤 드롭"에서 만들어지는 임시 다운로드 파일도 이 과정으로 자동 정리된다.
4. `ManagedVideoItem.ThumbnailPath`(썸네일용)와 `ThumbnailOriginalPath`(원본)를 각각 갱신한다.
5. 같은 항목에 썸네일을 다시 지정하면 같은 파일명이므로 두 파일 모두 자동으로 덮어써진다.

`ThumbnailHelper.CreateThumbnail`은 `(string ThumbnailPath, string OriginalPath)` 두 경로를 담은 `Result`를 반환하며, 메인창/속성창의 `ApplyThumbnail`이 동일하게 이 결과를 항목에 반영한다. "파일 없이 추가"된 항목(실제 폴더가 없음)을 위한 `CreatePlaceholderThumbnail`(고정 폴더 `E:\happy\thumbnail`에 저장)은 [속성 관리](properties-management.md)의 "새 항목 모드" 참고. 배우 썸네일용 `CreateActorThumbnail`(100x100, 원본 미보관)은 [배우 관리](actor-management.md) 참고.

### 인터넷 이미지 드래그 앤 드롭 (`DragDropImageHelper`)

브라우저에서 보고 있는 이미지를 직접 드래그해 썸네일 뷰어에 놓아도 동작한다 — **구현 완료**. 로컬 파일 탐색기에서 파일을 끄는 경우와 달리, 브라우저 드래그는 디스크 파일이 아니라 URL/HTML/data: URI/렌더링된 비트맵 형태로 전달되므로 별도 처리가 필요하다. `DragDropImageHelper.TryGetImagePath`가 아래 순서로 시도한다:

1. **로컬 파일** (`DataFormats.FileDrop`): 기존과 동일하게 그 경로를 그대로 사용.
2. **후보 URL 수집** (`ExtractImageCandidates`): 다음을 모두 모은다 —
   - `text/uri-list` / `DataFormats.Text` / `UniformResourceLocatorW` / `UniformResourceLocator`에서 `http(s)://`로 시작하는 줄
   - `text/html` 안의 `<img>` 태그의 `src`/`data-src`/`data-iurl` 속성값과 `srcset` 속성의 첫 URL (Google 이미지 검색 등 지연 로딩 페이지는 `src`가 아니라 `data-src`/`data-iurl`에 실제 이미지가 있는 경우가 흔함)
   - 수집된 후보 중 **http(s) URL을 data: URI보다 먼저** 시도한다 (data: URI는 대개 저해상도 미리보기이므로).
3. **후보 해석**: `http(s)://`면 `HttpClient`로 **실제로 다운로드**하고(확장자는 응답의 `Content-Type` 우선 → URL 확장자 → `.jpg`), `data:image/...;base64,...`면 **네트워크 접근 없이 base64를 바로 디코딩**해 파일로 저장한다.
4. **비트맵** (`DataFormats.Bitmap`): 위에서 아무 것도 못 찾았을 때의 최후 수단으로, 드래그 데이터에 포함된 렌더링된 비트맵을 PNG로 인코딩해 저장한다 (원본보다 화질이 낮을 수 있음).

**형식 파싱 안정성**: `IDataObject.GetData(format)`는 형식에 따라 `string`이 아니라 `MemoryStream`(원시 바이트)으로 반환되는 경우가 있어(브라우저에서 이 경우가 흔하다), `TryGetString`이 두 경우를 모두 처리한다(`...W`로 끝나는 형식은 UTF-16, 나머지는 UTF-8로 해석). `TryGetImagePath` 전체가 하나의 try/catch로 감싸여 있어 어떤 형식 처리 중 예외가 나도(OLE 드래그 앤 드롭 콜백 안이므로) 앱에 영향 없이 조용히 실패로 처리된다.

이렇게 얻은 임시 파일 경로를 기존 `ApplyThumbnail`/`ThumbnailHelper.CreateThumbnail` 흐름에 그대로 넘기므로, 위 "소스 정리" 단계에서 임시 파일이 자동으로 삭제된다. 다운로드는 동기적으로(`GetAwaiter().GetResult()`) 수행되어 완료될 때까지 UI가 잠깐 멈출 수 있다 — 진행 표시줄은 없다.

**실패 시 진단 메시지 — 구현 완료.** 이미지를 하나도 찾지 못하면(모든 경로 실패) 조용히 넘어가지 않고, 드롭된 데이터에 실제로 어떤 형식이 있었는지(`IDataObject.GetFormats()`)를 보여주는 메시지 상자를 띄운다. 브라우저/사이트마다 제공하는 형식이 달라 코드만으로 모든 경우를 예측하기 어렵기 때문에, 실패 시 이 형식 목록을 근거로 `ExtractImageCandidates`에 새 형식/속성을 추가하는 식으로 대응한다.

다음은 아직 정책이 정해지지 않은 예외 상황이며, 구현 시 결정이 필요하다:

- 지정된 썸네일/원본 이미지 파일이 삭제된 경우의 처리 (예: 다음 로딩 시 자동으로 기본 아이콘으로 되돌릴지, 깨진 이미지로 둘지)
- 원본 동영상 파일이 [동영상 파일 관리](video-file-management.md)의 제거(Remove) 메커니즘으로 이동/삭제 처리될 때, 같은 폴더의 `.thumbnail.jpg`/`.original.*` 파일은 함께 정리하지 않는다 (고아 파일로 남을 수 있음)
- 브라우저가 WebP 등 WPF의 기본 `BitmapDecoder`(WIC)가 지원하지 않는 형식의 이미지를 제공하는 경우, 다운로드/저장은 되어도 리사이즈 단계에서 디코딩 오류가 날 수 있다 (별도 코덱 없이는 미지원)

향후 확장 아이디어:

- **자동 썸네일 생성**: ffmpeg 등으로 동영상 첫 프레임을 캡처해 자동으로 썸네일을 지정하는 옵션. [동영상 파일 관리](video-file-management.md)의 데이터 모델에 있는 `AutoThumbnailGenerated`(bool, **아직 미구현 — 현재 코드에는 없는 필드**)는 도입 시 자동 생성된 썸네일인지(사용자가 직접 지정한 것과 구분) 여부를 기록하기 위해 미리 이름이 정해져 있는 예정 필드일 뿐이다.

### 셸 아이콘 헬퍼 (`WindowsIconHelper`)

Windows 셸에서 특정 확장자에 연결된 표준 아이콘을 가져오는 헬퍼(`SHGetFileInfo` P/Invoke). `PngFileIcon`은 `.png` 파일의 표준 아이콘을 조회해 `ImageSource`로 변환하고 캐시한다(`SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES`로 실제 파일 없이 확장자만으로 조회 → `Imaging.CreateBitmapSourceFromHIcon` → `Freeze()`로 캐시, 핸들은 `DestroyIcon`으로 즉시 해제해 누수 방지). [동영상 파일 관리](video-file-management.md)의 관리 리스트 "썸네일 유무 컬럼" 아이콘에 사용된다.

## 설정 관리

프로그램 종료 후에도 다음 상태를 복원한다 — **구현 완료**.

- 마지막 보기 모드 (리스트 / 아이콘)
- 마지막 정렬 상태 (컬럼 + 오름/내림차순)
- 마지막 필터 상태 (파일명 검색어, 폴더명 검색어, 선택된 시리즈(`AppSettings.SeriesFilter`, 2026-08-09 추가), 선택된 태그, 선택된 배우, "제거된 항목도 표시" 체크 여부)
- 마지막 열린 폴더 (폴더가 더 이상 존재하지 않으면 무시하고 빈 폴더 목록으로 시작)
- 마지막으로 표시하던 컬럼 목록 (`AppSettings.VisibleColumns`, 예: `["Size", "PlayCount", "Tags"]`) — 파일명은 항상 표시되므로 이 목록에 포함되지 않는다. 구버전 `settings.json`(이 필드가 없는 파일)을 불러오면 기본값(크기/재생횟수/태그)이 적용된다.
- **리스트 보기 컬럼 너비** (`AppSettings.ColumnWidths`, 2026-08-02 추가, **구현 완료**) — 파일명 컬럼을 포함해 모든 컬럼(`FileName`/`Thumbnail`/`Size`/`PlayCount`/`Tags`/`Actor`/`Memo`/`Folder`, `VisibleColumns`와 같은 컬럼 키)의 너비를 `Dictionary<string, double>`로 저장한다. **숨겨진 컬럼도 마지막으로 가졌던 너비를 함께 저장**해뒀다가 나중에 다시 표시할 때 그 너비로 복원한다. 구버전 `settings.json`(이 필드가 없거나 특정 키가 없는 파일)을 불러오면 해당 컬럼은 XAML/`BuildManagedColumns`에 정의된 기본 너비가 그대로 적용된다.
- **마지막 선택 항목** (`AppSettings.SelectedItemPath`, 2026-08-02 추가, **구현 완료**) — 종료 시점에 관리 리스트에서 선택돼 있던 항목의 `FullPath`를 저장해두고, 다음 실행 시 그 경로와 일치하는 항목을 찾아 선택하고 화면에 스크롤해서 보여준다(리스트 보기/아이콘 보기 모두 지원). 파일명이 아니라 전체 경로로 매칭해 같은 이름의 다른 파일과 혼동되지 않게 한다. 항목을 찾지 못하면(파일이 삭제됐거나 현재 필터에 걸러짐) 조용히 무시한다. 선택(`Selector.SelectedItem`)은 `ApplySettings`에서 바로 적용하지만, **스크롤(`ScrollIntoView`)은 창이 처음 렌더링된 뒤인 `Window.Loaded`(`MainWindow_Loaded`)에서 수행**한다 — 생성자 시점에는 아직 레이아웃이 없어 `ScrollIntoView`가 동작하지 않기 때문이다.
- **주요 창의 마지막 화면 위치** (`AppSettings.WindowPositions`, 2026-08-04 추가, **구현 완료**) — 아래 "창 관리 정책"의 규칙 4 참고. `WindowPositionMemory`가 메모리에서 관리하며, 이 설정 필드는 그 스냅샷을 불러오고(`ApplySettings`) 저장하는(`SaveSettings`) 용도다.
- **`MainWindow` 자신의 마지막 크기/위치** (`AppSettings.MainWindowWidth/Height/Left/Top`, 2026-08-07 추가, **구현 완료**) — 주요 창 4개와 별개로 메인 창 자신의 크기/위치도 기억한다. `ApplySettings`가 시작 시 네 값이 모두 있고 `WindowPositionMemory.IsOnScreen`으로 화면 안에 있을 만하면 `WindowStartupLocation`을 `Manual`로 바꾸고 적용하며, 값이 없거나(구버전 설정 파일) 화면 밖으로 판정되면 XAML 기본값(`Height="1200" Width="1720"`, `CenterScreen`)을 그대로 쓴다. `SaveSettings`가 저장할 때 **창이 최대화 상태이면 `RestoreBounds`(최대화 이전의 "정상" 크기/위치)를 저장한다** — 그렇지 않으면 다음 실행 시 항상 전체 화면 크기로 열리게 된다.
- **아이콘 보기 크기/표시 정보 선택** (`AppSettings.IconSizePreset`, `IconShowSize`/`IconShowPlayCount`/`IconShowTags`/`IconShowSeries`, 2026-08-12 추가, **구현 완료**) — [동영상 파일 관리](video-file-management.md)의 "아이콘 크기 선택"/"카드에 표시할 정보 선택" 참고. `IconSizePreset`은 `IconSize` enum 이름을 문자열로 저장하고(`IconSizeSettings.Current.Preset`), 나머지 넷은 `IconCardFieldsSettings.Current`의 bool 속성을 그대로 저장한다. `ApplySettings`가 시작 시 `IconSizeSettings.Current.Apply(...)`/`IconCardFieldsSettings.Current`에 반영하는 동시에 `IconSizeComboBox`/체크박스 4개의 화면 상태도 맞춘다(체크박스는 `Click` 이벤트 기반 로직이라 `IsChecked`만 설정해서는 실제 상태가 바뀌지 않으므로, 싱글턴 속성도 함께 직접 설정한다). 구버전 `settings.json`(이 필드들이 없는 파일)을 불러오면 `IconSizePreset`은 "Normal"로, `IconShowPlayCount`/`IconShowTags`는 `true`(C# 속성 기본값)로, `IconShowSize`/`IconShowSeries`는 `false`로 — 즉 이 기능이 추가되기 전의 카드 모습과 동일하게 적용된다.

저장 위치는 `%LOCALAPPDATA%\VideoVault\settings.json`이며(`AppPaths.SettingsPath`), `library.json`/`tags.json`과 동일하게 변경 시마다 500ms debounce 후 자동 저장된다 (`AppSettings` 모델 + `SettingsRepository`). 시작 시 자동 로딩 로직(`LoadInitialData`) 안에서 함께 불러와 적용하며, 이 복원 과정 자체는 다시 저장을 트리거하지 않는다. **예외**: 컬럼 너비·마지막 선택 항목·주요 창 위치·메인 창 자신의 크기/위치·아이콘 보기 크기/표시 정보 선택은 헤더 드래그/일반 클릭 선택/창 이동·크기 조절/아이콘 크기 콤보박스·체크박스 토글 시점마다 자동 저장을 예약하기엔 너무 잦아서(변경 이벤트도 마땅치 않은 경우가 많음) 다른 설정처럼 즉시 debounce가 걸리지 않는다 — 대신 **`MainWindow.Closing`(창을 닫는 시점)에 한 번, 그 시점의 최종 상태를 즉시 저장**해 "종료 전 상태 유지"를 보장한다(`MainWindow_Closing` → `SaveAllNow()`, `Ctrl+S`의 `SaveNow_Click`과 저장 로직을 공유). `SaveSettings()`가 매번 `IconSizeSettings.Current`/`IconCardFieldsSettings.Current`의 "지금 값"을 그대로 읽어 담으므로, 별도의 변경 감지나 즉시 debounce 예약 없이도 항상 최신 상태가 저장된다.

**즉시 저장 (Ctrl+S)**: 위 debounce를 기다리지 않고 관리 리스트(`library.json`)/태그(`tags.json`)/배우(`actors.json`)/시리즈(`series.json`)/설정(`settings.json`)을 즉시 저장한다 — **구현 완료**. `MainWindow`의 전역 `PreviewKeyDown`에서 처리하며, 파일 메뉴의 "저장"이 동일한 `SaveAllNow()`(`SaveNow_Click`이 호출)를 공유하고, 창을 닫을 때도 같은 메서드가 호출된다(관리 리스트 툴바의 "저장 (Ctrl+S)" 버튼은 2026-08-09 삭제되었지만 이 메뉴/단축키로 기능은 그대로 유지된다). 평소에는 어차피 500ms 후 자동 저장되므로, Ctrl+S는 "지금 바로 디스크에 반영됐다는 확신"을 주는 용도에 가깝다.

## 오류 처리

아래 상황들에 대해 일관된 정책을 따른다: **예외를 catch하여 `MessageBox`로 사용자에게 알리고, 프로그램은 종료하지 않고 가능한 범위에서 계속 동작 가능한 상태를 유지한다.** (현재 각 Repository 호출부에서 이미 이 패턴을 따르고 있다.)

- 재생하려는 파일이 존재하지 않는 경우 → 재생 실패 메시지, 관리 리스트 항목은 유지 ([동영상 파일 관리](video-file-management.md))
- 파일/폴더 접근 권한이 없는 경우 → 오류 메시지
- **시작 시 로딩 실패 처리(2026-08-02 개선, 구현 완료)**: `MainWindow.LoadInitialData()`가 관리 리스트/레거시 제거 항목 병합/태그/배우/설정/파일 존재 여부 확인 6단계를 각각 독립된 `LoadStep(설명, 동작)` 헬퍼로 감싼다 — 한 단계가 손상된 파일 등으로 실패해도 그 단계만 오류 메시지를 띄우고 건너뛴 뒤 나머지 단계는 계속 진행한다(예: `tags.json`이 손상돼도 `actors.json`/`settings.json` 로딩과 파일 존재 여부 확인은 정상 진행됨). 예전에는 전체가 하나의 try/catch였어서 첫 실패 지점 이후 모든 로딩이 통째로 중단됐다. `library.json`/`tags.json`처럼 특정 파일이 손상됐을 때 "빈 목록으로 시작"할지 "로딩을 막고 사용자가 직접 조치"하게 할지는 여전히 추후 결정 필요 — 지금은 오류 메시지를 띄우고 그 단계만 빈 상태로 건너뛴다.
- 썸네일 이미지 손상/깨짐 → 위 "썸네일 관리" 절 참고

## 성능

대용량 데이터에서도 반응성을 유지하기 위한 방향 (일부는 이미 구현, 일부는 향후 작업):

- 관리 리스트는 `ObservableCollection` + `CollectionViewSource`(정렬/필터)로 구현되어 있음 — **구현 완료**
- 리스트 보기의 `ListView`는 WPF 기본 가상화(`VirtualizingStackPanel`)를 사용함 — **구현 완료** (기본 동작)
- **아이콘 보기 UI 가상화**(2026-08-16 추가, **구현 완료**) — 아이콘 보기(`ManagedIconView`)가 기존 `WrapPanel`(항목 수만큼 카드를 전부 즉시 생성) 대신 직접 구현한 `VirtualizingWrapPanel.cs`(`VirtualizingPanel` + `IScrollInfo` 구현)를 사용한다. 화면에 보이는 행 범위의 카드만 `ItemContainerGenerator`로 실제 생성하고, 스크롤을 벗어난 카드는 컨테이너를 제거해 재사용한다. 모든 카드가 `IconSizeSettings.Current.CardWidth/CardHeight`로 동일 크기라는 전제 덕분에 가변 크기 아이템을 다루는 범용 구현보다 단순하게 만들 수 있었다. `ListBox`에는 `ScrollViewer.CanContentScroll="True"` + `VirtualizingPanel.IsVirtualizing="True"`를 지정해 스크롤을 패널의 `IScrollInfo`에 위임한다. 실 라이브러리(2913개 항목)로 전체 스크롤(Ctrl+End 포함)/키보드 탐색/아이콘 크기 프리셋 변경/필터링/카드 우클릭 컨텍스트 메뉴/앱 시작 시 아이콘 보기로 바로 진입하는 경우까지 검증했다. **구현 시 주의점**: `Panel.ItemContainerGenerator`(패널이 상속받는 protected 속성)는 `IndexFromContainer` 등을 제공하지 않는 `IItemContainerGenerator` 인터페이스 타입이라, 그 메서드들이 필요하면 `ItemsControl.GetItemsOwner(panel).ItemContainerGenerator`(구체 타입)를 사용해야 한다. 또한 `ManagedIconView`가 처음에 `Visibility="Collapsed"`로 시작해 최초 `MeasureOverride` 시점에 제너레이터가 아직 준비되지 않을 수 있어, 준비 전이면 빈 크기를 반환하고 `Dispatcher.BeginInvoke(..., DispatcherPriority.Loaded)`로 재측정을 예약해야 한다(그냥 널 체크만 하면 빈 화면으로 멈추는 버그가 생김). **스크롤 단위**: `LineUp`/`LineDown`은 한 행(`RowHeight`)씩, `PageUp`/`PageDown`은 뷰포트 높이만큼 움직인다. `MouseWheelUp`/`MouseWheelDown`(마우스 휠 한 칸)은 원래 `RowHeight * 3`(세 줄씩, "페이지 단위처럼 느껴진다"는 피드백을 받음)이었으나 2026-08-16에 `LineUp`/`LineDown`과 동일하게 한 줄씩으로 변경했다 — 어차피 가상화 덕분에 매 스크롤마다 하는 일은 "현재 보이는 행 범위만 다시 계산"뿐이라 한 줄씩으로 줄여도 계산 비용은 늘지 않는다(같은 총 스크롤 거리를 가는 데 필요한 호출 횟수만 늘 뿐). **검증 관련 참고**: 이 PC의 자동화 환경에서 `mouse_event`/`SendMessage`로 `WM_MOUSEWHEEL`을 흉내 내면 델타가 예상과 다르게 증폭되거나(스크롤이 한 번에 수백 개 항목을 건너뜀) 아예 무시되는 등 신뢰할 수 없었다(이 세션에서 이미 한 번 겪은 문제와 동일) — 이 변경은 키보드 탐색(`LineUp`/`LineDown`, 위에서 검증됨)과 완전히 동일한 수식을 쓴다는 점으로 정확성을 확인했고, 휠 입력 자체는 실제 마우스로 다시 한번 확인해보는 것을 권장한다.
- **아이콘 보기 썸네일 디코딩 해상도 제한**(2026-08-14 추가, **구현 완료**) — `ImageLoadHelper.Load`가 선택적 `decodePixelWidth` 매개변수를 받아 `BitmapImage.DecodePixelWidth`로 지정된 너비까지만 디코딩한다(세로는 원본 비율대로 자동 계산됨, 지정 안 하면 예전처럼 원본 해상도 그대로). `ThumbnailPathConverter`가 XAML의 `ConverterParameter`를 정수로 파싱해 그대로 전달하며, 아이콘 보기 카드의 `Image`(`MainWindow.xaml`)에만 `ConverterParameter=240`을 지정했다 — 240은 아이콘 크기 프리셋 중 가장 큰 "아주 큰 아이콘"의 썸네일 표시 너비(240px, [동영상 파일 관리](video-file-management.md)의 "아이콘 크기 선택" 참고)에 맞춘 값으로, 이보다 작은 프리셋에서는 디코딩된 비트맵을 그냥 축소해서 보여준다. **다른 뷰어(메인창/속성창 320x240 뷰어, 배우 관리 창 100x100 뷰어)에는 적용하지 않았다** — 그 뷰어들이 참조하는 파일(`ThumbnailPath`는 최대 320x240, 배우 썸네일은 최대 100x100로 이미 저장 시점에 리사이즈됨)이 표시 크기와 비슷하거나 더 작아서 디코딩 폭을 제한해도 이득이 없기 때문이다. **한계**: 이것만으로는 아이콘 보기가 느린 근본 원인(위 항목의 미가상화)을 해결하지 못한다 — 항목 수가 많을수록 화면 밖 카드까지 전부 즉시 렌더링되는 문제는 그대로이며, 이 변경은 그 렌더링 하나하나의 디코딩 비용만 줄인 것이다.
- 폴더 스캔은 현재 동기적으로 실행되어 대형 폴더에서 UI가 잠시 멈출 수 있음 — **향후 비동기(Task)로 전환 필요**, 전환 시 UI 스레드를 블로킹하지 않도록 한다 ([동영상 파일 관리](video-file-management.md))
- **관리 리스트 항목 개수 표시가 모든 속성 변경마다 전체 스캔하던 문제**(2026-08-16 수정, **구현 완료**) — `MainWindow.ManagedItem_PropertyChanged`(항목 하나의 속성이 바뀔 때마다 실행되는 공용 훅)가 어떤 속성이 바뀌었든 상관없이 매번 `UpdateManagedCountDisplay()`(전체 항목을 3번 훑는 `Count()`/`Count(HasThumbnail)`/`Count(!IsValid)`)를 호출하고 있었다. 라이브러리가 작을 때는 티가 안 났지만, 항목 수가 늘어날수록(현재 2913개) 재생횟수 증가·이름 변경·태그 편집 등 **모든** 속성 변경마다 불필요한 전체 스캔이 반복돼 체감 속도가 떨어졌다 — 특히 "배우 동기화"/"시리즈 동기화"([배우 관리](actor-management.md)/[시리즈 관리](series-management.md) 참고)처럼 여러 항목을 한 번에 바꾸는 버튼에서 항목 수만큼 반복되어 크게 느려졌다. 실제 개수에 영향을 주는 속성(`HasThumbnail`/`IsValid`)이 바뀔 때만 재계산하도록 `e.PropertyName` 조건을 추가해 해결했다. 실제 항목(hunbl-112.mp4)의 제거/복구로 개수 표시가 여전히 정확히 갱신되는 것과, 무관한 속성 변경 시 더 이상 불필요하게 재계산하지 않는 것을 확인했다. **관련해서 향후 더 볼 만한 지점**: "배우 동기화"/"시리즈 동기화"가 여러 항목을 바꿀 때 항목마다 `_managedView.Refresh()`(Tags/Actors 변경 시)가 반복 호출되는 것은 아직 그대로다 — 끝난 뒤 한 번만 갱신하도록 묶는 최적화는 미착수.

## 확장성

향후 구현 교체를 쉽게 하기 위해, 아래 인터페이스 도입을 고려한다 (아직 미구현 — 현재는 구체 클래스만 존재):

```
IThumbnailProvider
IVideoScanner
IManagedListRepository
ITagRepository
```

## 창 관리 정책

주요 창(`FolderListWindow`/`PropertiesWindow`/`ActorManagerWindow`/`TagManagerWindow`/`SeriesManagerWindow` — `MainWindow`에서 직접 열거나 이들끼리 서로 넘나드는 창들)에 적용되는 규칙이다(2026-08-04 추가, **구현 완료**; `SeriesManagerWindow`는 2026-08-09 추가). 이 다섯 외의 작은 입력 대화상자(`RenameWindow`/`ActorInfoWindow`/`AddCreditWindow`/`OriginalImageWindow`, 그리고 `OpenFileDialog`/`SaveFileDialog`/`OpenFolderDialog` 같은 시스템 대화상자)는 대상이 아니며, 지금처럼 여는 쪽 창을 `Owner`로 하는 진짜 모달(`ShowDialog()`)로 남아있다 — 어차피 한 창에서만 짧게 값을 입력받고 바로 돌아오는 용도라 이 규칙이 필요 없다.

- **규칙 1: 모든(주요) 창은 독립적인 상태를 갖는다.** 여러 개가 동시에 열려 있어도 서로의 내부 UI 상태(선택 항목, 스크롤 위치, 입력 중인 값)에 영향을 주지 않는다.
- **규칙 2: 모든 창은 데이터가 바뀌면 동시에 갱신된다.** 어느 창에서 관리 리스트/태그/배우 데이터를 바꾸든, 다른 창(과 메인 창의 목록·상세정보 패널)이 닫았다 열지 않아도 즉시 반영된다.
- **규칙 3: 창 종류별로 동시에 최대 1개만 열린다.** 이미 같은 종류의 창이 열려 있는 상태에서 그 종류를 다시 열려고 하면, 기존 창을 먼저 닫고 새 창을 연다. (서로 다른 종류끼리는 동시에 열려 있을 수 있다 — 예: 배우 관리 창과 속성 창을 동시에 띄워둘 수 있다.)
- **규칙 4: 마지막으로 열려 있던 화면 위치를 기억한다**(2026-08-04 추가, **구현 완료**). 창을 닫을 때의 위치(Left/Top)를 창 종류별로 기억해뒀다가, 다음에 그 종류의 창을 열면 같은 위치에 연다. 프로그램을 재시작해도 유지된다(설정 파일에 저장). **`ActorManagerWindow`만 예외적으로 크기(Width/Height)까지 함께 기억한다**(2026-08-27 추가, 사용자 요청) — 이 규칙(공용 `WindowPositionMemory`)은 5개 주요 창 전부 위치만 다루므로, 크기는 `ActorManagerWindow`가 독자적으로(`RememberedWindowWidth`/`Height` 정적 프로퍼티, [배우 관리](actor-management.md) 참고) 저장·복원한다. 다른 4개 주요 창은 여전히 XAML에 정의된 기본 크기로만 열린다.
- **규칙 5: 관리 리스트의 선택이 바뀌면 열려 있는 속성 창도 그 항목을 따라간다**(2026-08-04 추가, **구현 완료**). 속성 창이 모덜리스로 바뀌면서 이제 그 창을 열어둔 채로 관리 리스트의 다른 행을 클릭할 수 있게 됐는데, 예전에는(모달이던 시절 그대로) 클릭해도 속성 창이 처음 열었던 항목을 계속 보여줬다 — 창을 닫았다가 다시 열어야만 새로 클릭한 항목을 볼 수 있었다. 지금은 클릭 즉시 같은 창이 그 항목으로 갱신된다(규칙 2 "동시 갱신"의 연장선).

**구현 방식**:
- 이 넷은 전부 `ShowDialog()`(모달)가 아니라 `Show()`(모덜리스)로 연다(2026-08-04 변경 — 예전에는 전부 모달이었다). 그래야 서로 다른 종류끼리 동시에 열려 있으면서 각자 독립적으로 상호작용할 수 있다(규칙 3의 "다른 종류는 동시에 가능" 부분).
- **`SingleInstanceWindow<T>.cs`**: 창 종류(T)별로 "현재 열려 있는 인스턴스"를 추적하는 제네릭 static 헬퍼. `SingleInstanceWindow<T>.Show(window)`를 호출하면, 같은 T의 기존 인스턴스가 있으면 먼저 닫고(`Close()`) 새 인스턴스를 등록한 뒤 보여준다. 제네릭 static 클래스라 `T`마다(예: `SingleInstanceWindow<PropertiesWindow>`와 `SingleInstanceWindow<ActorManagerWindow>`) 독립된 저장소를 가지므로, 창 클래스마다 같은 static 필드를 반복해서 둘 필요가 없다(규칙 3 구현).
- **`Owner`는 항상 `MainWindow`로 고정한다** — `PropertiesWindow`↔`ActorManagerWindow` 서로 넘나드는 코드(`PropertiesWindow.OpenActorManagerFor`, `ActorManagerWindow.CreditChip_MouseLeftButtonUp`)에서 `Owner = this`(여는 쪽 창 자신)로 하면, WPF가 창을 닫을 때 그 창이 소유한(`OwnedWindows`) 창까지 자동으로 함께 닫아버리는 기본 동작 때문에, 나중에 그 "여는 쪽 창"이 종류별 단일 인스턴스 규칙으로 다른 창에 밀려 닫힐 때 방금 연 창까지 덩달아 닫혀버려 규칙 1(독립적 상태)이 깨진다. `Owner = Application.Current.MainWindow`로 고정해 이 연쇄 닫힘을 피한다.
- **`PropertiesWindow`의 확인/취소/완전삭제**: 모덜리스 전환으로 `Window.DialogResult`(모달 전용 API)를 더 이상 쓸 수 없어, `_explicitCloseHandled` bool 필드로 대체했다 — "확인"(`Ok_Click`)/"취소"(`Cancel_Click`, 예전에는 `Button.IsCancel="True"`였으나 이것도 모달 전용이라 명시적 핸들러로 교체)/"완전 삭제"(`DeleteButton_Click`) 버튼은 각자 처리를 마치자마자 이 플래그를 세우고 `Close()`를 호출하며, `Window_Closing`은 이 플래그가 없을 때만(제목 표시줄 X, Alt+F4 등으로 버튼을 거치지 않고 닫힌 경우) "확인"과 동일하게 커밋한다. Esc 키로 닫는 동작(`IsCancel`이 대신 해주던 것)은 `Window_PreviewKeyDown`에서 직접 처리한다.
- **`MainWindow`의 각 "창 열기" 핸들러**(`OpenFolderList_Click`/`ManageTags_Click`/`ManageActors_Click`/`OpenPropertiesWindow`)는 예전에 `ShowDialog()` 호출이 반환된 직후 동기적으로 하던 후처리(`_managedView.Refresh()`, `LastFolder`/`PermanentlyDeleted` 반영 등)를 전부 그 창의 `Closed` 이벤트 핸들러로 옮겼다 — `Show()`는 블로킹하지 않으므로 더 이상 "반환된 직후"라는 시점이 없기 때문이다.
- **규칙 4(위치 기억)의 실제 구현**: `WindowPositionMemory.cs`가 창 종류 이름(`typeof(T).Name`)을 키로 위치를 메모리에 들고 있고, `SingleInstanceWindow<T>.Show(window)`가 창을 열기 직전에 기억된 위치가 있으면(그리고 `SystemParameters.VirtualScreenLeft/Top/Width/Height` 기준으로 지금도 화면 안에 있을 만하면 — 모니터 구성이 바뀌어 화면 밖 좌표가 남는 경우를 걸러내기 위함) `WindowStartupLocation`을 `Manual`로 바꾸고 `Left`/`Top`을 적용하며, 창이 닫힐 때 그 시점의 위치를 다시 기억해둔다. 기억된 위치가 없으면(처음 열거나 화면 밖으로 판정된 경우) XAML에 정의된 기본 시작 위치(`CenterOwner`)를 그대로 쓴다. `MainWindow`는 시작 시(`ApplySettings`) `AppSettings.WindowPositions`(키: 창 클래스 이름, 값: `[Left, Top]`)를 `WindowPositionMemory.LoadFrom`으로 불러오고, 설정 저장 시(`SaveSettings`) `WindowPositionMemory.ToDictionary()`로 다시 채워 넣는다 — 컬럼 너비/마지막 선택 항목과 같은 카테고리라 값이 바뀔 때마다 즉시 debounce가 걸리지는 않고, `Ctrl+S`/창 닫기 시점에 그때까지 기억된 값이 저장된다. **적용 범위는 "주요 창" 4개뿐이다** — `MainWindow` 자신의 위치·크기는 이 `WindowPositionMemory`(창 종류별 딕셔너리) 대상이 아니다. 대신 `MainWindow`는 `AppSettings.MainWindowWidth/Height/Left/Top` 4개 필드로 **자기 자신의** 크기/위치를 별도로 저장한다(2026-08-07 추가) — 위 "설정 관리" 참고. `WindowPositionMemory.IsOnScreen(left, top)`은 이 두 메커니즘이 공유하는 화면 안쪽 판정 로직이라 `public`으로 노출되어 있다.
- **규칙 5(선택 항목 따라가기)의 실제 구현**: `PropertiesWindow.SwitchToItem(newItem)`이 창을 새로 만들지 않고 지금 열려 있는 창 안의 `_item`(그리고 파일 정보/태그 체크박스/배우 콤보박스·칩/썸네일/스크롤 위치)만 새 항목 기준으로 다시 그린다. `MainWindow.ManagedSelection_Changed`가 선택이 바뀔 때마다 `SingleInstanceWindow<PropertiesWindow>.Current?.SwitchToItem(item)`을 호출한다(`SingleInstanceWindow<T>.Current`는 이 기능을 위해 추가한 정적 프로퍼티 — "지금 열려 있는 이 종류의 창"을 다른 코드가 찾아 상호작용할 수 있게 해준다). 전환 전에 이전 항목의 커밋 대기 중인 변경사항(태그/배우 선택 등)을 먼저 커밋한다(`TryCommitFormFields`) — 조용히 버리지 않기 위함이다. **주의**: 이 때문에 속성 창이 지금 보여주는 항목은 창을 처음 열 때 넘긴 항목과 다를 수 있으므로, "완전 삭제" 시 관리 리스트에서 지울 대상은 호출자가 붙잡고 있던 원래 변수가 아니라 `PropertiesWindow.CurrentItem`(지금 보여주는 항목을 노출하는 프로퍼티)이어야 한다 — `MainWindow.OpenPropertiesWindow`/`ActorManagerWindow.CreditChip_MouseLeftButtonUp`의 `Closed` 핸들러 모두 이 프로퍼티를 사용한다.
- **규칙 2(동시 갱신)의 실제 구현**:
  - `MainWindow._managedView`(관리 리스트의 `ICollectionView`)에 **라이브 필터링/정렬**(`ICollectionViewLiveShaping.IsLiveFiltering`/`IsLiveSorting` + `LiveFilteringProperties`/`LiveSortingProperties`, `EnableLiveShaping()`)을 켜뒀다. 필터/정렬에 쓰이는 속성(`IsValid`/`FileName`/`Tags`/`Actors`, 정렬 가능한 모든 컬럼 속성)을 감시 대상으로 등록해두면, 그 속성이 바뀔 때마다(어느 창에서 바꿨든) WPF가 자동으로 그 항목의 필터 통과 여부·정렬 위치를 재평가한다 — `_managedView.Refresh()`를 수동으로 호출할 필요가 없다(다만 창을 닫을 때는 여전히 안전망으로 `Refresh()`도 함께 호출한다).
  - `MainWindow.ManagedItem_PropertyChanged`가 **바뀐 항목이 현재 선택된 항목이면** `UpdateSelectedItemDetails()`를 호출한다 — 하단 "선택된 항목 상세 정보" 패널의 배우 썸네일 목록(`SelectedItemActorsPanel.ItemsSource`)은 `Actors`에 직접 바인딩된 게 아니라 매번 계산해서 채우는 값이라, 이 훅이 없으면 다른 창에서 그 항목의 배우/태그/메모 등을 바꿔도 메인 창 하단 패널이 그 즉시 반영되지 않는다.
  - 이 두 가지 덕분에, 예를 들어 배우 관리 창에서 배우 이름을 바꾸거나 속성 창에서 태그/배우/메모를 바꾸면, 다른 창을 닫지 않고도 메인 창의 목록과 하단 상세정보 패널에 즉시 반영된다.
  - **태그/배우 칩 셀이 창을 옮겨야만 다시 그려지던 문제(2026-08-04 수정, 구현 완료)**: 위 라이브 필터링/정렬은 항목의 필터 통과 여부·정렬 "위치"만 재평가할 뿐, 이미 화면에 그려진 행(row)의 셀 내용을 다시 그리는 것까지 보장하지는 않는다. 태그/배우 칩은 `WrapPanel`이라 내용에 따라 셀 높이가 바뀌는데, 가상화된 `ListView`/`ListBox`가 이 높이 변화를 곧바로 반영하지 않아 데이터는 바뀌었는데도 화면에는 예전 칩이 그대로 남아있다가, 창을 옮기는 등 강제 레이아웃이 있어야 드러나는 문제가 있었다. `MainWindow.ManagedItem_PropertyChanged`에서 `Tags`/`Actors`가 바뀌면 `_managedView.Refresh()`를 호출해(목록 컨테이너를 완전히 다시 생성) 해결했다 — `_managedView.Refresh()`는 `ManagedListView`/`ManagedIconView`가 같은 `ICollectionView` 인스턴스를 공유하므로 양쪽 모두에 적용된다.
- **버그 수정: 속성 창에서 배우/시리즈를 바꿔도 이미 열려 있는 배우 관리/시리즈 관리 창의 Credits 패널이 갱신되지 않던 문제**(2026-08-10 수정, **구현 완료**) — `ActorManagerWindow`/`SeriesManagerWindow`를 열어둔 채로 `PropertiesWindow`에서 배우/시리즈 칩을 추가·제거해도, 이미 열려 있는 관리 창의 Credits 칩 목록이 그 즉시 반영되지 않고 창을 닫았다 다시 열거나(또는 다른 배우/시리즈를 선택했다가 되돌아와야만) 갱신되는 문제가 있었다. `RefreshCreditsPanel`(Credits 칩 목록을 다시 그리는 메서드)을 트리거하는 훅이 전혀 없었던 것이 원인이며, 수정은 **두 층(layer)**으로 이루어진다 — 한 층만으로는 불충분했다(아래 참고). 상세 로직은 [배우 관리](actor-management.md)/[시리즈 관리](series-management.md)가 소유하지만, 창 관리 정책 규칙 2의 연장선이라 여기 함께 기록한다.
  - **1층: 관리 리스트 항목의 `Actors`/`Series`가 바뀌면** — `MainWindow.ManagedItems_CollectionChanged`/`ManagedItem_PropertyChanged`와 동일한 패턴으로, `ActorManagerWindow`/`SeriesManagerWindow`도 생성자에서 `_managedItems`의 모든 항목에 `PropertyChanged`를 구독하고(컬렉션 변경 시 구독도 함께 추가/해제) `Actors`(배우 창)/`Series`(시리즈 창)가 바뀌면 `RefreshCreditsPanel`을 호출한다. 창이 닫힐 때 모든 구독을 해제한다.
  - **2층(1층만으로는 불충분해서 추가로 필요했던 부분): 선택된 `ActorItem`/`SeriesItem` 자신의 `Credits`가 바뀌면** — `PropertiesWindow.TryCommitFormFields()`는 `_item.SetActors(...)`(1층이 감지하는 지점)를 먼저 호출하고, 그 **다음에** `ActorCreditSync.OnActorRemovedFromItem`이 실제로 `actor.Credits`에서 품번을 제거한다(시리즈도 `SeriesCreditSync.OnFileRenamed`/`UpdateManagedItemSeriesForCredit`와 같은 이유로 동일한 순서 문제가 있음). 즉 1층 훅이 실행되는 시점에는 `actor.Credits`가 **아직 옛 값 그대로**라 `RefreshCreditsPanel`을 호출해도 화면에 변화가 없었다 — 이게 실제로 재현되어 확인된 버그다. 해결을 위해 `ActorManagerWindow`/`SeriesManagerWindow`가 **현재 선택된 `ActorItem`/`SeriesItem` 자신의 `PropertyChanged`도 직접 구독**해 `Credits`가 바뀌면 `RefreshCreditsPanel`을 호출한다. 선택이 바뀔 때마다 이전 선택 대상 구독을 해제하고 새 선택 대상을 구독하는 "구독 교체" 패턴(`_subscribedCreditsActor`/`_subscribedCreditsSeries` 필드, `RefreshThumbnailPreview`/`RefreshCreditsPanel` 시작 부분에서 처리)을 쓴다 — 선택이 `null`이 되는 경우(시리즈)도 놓치지 않도록 이 구독 교체 로직은 `series is null` 조기 반환보다 먼저 실행된다.
  - **검증**: 실제 사용자 데이터(배우 "Suzumura Airi", 품번 "abp-197")로 재현 — 배우 관리 창을 열어 Suzumura Airi를 선택해 Credits에 `abp-163`/`abp-197`/`abp-383`이 보이는 상태를 만든 뒤, 그 창을 닫지 않고 속성 창에서 abp-197.avi의 배우 칩(Suzumura Airi)을 제거·확인하면, 배우 관리 창을 전혀 건드리지 않았는데도 Credits 목록에서 `abp-197`이 즉시 사라짐을 스크린샷으로 확인함(검증 후 실제 데이터는 원래 상태로 복원함).
- **적용 범위 밖(의도적으로 남겨둔 부분)**: `PropertiesWindow` 자신이 표시 중인 태그 체크박스/배우 콤보박스/배우 칩은, 그 창이 열려 있는 동안 **다른** 창에서 마스터 목록(태그/배우 이름)이나 이 항목의 `Tags`/`Actors`가 외부에서 바뀌어도 자동으로 다시 그려지지 않는다(스냅샷이라 라이브 반영 안 됨) — 동시에 같은 항목을 여러 창에서 편집하는 경우는 드물고, 잘못하면 사용자가 입력 중인 내용을 덮어써버릴 위험이 더 크다고 판단해 범위에서 제외했다. 창을 닫았다 다시 열면(또는 그 창이 "배우 관리 창에서 돌아왔을 때"처럼 자기 자신의 `Closed` 콜백을 갖는 경우는) 항상 최신 상태로 다시 그려진다.

### 앱 단일 인스턴스 (작업표시줄 재실행 시 활성화)

작업표시줄(핀 고정 등)에서 아이콘을 클릭해 앱을 다시 실행해도 새 프로세스/새 창이 또 뜨지 않고, 이미 실행 중인 인스턴스의 메인 창이 앞으로 나와 활성화된다(2026-08-22 추가, **구현 완료**, 사용자 요청). 이건 위 "창 관리 정책"(주요 창 4~5개끼리의 규칙)과는 다른 층위 — `MainWindow` 자신을 포함한 **프로세스 전체**에 대한 정책이라 `App.xaml.cs`에 구현했다.

- **감지**: `App.OnStartup`에서 이름 있는 `Mutex`(`"VideoVault_SingleInstance_2E1B7F3A"`)를 `initiallyOwned: true`로 생성한다. `out isNewInstance`가 `false`면(이미 같은 이름의 뮤텍스를 다른 프로세스가 쥐고 있음) 이 프로세스가 두 번째 인스턴스라는 뜻이다.
- **두 번째 인스턴스의 동작**: 창을 하나도 만들지 않는다(`App.xaml`의 `StartupUri="MainWindow.xaml"`이 평소엔 `OnStartup` 이후 자동으로 `MainWindow`를 띄우는데, `base.OnStartup(e)`를 호출하지 않고 `Environment.Exit(0)`으로 즉시 종료해 이 자동 생성 자체를 막는다). 종료 직전에 `Process.GetProcessesByName(현재 프로세스 이름)`로 자기 자신 말고 다른 VideoVault 프로세스를 찾아 그 `MainWindowHandle`에 Win32 `ShowWindowAsync(SW_RESTORE)` + `SetForegroundWindow`를 호출한다 — 최소화돼 있었으면 복원하고, 최소화가 아니라 단지 다른 창에 가려져 있었을 뿐이면 앞으로 가져온다.
- **`SetForegroundWindow` 제약을 피하는 이유**: Windows는 보통 백그라운드 프로세스가 다른 프로세스의 창을 포그라운드로 강제로 가져오는 것을 막는다(포커스 강탈 방지). 하지만 여기서 이 호출을 하는 두 번째 프로세스는 **사용자가 방금 직접 아이콘을 클릭해서 막 실행시킨** 프로세스라, Windows가 이 프로세스에게 포그라운드 전환 권한을 이미 부여한 상태다 — 이 권한으로 다른(첫 번째 인스턴스의) 창을 활성화하는 것도 허용된다. 별도의 IPC(파이프 등)로 첫 번째 프로세스에게 "네가 직접 활성화해라"라고 부탁할 필요가 없다.
- **뮤텍스 정리**: `App.OnExit`에서 `ReleaseMutex()` + `Dispose()`로 정리한다.
- **검증**: 실행 중인 인스턴스를 최소화한 뒤 같은 exe를 다시 실행 → 두 번째 프로세스가 즉시 종료(프로세스 수 그대로 1개 유지)하고, 첫 번째 인스턴스의 `WindowVisualState`가 `Normal`로 복원되며 `GetForegroundWindow()`가 그 창 핸들과 일치함을 확인함.

### 보조 창의 작업표시줄 숨김

`MainWindow`와 보조 창(속성 창 등)이 동시에 열려 있을 때 작업표시줄 아이콘을 클릭하면 "메인 창/보조 창 중 무엇을 선택할지" 묻는 메뉴(플립/썸네일 미리보기 목록)가 뜨는 문제가 있었다(2026-08-22 수정, **구현 완료**, 사용자 리포트) — Owner가 있는 창이라도 WPF는 기본적으로(`ShowInTaskbar` 기본값 `true`) 각자 별도의 작업표시줄 항목을 갖고, Windows는 같은 프로세스의 창들을 아이콘 하나로 묶어 보여주되 그 안에서 선택하게 한다. `MainWindow`를 제외한 **Owner가 있는 모든 창**(`FolderListWindow`/`PropertiesWindow`/`ActorManagerWindow`/`TagManagerWindow`/`SeriesManagerWindow` 5개 주요 창 + `RenameWindow`/`ActorInfoWindow`/`AddCreditWindow`/`OriginalImageWindow` 소형 대화상자 4개, 총 9개 — 위 "창 관리 정책"의 주요/소형 구분과 무관하게 전부 대상) XAML 루트에 `ShowInTaskbar="False"`를 추가해, 이 창들이 작업표시줄에 아예 나타나지 않게 했다. 그 결과 작업표시줄에는 항상 `MainWindow` 하나만 있고, 클릭하면 곧바로 그 창이 활성화된다(선택 메뉴 자체가 뜨지 않음). 창 자체의 기능(모덜리스로 열림, Owner 위에 겹쳐 보임, Alt+F4 등)은 이 속성과 무관하게 그대로 동작한다.

- **검증**: `PropertiesWindow`를 연 상태에서 `Win32 GetWindowLong(GWL_EXSTYLE)`로 확인 — `PropertiesWindow`는 (WPF가 `ShowInTaskbar="False"`일 때 내부적으로 설정하는) `WS_EX_TOOLWINDOW`가 붙어 UI Automation의 최상위 창 목록(`AutomationElement.RootElement`의 자식, 작업표시줄/Alt+Tab이 열거하는 것과 같은 목록)에서 아예 빠지는 반면, `MainWindow`는 정상적으로 그 목록에 남아있음을 확인했다.

### 창 스냅(자석 붙기)

메인 창과 주요 창(폴더 목록/속성/배우 관리/태그 관리)을 서로 가까이 끌어다 놓으면 가장자리가 자석처럼 딱 달라붙는다(2026-08-07 추가, **구현 완료**). `WindowSnapHelper.cs`가 담당하며, 각 주요 창의 생성자에서 `InitializeComponent()` 직후 `WindowSnapHelper.Attach(this)`를 호출해 등록한다(창이 닫히면 자동으로 등록 해제).

- **왜 `LocationChanged`가 아니라 `WM_MOVING`인가**: WPF `Window.LocationChanged`는 이동이 끝난 뒤에만 발생해서, 드래그 "도중"에 실시간으로 위치를 보정하는 진짜 스냅에는 쓸 수 없다. 대신 Win32 `WM_MOVING` 메시지(`HwndSource.AddHook`으로 후킹, `Window.SourceInitialized` 시점에 연결)를 가로채, OS가 창을 실제로 옮기기 전에 제안된 사각형(`RECT`, `lParam`)을 직접 고쳐서 돌려준다.
- **DPI 변환**: `WM_MOVING`의 `RECT`는 물리 픽셀 기준이지만 `Window.Left`/`Top`/`ActualWidth`/`ActualHeight`는 DIU(96 DPI 기준)라서, 스냅 계산 전에 `VisualTreeHelper.GetDpi(window)`로 DIU로 변환하고 계산이 끝나면 다시 물리 픽셀로 되돌려 `RECT`에 채운다.
- **DWM 그림자 여백 보정(2026-08-07 추가, 구현 완료)**: Windows 10/11은 창 바깥에 보이지 않는 그림자 여백을 덧붙이는데, `WM_MOVING`의 `RECT`/`GetWindowRect`는 이 여백을 포함한 "원시" 사각형을 기준으로 하는 반면 사용자 눈에 보이는 실제 가장자리는 `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)`로만 알 수 있다. 이 여백을 보정하지 않으면 스냅이 되긴 해도 두 창 사이에 몇 px짜리 틈이 남아 "딱 붙지 않고 살짝 떨어져 보이는" 문제가 있었다 — `GetWindowRect`(원시)와 `DwmGetWindowAttribute`(보이는 프레임) **둘 다 같은 순간의 값**을 짝지어 4방향 여백(Left/Top/Right/Bottom, 물리 픽셀)을 계산하고, 스냅 계산 전후로 이 여백만큼 안쪽/바깥쪽으로 보정한다. 이 시스템의 모든 창에서 실측 여백은 좌/우/아래 7px, 위 0px이었다(테마/DPI에 따라 값은 달라질 수 있음).
  - **여백은 창당 한 번만 계산해서 캐시한다** — 처음에는 `WM_MOVING`마다(추적 중인 다른 창 수만큼) 매번 `GetWindowRect`+`DwmGetWindowAttribute` Win32 호출을 두 번씩 반복했는데, 빠르게 드래그할 때 이 반복 호출의 지연이 누적되어 창 위치가 마우스를 순간적으로 못 따라가거나 드물게 크기 조절로 오인되는 것처럼 보이는 문제가 있었다(자동화 테스트로 재현). 창의 그림자 여백은 테마/DPI가 같으면 창 크기·위치가 바뀌어도 상수이므로, `Dictionary<Window, (Left,Top,Right,Bottom)>`에 창당 한 번만 계산해 캐시해두고 이후에는 조회만 한다(창이 닫히면 캐시도 함께 정리).
- **스냅 대상**: `WindowSnapHelper`가 정적 리스트로 등록된 모든 창을 추적하며, 드래그 중인 창 자신과 `WindowState`가 `Normal`이 아닌(최소화/최대화) 창은 대상에서 제외한다.
- **스냅 규칙**: 가로/세로를 독립적으로 계산한다. 각 축에 대해 다른 창의 **바깥쪽에 인접하는 경우만**(예: 내 왼쪽 ↔ 상대 오른쪽, 내 오른쪽 ↔ 상대 왼쪽) `SnapDistance`(8 DIU) 안에서 가장 가까운 후보를 고른다. 해당 거리 안에 아무것도 없으면 원래 드래그 위치를 그대로 쓴다. **같은 쪽 가장자리끼리 맞추는 "안쪽" 정렬(내 왼쪽 ↔ 상대 왼쪽 등)은 후보에서 제외한다**(2026-08-08 변경, 구현 완료 — 예전에는 이 정렬도 후보에 있었으나, 창이 서로 겹쳐 들어가는 것처럼 보인다는 피드백으로 제거함).
- **스냅 거리(2026-08-08 축소, 구현 완료)**: `SnapDistance`를 15 DIU에서 8 DIU로 줄였다 — 값이 크면 일단 스냅된 창을 다시 떼어내려 할 때 마우스가 그만큼 움직여야 풀리기 시작해서 뻑뻑하게 느껴진다는 피드백이 있었다.
- **검증**: 실제 앱에서 마우스 드래그를 시뮬레이션해 확인함 — (1) 창 가장자리를 다른 창 가장자리에서 몇 px 떨어진 지점까지 끌면 DWM 그림자 여백을 보정한 정확한 좌표로 딱 붙어 두 창의 보이는 가장자리 사이 틈이 0px임을 `DwmGetWindowAttribute` 직접 조회로 확인함, (2) 스냅 대상에서 먼 위치로 끌면(예: 다른 창의 "같은 쪽" 가장자리와만 우연히 값이 비슷한 위치) 스냅되지 않고 드래그한 그대로의 위치에 정확히 놓여 "안쪽 정렬" 후보가 실제로 제거되었음을 확인함. **주의**: 이 검증을 자동화(PowerShell UI Automation + 시뮬레이션 드래그)로 하는 과정에서, 같은 `AutomationElement` 참조의 `BoundingRectangle`을 여러 드래그에 걸쳐 재사용하면 값이 갱신되지 않고 오래된 좌표를 반환하는 경우가 있어(테스트 스크립트 자체의 문제, 앱 코드와 무관) 클릭 좌표 계산이 어긋나 창이 안 움직이거나 제목 표시줄 대신 크기 조절 테두리를 잡아 의도치 않게 리사이즈되는 것처럼 보인 적이 있었다 — 매 드래그 시도 전에 `FindFirst`로 창을 새로 조회해 최신 좌표를 쓰도록 고쳐서 해결했다(실제 버그가 아니었음).
- 새 "주요 창"을 추가할 때는 아래 컨벤션에 따라 `SingleInstanceWindow<T>` 등록과 함께 생성자에 `WindowSnapHelper.Attach(this)`도 호출해야 한다.

## 전역 컨벤션

이 절은 앱 전체에 적용되는 컨벤션만 다룬다. 도메인 특화 컨벤션(동영상/태그/배우/시리즈/속성)은 각 문서의 "이 영역의 컨벤션" 절 참고.

- UI는 XAML로, 로직은 code-behind 또는 별도 클래스로 분리한다. 신규 기능은 [메인 지시서](../CLAUDE.md)의 아키텍처 절(Repository/Service/ViewModel 계층)을 따른다.
- 새 화면/윈도우를 추가할 때는 기존 `MainWindow.xaml` 패턴(XAML + `.xaml.cs`)을 따른다.
- **주요 창**(`MainWindow`에서 직접 열거나 주요 창끼리 서로 넘나드는 창)을 새로 추가할 때는 위 "창 관리 정책"을 따른다 — `Show()`(모덜리스) + `SingleInstanceWindow<T>.Show(window)`로 열고, `Owner`는 `Application.Current.MainWindow`로 고정하며, `ShowDialog()` 반환 직후 하던 후처리는 `Closed` 이벤트로 옮긴다. 생성자의 `InitializeComponent()` 직후에는 "창 스냅(자석 붙기)"을 위해 `WindowSnapHelper.Attach(this)`도 호출해야 한다. 반대로 한 창에서 값을 잠깐 입력받고 바로 돌아오는 작은 대화상자(이름 변경/정보 수정 등)는 이 정책 대상이 아니며 기존처럼 `ShowDialog()`를 그대로 쓰면 된다.
- 화면(윈도우) 기본 크기는 일괄된 고정값을 따르지 않고, 창의 용도와 내용물에 맞게 개별적으로 정한다(2026-08-02 정정 — 예전에는 "기본 1920x1080, MainWindow만 예외"라고 되어 있었으나 실제로는 지켜진 적이 없어 문서를 실제 관행에 맞게 고쳤다). 새 창을 추가할 때도 내용에 맞는 크기를 자유롭게 정하면 된다. 참고로 현재 각 창의 실제 크기는 다음과 같다:
  - `MainWindow`: `Height="1399" Width="1240"` (2026-08-16 변경 — 아이콘 보기(특히 "아이콘만 보기")에 맞춰 세로로 더 길고 가로로는 좁은 비율로 조정됨. 실제로는 `settings.json`의 `MainWindowWidth`/`Height`가 있으면 이 XAML 기본값 대신 그 값으로 복원되므로(아래 "설정 관리" 절 참고), 이 값은 최초 실행이나 설정 초기화 시에만 쓰이는 폴백이다)
  - `FolderListWindow`: `Height="700" Width="1000"`
  - `PropertiesWindow`: `Height="1200" Width="500"`
  - `ActorManagerWindow`: `Height="1000" Width="1000"`
  - `TagManagerWindow`: `Height="500" Width="400"`
  - `SeriesManagerWindow`: `Height="700" Width="450"`
  - `ActorInfoWindow`: `Height="290" Width="380"`
  - `RenameWindow`: `Height="150" Width="420"`
  - `OriginalImageWindow`: `Height="800" Width="1000"`
- 헤더 클릭(정렬)과 헤더 우클릭(표시할 컬럼 선택)은 서로 다른 이벤트이므로 UI/이벤트 핸들러를 명확히 분리한다. 파일명/태그 검색 필터는 헤더가 아니라 리스트 위 상시 UI에서 처리한다 ([동영상 파일 관리](video-file-management.md) 참고).
- 헤더 우클릭(컬럼 선택)/태그 필터 팝업/아이콘 크기·표시 정보 팝업은 WPF `ContextMenu`가 아니라 `Popup`으로 구현한다 (텍스트 입력/체크박스 목록 등 인터랙티브 컨트롤을 담기에 `ContextMenu`보다 다루기 쉬움). 반대로 항목(행/카드) 우클릭 메뉴처럼 단순 명령 목록은 `ContextMenu`를 쓴다.
- 상단 `Menu`의 각 `MenuItem`은 새 로직을 만들지 않고 기존 버튼과 동일한 이벤트 핸들러를 재사용한다. 새 기능을 추가할 때도 버튼과 메뉴 항목이 같은 핸들러를 공유하도록 한다 (기능이 두 곳에서 따로 구현되어 어긋나는 것을 방지).
- 비동기 메서드는 이름에 `Async` 접미사를 붙인다 (예: `LoadVideoFilesAsync`).
- Nullable reference type을 활성화한다 (`<Nullable>enable</Nullable>`, 이미 csproj에 설정되어 있음).
- 파일 경로 조합은 문자열 연결 대신 `Path.Combine`을 사용한다.
- 하드코딩 문자열(경로, 기본 파일명 등)은 (규모가 커지면) `Constants.cs` 클래스로 분리한다.
- 다국어 등 공용 문자열이 늘어나면 `Resources.resx`로 옮길 수 있도록 문자열을 코드 곳곳에 흩뿌리지 않는다.
- **JSON으로 직렬화되는 모델(`ManagedVideoItem`, `AppSettings`, `ActorItem`, `SeriesItem` 등)의 속성에 `private set`(또는 setter 없음)을 쓸 때는 반드시 `[JsonInclude]`(`System.Text.Json.Serialization`)를 붙인다.** System.Text.Json은 기본적으로 public setter가 없는 속성을 역직렬화 시 조용히 건너뛴다 — 예외가 발생하지 않아 눈치채기 어렵다. 실제로 `ManagedVideoItem.Tags`가 이 문제로, 저장(직렬화)은 정상 동작하면서(getter만 있으면 됨) 다음 실행 시 불러오기(역직렬화)에서만 태그가 항상 빈 목록이 되는 버그가 있었다(2026-07-31 수정, `[JsonInclude]` 추가). 캡슐화가 필요 없는 필드는 애초에 public setter를 쓰는 것도 방법이다.
- **로컬 파일 경로에서 이미지를 표시할 때는 항상 `ImageLoadHelper.Load`(코드) 또는 `ThumbnailPathConverter`(XAML `{Binding}`)를 거친다. `new BitmapImage(new Uri(path))`나 XAML의 암시적 문자열→`ImageSource` 변환을 캐시 옵션 없이 직접 쓰지 않는다.** 이유와 재현된 버그는 위 "썸네일 관리 > 썸네일 파일 잠금 문제" 절 참고.

## 로그

디버깅을 위한 로그 정책 (아직 미구현):

- 기록 대상: 프로그램 시작/종료, JSON 저장, JSON 로드, 재생, 태그 변경, 예외 발생
- 저장 위치: `%LOCALAPPDATA%\VideoVault\Logs\` (다른 데이터 파일과 동일한 로컬 데이터 폴더 하위)

## 테스트

향후 강화할 테스트 범위:

- 단위 테스트: JSON 직렬화/역직렬화(`ManagedListRepository`, `TagRepository`), 태그 이름 변경·삭제 시 관리 리스트 동기화 로직, 재생횟수 증가 로직
- UI 자동화 테스트: 폴더 열기 → 관리 리스트 추가 → 정렬/필터 → 저장까지의 골든 패스
