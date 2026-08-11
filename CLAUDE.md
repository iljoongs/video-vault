# VideoVault

C# WPF (.NET 8) 데스크톱 GUI 애플리케이션.

## 프로젝트 목적

동영상 파일을 관리하는 GUI 애플리케이션.

- 특정 폴더(하위 폴더 포함)를 열어 동영상 파일 목록을 스캔한다 (**폴더 목록**).
- 사용자가 원하는 파일을 별도의 **관리 리스트**에 추가하여 지속적으로 관리한다 (JSON 파일로 저장).
- 관리 리스트에서 파일명, 크기, 재생횟수, tags(태그 형태의 사용자 정의 속성)를 확인하고 정렬·필터링할 수 있다.
- 태그는 별도의 **태그 마스터 목록**(JSON 파일)으로 관리되며, 사용자는 이 마스터 목록에서 태그를 추가/수정/삭제할 수 있다. 관리 리스트 항목에 태그를 붙일 때는 자유 입력이 아니라 마스터 목록에서 선택하는 방식이다.
- 관리 리스트는 리스트 보기 / 아이콘(썸네일) 보기 두 가지 방식으로 볼 수 있으며, 각 항목에 사용자가 직접 이미지 파일을 썸네일로 지정할 수 있다.
- 배우 마스터 목록은 배우가 출연한 작품(품번) 목록인 **Credits**를 함께 관리하며, 관리 리스트의 파일-배우 태깅과 서로 자동으로 동기화된다 — [배우 마스터 목록 관리](#배우-마스터-목록-관리-actormanagerwindow) 참고.

## 기술 스택

- .NET 8 SDK (WPF, `net8.0-windows`)
- XAML + code-behind, MVVM으로 점진 전환 중 (자세한 방향은 [아키텍처](#아키텍처) 참고)

## 빌드 / 실행

```
dotnet build
dotnet run
```

## 코드 서명 (로컬 개발용)

이 PC는 Windows **Smart App Control(SAC)**이 평가 모드로 켜져 있어서, 서명되지 않은 새 빌드 실행 파일을 "신뢰할 수 없음"으로 판단해 실행을 차단한다(코드 무결성 로그에 "Enterprise signing level requirements" 오류로 나타남 — Defender 위협 탐지 로그는 비어 있어 악성코드 탐지가 아니라 순수 서명/평판 정책임을 확인함). 이를 우회하기 위해 **자체 서명(self-signed) 인증서로 빌드 결과물에 자동 서명**하도록 구성했다 — **구현 완료**.

- **인증서**: `CN=VideoVault Dev Signing` (CurrentUser\My 저장소, 5년 유효). 신뢰를 위해 같은 인증서(공개키)를 `CurrentUser\Root`와 `CurrentUser\TrustedPublisher`에도 등록해뒀다. 개인키는 이 PC의 사용자 계정에만 존재하며 저장소/파일로 커밋하지 않는다.
- **자동 서명**: `VideoVault.csproj`에 `AfterTargets="Build"` 타겟이 있어, 빌드가 끝날 때마다 `Sign-Build.ps1`이 `$(TargetDir)`의 `.exe`와 `.dll`을 모두 서명한다. 인증서를 찾을 수 없는 다른 PC에서는(예: 다른 개발자의 PC) 오류 없이 조용히 서명을 건너뛴다.
- **다른 PC에서 설정하려면**: 아래 PowerShell을 관리자 권한 없이 실행하면 된다 (모두 `CurrentUser` 범위, 시스템 전체에는 영향 없음). 생성된 인증서의 Thumbprint 값을 `Sign-Build.ps1`의 `$thumbprint` 변수에 반영해야 한다.

  ```powershell
  $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=VideoVault Dev Signing" `
      -CertStoreLocation Cert:\CurrentUser\My -KeyUsage DigitalSignature `
      -FriendlyName "VideoVault Dev Code Signing" -NotAfter (Get-Date).AddYears(5) -KeyExportPolicy Exportable

  $certBytes = $cert.Export('Cert')
  $tmp = "$env:TEMP\VideoVaultDevSigning.cer"
  [System.IO.File]::WriteAllBytes($tmp, $certBytes)
  Import-Certificate -FilePath $tmp -CertStoreLocation Cert:\CurrentUser\TrustedPublisher
  Remove-Item $tmp

  # CurrentUser\Root는 Import-Certificate가 대화형 확인을 요구해 비대화형으로 막힌다 — .NET X509Store로 우회:
  Add-Type -AssemblyName System.Security
  $store = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root", "CurrentUser")
  $store.Open("ReadWrite"); $store.Add($cert); $store.Close()
  ```

- **한계**: 이 방식은 Authenticode 서명 자체는 "Valid"로 만들어 SAC의 실행 차단은 우회하지만, 자체 서명 인증서라 마이크로소프트 클라우드 평판(ISG) 기반 신뢰까지 얻는 것은 아니다. 다른 PC로 배포하는 진짜 릴리스 빌드에는 정식 코드 서명 인증서(EV 등)가 필요하다 — 지금 방식은 어디까지나 로컬 개발/테스트용.

## 앱 아이콘

exe 아이콘(탐색기/작업 표시줄)과 `MainWindow` 타이틀바 아이콘 모두 `AppIcon.ico`(2026-08-02 추가, **구현 완료**)를 사용한다.

- **원본**: 프로젝트 루트의 `media-player-interface-symbol-svgrepo-com.svg`(재생 버튼이 있는 미디어 플레이어 창 모양 아이콘)를 소스로 만들었다. WPF에 SVG를 직접 아이콘으로 쓰는 기능이 없어(`.ico` 필요), SVG의 `<path>`/`<polygon>` 데이터를 WPF `Geometry.Parse`로 그대로 파싱해(SVG path mini-language와 XAML의 것이 거의 동일해 별도 변환 없이 재사용 가능했음) `DrawingVisual`에 렌더링한 뒤 16/32/48/256px 크기로 각각 `RenderTargetBitmap` + `PngBitmapEncoder`로 PNG를 만들고, 이 PNG들을 담은 `.ico`(PNG-압축 아이콘 항목, Vista 이상에서 지원)를 직접 조립하는 격리된 콘솔 스크립트로 변환했다(외부 SVG 변환 도구 없이 처리, 이 PC에 ImageMagick/Inkscape 등이 없었음). 스크립트 자체는 프로젝트에는 포함하지 않음(1회성 변환용).
- **적용 위치**:
  - `VideoVault.csproj`: `<ApplicationIcon>AppIcon.ico</ApplicationIcon>`로 exe 자체의 아이콘(탐색기/작업 표시줄, 앱을 실행하지 않은 상태에서도 보임)을 지정. `<Resource Include="AppIcon.ico" />`로 함께 포함해 런타임에도 참조 가능하게 함.
  - `MainWindow.xaml`: `Icon="AppIcon.ico"`로 타이틀바/Alt+Tab 아이콘을 지정. 서브 창(`FolderListWindow` 등)은 별도로 지정하지 않았다(자식 창은 보통 별도 작업 표시줄 항목을 갖지 않으므로).
- **검증**: 빌드된 exe에서 `System.Drawing.Icon.ExtractAssociatedIcon`으로 아이콘을 실제로 추출해 기본 아이콘이 아닌 지정한 미디어 플레이어 아이콘이 임베드되었음을 확인했다.

## 프로젝트 구조

### 현재 구현된 파일

- `VideoVault.csproj` — 프로젝트 파일 (빌드 후 자동 서명 타겟 포함 — [코드 서명](#코드-서명-로컬-개발용) 참고, 앱 아이콘(`ApplicationIcon`) 지정 포함 — [앱 아이콘](#앱-아이콘) 참고)
- `Sign-Build.ps1` — 빌드 결과물(exe/dll)에 로컬 개발용 인증서로 서명하는 스크립트
- `AppIcon.ico` — 앱 아이콘 (16/32/48/256px PNG-압축 아이콘을 담은 `.ico`) — [앱 아이콘](#앱-아이콘) 참고
- `App.xaml` / `App.xaml.cs` — 애플리케이션 진입점
- `MainWindow.xaml` / `MainWindow.xaml.cs` — 메인 윈도우 (관리 리스트 UI, 정렬/필터/썸네일 로직). 폴더 목록은 `FolderListWindow` 서브 창으로 분리되어 있다
- `FolderListWindow.xaml` / `FolderListWindow.xaml.cs` — 폴더를 열어 동영상 파일을 스캔하고 관리 리스트에 추가하는 서브 창 (2026-08-02 추가, 예전에는 `MainWindow` 왼쪽에 상시 표시되는 패널이었음). `MainWindow`의 관리 리스트 컬렉션(`_managedItems`)을 참조로 그대로 받아 직접 추가/수정한다 — [폴더 목록](#1-폴더-목록-현재-열린-폴더-스캔-결과) 참고
- `VideoFileItem.cs` — 폴더 목록(임시 스캔 결과) 항목 모델
- `ManagedListImporter.cs` — 동영상 파일들을 관리 리스트에 추가하는 공용 로직(동영상 확장자 목록 `VideoExtensions`, 제거된 데이터 재사용 확인 대화상자 포함). 폴더 목록 창의 "관리 리스트에 추가"와 관리 리스트 영역으로의 드래그 앤 드롭이 공유한다(2026-08-05 추가, **구현 완료**) — [관리 리스트](#2-관리-리스트-사용자가-지속적으로-관리하는-목록)의 "드래그 앤 드롭으로 추가" 참고
- `ManagedVideoItem.cs` — 관리 리스트 항목 모델 (JSON 직렬화 대상, `INotifyPropertyChanged` 구현, 태그/배우는 각각 `List<string>`)
- `ManagedListRepository.cs` — 관리 리스트 JSON 파일 읽기/쓰기 로직
- `TagRepository.cs` — 태그 마스터 목록(`tags.json`) 읽기/쓰기 로직
- `TagManagerWindow.xaml` / `TagManagerWindow.xaml.cs` — 태그 마스터 목록 추가/수정/삭제 관리 화면
- `ActorItem.cs` — 배우 마스터 목록 항목 모델 (이름, 100x100 썸네일 경로, 출생년도/키/신체정보, 출연 작품 품번 목록 `Credits`, JSON 직렬화 대상, `INotifyPropertyChanged` 구현)
- `ActorRepository.cs` — 배우 마스터 목록(`actors.json`) 읽기/쓰기 로직
- `ActorManagerWindow.xaml` / `ActorManagerWindow.xaml.cs` — 배우 마스터 목록 추가/이름변경/삭제 관리 화면 + 배우별 100x100 썸네일 지정/삭제 + 배우 정보(출생년도/키/신체정보) 표시 + 이 배우가 출연한 작품(Credits) 목록 표시·관리([배우 마스터 목록 관리](#배우-마스터-목록-관리-actormanagerwindow) 참고)
- `SeriesItem.cs` — 시리즈 마스터 목록 항목 모델 (이름, 소속 작품 품번 목록 `Credits`, JSON 직렬화 대상, `INotifyPropertyChanged` 구현) — [시리즈 마스터 목록 관리](#시리즈-마스터-목록-관리-seriesmanagerwindow) 참고 (2026-08-09 추가)
- `SeriesRepository.cs` — 시리즈 마스터 목록(`series.json`) 읽기/쓰기 로직 (2026-08-09 추가)
- `SeriesManagerWindow.xaml` / `SeriesManagerWindow.xaml.cs` — 시리즈 마스터 목록 추가/삭제 관리 화면 + 이 시리즈에 속한 작품(Credits) 목록 표시·관리 ([시리즈 마스터 목록 관리](#시리즈-마스터-목록-관리-seriesmanagerwindow) 참고, 2026-08-09 추가)
- `AddCreditWindow.xaml` / `AddCreditWindow.xaml.cs` — 배우의 Credits에 새 품번을 추가하는 대화상자. 입력하는 동안 관리 리스트에서 일치하는 항목을 미리보기로 보여주지만, 목록에 없는 품번도 자유롭게 입력할 수 있다 (2026-08-02 추가, **구현 완료**) — [배우 마스터 목록 관리](#배우-마스터-목록-관리-actormanagerwindow) 참고
- `ActorCreditSync.cs` — 관리 리스트 항목의 `Actors`와 배우 마스터 목록의 `Credits`를 서로 동기화하는 공용 로직 (파일명 변경 시 Credits 값 갱신/배우 자동 추가, 속성 창에서 배우 제거 시 Credits에서도 제거). `RenameHelper`/`PropertiesWindow`/`ActorManagerWindow`가 공유한다 (2026-08-02 추가, **구현 완료**) — [배우 마스터 목록 관리](#배우-마스터-목록-관리-actormanagerwindow) 참고
- `SeriesCreditSync.cs` — 관리 리스트 항목의 `Series`와 시리즈 마스터 목록의 `Credits`를 서로 동기화하는 공용 로직. `ActorCreditSync`와 동일한 목적이지만 `Series`가 단일 값이라 로직이 더 단순하다 — 파일명이 바뀌면 그 파일이 지정된 시리즈의 Credits에서 옛 품번을 새 품번으로 갱신한다(2026-08-09 추가, **구현 완료**). `RenameHelper`가 호출한다 — [시리즈 마스터 목록 관리](#시리즈-마스터-목록-관리-seriesmanagerwindow) 참고
- `ActorInfoWindow.xaml` / `ActorInfoWindow.xaml.cs` — 배우의 이름/출생년도/키/신체정보를 추가·수정·삭제하는 대화상자. `ActorManagerWindow`의 배우 목록을 우클릭 → "배우 정보 수정"으로 연다
- `ImageLoadHelper.cs` — 로컬 파일 경로에서 `BitmapImage`를 즉시 전부 읽어들여(`BitmapCacheOption.OnLoad` + `Freeze`) 반환하는 공용 로직. 화면에 표시 중인 썸네일 파일을 새 썸네일로 덮어쓸 수 있도록 파일 잠금을 피하기 위한 것 ([썸네일 파일 잠금 문제](#썸네일-파일-잠금-문제-이미-표시-중인-썸네일-덮어쓰기) 참고)
- `WindowsIconHelper.cs` — Windows 셸에서 특정 확장자에 연결된 표준 아이콘을 가져오는 헬퍼(`SHGetFileInfo` P/Invoke). `PngFileIcon`은 `.png` 파일의 표준 아이콘을 조회해 `ImageSource`로 변환하고 캐시한다. 관리 리스트의 썸네일 유무 컬럼 아이콘에 사용 ([헤더 우클릭 → 표시할 컬럼 선택](#2-관리-리스트-사용자가-지속적으로-관리하는-목록) 참고)
- `ThumbnailPathConverter.cs` — 썸네일 경로 문자열을 `ImageLoadHelper.Load`로 변환하는 `IValueConverter`. XAML에서 `Image.Source`를 `ThumbnailPath`에 바인딩할 때 암시적 문자열 변환 대신 이 컨버터를 사용해야 파일 잠금 문제가 생기지 않는다
- `PropertiesWindow.xaml` / `PropertiesWindow.xaml.cs` — 관리 리스트 항목의 속성 대화상자: 파일 정보 표시(파일명은 이름/확장자로 분리된 두 개의 수정 가능한 텍스트 상자, 전체경로 옆 "경로 수정" 버튼 포함), 썸네일 버튼 줄 왼쪽의 "품번" 표시, 재생횟수 읽기 전용 표시·초기화, 3줄 메모 편집, 태그 마스터 목록에서 태그 선택(체크박스, 여러 개 동시 선택 가능), 배우 마스터 목록에서 배우 선택(오름차순 정렬된 콤보박스 + 추가 버튼, 여러 명 동시 지정 가능 — 배우 제거 시 `ActorCreditSync`로 그 배우의 Credits에서도 이 파일의 품번이 제거됨), 배우 이름/썸네일 클릭 시 배우 관리 창을 열고 그 배우로 이동(2026-08-03 추가), 배우 밑에 시리즈 마스터 목록에서 단일 선택하는 콤보박스(2026-08-09 추가 — [시리즈 마스터 목록 관리](#시리즈-마스터-목록-관리-seriesmanagerwindow) 참고), 썸네일 추가/삭제. 예전 `TagEditWindow`를 이 요구사항에 맞게 확장하며 이름을 변경한 것
- `RenameWindow.xaml` / `RenameWindow.xaml.cs` — 새 파일명을 입력받는 간단한 대화상자. `MainWindow`의 F2/우클릭 "이름변경"에서 사용 (`PropertiesWindow`의 파일명 텍스트 상자는 이 대화상자를 거치지 않고 바로 적용됨)
- `RenameHelper.cs` — 관리 리스트 항목이 가리키는 실제 파일을 rename/이동하고(`File.Move`), 성공 시 항목의 `FileName`/`FullPath`를 갱신하는 공용 로직. `TryRenameManagedItem`(`RenameWindow` 대화상자로 새 이름을 입력받음, 컨텍스트 메뉴/F2의 "이름변경"이 사용)은 내부적으로 대화상자 없는 `TryRenameManagedItemTo`(새 파일명을 바로 받아 rename, `PropertiesWindow`의 파일명 텍스트 상자가 사용)에 위임한다. `TryEditFullPath`(폴더/파일명 모두 바꿀 수 있음, `SaveFileDialog`로 새 위치를 선택, `PropertiesWindow`의 "파일 변경" 버튼)도 있다. `TryMoveToFolder`(`OpenFolderDialog`로 폴더를 선택, `PropertiesWindow`의 "경로 수정" 버튼)는 대화상자 없이 대상 폴더를 바로 받는 `TryMoveToSpecificFolder`(대상 폴더가 없으면 `Directory.CreateDirectory`로 새로 만듦, `PropertiesWindow`의 "파일 이동" 버튼도 코드로 계산한 폴더를 이 메서드에 바로 넘겨 사용)에 위임한다. 모든 이동/rename 메서드는 내부적으로 같은 썸네일/원본 파일 동반 이동 로직(`RenameAssociatedFile`)을 공유한다. 파일명이 실제로 바뀌는 두 메서드(`TryRenameManagedItemTo`/`TryEditFullPath`, `TryMoveToFolder`류는 파일명이 안 바뀌므로 제외)는 성공 시 `ActorCreditSync.OnFileRenamed`/`SeriesCreditSync.OnFileRenamed`를 호출해 배우/시리즈 Credits와도 동기화한다(2026-08-02 추가, `SeriesCreditSync` 연동은 2026-08-09 추가, **구현 완료**) — [배우 마스터 목록 관리](#배우-마스터-목록-관리-actormanagerwindow), [시리즈 마스터 목록 관리](#시리즈-마스터-목록-관리-seriesmanagerwindow) 참고. `TryRenameManagedItem`/`TryRenameManagedItemTo`/`TryEditFullPath`는 이제 `masterActors`뿐 아니라 `masterSeries`도 매개변수로 받는다. **자막 파일도 함께 rename/이동된다**(`RenameAssociatedSubtitles`, 2026-08-05 추가, **구현 완료**) — 같은 폴더에 동영상과 이름이 같거나("movie.srt") 그 뒤에 언어 코드가 "."으로 이어지는("movie.kor.srt") 자막 파일(확장자: srt/smi/ass/ssa/vtt/sub)이 있으면 동영상 이름 부분만 새 이름으로 바꿔 함께 옮긴다. 관리 리스트가 자막 파일 자체를 추적하지 않으므로(`ManagedVideoItem`에 별도 필드 없음) 매번 폴더를 스캔해서 파일명 패턴으로만 찾는다 — 썸네일/원본과 달리 대상 경로를 저장해두지 않는다. 대상 이름 충돌 등으로 실패해도(부가 정리이므로) 동영상 파일 rename/이동 자체는 유지된다. `TryMoveToFolder`/`TryMoveToSpecificFolder`(파일명은 안 바뀌고 폴더만 이동)도 자막을 함께 옮긴다.
- `FormatUtil.cs` — 파일 크기 표시 등 공용 포맷 유틸리티
- `ThumbnailHelper.cs` — 원본 이미지를 동영상 파일과 같은 폴더에 원본 그대로 + 320x240 리사이즈본으로 각각 저장하는 공용 로직 ([썸네일 관리](#썸네일-관리) 참고). 배우 썸네일용으로 원본 없이 100x100 리사이즈본만 저장하는 `CreateActorThumbnail`도 포함 ([배우 마스터 목록 관리](#배우-마스터-목록-관리-actormanagerwindow) 참고). "파일 없이 추가"된 항목(실제 폴더가 없음)을 위한 `CreatePlaceholderThumbnail`도 포함 — 고정 폴더 `PlaceholderThumbnailDir`(`E:\happy\thumbnail`)에 저장한다(2026-08-06 추가) — [관리 리스트](#2-관리-리스트-사용자가-지속적으로-관리하는-목록)의 "파일 없이 추가" 참고
- `OriginalImageWindow.xaml` / `OriginalImageWindow.xaml.cs` — 원본(리사이즈 전) 썸네일 이미지를 크게 보여주는 창. 클릭하면 닫힌다
- `DragDropImageHelper.cs` — 드래그 앤 드롭된 데이터(로컬 파일/웹 URL/렌더링된 비트맵)에서 이미지를 꺼내 임시 파일로 저장하는 공용 로직 ([인터넷 이미지 드래그 앤 드롭](#인터넷-이미지-드래그-앤-드롭-dragdropimagehelper) 참고)
- `AppPaths.cs` — 관리 리스트 전체(`library.json`, 유효/제거 항목 모두 `IsValid`로 구분해 한 파일에 저장)/태그 마스터 목록(`tags.json`)/배우 마스터 목록(`actors.json`)/배우 썸네일 폴더(`actresses/`)/시리즈 마스터 목록(`series.json`, 2026-08-09 추가)/설정(`settings.json`)의 기본 저장 경로 상수. `LegacyRemovedLibraryPath`(`removed.json`)도 있지만 예전 버전이 활성/제거 항목을 파일로 나눠 저장하던 흔적을 시작 시 한 번 병합·삭제하는 마이그레이션 전용이다 — [제거(Remove) 메커니즘](#제거remove-메커니즘) 참고
- `AppSettings.cs` — 마지막 보기 모드/정렬/필터/폴더 상태 모델 (JSON 직렬화 대상)
- `SettingsRepository.cs` — 설정 JSON 파일 읽기/쓰기 로직
- `SingleInstanceWindow.cs` — 창 종류별로 동시에 인스턴스가 하나만 열리도록 보장하는 제네릭 static 헬퍼(2026-08-04 추가, **구현 완료**) — [창 관리 정책](#창-관리-정책) 참고
- `WindowPositionMemory.cs` — 주요 창이 마지막으로 열려 있던 화면 위치를 창 종류별로 기억해두는 공용 저장소(2026-08-04 추가, **구현 완료**) — [창 관리 정책](#창-관리-정책) 참고
- `WindowSnapHelper.cs` — 메인 창과 주요 창들을 서로 가까이 끌어다 놓으면 가장자리가 자석처럼 달라붙는 기능(2026-08-07 추가, 2026-08-08 안쪽 정렬 제거·DWM 여백 보정·스냅 거리 축소로 개선, **구현 완료**) — [창 관리 정책](#창-관리-정책)의 "창 스냅(자석 붙기)" 참고

### 계획된 추가 파일 (아직 미구현)

아래는 [아키텍처](#아키텍처)·[로그](#로그) 절에서 요구하는 기능을 구현할 때 추가될 예정인 파일이다. 현재 코드베이스에는 존재하지 않는다.

```
Services/
    VideoLibraryService.cs   # 관리 리스트 비즈니스 로직 (Repository와 ViewModel 사이)
    TagService.cs            # 태그 마스터 목록 비즈니스 로직 (동기화 포함)
    ThumbnailService.cs      # 썸네일 유효성 검사·자동 생성

ViewModels/
    MainWindowViewModel.cs
    TagManagerViewModel.cs

Constants.cs                  # 하드코딩 문자열 상수 모음
```

## 주요 기능 (MainWindow)

창 상단에는 표준 `Menu`(파일/편집/보기/재생/도구)가 있어 아래에서 설명하는 기능들에 메뉴로도 접근할 수 있다 — **구현 완료**. 메뉴 항목은 새 로직을 추가하지 않고 기존 버튼/이벤트 핸들러를 그대로 호출하며(예: "파일 > 폴더 목록"은 `OpenFolderList_Click` 재사용), "보기 > 리스트 보기/아이콘 보기"는 체크 가능한 `MenuItem`으로 툴바의 라디오 버튼과 상태를 항상 동기화한다. **메뉴바 오른쪽 끝에는 현재 창 크기를 "{가로} x {세로}" 형식으로 실시간 표시한다**(`WindowSizeText`, 2026-08-07 추가, **구현 완료**) — 메뉴를 감싸는 `DockPanel`에서 `WindowSizeText`를 `DockPanel.Dock="Right"`로 먼저 배치하고 `Menu`가 나머지 공간을 채우는 방식으로 오른쪽 정렬했다. `Window.SizeChanged`(`MainWindow_SizeChanged`)에서 `ActualWidth`/`ActualHeight`를 정수로 반올림해 표시하며, 창 크기 조절/최대화 등 어떤 방식으로 크기가 바뀌어도 즉시 갱신된다. **배경색은 메뉴바와 동일하게 맞춘다**(`{x:Static SystemColors.MenuBarBrush}`, 2026-08-09 추가) — `WindowSizeText`(`TextBlock`)는 내용 크기만큼만 배경이 칠해져 여백/세로 방향에 빈틈이 남으므로, `Border`(`DockPanel.Dock="Right"`, `Background`를 이 색으로 지정)로 감싸 그 안에 `TextBlock`을 넣는 방식으로 `DockPanel` 행 전체 높이를 틈 없이 채운다. `Menu` 자신도 같은 브러시를 `Background`로 명시해(기본 테마 브러시에 의존하지 않고) 정확히 같은 색이 되도록 했다.

화면 본문은 **관리 리스트** 하나로 채워진다 (2026-08-02 변경 — 예전에는 **폴더 목록**과 좌우 분할로 나란히 표시했으나, 폴더 목록은 [서브 창](#1-폴더-목록-현재-열린-폴더-스캔-결과)으로 분리됨).

### 1. 폴더 목록 (현재 열린 폴더 스캔 결과)

**메인 창이 아니라 별도의 서브 창(`FolderListWindow`)에서 동작한다** (2026-08-02 변경 — 예전에는 메인 창 왼쪽에 항상 표시되는 패널이었음). 관리 리스트 툴바의 "목록 추가" 버튼(태그 관리 버튼 왼쪽) 또는 "파일 > 폴더 목록" 메뉴로 모달 대화상자(`ShowDialog`)로 연다. `MainWindow`의 관리 리스트 컬렉션(`_managedItems`)을 생성자에서 그대로 참조로 넘겨받으므로, 이 창에서 파일을 추가/재사용하면 같은 `ObservableCollection` 인스턴스를 공유해 메인 창에도 즉시 반영된다(별도의 동기화 코드 없이 기존 `CollectionChanged` 구독이 그대로 동작).

- **폴더 열기**: 사용자가 폴더를 선택하면 해당 폴더와 모든 하위 폴더를 재귀적으로 스캔하여 동영상 파일(mp4, mkv, avi, mov, wmv, flv, webm, m4v, mpg, mpeg)을 표시
- 이 목록은 창이 열려있는 동안만 보여지는 **임시 목록**이며, 그 자체로는 저장되지 않는다 (관리 리스트와 별개).
- **새로고침**: 현재 폴더를 다시 스캔
- **초기화**: 현재 스캔 결과와 선택된 폴더 경로를 모두 지우고 "폴더를 선택하세요." 초기 상태로 되돌린다 (`ResetFolderList_Click`) — **구현 완료**.
- **읽어온 파일 개수 표시**: 리스트 아래 오른쪽(파일 삭제/관리 리스트에 추가 버튼과 같은 줄, 버튼들 왼쪽)에 "파일 N개"로 스캔된 파일 개수를 보여준다(`FolderFileCountText`) — **구현 완료**. `LoadVideoFiles()`가 스캔을 마칠 때마다 갱신하고, "초기화" 시 비운다.
- **관리 리스트에 추가**: 폴더 목록에서 선택한 파일을 관리 리스트로 추가. 같은 파일명의 **보관된**(전에 제거되었거나 파일을 찾지 못해 자동으로 빠진) 데이터가 있으면 재사용 여부를 묻는다 — 자세한 내용은 [관리 리스트의 "추가"](#2-관리-리스트-사용자가-지속적으로-관리하는-목록) 참고
- **파일 삭제**: 선택한 파일(들)을 실제로 디스크에서 삭제한다(관리 데이터가 아니라 원본 파일 자체 삭제, 되돌릴 수 없음). 목록이 `SelectionMode="Extended"`라 여러 파일을 선택할 수 있으며, **여러 개 선택 시 전부 삭제 대상이 된다**(2026-08-02 수정, **구현 완료** — 예전에는 여러 개를 선택해도 그중 하나만 조용히 삭제되고 나머지는 안내 없이 그대로 남는 버그가 있었음). 개별 파일 삭제가 실패해도(권한 등) 나머지는 계속 삭제를 진행하고, 실패한 파일이 있으면 목록으로 안내한다.
- **마지막 폴더 기억**: 창을 열 때 `MainWindow`가 마지막으로 기억해둔 폴더(`_currentFolder`, [설정 관리](#설정-관리)의 `LastFolder`)를 생성자 인자로 넘겨받아 자동으로 스캔해서 보여준다. 창을 닫으면(`LastFolder` 프로퍼티로) `MainWindow`가 그 값을 다시 받아 `_currentFolder`를 갱신하고 바뀐 경우에만 설정을 저장한다.
- **닫기**: 창 하단의 "닫기" 버튼으로 서브 창을 닫고 메인 창으로 돌아간다. `MainWindow`는 창이 닫히면(`ShowDialog()` 반환 후) `_managedView.Refresh()`를 호출해, "재사용" 시 `IsValid`가 바뀐 항목이 필터에 바로 반영되도록 한다.
- (향후) 폴더 스캔은 비동기(Task)로 수행해 UI 스레드를 막지 않는다 — [성능](#성능) 참고

### 2. 관리 리스트 (사용자가 지속적으로 관리하는 목록)

**툴바 레이아웃(2026-08-09 재배치, 2026-08-10 동기화 버튼 줄 분리, 구현 완료)**: 관리 리스트 영역 상단은 위에서부터 아래 순서로 다음 6개 줄로 구성된다 — "관리 리스트"라는 제목 텍스트 자체는 삭제되었다.
1. 파일 갯수(`ManagedCountText`) + 자동 저장 위치(`LibraryPathText`)가 한 줄, 그 밑에 안내 문구("헤더를 우클릭하면...")가 2번째 줄.
2. 보기 모드 라디오(리스트 보기/아이콘 보기) + "제거된 항목도 표시" 체크박스(2026-08-09 위치 변경 — 예전에는 필터 영역에 있었으나 이 줄로 옮겨짐, 동작은 동일) — "저장"/"열기"/"다른 이름으로 저장" 버튼은 삭제되었지만 기능은 그대로 유지된다(파일 메뉴 + Ctrl+S로 계속 사용 가능).
3. "파일 없이 추가" / "목록 추가" / "재생" 버튼.
4. "속성 (F1)" / "태그 관리" / "배우 관리" / "시리즈 관리" 버튼.
5. "배우 동기화" / "시리즈 동기화" 버튼(2026-08-10 추가, "속성" 버튼 바로 밑 줄 — 처음에는 4번째 줄에서 "배우/시리즈 관리" 버튼 옆에 있었으나 별도 줄로 분리됨).
6. 필터 영역 — 아래 "검색(필터)" 항목 참고.

"이름변경"/"제거" 버튼도 삭제되었지만(위 3번 줄에서 제외) F2/Del 단축키, 행 우클릭 메뉴, "편집" 메뉴로 기능은 동일하게 유지된다 — 새 로직 없이 기존 핸들러(`RenameManaged_Click`/`RemoveFromManagedList_Click`)를 그대로 참조하는 진입점만 없앤 것이다. 오른쪽 썸네일 뷰어는 이 6개 줄 전체 높이에 걸쳐(`Grid.RowSpan="6"`) 그대로 유지된다.

- 폴더 목록과 별도의 영역/컨트롤로 표시되며, 내부적으로는 **JSON 파일로 영속 저장**된다.
- **"목록 추가" 버튼**: 위 3번째 줄, "파일 없이 추가" 버튼 오른쪽에 있다(2026-08-02 추가, 2026-08-09 위치 변경) — **구현 완료**. [폴더 목록](#1-폴더-목록-현재-열린-폴더-스캔-결과) 서브 창(`FolderListWindow`)을 여는 진입점이며, "파일 > 폴더 목록" 메뉴와 동일한 `OpenFolderList_Click` 핸들러를 공유한다.
- **관리되는 파일 개수 표시**: 위 1번째 줄 맨 왼쪽에 `(전체 N / 썸네일 N / 제거됨 N)` 형식으로 표시된다(`ManagedCountText`) — **구현 완료** (2026-08-09 변경 — 예전에는 "관리 리스트" 제목 밑에 있었으나 제목 자체가 삭제되면서 맨 위 줄로 올라옴). **전체**는 `_managedItems`에 들어있는 모든 항목 수(활성+제거됨 합산), **썸네일**은 그중 `HasThumbnail`이 true인 개수, **제거됨**은 `IsValid`가 false인 개수다. 항목이 추가/제거/완전삭제되거나(`ManagedItems_CollectionChanged`) 개별 항목의 속성이 바뀔 때마다(`ManagedItem_PropertyChanged`, 예: 썸네일 지정·삭제) `UpdateManagedCountDisplay()`로 다시 계산된다. **시작 시 로딩 중에는 이 재계산을 건너뛴다**(2026-08-02 최적화, **구현 완료**) — `LoadInitialData()`가 항목을 하나씩 `Add`할 때마다 매번 전체를 다시 스캔하면 로딩이 O(n²)가 되므로, 로딩 중(`_suppressAutoSave`)에는 건너뛰고 로딩이 끝난 뒤 한 번만 계산한다.
- **추가**: 폴더 목록에서 선택한 파일을 관리 리스트에 추가. 같은 파일명의 **제거된 데이터**가 있으면 재사용 여부를 확인 대화상자로 물어본다 — 예를 선택하면 재생횟수/태그/배우/메모/썸네일은 그대로 유지한 채 경로(`FullPath`)·크기·수정일만 새로 스캔된 파일 것으로 갱신하고 다시 활성화한다. 아니요를 선택하면 완전히 새 항목으로 추가한다. → [제거(Remove) 메커니즘](#제거remove-메커니즘) 참고
- **드래그 앤 드롭으로 추가** (2026-08-05 추가, **구현 완료**): 탐색기에서 동영상 파일을 관리 리스트 영역(리스트 보기/아이콘 보기 모두, `MainWindow.xaml`의 두 뷰를 감싸는 `Grid`에 `AllowDrop`)으로 직접 끌어다 놓으면 폴더 목록 창을 거치지 않고 바로 추가된다. **여러 파일을 한 번에 끌어다 놓을 수 있다**(`DataFormats.FileDrop`이 드롭된 모든 경로를 배열로 한 번에 넘겨주므로 별도 처리 없이 지원됨). 놓은 파일 중 동영상 확장자가 아닌 것은 조용히 걸러진다(전부 동영상이 아니면 안내 메시지). 재사용 확인 대화상자 등 추가 로직 자체는 위 "추가"와 완전히 동일한 코드(`ManagedListImporter.AddFiles`)를 공유한다 — [폴더 목록](#1-폴더-목록-현재-열린-폴더-스캔-결과)의 "관리 리스트에 추가"와 이 드래그 앤 드롭 둘 다 `ManagedListImporter`를 호출하는 얇은 진입점일 뿐이다.
  - **이미 관리 중인 파일은 목록으로 한 번에 안내한다** (2026-08-05 추가, **구현 완료**): 여러 파일을 한 번에 추가할 때 그중 일부가 이미 활성 상태로 관리 리스트에 있으면(전에는 조용히 건너뛰기만 하고 아무 안내가 없었음), 건너뛴 파일명을 전부 모아뒀다가 추가 처리가 모두 끝난 뒤 **대화상자 하나에 목록으로** 보여준다(파일마다 따로 팝업을 띄우지 않음). 같은 파일명의 **제거된** 데이터에 대한 재사용 여부 확인(예/아니요)은 이 목록화 대상이 아니다 — 그건 항목별로 실제 결정이 필요한 별개의 동작이라 기존처럼 파일마다 확인 대화상자를 띄운다.
- **제거** (버튼/메뉴 이름: "제거", 예전 이름 "목록에서 제거"): 관리 리스트에서 선택한 항목을 활성 목록에서 뺀다 (실제 동영상 파일은 삭제되지 않음 — 실제 파일 삭제 기능은 폴더 목록 쪽에 별도로 있음). **데이터 자체는 사라지지 않고 `IsValid = false`로 표시되어 유지**되며(같은 `library.json`에 그대로 남아있음), "제거된 항목도 표시" 체크박스로 다시 볼 수 있고 나중에 같은 파일명으로 다시 추가하면 재사용할 수 있다 → [제거(Remove) 메커니즘](#제거remove-메커니즘) 참고. **여러 항목을 동시에 선택해 한 번에 제거할 수 있다**(아래 "다중 선택" 항목 참고)
- **재생**: 선택한 파일을 OS 기본 연결 프로그램으로 실행, 재생 시 해당 항목의 재생횟수(PlayCount) 증가
  - **리스트 보기**: 행(row)의 어느 영역을 더블클릭해도 재생된다 (`ListViewItem`의 `MouseDoubleClick` 이벤트를 `ItemContainerStyle`에서 처리, `ManagedListRow_MouseDoubleClick`) — **구현 완료**.
  - **아이콘 보기**: 파일명 텍스트를 더블클릭해야만 재생된다(`FileNameCell_MouseLeftButtonDown`). 카드의 다른 영역(썸네일/재생횟수/태그 등)을 더블클릭해도 재생되지 않는다.
- **우클릭 팝업 메뉴**: 행(항목)을 우클릭하면 컨텍스트 메뉴가 뜬다 — 리스트 보기/아이콘 보기 모두 동일하게 지원.
  - **재생**: 위 재생과 동일
  - **이름변경** (단축키 **F2**): `RenameWindow`로 새 파일명을 입력받아 실제 디스크 파일을 `File.Move`로 rename하고, 성공 시 항목의 `FileName`/`FullPath`를 갱신한다 (관리 리스트에서의 이름 표시만 바뀌는 게 아니라 실제 파일 이름이 바뀐다). 이때 같은 폴더의 `{예전 이름}.thumbnail.jpg`/`{예전 이름}.original{확장자}` 파일이 있으면 새 이름에 맞춰 함께 rename하고 `ThumbnailPath`/`ThumbnailOriginalPath`도 갱신한다(`RenameHelper.RenameAssociatedFile`) — **구현 완료**. 대상 이름이 이미 존재하는 등 썸네일/원본 rename이 실패해도 동영상 파일 이름 변경 자체는 그대로 유지된다(부가 정리로 취급, 조용히 건너뜀).
  - **속성** (단축키 **F1**): [속성 창](#속성-창-propertieswindow) 참고
  - **복구**: 선택 항목이 제거된(`IsValid = false`) 상태면 다시 활성으로 되돌린다(`RestoreManaged_Click`, `item.IsValid = true`). **여러 항목을 선택했으면 그중 제거된 상태인 항목만 골라 복구하고, 이미 활성 상태인 항목은 조용히 건너뛴다**(2026-08-02 변경 — 예전에는 단일 선택만 가능했고, 선택 항목이 활성 상태면 안내 메시지만 표시했음. 지금은 선택한 항목이 모두 활성 상태일 때만 안내 메시지를 표시한다). "관리 리스트에 추가"로 같은 파일명을 다시 스캔해서 재사용을 거치지 않고도 바로 복구할 수 있는 지름길이다 — **구현 완료**.
  - **삭제** (단축키 **Del**): 기존 "제거"와 동일한 동작이다(`RemoveFromManagedList_Click` 핸들러를 그대로 공유) — [제거(Remove) 메커니즘](#제거remove-메커니즘)에 따라 `IsValid = false`로 바뀐다 — **구현 완료**.
  - **완전삭제**: `PermanentlyDeleteManaged_Click`. 확인 후 `_managedItems`에서 항목 자체를 제거한다 — 다음 자동 저장부터 `library.json`에 다시 나타나지 않는다(되돌릴 수 없음). 실제 동영상 파일은 삭제하지 않는다 — **구현 완료**.
  - **다중 선택** (2026-08-02 추가, **구현 완료**) — 리스트 보기/아이콘 보기 모두 `SelectionMode="Extended"`로 여러 항목을 Ctrl/Shift+클릭으로 동시에 선택할 수 있다. **복구/삭제(제거)/완전삭제 세 가지는 선택한 모든 항목에 한 번에 적용된다**(`GetSelectedManagedItems()`가 선택된 항목 목록을 반환, 확인 대화상자 문구도 1개 선택 시 파일명을, 여러 개 선택 시 "선택한 N개 항목"으로 표시). 속성/이름변경/재생은 여전히 단일 항목 대상이다(`GetSelectedManagedItem()`, 여러 개를 동시에 이름변경/재생하는 것은 의미가 없으므로 다중화하지 않음).
  - 우클릭한 항목이 이미 선택되어 있던 다중 선택의 일부이면 선택을 그대로 유지하고(다중 선택 대상 메뉴 동작을 위함), 선택되지 않은 항목을 우클릭하면 그 항목 하나만 선택한다(`ManagedListItem_PreviewMouseRightButtonDown`/`ManagedIconItem_PreviewMouseRightButtonDown`, 탐색기와 동일한 동작 — 2026-08-02 변경, 예전에는 우클릭 시 항상 그 항목 하나만 선택되어 다중 선택이 불가능했음).
  - F1/F2/Del 단축키는 `MainWindow`의 `PreviewKeyDown`에서 전역으로 처리하며, 관리 리스트에서 선택된 항목(들)을 대상으로 동작한다(Del은 다중 선택 시 선택된 모든 항목에 적용됨). **Del 키는 포커스가 `TextBox`(예: 파일명 검색 상자)에 있을 때는 무시된다**(`e.OriginalSource is not TextBox` 체크) — 그렇지 않으면 텍스트 편집 중 Delete로 글자를 지우려다 항목이 삭제되는 문제가 생기기 때문 — **구현 완료**.
- **정렬**:
  - 리스트 보기의 각 컬럼 헤더를 클릭하면 해당 컬럼 기준으로 정렬 (클릭할 때마다 오름차순/내림차순 토글). 파일명/크기/재생횟수/태그/배우/메모 모두 정렬 가능.
  - (예전에는 재생횟수 기준 정렬을 위한 전용 버튼이 툴바에 별도로 있었으나, 헤더 클릭으로도 충분히 정렬할 수 있어 제거함 — **재생횟수 컬럼이 보일 때만** 재생횟수 기준 정렬이 가능하다.)
- **검색(필터) — 리스트 위 상시 UI**: 관리 리스트 위쪽(툴바 5번째 줄)에 필터 영역이 항상 표시된다 (더 이상 헤더를 우클릭해서 열지 않는다).
  - **시리즈 선택**(2026-08-09 추가, **구현 완료**): 필터 영역 맨 왼쪽에 있는 콤보박스(`SeriesFilterComboBox`)로, 태그/배우 필터와 달리 팝업이 아니라 **항상 보이는 단일 선택 콤보박스**다(시리즈 자체가 항목당 하나만 지정되므로 — [시리즈 마스터 목록 관리](#시리즈-마스터-목록-관리-seriesmanagerwindow) 참고). 항목은 "(전체)"(필터 없음을 나타내는 센티널, `MainWindow.AllSeriesLabel`) + 시리즈 마스터 목록(이름 기준 오름차순)이며, 시리즈가 추가/삭제될 때마다(`MasterSeries_CollectionChanged`) `RefreshSeriesFilterComboBox()`가 항목과 현재 선택 상태를 다시 맞춘다. 선택을 바꾸면 `item.Series`가 정확히 일치하는 항목만 남기고 필터링되며(대소문자 무시), 선택 상태는 다른 필터들과 마찬가지로 [설정 관리](#설정-관리)에 저장되어 다음 실행에도 유지된다.
  - **파일명 검색**: 텍스트 상자에 입력하는 즉시(`TextChanged`) 부분 일치로 필터링된다.
  - **폴더명 검색**(2026-08-07 추가, **구현 완료**): "파일명 검색" **바로 오른쪽**에 있다(`FolderFilterTextBox`). `ManagedVideoItem.FolderName`(파일이 들어있는 폴더의 마지막 이름 한 단계, "폴더명" 컬럼과 같은 값) 기준으로 입력하는 즉시 부분 일치로 필터링된다 — 파일명 검색과 완전히 동일한 패턴(`_folderFilter`, `FolderFilterTextBox_TextChanged`)이며 "필터 초기화" 대상에도 포함된다.
  - **태그 필터**: "태그 필터" 버튼을 클릭하면 태그 마스터 목록 전체를 체크박스로 보여주는 팝업이 뜨고, 선택한 태그를 포함한 항목만 표시된다 (여러 태그 선택 가능).
  - **배우 필터**: "배우 필터" 버튼을 클릭하면 배우 마스터 목록 전체(이름 기준 오름차순)를 체크박스로 보여주는 팝업이 뜨고, 선택한 배우가 지정된 항목만 표시된다 (**여러 명 동시 선택 가능**) — **구현 완료** (태그 필터와 완전히 동일한 UI/로직 패턴, `ActorFilterPopup`/`ActorFilterList`/`ActorFilterApply_Click`/`ActorFilterClear_Click`).
  - **제거된 항목도 표시**: 체크박스(`ShowRemovedItemsCheckBox`)를 켜면 [제거(Remove) 메커니즘](#제거remove-메커니즘)에 의해 제거된(`IsValid = false`) 항목도 목록에 함께 표시된다 — **구현 완료**. **툴바 2번째 줄(보기 모드 라디오 오른쪽)에 있다**(2026-08-09 위치 변경 — 예전에는 이 필터 영역 줄에 있었으나, 필터라기보다 표시 옵션에 가까워 보기 모드 옆으로 옮겼다). 기본값은 꺼짐이며 [설정 관리](#설정-관리)에 저장된다. 이렇게 표시된 제거 항목은 **글자 색이 회색**으로 보여 활성 항목과 구분된다(리스트 보기/아이콘 보기 모두 `ItemContainerStyle`에 `DataTrigger Binding="{Binding IsValid}"`로 `Foreground="Gray"`를 적용, 셀 내부 `TextBlock`들은 별도 `Foreground`를 지정하지 않아 자동으로 상속받는다). **`IsValid = true`이면서 `IsExist = false`인 항목**(파일을 못 찾는 활성 항목)은 `MultiDataTrigger`로 주황빛(`OrangeRed`)으로 표시된다(2026-08-06 추가) — **구현 완료**.
  - "필터 초기화" 버튼으로 파일명/폴더명 검색어, 시리즈 선택, 태그 선택, 배우 선택을 한 번에 초기화한다 (제거된 항목 표시 체크박스는 초기화 대상이 아니다).
  - (향후) 파일명 + 태그 + 재생횟수 조건을 조합하는 고급 검색
  - 필터 영역 위(툴바 1번째 줄 2번째 줄)의 "(헤더를 우클릭하면 표시할 컬럼을 고를 수 있습니다)" 같은 안내 문구는 `TextWrapping="Wrap"`을 지정해, 창 폭이 좁아 한 줄에 다 들어가지 않으면 잘리지 않고 여러 줄로 표시된다 — **구현 완료** (2026-08-09 변경 — 예전에는 필터 영역과 같은 줄에 `DockPanel`로 배치했으나, 툴바 재배치로 맨 위 파일 갯수/자동 저장 위치 밑 2번째 줄로 옮겨졌다).
- **헤더 우클릭 → 표시할 컬럼 선택**: (예전에는 헤더 우클릭이 필터였지만) 이제는 컬럼 표시/숨김을 고르는 팝업이 뜬다. 파일명은 항상 표시되며 토글 대상이 아니다. 크기/재생횟수/태그는 기본으로 표시되고, 배우/메모/폴더명/**코드/시리즈/출시일**(2026-08-09 추가)은 기본으로 숨겨져 있다. 코드/시리즈/출시일 컬럼은 각각 `ManagedVideoItem.Code`/`Series`/`ReleaseDate`에 그대로 바인딩되는 단순 텍스트 컬럼이다(태그 컬럼처럼 별도 `CellTemplate` 없이 `DisplayMemberBinding`만 사용, 배우/메모/폴더명 컬럼과 동일한 패턴). 헤더 클릭 정렬도 지원한다(`HeaderTextToSortProperty`에 "코드"/"시리즈"/"출시일" 매핑 추가). 컬럼 표시 여부는 [설정 관리](#설정-관리)에 저장되어 다음 실행 시에도 유지된다 — **구현 완료**.
  - **폴더명 컬럼** (2026-08-02 추가, **구현 완료**): `ManagedVideoItem.FolderName`(`[JsonIgnore]` 계산 속성, `FullPath`에서 `Path.GetDirectoryName` → `Path.GetFileName`으로 도출)을 표시한다. **전체 경로가 아니라 파일이 들어있는 폴더의 마지막 이름 한 단계만** 보여준다(예: `E:\...\쿠로카와 사리나\a.mp4` → `쿠로카와 사리나`). `FullPath`가 바뀌면(이름 변경/경로 수정/폴더 이동) `FolderName`도 함께 `PropertyChanged`를 발생시켜 화면이 즉시 갱신된다.
  - **썸네일(아이콘) 컬럼**: 팝업 맨 위에 "썸네일 (맨 앞)" 체크박스가 있다. 켜면 파일명 컬럼보다 앞(리스트 맨 앞)에 읽기 전용 컬럼이 삽입되어, 항목에 썸네일이 지정되어 있는지(`ManagedVideoItem.HasThumbnail`)를 한눈에 보여준다. 기본값은 꺼짐이며, 다른 컬럼처럼 [설정 관리](#설정-관리)에 저장된다 — **구현 완료**. **셀 표시는 체크박스가 아니라 파일 아이콘이다** — 썸네일이 있는 항목만 아이콘이 보이고(`HasThumbnail = true`일 때 `Visibility=Visible`), 없는 항목은 빈 칸으로 보인다(`Visibility=Collapsed`). 표시 전용이며 클릭해도 항목의 썸네일이 바뀌지 않는다. **리스트 헤더에는 텍스트를 표시하지 않는다**(`GridViewColumn.Header = string.Empty`) — 아이콘 자체로 의미가 드러나므로. 헤더 클릭 정렬은 계속 동작한다(`HeaderTextToSortProperty`에서 빈 문자열 헤더를 `HasThumbnail` 정렬로 매핑).
    - **아이콘(2026-08-02 변경, 구현 완료)**: 이모지(🖼️) 대신 **Windows 탐색기가 `.png` 파일에 실제로 표시하는 셸 아이콘**을 그대로 가져와 쓴다. `WindowsIconHelper.PngFileIcon`이 `SHGetFileInfo`(P/Invoke, `SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES`로 실제 파일 없이 확장자만으로 조회)로 아이콘 핸들을 얻고, `Imaging.CreateBitmapSourceFromHIcon`으로 `ImageSource`로 변환한 뒤 `Freeze()`해서 프로세스 안에서 캐시해 재사용한다(핸들은 `DestroyIcon`으로 즉시 해제해 누수 방지). XAML에서는 `<Image Source="{x:Static local:WindowsIconHelper.PngFileIcon}" .../>`로 참조한다.
    - **컬럼 너비(2026-08-02 변경, 구현 완료)**: `GridViewColumn.Width = 32`로 **고정**한다 — 예전에는 `double.NaN`(내용 기준 자동 크기)이었는데, 빈 헤더 텍스트 때문에 컬럼이 아이콘보다 좁게 계산되어 아이콘이 잘려 안 보이는 문제가 있었다.
  - **재생횟수 컬럼은 가운데 정렬**, **크기 컬럼은 오른쪽 정렬**로 셀 값이 표시된다(`PlayCountCellTemplate`/`SizeCellTemplate`) — **구현 완료**.
- **보기 모드 전환**:
  - 리스트 보기: 표 형태. 컬럼은 파일명(고정) + 헤더 우클릭에서 선택한 컬럼(크기/재생횟수/태그/배우/메모). 태그는 개별 태그(chip) 형태로 각 단어가 구분되어 보인다.
  - 아이콘 보기: 썸네일 그리드 형태. 각 아이템의 오른쪽(썸네일 이미지) 영역을 클릭하면 파일 선택 대화상자를 통해 이미지 파일을 해당 항목의 썸네일로 지정할 수 있다. 카드 하단에는 파일명/재생횟수에 이어 해당 항목의 태그가 칩(chip) 형태로 표시된다 (리스트 보기의 태그 컬럼과 같은 스타일, `Tags`가 비어있으면 아무것도 표시되지 않음) — **구현 완료**.
- **썸네일 관리**: [썸네일 관리](#썸네일-관리) 절 참고
- **화면 표시 컬럼**: 파일명(고정) + 사용자가 헤더 우클릭에서 선택한 컬럼(썸네일/크기/재생횟수/태그/배우/메모). 썸네일 컬럼만 파일명보다 앞에 삽입되고 나머지는 파일명 뒤에 추가된다. 수정일/전체경로는 JSON에는 저장되지만 컬럼으로는 노출하지 않는다 (속성 창·하단 상세정보 패널에서는 노출됨).
- **태그 지정**: 관리 리스트 항목에 태그를 붙일 때는 자유 텍스트 입력이 아니라, 태그 마스터 목록에서 원하는 태그를 체크박스로 선택하는 방식이다 ([속성 창](#속성-창-propertieswindow)에서 처리, 여러 태그 동시 선택 가능). 리스트 보기에서 항목의 태그 칩 영역을 클릭해도 같은 속성 창이 열린다(우클릭 메뉴의 "속성"/상단 "속성" 버튼과 동일 동작) — **구현 완료**.
- **선택된 항목 상세 정보**: 관리 리스트 하단에 현재 선택된 항목의 모든 정보(파일명, 크기, 수정일, 전체 경로, 재생횟수, 배우, 메모, 태그)를 보여주는 패널이 있다. 메모는 별도 줄에 `TextWrapping="Wrap"` + `MaxHeight`(약 2줄 분량)로 표시되어 짧은 메모는 그대로, 긴 메모는 2줄까지 보이고 그 이상은 잘린다 — **구현 완료**. 태그는 해당 항목에 실제로 지정된 것만 표시한다(마스터 목록 전체가 아님). 리스트 보기/아이콘 보기 중 어느 쪽에서 선택하든 동일하게 갱신되며, 선택된 항목이 없으면 "선택된 항목이 없습니다" 안내만 보인다.
- **선택 항목 동작 버튼(속성/재생)**: 관리 리스트 상단 툴바에 위치한다("재생"은 3번째 줄, "속성 (F1)"은 4번째 줄) — **구현 완료** (예전에는 관리 리스트 맨 하단에 있었으나 상단으로 이동함). 우클릭 메뉴/F1과 동일한 핸들러를 공유한다. **"이름변경"/"목록에서 제거" 버튼은 2026-08-09에 삭제되었다** — F2/Del 단축키와 우클릭 메뉴, "편집" 메뉴로 기능은 동일하게 유지된다.
- **파일 없이 추가** (2026-08-06 추가, **구현 완료**, 2026-08-09 위치 변경): 툴바 3번째 줄, "목록 추가" 버튼 **왼쪽**에 있다(예전에는 "속성 (F1)" 버튼 왼쪽이었으나 툴바 재배치로 옮겨짐). 아직 실제로 구하지 못한 작품 등을 미리 기록해두기 위해, 실제 동영상 파일 없이 관리 리스트 항목을 만들 수 있다. 클릭하면 `ManagedVideoItem.IsPlaceholder = true`로 표시된 새 항목(아직 `_managedItems`에는 추가되지 않음)에 대해 [속성 창](#속성-창-propertieswindow)이 "새 항목" 모드로 열리고, 파일명 등 정보를 입력받는다. **"확인"(또는 닫기)을 눌렀을 때 파일명을 입력했으면 그 시점에 비로소 관리 리스트에 추가되고, 파일명을 입력하지 않았으면(빈 채로 두면) 조용히 추가가 취소된다** — 별도 확인 메시지 없이 그냥 아무 일도 일어나지 않는다.
  - **제거된 항목으로 취급된다**(2026-08-06 변경, **구현 완료** — 예전에는 활성 상태로 추가되어 목록에 바로 노출됐으나, 실제 파일이 없는 항목이 일반 항목과 똑같이 클릭·재생·정렬 대상이 되면서 여러 문제가 있어 [제거(Remove) 메커니즘](#제거remove-메커니즘)에 편입했다) — 추가되는 즉시 `IsValid = false`로도 설정되어 `library.json`에 다른 항목과 함께(구분값만 다르게) 저장되고, 기본 목록에는 보이지 않으며(회색으로 "제거된 항목도 표시" 체크 시에만 노출) `IsPlaceholder = true`인 항목은 [파일 존재 여부 확인](#오류-처리)(`UpdateFileExistence`)에서 항상 `IsExist = false`로 취급된다.
  - **추가되면 곧바로 선택되고 스크롤되어 보인다**(2026-08-06 추가, **구현 완료**) — "제거된 항목도 표시"가 켜져 있어도, 새로 추가된 항목이 정렬 순서상 화면 밖에 있으면(수천 개 중 임의 위치) 실제로는 추가됐는데도 눈에 안 띄어 "목록에 안 보인다"고 오인하기 쉬웠다. `MainWindow.OpenPropertiesWindow`가 새 항목 모드로 연 창이 닫힐 때(`isNewItem`이 true였고 커밋되어 `_managedItems`에 실제로 들어갔으면) `SelectAndScrollToManagedItem`으로 방금 추가된 항목을 선택하고 스크롤해서 바로 보여준다(리스트 보기/아이콘 보기 모두 지원, 태그/배우 마스터 목록 관리 창의 "추가된 항목 선택+스크롤" 패턴과 동일). **단, "제거된 항목도 표시"가 꺼져 있으면 선택/스크롤이 적용되어도 필터에 걸려 보이지 않는다** — 이 경우는 정상 동작(다른 제거된 항목과 동일하게 취급)이므로 체크박스를 켜야 확인할 수 있다.
  - **나중에 실제 파일이 추가되면 데이터를 재사용한다**(2026-08-06 추가, **구현 완료**) — [폴더 목록](#1-폴더-목록-현재-열린-폴더-스캔-결과)에 추가하거나 드래그 앤 드롭으로 추가할 때(`ManagedListImporter.AddFiles`)의 "제거된 데이터 재사용" 매칭이 `IsPlaceholder` 항목에 한해 **확장자를 뺀 이름**으로도 비교한다(대소문자 무시) — 플레이스홀더를 만들 때는 실제 파일의 확장자를 알 수 없어 "sdmu-102"처럼 확장자 없이 입력하는 경우가 흔하기 때문("sdmu-102" ↔ "sdmu-102.mp4"). 재사용을 선택하면(예/아니요 확인 대화상자는 기존과 동일) 재생횟수/태그/배우/메모/썸네일은 유지한 채 `FileName`/`FullPath`/크기/수정일을 새 파일 것으로 갱신하고 `IsValid`/`IsExist`를 모두 `true`로, `IsPlaceholder`를 `false`로 되돌려 평범한 항목으로 전환한다. **썸네일 파일도 새 동영상 파일과 같은 폴더로 함께 옮겨진다**(2026-08-07 추가, **구현 완료**) — 재사용 전 썸네일이 고정 폴더 `E:\happy\thumbnail`(플레이스홀더였을 때)나 예전 파일 위치에 있었다면, `RenameHelper.MoveThumbnailsToFolder`(rename 시 썸네일을 함께 옮기는 `RenameAssociatedFile`을 재사용)로 새 `FullPath`의 폴더로 이동하고 `ThumbnailPath`/`ThumbnailOriginalPath`를 갱신한다 — 그렇지 않으면 항목은 새 파일을 가리키는데 썸네일만 예전 위치에 남아 어긋나는 문제가 있었다. 이 이동은 플레이스홀더 재사용뿐 아니라 **일반 제거된 항목을 재사용할 때도 동일하게 적용된다**(파일이 다른 폴더로 옮겨진 뒤 재추가된 경우에도 같은 문제가 생길 수 있으므로).
  - 속성 창에서 실제 파일이 있어야 동작하는 기능(재생/파일 변경/경로 수정/파일 이동)은 비활성화된다(툴팁 "실제 파일이 없는 항목입니다.") — 파일명을 rename하는 것도 실제로는 `File.Move` 없이 `FileName`/`FullPath` 값만 갱신한다(`PropertiesWindow.CommitFileNameIfChanged`). **썸네일 추가/삭제는 계속 가능하다** — 동영상과 같은 폴더 대신 고정 폴더 `E:\happy\thumbnail`(`ThumbnailHelper.CreatePlaceholderThumbnail`)에 저장된다(아이콘 보기 클릭·드래그 앤 드롭으로 지정해도 동일하게 분기됨). **재생을 시도하면**(더블클릭/재생 버튼/우클릭 메뉴 모두) 파일을 열려 하지 않고 "실제 파일이 없는 항목입니다." 안내만 표시한다. 태그/배우/메모/코드/출시일은 일반 항목과 동일하게 편집할 수 있다.

### 제거(Remove) 메커니즘

**2026-08-06 대대적 재설계** — 예전에는 "제거됨" 여부를 `IsArchived` 하나의 bool로 표현하고 활성/제거 항목을 `library.json`/`removed.json` 두 파일로 나눠 저장했다. 지금은 **파일을 하나로 합치고**(`removed.json` 개념 자체를 없앰), 상태도 서로 독립된 **두 개의 축**으로 분리했다:

- **`IsValid`** — 사용자가 이 항목을 관리 리스트에 계속 두고 싶어 하는지("유효"한지). `false`면 예전의 "제거됨"과 같은 의미다.
- **`IsExist`** — `FullPath`의 실제 파일이 지금 디스크에 있는지. 시작 시(그리고 향후 재확인 시점마다) 모든 항목에 대해 다시 판단해서 갱신한다.

이 둘은 **서로 독립적**이다 — 예를 들어 외장 드라이브를 잠깐 분리해서 `IsExist = false`가 되어도 `IsValid`는 건드리지 않으므로, 사용자가 명시적으로 "제거"하지 않는 한 그 항목은 관리 리스트에서 사라지지 않고 계속 활성 상태로 남는다(예전에는 파일이 없어지면 자동으로 "제거됨" 취급되어 활성 목록에서 빠졌었다 — 이 자동 강등이 이번 재설계로 사라졌다). 네 가지 조합이 모두 의미가 있다:

| `IsValid` | `IsExist` | 의미 | 화면 표시 |
|---|---|---|---|
| true | true | 평범한 활성 항목 | 검정 글자 |
| true | false | 사용자는 계속 원하지만 지금은 파일을 못 찾음(드라이브 분리 등) | **주황빛 글자**(`OrangeRed`) — 기본 목록에 그대로 노출됨 |
| false | true | 사용자가 명시적으로 "제거"했지만 파일 자체는 존재 | 회색 글자, "제거된 항목도 표시" 체크 시에만 노출 |
| false | false | 제거됐고 파일도 없음(또는 "파일 없이 추가"로 만든, 실제 파일이 아예 없던 플레이스홀더) | 회색 글자, "제거된 항목도 표시" 체크 시에만 노출 |

- **저장**: `SaveLibrary()`가 `_managedItems` 전체(유효/제거 항목 모두)를 `ManagedListRepository.Save`로 `AppPaths.LibraryPath`(`library.json`) **하나에만** 저장한다.
- **로딩**: `LoadInitialData()`가 `library.json` 하나만 읽어 `_managedItems`를 채운다. **레거시 마이그레이션**(2026-08-06 추가): 예전 버전이 남긴 `removed.json`(`AppPaths.LegacyRemovedLibraryPath`)이 있으면 시작 시 한 번만 읽어 `IsValid = false`로 강제한 뒤 같은 컬렉션에 합치고, 병합이 끝나면 그 파일을 삭제한다 — 다음 실행부터는 이 단계가 다시 일어나지 않고(파일이 없으므로 건너뜀) 모든 항목이 `library.json` 하나에서만 로드된다. 예전 JSON의 `IsArchived` 필드는 새 모델에 더 이상 없는 속성이라 역직렬화 시 조용히 무시되고, `IsValid`/`IsExist`가 없는 예전 레코드는 클래스 필드 기본값(`true`/`true`)을 그대로 받는다 — `library.json`에서 읽은 항목은 이미 "활성"이었으므로 정확히 맞는 기본값이고, 뒤이은 파일 존재 여부 확인 단계가 `IsExist`를 실제 상태로 다시 맞춘다.
- **제거되는 경우**: 사용자가 "제거" 버튼/메뉴(예전 이름 "목록에서 제거")를 실행했을 때 → `item.IsValid = false`. **더 이상 파일이 없어졌다고 자동으로 제거되지 않는다** — 아래 "파일 존재 여부 확인" 참고.
- **파일 존재 여부 확인**(`MainWindow.UpdateFileExistence`, 시작 시 자동 실행): **활성/제거 여부와 무관하게 모든 항목**을 대상으로 `File.Exists(FullPath)`를 다시 확인해 `IsExist`를 갱신한다("파일 없이 추가"된 `IsPlaceholder` 항목은 항상 `false`로 취급 — 실제 폴더가 없는 경로이므로). `IsValid`는 이 과정에서 전혀 바뀌지 않는다.
- **화면 노출**: 기본적으로 `IsValid = false`인 항목은 관리 리스트(리스트 보기/아이콘 보기, 정렬, 필터)에 나타나지 않는다(`FilterManagedItem`에서 제외). 단, **관리 리스트 필터 영역의 "제거된 항목도 표시" 체크박스**(`ShowRemovedItemsCheckBox`)를 켜면 함께 표시된다 — 이 상태는 `AppSettings.ShowRemovedItems`로 저장되어 다음 실행에도 유지된다. `IsValid = false`인 항목은 글자 색이 회색으로, `IsValid = true`이면서 `IsExist = false`인 항목(파일을 못 찾는 활성 항목)은 주황빛으로 표시되어 구분된다(`ItemContainerStyle`의 `DataTrigger`/`MultiDataTrigger`).
- **복원 방법 두 가지**:
  1. **재사용**(암묵적): "관리 리스트에 추가" 시, 추가하려는 파일과 **파일명이 같은**(대소문자 **구분**, "파일 없이 추가"로 만든 항목은 확장자 없는 이름으로도 매칭) `IsValid = false`인 항목이 있으면 재사용 여부를 묻는다. "예"를 선택하면 그 항목의 `FileName`/`FullPath`/`SizeBytes`/`ModifiedDate`를 새 파일 것으로 갱신하고 `IsValid`/`IsExist`를 모두 `true`로, `IsPlaceholder`를 `false`로 되돌려 다시 활성화한다 — 재생횟수/태그/배우/메모/썸네일은 그대로 유지된다. 파일명이 아니라 경로로 매칭하지 않는 이유는, 파일이 이동된 뒤에도 같은 파일로 인식해 데이터를 이어가기 위함이다.
  2. **복구**(명시적, **구현 완료**): "제거된 항목도 표시"로 항목을 보이게 한 뒤, 우클릭 메뉴의 "복구"를 누르면 폴더를 다시 스캔하지 않고도 바로 `IsValid = true`로 되돌릴 수 있다 (`RestoreManaged_Click`).
- **완전삭제(하드 삭제, 구현 완료)**: "제거"(소프트 삭제, `IsValid = false`)와 별개로, **관리 데이터 자체를 완전히 지우는** 기능이 두 곳에 있다 — 관리 리스트 우클릭 메뉴의 "완전삭제"(`PermanentlyDeleteManaged_Click`), `PropertiesWindow` 하단의 "완전 삭제" 버튼(`DeleteButton_Click` → `PropertiesWindow.PermanentlyDeleted = true`로 표시하고 닫으면 `MainWindow.OpenPropertiesWindow`가 감지해 처리). 둘 다 확인 대화상자를 거친 뒤 `_managedItems.Remove(item)`으로 컬렉션 자체에서 항목을 제거한다 — 이후 자동 저장부터는 `library.json`에 **다시 나타나지 않는다** (되돌릴 수 없음). 실제 동영상 파일은 삭제하지 않는다(관리 데이터만 삭제 — 파일 삭제는 폴더 목록 쪽의 별도 기능).
- **알려진 한계**: 재사용/복구 매칭은 파일명 문자열 완전 일치(`StringComparison.Ordinal`, 대소문자 구분, "파일 없이 추가" 항목은 확장자 제외 대소문자 무시)만 사용한다 — 크기/해시 등으로 더 정교하게 구분하지 않는다. 동일한 파일명을 가진 서로 다른 제거된 항목이 여러 개 있으면 그중 하나가(순서 임의) 매칭된다.
- 태그/배우 마스터 목록에서 이름 변경/삭제 시의 동기화는 활성/제거된 항목 모두에 적용된다 (전체 `_managedItems`를 대상으로 하므로).
- "다른 이름으로 저장"/"열기"(수동 내보내기/가져오기)는 `_managedItems` 전체(유효+제거됨)를 하나의 파일로 다룬다 — 예전과 동일(원래도 파일 분리와 무관했음).

### 속성 창 (`PropertiesWindow`)

우클릭 메뉴의 "속성", 태그 칩 영역 클릭, 상단 "속성" 버튼으로 열리는 대화상자 (기본 크기 500x1200, 2026-08-02 변경 — 예전 450x1000, 그 전에는 세로 950) — **구현 완료**.

**레이아웃(2026-08-02 변경)**: 창 내용은 `ScrollViewer`로 감싸져 있고, "완전 삭제"/"확인"/"취소" 버튼 줄만 그 밖(창 맨 아래)에 고정되어 있다 — **구현 완료**. 배우를 여러 명 추가해 배우 썸네일 영역이 늘어나는 등 내용이 길어져도 버튼 줄은 항상 보이며, 내용 영역만 세로로 스크롤된다(예전에는 내용이 늘어나면 "확인" 버튼이 창 밖으로 밀려나 보이지 않는 문제가 있었음).

1. 대화상자로 열린다 (모달, `Owner`는 `MainWindow`).
2. 항목이 가진 파일 정보를 보여준다: 파일명, 크기, 수정일(**크기와 수정일은 같은 줄에 나란히 배치된다**, 2026-08-02 변경 — 예전에는 각각 별도 줄이었음), 전체 경로(읽기 전용 텍스트 상자, 선택/복사 가능), 재생횟수. **파일명은 이름과 확장자가 분리된 두 개의 텍스트 상자로, 같은 줄에 "." 구분자를 사이에 두고 나란히 배치된다**(2026-08-02 변경, **구현 완료** — 예전에는 확장자까지 포함한 파일명 전체가 텍스트 상자 하나였음). 둘 중 하나라도 포커스를 잃으면(`FileNamePart_LostFocus`) 두 값을 합쳐서 기존과 동일하게 rename이 적용된다.
3. **"코드" 입력란이 있다** (2026-08-02 추가, **구현 완료**). `_item.Code`가 비어있으면 창을 열 때 파일명에서 자동으로 제안값을 채운다 — `ManagedVideoItem.DeriveCode(fileName, fullPath)`의 생성 규칙: **① 파일명의 첫 번째 "-" 이전 부분**을 사용하고, **② "-"를 찾지 못하면 폴더명**(`FolderName`과 동일한 규칙, 마지막 폴더 이름 한 단계)을 대신 사용한다. 이미 저장된 코드 값이 있으면 그 값을 그대로 보여준다. 텍스트 상자는 자유롭게 수정 가능하며(**직접 수정 가능**), "확인" 클릭 시 `_item.Code`에 커밋된다("파일 이동" 버튼을 쓰면 그 시점에도 바로 커밋됨). **파일명 텍스트 상자에서 이름을 바꾸면(rename 성공 시) "코드" 입력란도 새 파일명/경로 기준으로 다시 계산되어 반영된다** (2026-08-02 추가, **구현 완료**) — 이때까지 사용자가 입력해둔 코드값은 덮어써진다. **"코드" 입력란 바로 옆(같은 줄)에 "출시일" 입력란이 있다**(`ManagedVideoItem.ReleaseDate`, 2026-08-02 추가, **구현 완료**) — 자유 텍스트 상자로, 자동 제안이나 형식 강제 없이 사용자가 직접 입력한 값을 그대로 저장한다(예: "2023-06-06"). "확인" 클릭 시 `_item.ReleaseDate`에 커밋된다.
4. 태그는 태그 마스터 목록 전체를 체크박스로 나열하고, 체크박스로 여러 개를 동시에 선택/해제할 수 있다.
5. 태그 체크박스 목록은 `WrapPanel`로 배치되어 한 줄로 흐르다가 공간이 부족하면 자동으로 다음 줄로 넘어간다.
6. **파일명은 수정 가능한 텍스트 상자(이름/확장자 분리, 위 2번 참고)로 표시된다** (더 이상 "이름 변경" 버튼이 없다 — **구현 완료**). 내용을 바꾸고 포커스를 다른 곳으로 옮기면(`LostFocus`) 두 값을 합쳐 바로 실제 파일이 rename된다(`RenameHelper.TryRenameManagedItemTo`, 대화상자 없이 즉시 적용). 실패(잘못된 파일명, 같은 이름의 파일이 이미 존재 등)하면 텍스트 상자가 원래 파일명/확장자로 되돌아간다. 성공하면 전체 경로/썸네일 미리보기(썸네일 파일도 함께 rename되므로)/품번 표시가 갱신되고, `ActorCreditSync.OnFileRenamed`로 배우 Credits와도 동기화된다(아래 "배우 Credits와의 동기화" 참고) — 그 결과로 배우가 자동 추가될 수 있으므로 화면의 배우 선택 상태(`_selectedActors`)도 `SyncSelectedActorsFromItem()`으로 즉시 다시 맞춘다(그렇지 않으면 "확인" 시 오래된 화면 상태로 덮어써서 방금 자동 추가된 배우가 사라짐). `MainWindow`의 F2/우클릭 "이름변경"은 여전히 `RenameWindow` 대화상자를 통해 `RenameHelper.TryRenameManagedItem`을 사용한다(별도 진입점, 내부적으로 같은 `TryRenameManagedItemTo`를 호출하므로 이 경로로 이름을 바꿔도 배우 Credits 동기화는 동일하게 적용된다).
   - **다른 창을 클릭해도 커밋된다(2026-08-04 수정, 구현 완료)**: 파일명 텍스트 상자에서 커서를 유지한 채(같은 창 안의 다른 컨트롤로 옮기지 않고) 다른 최상위 창(예: `MainWindow`)을 클릭하면 이름 변경이 적용되지 않는 버그가 있었다 — WPF의 `LostFocus`는 그 창 자신의 포커스 스코프 안에서의 논리적 포커스 이동에만 반응하는데, 창이 모덜리스로 바뀌면서(규칙 5 참고) 사용자가 텍스트 상자 안에 커서를 둔 채로 다른 창을 자유롭게 클릭할 수 있게 됐고, 이 경우 논리적 포커스는 그대로라 `LostFocus`가 발생하지 않았다. `FileNamePart_LostFocus`의 커밋 로직을 `CommitFileNameIfChanged()`로 뽑아내고, `Window.Deactivated`(창이 비활성화될 때 — 실제 키보드 입력이 다른 창으로 넘어가는 시점 기준)에도 같은 메서드를 호출해서 이 경우도 커밋되도록 했다.
7. "전체 경로:" 옆에 버튼이 **세 개** 있다 (2026-08-02 변경 — "파일 이동" 추가) — **구현 완료**. 셋 다 확인/취소와 무관하게 즉시 반영되며, 성공 시 전체 경로/썸네일 미리보기가 모두 갱신된다(썸네일/원본 파일도 함께 이동).
   - **"파일 이동"** (맨 왼쪽, 신규): `MoveByCodeButton_Click` → `RenameHelper.TryMoveToSpecificFolder`. **"코드" 입력란의 값으로 맨 마지막 폴더명을 바꾼 위치**로 파일을 옮긴다 — 현재 폴더의 부모 폴더 아래에 코드 이름의 폴더를 만들고(이미 있으면 그대로 사용) 그 안으로 이동한다(예: `...\RandomFolder\SSNI-123.mp4`, 코드 `SSNI` → `...\SSNI\SSNI-123.mp4`). **이동 전에 "이전 위치"와 "이동할 위치"를 모두 보여주는 확인 대화상자**를 띄운다 — **구현 완료**. 대상 폴더가 없으면 자동으로 만든다(`Directory.CreateDirectory`). 이동에 성공하면 그때의 코드 값을 `_item.Code`에도 저장한다.
   - **"파일 변경"** (예전 이름 "경로 수정"): `RenameHelper.TryEditFullPath`, **파일 대화상자(`SaveFileDialog`)로 새 위치를 선택**한다(폴더/파일명 모두 자유롭게 변경 가능). 대상이 이미 존재하면 대화상자 자체의 덮어쓰기 확인을 거치고, `File.Move(..., overwrite: true)`로 이동한다.
   - **"경로 수정"**: `RenameHelper.TryMoveToFolder`, **폴더 선택 대화상자(`OpenFolderDialog`)로 이동할 폴더만 선택**한다 — 파일명은 그대로 유지한 채 폴더만 옮기는 더 단순한 시나리오. 대상 폴더에 같은 이름의 파일이 이미 있으면 실패 메시지를 표시한다.
   - "파일 이동"/"경로 수정" 둘 다 내부적으로 같은 `RenameHelper.TryMoveToSpecificFolder`(폴더로 이동 + `RenameAssociatedFile`로 썸네일/원본 동반 이동 + 대상 폴더 자동 생성)를 공유한다 — "경로 수정"은 대화상자로 고른 폴더를, "파일 이동"은 코드로 계산한 폴더를 넘겨 호출하는 차이만 있다.
8. **재생횟수 입력란은 읽기 전용**이다(`IsReadOnly="True"`, 직접 타이핑으로 수정 불가) — **구현 완료**. 옆의 "초기화" 버튼으로만 0으로 되돌릴 수 있으며, 다른 필드와 마찬가지로 실제 반영은 "확인" 클릭 시 커밋된다. **"재생" 버튼은 "초기화" 버튼 바로 오른쪽, 같은 줄에 있다**(2026-08-02 변경 — 예전에는 아래 별도 줄에 있었음, **구현 완료**) — 클릭하면 창을 닫지 않고도 이 항목의 파일을 바로 재생할 수 있다. `MainWindow`의 재생과 동일하게 재생과 동시에 `PlayCount`가 즉시 1 증가하고(확인/취소와 무관하게 바로 반영, 이름 변경/경로 수정/썸네일 변경과 같은 카테고리), 화면의 재생횟수 입력란도 곧바로 갱신된다. **이 창이 열려 있는 동안 `MainWindow`(관리 리스트)에서 같은 항목을 재생해도 입력란이 그 즉시 따라간다**(2026-08-04 추가, **구현 완료**) — 속성 창이 모덜리스로 바뀌어(규칙 5 참고) 열어둔 채로 관리 리스트에서 같은 파일을 더블클릭/재생 버튼으로 재생할 수 있게 됐는데, 예전에는 그 증가분이 입력란에 반영되지 않다가 "확인"을 누르면 `TryCommitFormFields`가 입력란에 남아있던 예전 값을 그대로 다시 써버려서 방금 늘어난 재생횟수가 도로 줄어드는 버그가 있었다. `PropertiesWindow`가 `_item.PropertyChanged`를 구독해(`ItemPropertyChanged`) `PlayCount`가 바뀔 때마다 입력란을 다시 그리는 방식으로 고쳤다 — 이 구독은 `SwitchToItem`으로 다른 항목으로 넘어갈 때 옛 항목에서 새 항목으로 옮겨지고, 창이 닫힐 때 해제된다. **같은 구독으로 썸네일도 따라간다(2026-08-04 추가, 구현 완료)** — `ThumbnailPath`/`ThumbnailOriginalPath`가 바뀌면(예: 이 창을 열어둔 채로 `MainWindow` 아이콘 보기에서 같은 항목의 썸네일을 바꾼 경우) `ItemPropertyChanged`가 `RefreshThumbnailPreview()`를 호출해 썸네일 뷰어도 그 즉시 갱신된다 — 이 창을 닫았다 다시 열어야만 반영되던 문제를 PlayCount와 같은 패턴으로 고쳤다. **시리즈/배우도 같은 구독으로 따라간다**(2026-08-10 추가, **구현 완료**, PlayCount와 동일한 패턴으로 발견·수정된 버그) — "시리즈 동기화"/"배우 동기화" 버튼(관리 리스트 툴바)처럼 화면(콤보박스/칩)을 거치지 않고 `item.Series`/`item.Actors`를 직접 바꾸는 일괄 작업이 있는데, 이 창을 어떤 항목에 대해 열어둔 채로 그런 작업이 실행되면 `Series`/`Actors`가 바뀌었는데도 화면(시리즈 콤보박스/배우 칩)은 그 변경을 모른 채 예전 상태를 그대로 유지하고 있다가, 다른 항목으로 전환(`SwitchToItem`)되는 순간 `TryCommitFormFields`가 그 예전 상태를 `_item`에 다시 덮어써서 방금 동기화된 값이 초기화되는 버그가 있었다(예: `scop-708`을 선택해 속성 창을 연 채로 "시리즈 동기화"를 실행하면 `item.Series`는 바뀌지만, 다른 항목을 클릭하는 순간 다시 빈 값으로 되돌아감). `ItemPropertyChanged`에 `Series`(`RefreshSeriesComboBox()` 재호출)와 `Actors`(`SyncSelectedActorsFromItem()` 재호출) 케이스를 추가해 해결했다.
9. **메모 입력란은 3줄 입력 가능한 여러 줄 텍스트 상자**다(`AcceptsReturn="True"`, `TextWrapping="Wrap"`, 높이는 약 3줄 분량, 넘치면 세로 스크롤) — **구현 완료**.
10. **배우 콤보박스는 이름 기준 오름차순으로 정렬되어 표시된다** — 생성자에서 `masterActors.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)`로 정렬한 스냅샷을 `ActorComboBox.ItemsSource`에 지정한다(태그 체크박스 목록과 동일한 정렬 패턴) — **구현 완료**. **선택된 배우 칩 목록 밑에 배우별 썸네일(80x80)이 표시된다**(2026-08-02 추가, **구현 완료**) — 배우를 추가/제거할 때마다(`AddActor_Click`/`RemoveActorChip_Click`) `RefreshSelectedActorsThumbnails()`가 `_selectedActors`의 이름들을 배우 마스터 목록에서 찾아 `ActorItem`으로 변환해 다시 그린다(`MainWindow` 하단 상세정보 패널의 배우 썸네일과 같은 패턴: `ThumbnailPathConverter` 바인딩, 썸네일 없으면 👤 기본 아이콘). 이 창이 `ThumbnailPathConverter`를 쓰는 것은 처음이라 `xmlns:local`과 `Window.Resources`에 컨버터를 새로 선언했다.
    - **배우 이름/썸네일 클릭 → 배우 관리 창으로 이동** (2026-08-03 추가, **구현 완료**): 배우 칩의 이름 텍스트(`ActorChipName_MouseLeftButtonUp`)나 배우 썸네일(`ActorThumbnail_MouseLeftButtonUp`) 중 아무 곳이나 클릭하면 `ActorManagerWindow`를 열면서 그 배우를 바로 선택하고 스크롤해서 보여준다(`OpenActorManagerFor`). 이를 위해 `ActorManagerWindow` 생성자에 선택 옵션 매개변수 `ActorItem? initialSelection = null`이 추가되었다(지정하면 `ActorsListBox.SelectedItem`으로 즉시 선택하고, `Window.Loaded` 시점에 `ScrollIntoView`— `MainWindow_Loaded`의 스크롤 패턴과 동일한 이유로 레이아웃이 준비된 뒤에 스크롤해야 한다). `PropertiesWindow`가 `ActorManagerWindow`를 열려면 배우 마스터 목록뿐 아니라 관리 리스트 컬렉션도 필요해서, `PropertiesWindow` 생성자가 `managedItems`를 새로 받고 `masterActors`/`managedItems` 매개변수 타입도 `ObservableCollection<T>`로 넓어졌다(호출부 `MainWindow.OpenPropertiesWindow`/`ActorManagerWindow.CreditChip_MouseLeftButtonUp` 모두 갱신). 배우 관리 창에서 돌아오면(이름 변경/삭제 등이 있었을 수 있으므로) 배우 콤보박스(`RefreshActorComboBox`)와 선택된 배우 표시(`SyncSelectedActorsFromItem`)를 다시 최신 상태로 맞춘다.
11. 썸네일 뷰어 위, "썸네일 추가"/"썸네일 삭제" 버튼과 같은 줄 **왼쪽**에 **"품번: {파일명, 확장자 제외}"**이 표시된다(`ProductCodeText`, 2026-08-02 추가, **구현 완료**) — 파일명이 바뀔 때마다(`RefreshProductCodeDisplay`) 함께 갱신된다. 그 옆(오른쪽)에 "썸네일 추가"/"썸네일 삭제" 버튼이 나란히 있다 — "썸네일 삭제"는 `ThumbnailPath`/`ThumbnailOriginalPath` 파일을 디스크에서 삭제하고 두 필드를 모두 null로 되돌려 기본 아이콘 상태로 만든다(즉시 반영, 확인/취소와 무관). 썸네일이 없으면 안내 메시지만 표시한다. 썸네일의 현재 경로 정보(`ThumbnailPathText`)는 뷰어 **하단**에 표시된다.
12. **창 맨 아래 왼쪽에 "완전 삭제" 버튼이 있다** (진한 빨간 글자로 강조) — **구현 완료**. 확인 대화상자를 거친 뒤 이 항목의 관리 데이터를 완전히 삭제한다(관리 리스트에서 완전히 사라지며 `library.json`에 다시 나타나지 않음, [제거(Remove) 메커니즘](#제거remove-메커니즘)의 "완전삭제" 참고). 실제 동영상 파일은 삭제하지 않는다. 클릭하면 `PropertiesWindow.PermanentlyDeleted`를 `true`로 설정하고 `DialogResult = false`로 창을 닫으며, 실제 컬렉션에서의 제거는 호출자인 `MainWindow.OpenPropertiesWindow`가 이 플래그를 보고 수행한다(다이얼로그 자신은 `_managedItems`에 접근할 수 없으므로).
13. **"새 항목" 모드 — 파일 없이 추가** (2026-08-06 추가, **구현 완료**): 관리 리스트 툴바의 "파일 없이 추가" 버튼(`MainWindow.AddPlaceholderItem_Click`)이 `IsPlaceholder = true`, `IsExist = false`인 새 `ManagedVideoItem`을 만들어 `OpenPropertiesWindow(item, isNewItem: true)`로 이 창을 연다 — 생성자의 `isNewItem` 매개변수(`_isNewItem` 필드)가 이 새 항목이 **아직 `_managedItems`에 없는 상태**임을 나타낸다. 이 상태에서 "확인"을 누르거나(또는 버튼을 거치지 않고 닫아도, 규칙은 동일) **파일명을 입력했으면 그 시점(`TryCommitFormFields`)에 `_item.IsValid = false`로 설정한 뒤 `_managedItems.Add(_item)`으로 비로소 관리 리스트에 추가되고 `_isNewItem`이 `false`로 바뀐다. 파일명을 입력하지 않았으면 조용히 추가가 취소된다**(별도 안내 없음). `SwitchToItem`으로 다른(이미 목록에 있는) 항목으로 전환되면 `_isNewItem`도 함께 `false`로 재설정된다.
    - **이미 같은 파일명의 항목이 있으면 막는다**(2026-08-07 추가, **구현 완료**) — 이미 관리 중인 실제 파일(예: `sdmt-261.mp4`)이 있는 줄 모르고 같은 이름으로 "파일 없이 추가"를 하면, 서로 다른 두 `ManagedVideoItem`이 같은 `FileName`을 갖게 되는 문제가 있었다(재사용 매칭이 어느 쪽을 골라야 할지 모호해짐 등). 이 검사는 `PropertiesWindow.TryGuardAgainstDuplicateFileName(candidateName)` 공용 헬퍼로 뽑혀 있고 **두 시점 모두에서 호출된다**: ① 새 항목을 처음 커밋할 때(`TryCommitFormFields`), ② **커밋된 뒤에도 파일명 텍스트 상자에서 이름을 다시 바꿀 때**(`CommitFileNameIfChanged`의 `IsPlaceholder` 분기 — 처음엔 "sdmt-261"처럼 안 겹치게 만들었다가 나중에 "sdmt-261.mp4"로 고쳐서 겹치게 되는 경우도 있었기 때문에 이 시점도 반드시 막아야 한다). `_managedItems`에 같은 `FileName`(대소문자 구분, 활성/제거 상태 무관, 자기 자신은 제외)을 가진 다른 항목이 있으면 "이미 존재하는 파일명" 경고를 띄우고 **적용을 취소한다**(①은 커밋 자체를 실패로 처리해 창을 닫지 않음 — `Window_Closing`의 "검증 실패 시 닫기 취소" 패턴과 동일, ②는 텍스트 상자를 원래 파일명으로 되돌림) — 사용자가 다른 이름을 입력하거나 "취소"로 빠져나가야 한다.
    - **제거된 항목으로 취급된다**(2026-08-06 추가, **구현 완료**) — 실제 파일이 없으므로 추가되는 즉시 `IsValid = false`로 설정해 일반 "제거"와 동일하게 처리한다: 기본 목록에는 보이지 않고 "제거된 항목도 표시" 체크박스로만 보이며(회색으로 표시), `library.json`에 다른 항목과 함께 저장된다(`IsExist`는 애초에 `false`). 나중에 같은 이름의 실제 파일이 추가되면 아래 "재사용" 매칭으로 데이터를 그대로 이어받는다.
    - `RefreshFileOperationAvailability()`가 `_item.IsPlaceholder`(실제 파일이 있어야 동작하는 재생/파일 변경/경로 수정/파일 이동 버튼을 비활성화하고 툴팁으로 "실제 파일이 없는 항목입니다."를 보여줌)와 `_isNewItem`("완전 삭제" 버튼 비활성화 — 아직 지울 대상이 없으므로)에 따라 버튼 활성/비활성을 갱신하며, 새 항목 생성 시점과 `SwitchToItem` 양쪽에서 다시 호출된다. **썸네일 추가/삭제 버튼은 비활성화하지 않는다** — 아래 참고.
    - 파일명 텍스트 상자의 커밋(`CommitFileNameIfChanged`)도 `IsPlaceholder`일 때는 `RenameHelper`(`File.Move`)를 거치지 않고 `_item.FileName`/`FullPath` 값만 직접 갱신한다 — 이동/이름 변경할 실제 파일이 없기 때문이다. 코드/출시일/메모/태그/배우는 일반 항목과 동일하게 편집 가능하다.
    - **썸네일은 실제 파일이 없어도 지정할 수 있다**(2026-08-06 추가, **구현 완료**) — `PropertiesWindow.ApplyThumbnail`/`MainWindow.ApplyThumbnail`(아이콘 보기 클릭·드래그 앤 드롭 공용) 모두 `item.IsPlaceholder`면 `ThumbnailHelper.CreatePlaceholderThumbnail`을 사용한다. 일반 항목의 `CreateThumbnail`은 "동영상 파일과 같은 폴더"에 저장하는데, 플레이스홀더는 실제 폴더가 없으므로(`FullPath`가 파일명뿐) 대신 고정 폴더 `ThumbnailHelper.PlaceholderThumbnailDir`(`E:\happy\thumbnail`)에 `{파일명}.thumbnail.jpg`/`{파일명}.original{확장자}`로 저장한다. **이전에는 이 분기가 없어 `Path.GetDirectoryName("파일명뿐인 경로")`가 빈 문자열을 반환해도 예외가 나지 않아, 앱의 실행 폴더(작업 디렉터리) 기준 상대 경로에 조용히 썸네일 파일이 생기는 버그가 있었다** — 고정 폴더 분기로 해결.
    - **재생 시도 시 친절한 안내로 대체된다**(2026-08-06 추가, **구현 완료**) — `MainWindow.PlayManagedItem`이 `item.IsPlaceholder`면 `Process.Start`를 시도하지 않고 "실제 파일이 없는 항목입니다." 안내만 표시한다(더블클릭 재생/재생 버튼/우클릭 메뉴 모두 이 메서드를 공유하므로 전부 적용됨). `PropertiesWindow`의 "재생" 버튼은 위와 같이 아예 비활성화된다.

**배우 Credits와의 동기화** (2026-08-02 추가, **구현 완료**): 이 항목의 `Actors`와 배우 마스터 목록의 `Credits`가 어긋나지 않도록 `ActorCreditSync`(자세한 내용은 [배우 마스터 목록 관리](#배우-마스터-목록-관리-actormanagerwindow) 참고)가 두 방향으로 동기화한다.
- 배우 칩을 ✕로 제거하고 "확인"으로 커밋하면, 그 배우의 Credits에서도 이 파일의 품번을 제거한다(`TryCommitFormFields`가 커밋 직전에 `_item.Actors`와 화면에 남은 선택을 비교해 빠진 배우를 찾아 `ActorCreditSync.OnActorRemovedFromItem`을 호출).
- 파일명이 바뀌면(위 6번) `ActorCreditSync.OnFileRenamed`가 실행된다.

"확인"을 누르면 재생횟수 입력란의 값, 코드, 메모, 선택된 태그/배우가 항목에 반영되고(배우 Credits 동기화도 이 시점에 함께 적용됨), "취소"를 누르면 그 변경 사항만 버려진다(파일명 변경/경로 수정/파일 이동/썸네일 추가·삭제/완전 삭제/재생(재생횟수 증가 포함)은 이미 실제로 적용된 상태라 취소로 되돌아가지 않는다. "파일 이동"으로 성공한 경우의 코드 값도 이미 즉시 반영되어 있다).

- **엔터 키로 버튼이 눌리지 않는다** (2026-08-02 변경, **구현 완료**) — "확인" 버튼의 `IsDefault="True"`를 제거했다. 예전에는 파일명/코드 같은 한 줄 텍스트 상자에서 Enter를 치면(줄바꿈을 직접 처리하는 메모 상자 제외) 의도치 않게 "확인"이 눌렸다.
- **창을 닫기(제목 표시줄의 X, Alt+F4 등) 버튼으로 닫아도 변경사항이 저장된다** (2026-08-02 추가, **구현 완료**) — `Window.Closing` 이벤트(`Window_Closing`)에서 `DialogResult`가 아직 설정되지 않은 경우(= "확인"/"취소"/"완전 삭제" 버튼을 거치지 않고 닫힌 경우)에만 "확인"과 동일한 커밋 로직(`TryCommitFormFields`, `Ok_Click`과 공유)을 실행하고 `DialogResult = true`로 저장한다. 재생횟수 값이 올바르지 않는 등 검증에 실패하면 `e.Cancel = true`로 닫기 자체를 막는다(확인 버튼과 동일한 검증 규칙). "취소"/"완전 삭제" 버튼은 클릭 시 이미 `DialogResult`를 설정해두므로 이 로직을 건너뛰어 기존 동작(변경사항 버림/완전 삭제)을 그대로 유지한다.

### 3. 관리 리스트 JSON 파일 관리

- **기본 저장 위치**: 사용자별 로컬 데이터 폴더(`%LOCALAPPDATA%\VideoVault\`, 즉 `Environment.SpecialFolder.LocalApplicationData` 하위)를 기본 경로로 사용한다. 다른 PC와 동기화되지 않는 로컬 전용 위치이므로, 파일 내 `FullPath` 등 이 PC에 종속된 경로 정보가 깨지지 않는다. 유효/제거 항목 모두 `library.json` **한 파일**에 `IsValid`로 구분해 저장한다(2026-08-06 변경 — 예전에는 `removed.json`으로 나눠 저장했음) — [제거(Remove) 메커니즘](#제거remove-메커니즘) 참고.
- **자동 로딩**: 프로그램 시작 시 `library.json`이 존재하면 자동으로 불러와 관리 리스트 컬렉션을 채운다. 없으면(최초 실행 등) 빈 관리 리스트로 시작한다. 예전 버전이 남긴 `removed.json`이 있으면 시작 시 한 번 병합·삭제하는 마이그레이션도 함께 실행된다 — 같은 절 참고.
- **열기**: 기본 위치가 아닌 다른 JSON 파일을 불러오고 싶을 때 사용하는 수동 열기 기능 (기본 저장 위치와는 별개, 유효/제거 구분 없이 파일 하나에 전체를 담아 다룸)
- **저장**: 관리 리스트에 변경이 발생할 때마다(추가/제거/편집/재생횟수 증가/태그·썸네일 변경 등) 기본 저장 위치(`library.json`)에 **자동 저장**한다. 별도의 "저장" 버튼 클릭 없이 항상 최신 상태가 디스크에 반영된다. "다른 이름으로 저장"을 통해 임의 위치에(유효+제거 항목 전체를 하나의 파일로) 별도로 저장하는 기능은 유지
  - **debounce 시간**: 마지막 변경 후 500ms — **구현 완료** (관리 리스트/태그/설정 자동 저장 모두 동일하게 500ms 적용)
- **편집**: 관리 리스트 항목(특히 tags, 재생횟수 등)을 사용자가 직접 수정 가능
- **저장 실패 처리**: 저장 중 예외가 발생하면 사용자에게 오류를 알리고(MessageBox), 메모리상의 관리 리스트는 그대로 유지한다 (자동으로 재시도하지 않음. 사용자가 재시도할 수 있는 방법은 추후 검토)
- **버전 필드 (향후)**: 향후 데이터 마이그레이션을 지원하기 위해 JSON 최상위에 `Version` 필드를 추가하는 것을 고려한다. 도입 시 현재의 "배열 하나"였던 최상위 구조를 아래처럼 감싸는 하위 호환 마이그레이션이 필요하다.

  ```json
  {
      "Version": 1,
      "Items": []
  }
  ```

  (아직 미구현 — 현재는 `ManagedVideoItem` 배열을 최상위로 바로 저장한다.)
- (향후) **백업/복원**: `library.json`/`tags.json`을 한 번에 내보내기·가져오기 하는 기능. "열기"/"다른 이름으로 저장"과 달리 두 파일을 묶어서 처리하는 원클릭 기능으로 구상.

### 4. 태그 마스터 목록 관리 (`tags.json`)

- **저장 위치**: 관리 리스트와 동일한 로컬 데이터 폴더에 별도 파일로 저장 (`%LOCALAPPDATA%\VideoVault\tags.json`)
- **자동 로딩**: 프로그램 시작 시 자동으로 불러오며, 파일이 없으면 빈 태그 목록으로 시작
- **자동 저장**: 태그 추가/수정/삭제 시 즉시(또는 debounce 후) `tags.json`에 자동 저장 (관리 리스트와 동일한 정책)
- **태그 관리 화면(`TagManagerWindow`)**: 태그 마스터 목록을 별도 창에서 관리
  - **목록은 항상 오름차순으로 정렬되어 표시된다** — `CollectionViewSource.GetDefaultView(_masterTags)`에 `SortDescription(string.Empty, Ascending)`을 적용한 `ICollectionView`를 `TagsListBox.ItemsSource`로 사용한다(문자열 자체를 정렬 키로 쓰므로 `PropertyName`을 빈 문자열로 지정). 실제 저장 순서(`tags.json`의 배열 순서)는 바꾸지 않고 화면 표시만 정렬한다 — **구현 완료**.
  - **추가**: 새 태그 이름 입력 후 목록에 추가. **추가된 태그를 목록에서 바로 선택하고 그 위치로 스크롤한다**(2026-08-02 추가, **구현 완료**) — 목록이 항상 정렬 상태라 새 태그가 끝이 아니라 임의 위치에 삽입될 수 있으므로, `TagsListBox.SelectedItem`/`ScrollIntoView`로 사용자가 방금 추가한 태그를 바로 눈으로 확인할 수 있게 한다.
  - **수정(이름 변경)**: 태그 이름을 바꾸면, 이 태그를 사용 중인 관리 리스트의 모든 항목에도 변경된 이름이 반영되어야 한다 (참조 무결성 유지). 이름이 바뀌면 정렬 위치도 바뀌어야 하므로 변경 후 `_tagsView.Refresh()`를 호출한다(`ObservableCollection`의 인덱스 교체만으로는 `ICollectionView`가 자동으로 재정렬하지 않기 때문).
  - **삭제**: 태그를 마스터 목록에서 삭제하면, 이 태그를 사용 중인 관리 리스트 항목들에서도 해당 태그가 제거되어야 한다
- **태그 동일성 판정 규칙**: 앞뒤 공백을 제거하고 대소문자를 구분하지 않는다 (`" Action "`, `"action"`, `"ACTION"`은 모두 같은 태그로 취급되어 중복 추가가 거부된다). 현재 `TagManagerWindow`의 중복 검사가 이 규칙으로 이미 구현되어 있다.

### 배우 마스터 목록 관리 (`ActorManagerWindow`)

태그와 별개로, 배우는 이름뿐 아니라 100x100 썸네일 이미지를 함께 갖는 마스터 목록으로 관리한다 — **구현 완료**.

- **데이터 모델(`ActorItem`)**: `Name`(문자열) + `ThumbnailPath`(문자열?, 100x100 리사이즈본 경로, 미지정 시 null) + `BirthYear`(int?, 출생년도) + `Height`(int?, 키/cm) + `BodyInfo`(문자열, 신체정보 자유 텍스트, 기본값 빈 문자열) + `Credits`(`List<string>`, 이 배우가 출연한 것으로 등록된 품번 목록, 2026-08-02 추가) + 계산 속성 `HasThumbnail`. `INotifyPropertyChanged` 구현. `Name`/`ThumbnailPath`/`BirthYear`/`Height`/`BodyInfo`는 모두 public setter라 `[JsonInclude]`가 필요 없지만, `Credits`는 `ManagedVideoItem.Tags`/`Actors`와 동일한 이유로 `private set` + `[JsonInclude]` + `SetCredits(...)` 메서드 패턴을 쓴다 — [컨벤션](#컨벤션) 참고.
- **저장 위치**: `%LOCALAPPDATA%\VideoVault\actors.json`. 관리 리스트/태그와 동일하게 프로그램 시작 시 자동 로딩되고, 파일이 없으면 빈 목록으로 시작한다.
- **자동 저장**: 배우 추가/이름변경/삭제/썸네일 변경 시 500ms debounce 후 `actors.json`에 자동 저장 (관리 리스트/태그와 동일한 정책, `ActorItem.PropertyChanged`를 구독해 썸네일 변경도 감지). Ctrl+S로도 즉시 저장된다.
- **관리 리스트 항목과의 관계**: `ManagedVideoItem.Actors`는 배우 이름의 목록(`List<string>`)으로, 태그와 마찬가지로 배우 마스터 목록에 존재하는 이름만 참조한다(자유 입력 아님). **한 항목에 배우를 여러 명 지정할 수 있다.**
- **배우 관리 화면(`ActorManagerWindow`, 기본 크기 1000x1000)**: 메인창 "도구 > 배우 관리" 메뉴 또는 관리 리스트 툴바의 "배우 관리" 버튼(태그 관리 버튼 옆)으로 연다.
  - **목록은 아이콘 보기로만 표시된다** (리스트 보기 없음): `WrapPanel` 기반 그리드에 각 배우의 썸네일(90x90)과 이름만 보여준다 — **구현 완료**.
  - **목록은 항상 이름 기준 오름차순으로 정렬되어 표시된다** — `TagManagerWindow`와 동일하게 `ICollectionView` + `SortDescription(nameof(ActorItem.Name), Ascending)`을 사용하며, `actors.json`의 실제 저장 순서는 바꾸지 않는다 — **구현 완료**.
  - **추가**: 새 배우 이름 입력 후 목록에 추가 (동일성 판정은 태그와 동일하게 앞뒤 공백 제거 + 대소문자 무시). **추가된 배우를 목록에서 바로 선택하고 그 위치로 스크롤한다**(2026-08-02 추가, **구현 완료**) — 태그 관리와 동일한 패턴(`ActorsListBox.SelectedItem`/`ScrollIntoView`), 이름 기준 정렬이라 새 배우가 임의 위치에 삽입되므로 직접 찾지 않아도 바로 보이게 한다.
  - **이름 변경**: 배우 이름을 바꾸면 이 배우가 지정된 모든 관리 리스트 항목의 `Actors`에도 새 이름이 반영된다(참조 무결성 유지, 태그 이름 변경과 동일한 패턴). 썸네일 파일이 있으면 새 이름 기준 파일명으로 함께 rename을 시도한다(부가 정리, 실패해도 이름 변경 자체는 유지 — `RenameHelper`의 썸네일 rename과 동일한 원칙). 이름 변경 후 정렬 위치 갱신을 위해 `_actorsView.Refresh()`를 호출한다.
  - **삭제**: 배우를 마스터 목록에서 삭제하면 이 배우를 사용 중인 관리 리스트 항목들의 `Actors`에서도 제거되고, 배우의 썸네일 파일도 함께 삭제를 시도한다(부가 정리, 실패해도 무시).
  - **썸네일 지정/삭제**: 창 오른쪽에 선택된 배우의 100x100 썸네일 뷰어가 있고, 그 위에 "썸네일 추가"/"썸네일 삭제" 버튼이 나란히 있다. 추가는 파일 선택 대화상자 또는 드래그 앤 드롭(`DragDropImageHelper` 재사용, 인터넷 이미지 드래그도 동일하게 지원)으로, 삭제는 썸네일 파일을 디스크에서 지우고 `ThumbnailPath`를 null로 되돌린다(즉시 반영, 썸네일이 없으면 안내만 표시). 동영상 썸네일 뷰어와 달리 **원본을 클릭해서 크게 보는 기능은 없다** (원본 자체를 보관하지 않으므로).
  - **썸네일 정보 밑에 배우 정보 표시**: 오른쪽 패널의 썸네일 뷰어 아래에 선택된 배우의 이름/출생년도/키/신체정보를 읽기 전용으로 보여준다(값이 없으면 "-"). 선택이 바뀔 때마다 `RefreshActorInfoPanel`이 갱신한다 — **구현 완료**.
  - **우클릭 → "배우 정보 수정"**: 목록의 배우를 우클릭하면 `ActorItemContextMenu`(다른 우클릭 메뉴들과 동일하게 `Window.Resources`에 선언 후 `ItemContainerStyle`에서 `StaticResource`로 참조하는 패턴)가 뜨고, "배우 정보 수정"을 클릭하면 `ActorInfoWindow`가 열린다. 이 창에서 이름/출생년도/키/신체정보를 편집할 수 있다(출생년도/키/신체정보는 비워두면 삭제됨). 이름을 바꾸면 대화상자 자체는 다른 배우와의 이름 중복만 검사하고, 실제 rename 동기화(썸네일 파일 rename + 관리 리스트 항목들의 `Actors` 참조 갱신)는 "선택 배우 이름 변경" 버튼과 공유하는 `RenameActorAndSync`가 처리한다(중복 로직 방지) — **구현 완료**.
  - **Credits(이 배우가 출연한 작품 목록, 2026-08-02 추가, 구현 완료)**: 썸네일 정보/배우 정보 밑에 **"Credits"** 라벨과 그 옆(오른쪽 정렬) **"작품 추가"** 버튼이 있고, 그 아래 이 배우의 `ActorItem.Credits`(품번 문자열 목록)를 태그 칩과 같은 모양(둥근 배경, 패딩)의 칩으로 나열한다. **품번 기준 오름차순으로 정렬되어 표시된다**(2026-08-09 추가, `OrderBy(code, StringComparer.OrdinalIgnoreCase)`) — `Credits`(저장 순서)는 그대로 두고 화면 표시만 정렬한다.
    - **색상 구분**: 관리 리스트(활성/제거됨 모두)에 같은 품번(파일명, 확장자 제외)의 파일이 실제로 있으면 **진한 파란색**(`#3D6FB4`, 흰 글자), 없으면 **연한 파란색**(`#CFE2F3`, 짙은 글자)으로 표시된다 — "알려진 작품이지만 아직 관리 리스트에 없는 파일"과 "이미 보유한 파일"을 한눈에 구분하기 위함.
    - **자동 동기화(배우 선택 시)**: 배우를 선택할 때마다(`RefreshCreditsPanel` → `SyncCreditsFromManagedItems`) 관리 리스트에서 이 배우가 `Actors`로 지정된 파일들을 찾아 그 품번들을 Credits에 자동으로 병합한다(이미 있는 값은 건드리지 않음) — 관리 리스트에서 이미 이 배우를 태깅해둔 파일이 있다면 "작품 추가"를 따로 거치지 않아도 Credits에 반영된다.
    - **작품 추가 → 배우 자동 태깅**: 아래 "작품 추가 창"으로 새 품번을 추가하면, 관리 리스트에 그 품번의 실제 파일이 있는 경우 `UpdateManagedItemActorsForCredit`가 그 파일의 `Actors`에도 이 배우를 자동으로 추가한다(이미 지정돼 있으면 건드리지 않음).
    - **칩 좌클릭 → 속성 창 열기**: Credits 칩을 클릭하면 관리 리스트에서 일치하는 항목을 찾아 `PropertiesWindow`를 연다. 일치하는 파일이 없으면(연한 파란색 칩) 안내 메시지만 표시한다.
    - **칩 우클릭 → Credits에서 삭제**: 확인 대화상자 후 이 배우의 Credits에서 제거한다. **이 품번과 일치하는 실제 파일이 아직 이 배우를 `Actors`로 갖고 있으면 그 파일의 `Actors`에서도 함께 제거한다**(2026-08-09 버그 수정, **구현 완료**) — 이걸 안 하면 곧바로 `RefreshCreditsPanel`의 `SyncCreditsFromManagedItems`가 그 파일을 근거로 품번을 다시 채워 넣어서, 사용자 입장에서는 "삭제가 안 되는" 것처럼 보이는 실제 버그가 있었다(우클릭 삭제 직후 같은 칩이 그대로 남아있음). 확인 대화상자 문구에도 "실제 파일에 이 배우가 지정되어 있다면 그 지정도 함께 해제됩니다"를 명시한다.
    - 배우 이름 변경/삭제 시에는 Credits 자체를 별도로 옮기지 않는다(Credits는 배우별 데이터이므로 `ActorItem`과 함께 그대로 유지/삭제됨).
- **작품 추가 창 (`AddCreditWindow`, 2026-08-02 추가, 2026-08-09 연속 추가 방식으로 개편, 구현 완료)**: "작품 추가" 버튼으로 여는 대화상자(기본 크기 520x420). 배우 관리/시리즈 관리 창이 공유하는 완전히 동일한 대화상자다.
  - 품번을 직접 입력할 수 있는 텍스트 상자가 있고, **바로 옆에 "추가" 버튼**이 있다(2026-08-09 추가).
  - **입력한 품번은 항상 소문자로, 앞뒤 공백은 제거하고 저장한다**(2026-08-09 추가, `AddCurrentCode`) — `CodeBox.Text.Trim().ToLowerInvariant()`.
  - **"추가" 버튼을 클릭하거나 입력란에서 Enter를 누르면 그 즉시 반영되고 창은 닫히지 않는다**(2026-08-09 변경, 구현 완료 — 예전에는 "확인" 버튼으로 값 하나만 확정하고 창이 닫혔다). 반영 후 입력란이 즉시 비워지고 포커스가 그대로 유지되어, 여러 품번을 연달아 입력해서 한 번에 여러 개를 추가할 수 있다. 생성자가 `Action<string> onAddCode` 콜백을 받아 각 추가 시점마다 즉시 호출하는 방식으로 구현되어 있다 — 호출자(`ActorManagerWindow.AddCreditToActor`/`SeriesManagerWindow.AddCreditToSeries`)가 이 콜백 안에서 중복 검사 + Credits 반영 + 동기화 + 화면 갱신을 수행한다. 창 맨 아래에는 "닫기" 버튼 하나만 있다(예전의 "확인"/"취소" 두 버튼을 대체 — 추가는 이미 즉시 반영되므로 "취소"로 되돌릴 대상이 없다).
  - 입력할 때마다(`TextChanged`) 관리 리스트에서 부분 일치하는 항목을 찾아 **썸네일 + 품번**으로 미리보기 목록을 보여준다(최대 50개, 품번 기준 오름차순). 미리보기 항목을 더블클릭하면 그 품번이 입력란에 채워진다(추가되지는 않음 — "추가" 버튼/Enter를 눌러야 실제로 추가됨).
  - 관리 리스트에 없는 품번도 자유롭게 입력해서 추가할 수 있다(미리보기는 어디까지나 참고용이며 입력을 제한하지 않음).
- **배우 Credits ↔ 관리 리스트 상호 동기화 (`ActorCreditSync.cs`, 2026-08-02 추가, 구현 완료)**: 위에서 설명한 개별 동기화 지점들을 한 클래스에 모아둔 공용 로직이다.
  - `OnFileRenamed(item, oldFileName, masterActors)`: 파일명이 바뀌면(`RenameHelper.TryRenameManagedItemTo`/`TryEditFullPath` 성공 시 호출) ① 이 파일에 이미 지정된 배우들 중 옛 품번을 Credits로 갖고 있던 배우는 새 품번으로 값을 갱신하고, ② 새 품번이 이미 다른(아직 파일 없이 "연한 파란색"으로만 있던) 배우의 Credits와 일치하면 그 배우를 이 파일의 `Actors`에 자동으로 추가한다. 우연히 같은 문자열을 Credits로 가진, 이 파일과 무관한 배우의 데이터까지 건드리지 않도록 ①은 이 파일에 이미 지정된 배우만 대상으로 한다.
  - `OnActorRemovedFromItem(item, removedActorName, masterActors)`: `PropertiesWindow`에서 배우 칩을 제거하고 커밋하면, 그 배우의 Credits에서 이 파일의 품번을 제거한다.
  - `RenameHelper`의 관련 메서드들은 이제 `masterActors`를 매개변수로 받는다 — `MainWindow`의 F2/우클릭 "이름변경"도 같은 경로를 타므로 어느 진입점으로 rename해도 동일하게 동기화된다.
- **"배우 동기화" 버튼(`MainWindow.SyncActors_Click`, 2026-08-10 추가, 같은 날 "속성" 버튼 밑 줄로 위치 변경, 구현 완료)**: 관리 리스트 툴바 5번째 줄("속성"/"태그 관리"/"배우 관리"/"시리즈 관리" 줄 바로 밑), "시리즈 동기화" 버튼 왼쪽에 있다(도구 메뉴에도 동일 항목 있음). "시리즈 동기화"와 같은 방향의 일괄 동기화("Credits → 관리 리스트")를 배우에 대해 수행한다 — 모든 배우의 Credits(품번)를 훑어서, 그 품번과 일치하지만 아직 그 배우가 `Actors`에 지정되어 있지 않은 관리 리스트 항목에 배우를 추가한다. **배우는 시리즈와 달리 여러 명을 동시에 지정할 수 있는 목록이라 "충돌"이라는 개념이 없다** — 기존에 지정된 다른 배우를 덮어쓰거나 지우지 않고 그냥 없는 것만 추가한다(따라서 결과 대화상자에는 업데이트 목록만 있고 시리즈 동기화와 달리 충돌 섹션이 없다). 결과 대화상자 문구 조립은 `MainWindow.BuildSyncResultMessage`를 시리즈 동기화와 공유한다(충돌 목록을 빈 리스트로 넘기면 자동으로 그 섹션이 생략됨).
- **썸네일 저장 방식(`ThumbnailHelper.CreateActorThumbnail`)**: 동영상 썸네일과 달리 원본 이미지는 별도로 보관하지 않고, 가로세로 비율을 유지한 채 **100x100 이내로 리사이즈한 결과만** 저장한다(동영상 썸네일의 320x240 로직과 같은 방식으로 축소 비율을 계산하되 크기만 다름). 저장 위치는 `%LOCALAPPDATA%\VideoVault\actresses\{배우명}.jpg`이며(`AppPaths.ActorsThumbnailDir`), 파일명에 쓸 수 없는 문자는 `_`로 치환한다. 소스로 쓰인 원본 파일은 저장이 끝나면 삭제한다(드래그 앤 드롭 임시 파일 정리 포함, 동영상 썸네일과 동일한 원칙).
- **화면 노출**:
  - **속성 창(`PropertiesWindow`)**: "배우" 항목이 콤보박스(배우 마스터 목록에서 선택) + "추가" 버튼으로 되어 있다. 추가하면 아래에 칩(chip) 형태로 표시되고, 칩의 "✕"를 클릭하면 제거된다 — 여러 명을 반복해서 추가/제거할 수 있다.
  - **관리 리스트 하단 "선택된 항목 상세 정보" 패널**: 패널 오른쪽에 선택된 항목에 지정된 배우들의 100x100 썸네일이 표시된다. **배우가 여러 명이면 오른쪽 정렬로 순서대로(왼쪽→오른쪽) 나열**된다. 썸네일이 없는 배우는 기본 아이콘(👤)으로 표시된다.
  - **리스트 보기 "배우" 컬럼**: 여러 배우 이름을 쉼표로 이어붙인 문자열(`ManagedVideoItem.ActorsDisplay`)로 표시한다 (태그 컬럼처럼 칩 형태는 아님).
- **하위 호환**: 예전 버전은 배우를 단일 문자열 필드 `Actor`로 저장했다. 이 필드는 새 버전에서 더 이상 사용하지 않으며(`Actors` 목록으로 대체), 예전 `library.json`을 불러와도 `Actor` 필드는 무시되고 `Actors`는 빈 목록으로 시작한다(당시 실제 사용자 데이터의 `Actor` 값이 모두 빈 문자열이었음을 확인 후 결정 — 별도 마이그레이션 로직 없음).

### 시리즈 마스터 목록 관리 (`SeriesManagerWindow`)

배우와 별개로, 관리 리스트 항목이 속한 **시리즈**(예: 같은 기획/레이블 묶음)를 관리하는 마스터 목록이다(2026-08-09 추가, **구현 완료**). 배우와 달리 한 항목에는 **시리즈를 하나만** 지정할 수 있다(콤보박스 단일 선택, 태그/배우처럼 여러 개 체크하는 방식이 아님).

- **데이터 모델(`SeriesItem`)**: `Name`(문자열) + `Credits`(`List<string>`, 이 시리즈에 속한 것으로 등록된 품번 목록, `ActorItem.Credits`와 완전히 동일한 패턴 — `private set` + `[JsonInclude]` + `SetCredits(...)`). 배우와 달리 썸네일/출생년도 같은 부가 정보는 없다.
- **저장 위치**: `%LOCALAPPDATA%\VideoVault\series.json`(`AppPaths.SeriesPath`, `SeriesRepository`). 관리 리스트/태그/배우와 동일하게 프로그램 시작 시 자동 로딩되고, 파일이 없으면 빈 목록으로 시작한다.
- **자동 저장**: 시리즈 추가/삭제/Credits 변경 시 500ms debounce 후 `series.json`에 자동 저장(관리 리스트/태그/배우와 동일한 정책, `SeriesItem.PropertyChanged`를 구독). Ctrl+S로도 즉시 저장된다(`SaveAllNow`에 포함).
- **관리 리스트 항목과의 관계**: `ManagedVideoItem.Series`는 시리즈 마스터 목록의 이름 하나만 참조하는 단일 문자열(기본값 빈 문자열)이다 — `Code`/`ReleaseDate`와 마찬가지로 public setter라 `[JsonInclude]`가 필요 없다.
- **시리즈 관리 화면(`SeriesManagerWindow`, 기본 크기 450x700)**: 메인창 "도구 > 시리즈 관리" 메뉴 또는 관리 리스트 툴바의 "시리즈 관리" 버튼("배우 관리" 버튼 옆)으로 연다. 배우 관리 창처럼 아이콘 그리드가 아니라 태그 관리 창과 비슷한 **단순 리스트** 형태다(썸네일이 없으므로).
  - **목록**: 창 맨 위에 시리즈 이름 리스트(약 10줄 높이로 보이도록 `Height="220"` 고정, `DisplayMemberPath="Name"`으로 이름을 표시 — 문자열이 아니라 `SeriesItem` 객체를 바인딩하므로 태그 목록과 달리 이 설정이 필요하다). 이름 기준 오름차순 정렬(`ICollectionView` + `SortDescription`, 태그/배우 관리 창과 동일한 패턴).
  - **시리즈 추가/삭제**: 목록 밑 텍스트 상자 + "시리즈 추가" 버튼으로 추가(추가된 항목을 바로 선택+스크롤). "시리즈 삭제" 버튼은 "시리즈 추가" 버튼 **바로 옆**에 있다(2026-08-09 위치 변경 — 예전에는 "작품 추가" 버튼과 같은 줄에 있었음). 선택한 시리즈를 삭제하면, 이 시리즈가 지정된 모든 관리 리스트 항목의 `Series`도 빈 문자열로 되돌아간다(참조 무결성 유지 — 태그/배우 삭제와 동일한 원칙). 이름 변경 기능은 없다(요구사항에 없어 범위 밖).
  - **Credits(이 시리즈에 속한 작품 목록)**: 버튼 줄 밑에 "품번" 라벨과 그 아래 Credits 칩 목록이 있다 — **배우 관리 창의 Credits 패널과 완전히 동일한 로직/스타일**(진한 색=관리 리스트에 실제 파일 있음, 연한 색=아직 없음; **품번 기준 오름차순 정렬**, 2026-08-09 추가; 칩 좌클릭 시 일치하는 항목의 속성 창 열기, 우클릭 시 Credits에서 제거; 선택 시 관리 리스트에서 이 시리즈로 지정된 파일들의 품번을 자동 병합; "작품 추가" 버튼은 배우 관리 창과 같은 `AddCreditWindow`를 그대로 재사용). **우클릭 삭제 시, 이 품번과 일치하는 실제 파일이 아직 이 시리즈를 `Series`로 갖고 있으면 그 필드도 함께 비운다**(2026-08-09 버그 수정) — 그렇지 않으면 자동 병합이 삭제 직후 곧바로 되살려서 "삭제가 안 되는" 버그가 있었다(배우 Credits 삭제와 동일한 원인/수정). 유일한 차이는 "작품 추가"로 새 품번을 추가했을 때 자동 태깅 동작이다 — 배우는 다중 선택이라 항상 추가하지만, 시리즈는 단일 값이므로 **매칭되는 파일의 `Series`가 비어있을 때만** 이 시리즈 이름으로 채운다(이미 다른 시리즈가 지정된 파일은 조용히 건드리지 않는다, `UpdateManagedItemSeriesForCredit`).
- **화면 노출**:
  - **속성 창(`PropertiesWindow`)**: "배우" 항목 바로 밑에 "시리즈:" 콤보박스가 있다(2026-08-09 추가). 마스터 목록 이름 + 맨 앞의 "(없음)" 항목(시리즈 미지정을 나타내는 센티널, `PropertiesWindow.NoSeriesLabel`)으로 채워지며, "확인" 클릭 시(다른 필드와 동일하게 커밋 시점에만 반영) 선택값이 "(없음)"이면 `_item.Series`를 빈 문자열로, 아니면 선택된 이름으로 설정한다. 태그/배우처럼 실시간 반영이 아니라 코드/출시일과 같은 "커밋 시점 반영" 패턴이다. 마스터 목록에서 이미 삭제된 시리즈 이름을 항목이 그대로 갖고 있으면(다른 곳에서 삭제된 경우) 목록에 함께 포함시켜 데이터가 조용히 사라지지 않게 한다(`RefreshSeriesComboBox`).
  - 관리 리스트 컬럼/하단 상세정보 패널에는 아직 노출하지 않는다(요구사항에 없어 범위 밖 — 향후 필요 시 "배우" 컬럼과 같은 패턴으로 추가 가능).
- **파일명 변경 시 Credits 동기화 (`SeriesCreditSync.cs`, 2026-08-09 추가, 구현 완료)**: `RenameHelper.TryRenameManagedItemTo`/`TryEditFullPath`(파일명이 실제로 바뀌는 두 rename/이동 경로)가 성공하면 `SeriesCreditSync.OnFileRenamed`를 호출해, 이 파일이 지정된 시리즈의 Credits에서 **옛 품번을 새 품번으로 갱신**한다. **버그 수정**: 예전에는 이 동기화가 없어서, 파일명이 바뀌어 품번이 달라지면(예: F2 이름변경, 속성 창 파일명 수정) 새 품번은 다른 곳(`SyncCreditsFromManagedItems`, 시리즈 선택 시 자동 병합)에서 알아서 추가되는데 **옛 품번은 아무도 지우지 않아 Credits에 고아로 남는** 버그가 있었다(배우 쪽은 `ActorCreditSync.OnFileRenamed`가 처음부터 이 역할을 했으나, 시리즈 기능이 나중에 추가되면서 이 연동이 빠져 있었다). `ActorCreditSync`와 마찬가지로 새 품번이 이미 다른(아직 파일 없는) 시리즈의 Credits와 일치하고 이 파일에 시리즈가 비어있으면 그 시리즈를 자동으로 지정하는 역방향 동작도 포함한다. `PropertiesWindow`도 rename 성공 시 `RefreshSeriesComboBox()`를 호출해(배우의 `SyncSelectedActorsFromItem()`과 동일한 패턴) 이 자동 지정을 화면에 즉시 반영한다.
- **"시리즈 동기화" 버튼(`MainWindow.SyncSeries_Click`, 2026-08-10 추가, 같은 날 "속성" 버튼 밑 줄로 위치 변경, 구현 완료)**: 관리 리스트 툴바 5번째 줄("속성"/"태그 관리"/"배우 관리"/"시리즈 관리" 줄 바로 밑), "배우 동기화" 버튼 오른쪽에 있다(도구 메뉴에도 동일 항목 있음). 지금까지의 동기화는 모두 "관리 리스트 → 시리즈 Credits" 방향(선택 시 자동 병합, rename 시 갱신)이었는데, 이 버튼은 **반대 방향**("시리즈 Credits → 관리 리스트")을 수동으로 일괄 실행한다 — 모든 시리즈의 Credits(품번)를 훑어서, 그 품번과 일치하지만 아직 `Series`가 비어있는 관리 리스트 항목에 그 시리즈를 채워 넣는다. **이미 다른 시리즈가 지정된 항목은 절대 덮어쓰지 않고 충돌로 건너뛴 뒤 결과 대화상자에 별도로 보고한다**(사용자가 직접 지정한 값을 임의로 바꾸지 않기 위함 — 배우/시리즈 Credits 우클릭 삭제 등 다른 곳과 동일한 원칙). 결과 대화상자는 업데이트된 항목 수와 목록(최대 20개 표시 후 "...외 N건 더"), 충돌 건수와 목록을 보여준다. 실행 전 확인 대화상자는 없다 — 기존 값을 덮어쓰지 않는 안전한 연산이므로.
- **`PropertiesWindow`/`ActorManagerWindow` 생성자가 `masterSeries`(`ObservableCollection<SeriesItem>`)를 추가로 받는다**(2026-08-09 변경) — 배우 관리 창의 Credits 칩에서도 속성 창을 열 수 있어야 하므로(그래야 그 창의 시리즈 콤보박스도 정상 동작), `ActorManagerWindow`가 내부적으로 여는 `PropertiesWindow`에도 `masterSeries`를 그대로 전달해야 한다. `MainWindow`/`SeriesManagerWindow`가 `PropertiesWindow`/`ActorManagerWindow`를 여는 모든 지점이 이 매개변수를 함께 넘긴다.

## 썸네일 관리

`ThumbnailPath`(320x240 리사이즈본 경로)와 `ThumbnailOriginalPath`(리사이즈 전 원본 경로)를 함께 저장한다. 지정하지 않으면 기본 아이콘(🎬)을 표시한다.

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
2. **속성창 뷰어** (`PropertiesWindow` 맨 아래): 뷰어 위에 현재 썸네일 경로(없으면 "지정된 썸네일 없음") 텍스트와 "썸네일 추가" 버튼이 있고, 그 아래 320x240 뷰어가 **가운데 정렬**로 놓인다.
   - **클릭**하면 원본창이 열린다.
   - **"썸네일 추가" 버튼** 또는 **드래그 앤 드롭**으로 새 썸네일을 지정할 수 있다.
3. **원본창** (`OriginalImageWindow`, 신규): 위 두 뷰어 중 하나를 클릭하면 뜨는 별도 창. `ThumbnailOriginalPath`의 원본(리사이즈 전) 이미지를 크게 보여준다. **아무 곳이나 클릭하면 닫힌다** (`Window_MouseLeftButtonUp` → `Close()`). 항목에 원본 이미지가 없으면(`ThumbnailOriginalPath`가 null이거나 파일이 없으면) 아무 반응도 하지 않는다.

(아이콘 보기 카드의 작은 썸네일 영역 클릭(`ThumbnailArea_Click`)은 위 "뷰어"와 별개의 UI로, 기존처럼 파일 선택 대화상자를 통한 추가만 지원한다 — 변경 없음.)

### 썸네일 생성 방식 (`ThumbnailHelper.CreateThumbnail`)

이미지를 새로 지정하면(버튼/드래그 앤 드롭 어느 경로든) 동영상 파일과 같은 폴더에 **원본 파일**과 **썸네일용 파일**을 각각 저장한다:

1. **원본**: 리사이즈하지 않고 그대로 `"{동영상 파일명(확장자 제외)}.original{원본 확장자}"`로 복사한다 (예: `movie.mp4` + `photo.png` → `movie.original.png`). 소스가 이미 이 경로 자신이면(원본을 다시 소스로 드래그하는 경우 등) 복사를 건너뛴다.
2. **썸네일**: 원본을 **320x240 이내로, 가로세로 비율을 유지하며** 리사이즈해 `"{동영상 파일명}.thumbnail.jpg"`로 저장한다. `min(320/원본가로, 240/원본세로)` 배율을 가로/세로에 동일하게 적용하므로(`TransformedBitmap` + `ScaleTransform`), 결과가 정확히 320x240일 필요는 없다 — 예: 2:1 비율 원본은 320x160, 1:2 비율 원본은 120x240이 된다. 뷰어는 어차피 `Stretch="Uniform"`이라 다양한 크기의 썸네일도 320x240 박스 안에 그대로 맞춰 보인다.
3. **소스 정리**: 원본/썸네일 두 파일로 복사가 끝나면, 소스로 쓰인 원래 파일(`sourceImagePath`)은 삭제한다 (`TryDeleteSource`, 실패해도 전체 동작은 성공으로 취급 — 삭제는 부가 정리일 뿐 핵심 결과가 아니므로). 사용자가 직접 고른 파일이 지워지는 것뿐 아니라, 아래 "인터넷 이미지 드래그 앤 드롭"에서 만들어지는 임시 다운로드 파일도 이 과정으로 자동 정리된다.
4. `ManagedVideoItem.ThumbnailPath`(썸네일용)와 `ThumbnailOriginalPath`(원본)를 각각 갱신한다.
5. 같은 항목에 썸네일을 다시 지정하면 같은 파일명이므로 두 파일 모두 자동으로 덮어써진다.

`ThumbnailHelper.CreateThumbnail`은 `(string ThumbnailPath, string OriginalPath)` 두 경로를 담은 `Result`를 반환하며, 메인창/속성창의 `ApplyThumbnail`이 동일하게 이 결과를 항목에 반영한다.

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
- 원본 동영상 파일이 [제거(Remove) 메커니즘](#제거remove-메커니즘)으로 이동/삭제 처리될 때, 같은 폴더의 `.thumbnail.jpg`/`.original.*` 파일은 함께 정리하지 않는다 (고아 파일로 남을 수 있음)
- 브라우저가 WebP 등 WPF의 기본 `BitmapDecoder`(WIC)가 지원하지 않는 형식의 이미지를 제공하는 경우, 다운로드/저장은 되어도 리사이즈 단계에서 디코딩 오류가 날 수 있다 (별도 코덱 없이는 미지원)

향후 확장 아이디어:

- **자동 썸네일 생성**: ffmpeg 등으로 동영상 첫 프레임을 캡처해 자동으로 썸네일을 지정하는 옵션. 데이터 모델의 `AutoThumbnailGenerated`(bool)는 이 기능을 위해 미리 추가된 필드로, 자동 생성된 썸네일인지(사용자가 직접 지정한 것과 구분) 여부를 기록한다.

## 설정 관리

프로그램 종료 후에도 다음 상태를 복원한다 — **구현 완료**.

- 마지막 보기 모드 (리스트 / 아이콘)
- 마지막 정렬 상태 (컬럼 + 오름/내림차순)
- 마지막 필터 상태 (파일명 검색어, 폴더명 검색어, 선택된 시리즈(`AppSettings.SeriesFilter`, 2026-08-09 추가), 선택된 태그, 선택된 배우, "제거된 항목도 표시" 체크 여부)
- 마지막 열린 폴더 (폴더가 더 이상 존재하지 않으면 무시하고 빈 폴더 목록으로 시작)
- 마지막으로 표시하던 컬럼 목록 (`AppSettings.VisibleColumns`, 예: `["Size", "PlayCount", "Tags"]`) — 파일명은 항상 표시되므로 이 목록에 포함되지 않는다. 구버전 `settings.json`(이 필드가 없는 파일)을 불러오면 기본값(크기/재생횟수/태그)이 적용된다.
- **리스트 보기 컬럼 너비** (`AppSettings.ColumnWidths`, 2026-08-02 추가, **구현 완료**) — 파일명 컬럼을 포함해 모든 컬럼(`FileName`/`Thumbnail`/`Size`/`PlayCount`/`Tags`/`Actor`/`Memo`/`Folder`, `VisibleColumns`와 같은 컬럼 키)의 너비를 `Dictionary<string, double>`로 저장한다. **숨겨진 컬럼도 마지막으로 가졌던 너비를 함께 저장**해뒀다가 나중에 다시 표시할 때 그 너비로 복원한다. 구버전 `settings.json`(이 필드가 없거나 특정 키가 없는 파일)을 불러오면 해당 컬럼은 XAML/`BuildManagedColumns`에 정의된 기본 너비가 그대로 적용된다.
- **마지막 선택 항목** (`AppSettings.SelectedItemPath`, 2026-08-02 추가, **구현 완료**) — 종료 시점에 관리 리스트에서 선택돼 있던 항목의 `FullPath`를 저장해두고, 다음 실행 시 그 경로와 일치하는 항목을 찾아 선택하고 화면에 스크롤해서 보여준다(리스트 보기/아이콘 보기 모두 지원). 파일명이 아니라 전체 경로로 매칭해 같은 이름의 다른 파일과 혼동되지 않게 한다. 항목을 찾지 못하면(파일이 삭제됐거나 현재 필터에 걸러짐) 조용히 무시한다. 선택(`Selector.SelectedItem`)은 `ApplySettings`에서 바로 적용하지만, **스크롤(`ScrollIntoView`)은 창이 처음 렌더링된 뒤인 `Window.Loaded`(`MainWindow_Loaded`)에서 수행**한다 — 생성자 시점에는 아직 레이아웃이 없어 `ScrollIntoView`가 동작하지 않기 때문이다.
- **주요 창의 마지막 화면 위치** (`AppSettings.WindowPositions`, 2026-08-04 추가, **구현 완료**) — [창 관리 정책](#창-관리-정책)의 규칙 4 참고. `WindowPositionMemory`가 메모리에서 관리하며, 이 설정 필드는 그 스냅샷을 불러오고(`ApplySettings`) 저장하는(`SaveSettings`) 용도다.
- **`MainWindow` 자신의 마지막 크기/위치** (`AppSettings.MainWindowWidth/Height/Left/Top`, 2026-08-07 추가, **구현 완료**) — 주요 창 4개와 별개로 메인 창 자신의 크기/위치도 기억한다. `ApplySettings`가 시작 시 네 값이 모두 있고 `WindowPositionMemory.IsOnScreen`으로 화면 안에 있을 만하면 `WindowStartupLocation`을 `Manual`로 바꾸고 적용하며, 값이 없거나(구버전 설정 파일) 화면 밖으로 판정되면 XAML 기본값(`Height="1200" Width="1720"`, `CenterScreen`)을 그대로 쓴다. `SaveSettings`가 저장할 때 **창이 최대화 상태이면 `RestoreBounds`(최대화 이전의 "정상" 크기/위치)를 저장한다** — 그렇지 않으면 다음 실행 시 항상 전체 화면 크기로 열리게 된다.

저장 위치는 `%LOCALAPPDATA%\VideoVault\settings.json`이며(`AppPaths.SettingsPath`), `library.json`/`tags.json`과 동일하게 변경 시마다 500ms debounce 후 자동 저장된다 (`AppSettings` 모델 + `SettingsRepository`). 시작 시 자동 로딩 로직(`LoadInitialData`) 안에서 함께 불러와 적용하며, 이 복원 과정 자체는 다시 저장을 트리거하지 않는다. **예외**: 컬럼 너비·마지막 선택 항목·주요 창 위치·메인 창 자신의 크기/위치는 헤더 드래그/일반 클릭 선택/창 이동·크기 조절 시점마다 자동 저장을 예약하기엔 너무 잦아서(변경 이벤트도 마땅치 않은 경우가 많음) 다른 설정처럼 즉시 debounce가 걸리지 않는다 — 대신 **`MainWindow.Closing`(창을 닫는 시점)에 한 번, 그 시점의 최종 상태를 즉시 저장**해 "종료 전 상태 유지"를 보장한다(`MainWindow_Closing` → `SaveAllNow()`, `Ctrl+S`의 `SaveNow_Click`과 저장 로직을 공유).

**즉시 저장 (Ctrl+S)**: 위 debounce를 기다리지 않고 관리 리스트(`library.json`)/태그(`tags.json`)/배우(`actors.json`)/시리즈(`series.json`)/설정(`settings.json`)을 즉시 저장한다 — **구현 완료**. `MainWindow`의 전역 `PreviewKeyDown`에서 처리하며, 파일 메뉴의 "저장"이 동일한 `SaveAllNow()`(`SaveNow_Click`이 호출)를 공유하고, 창을 닫을 때도 같은 메서드가 호출된다(관리 리스트 툴바의 "저장 (Ctrl+S)" 버튼은 2026-08-09 삭제되었지만 이 메뉴/단축키로 기능은 그대로 유지된다). 평소에는 어차피 500ms 후 자동 저장되므로, Ctrl+S는 "지금 바로 디스크에 반영됐다는 확신"을 주는 용도에 가깝다.

## 오류 처리

아래 상황들에 대해 일관된 정책을 따른다: **예외를 catch하여 `MessageBox`로 사용자에게 알리고, 프로그램은 종료하지 않고 가능한 범위에서 계속 동작 가능한 상태를 유지한다.** (현재 각 Repository 호출부에서 이미 이 패턴을 따르고 있다.)

- 재생하려는 파일이 존재하지 않는 경우 → 재생 실패 메시지, 관리 리스트 항목은 유지
- 파일/폴더 접근 권한이 없는 경우 → 오류 메시지
- **시작 시 로딩 실패 처리(2026-08-02 개선, 구현 완료)**: `MainWindow.LoadInitialData()`가 관리 리스트/레거시 제거 항목 병합/태그/배우/설정/파일 존재 여부 확인 6단계를 각각 독립된 `LoadStep(설명, 동작)` 헬퍼로 감싼다 — 한 단계가 손상된 파일 등으로 실패해도 그 단계만 오류 메시지를 띄우고 건너뛴 뒤 나머지 단계는 계속 진행한다(예: `tags.json`이 손상돼도 `actors.json`/`settings.json` 로딩과 파일 존재 여부 확인은 정상 진행됨). 예전에는 전체가 하나의 try/catch였어서 첫 실패 지점 이후 모든 로딩이 통째로 중단됐다. `library.json`/`tags.json`처럼 특정 파일이 손상됐을 때 "빈 목록으로 시작"할지 "로딩을 막고 사용자가 직접 조치"하게 할지는 여전히 추후 결정 필요 — 지금은 오류 메시지를 띄우고 그 단계만 빈 상태로 건너뛴다.
- 썸네일 이미지 손상/깨짐 → [썸네일 관리](#썸네일-관리) 절 참고

## 성능

대용량 데이터에서도 반응성을 유지하기 위한 방향 (일부는 이미 구현, 일부는 향후 작업):

- 관리 리스트는 `ObservableCollection` + `CollectionViewSource`(정렬/필터)로 구현되어 있음 — **구현 완료**
- 리스트 보기의 `ListView`는 WPF 기본 가상화(`VirtualizingStackPanel`)를 사용함 — **구현 완료** (기본 동작)
- 아이콘 보기는 `WrapPanel` 기반이라 기본적으로 가상화되지 않음 — **향후 개선 필요** (아이템 수가 많아지면 `VirtualizingWrapPanel` 등으로 교체 검토)
- 폴더 스캔은 현재 동기적으로 실행되어 대형 폴더에서 UI가 잠시 멈출 수 있음 — **향후 비동기(Task)로 전환 필요**, 전환 시 UI 스레드를 블로킹하지 않도록 한다

## 확장성

향후 구현 교체를 쉽게 하기 위해, 아래 인터페이스 도입을 고려한다 (아직 미구현 — 현재는 구체 클래스만 존재):

```
IThumbnailProvider
IVideoScanner
IManagedListRepository
ITagRepository
```

## 아키텍처

- `MainWindow.xaml.cs`에 모든 로직이 집중되지 않도록, MVVM을 초기 단계부터 도입하는 방향으로 전환한다.
  - **현재 상태**: 아직 code-behind 중심(`MainWindow.xaml.cs`)으로 구현되어 있으며 MVVM 전환은 완료되지 않았다. 새로 추가하는 기능부터 아래 계층 구조를 따르고, 기존 code-behind 로직은 점진적으로 옮긴다.
- 계층 구조:
  - **Repository** (`ManagedListRepository`, `TagRepository`, 추가될 `SettingsRepository`): 파일 읽기/쓰기만 담당, 비즈니스 로직 없음
  - **Service** (계획: `VideoLibraryService`, `TagService`, `ThumbnailService`): 정렬/필터/태그 동기화 등 비즈니스 로직
  - **ViewModel** (계획: `MainWindowViewModel`, `TagManagerViewModel`): View에 바인딩할 상태 및 커맨드 노출
  - **View** (`MainWindow.xaml` 등): UI만 담당, ViewModel에 바인딩

## 창 관리 정책

주요 창(`FolderListWindow`/`PropertiesWindow`/`ActorManagerWindow`/`TagManagerWindow`/`SeriesManagerWindow` — `MainWindow`에서 직접 열거나 이들끼리 서로 넘나드는 창들)에 적용되는 규칙이다(2026-08-04 추가, **구현 완료**; `SeriesManagerWindow`는 2026-08-09 추가). 이 다섯 외의 작은 입력 대화상자(`RenameWindow`/`ActorInfoWindow`/`AddCreditWindow`/`OriginalImageWindow`, 그리고 `OpenFileDialog`/`SaveFileDialog`/`OpenFolderDialog` 같은 시스템 대화상자)는 대상이 아니며, 지금처럼 여는 쪽 창을 `Owner`로 하는 진짜 모달(`ShowDialog()`)로 남아있다 — 어차피 한 창에서만 짧게 값을 입력받고 바로 돌아오는 용도라 이 규칙이 필요 없다.

- **규칙 1: 모든(주요) 창은 독립적인 상태를 갖는다.** 여러 개가 동시에 열려 있어도 서로의 내부 UI 상태(선택 항목, 스크롤 위치, 입력 중인 값)에 영향을 주지 않는다.
- **규칙 2: 모든 창은 데이터가 바뀌면 동시에 갱신된다.** 어느 창에서 관리 리스트/태그/배우 데이터를 바꾸든, 다른 창(과 메인 창의 목록·상세정보 패널)이 닫았다 열지 않아도 즉시 반영된다.
- **규칙 3: 창 종류별로 동시에 최대 1개만 열린다.** 이미 같은 종류의 창이 열려 있는 상태에서 그 종류를 다시 열려고 하면, 기존 창을 먼저 닫고 새 창을 연다. (서로 다른 종류끼리는 동시에 열려 있을 수 있다 — 예: 배우 관리 창과 속성 창을 동시에 띄워둘 수 있다.)
- **규칙 4: 마지막으로 열려 있던 화면 위치를 기억한다**(2026-08-04 추가, **구현 완료**). 창을 닫을 때의 위치(Left/Top)를 창 종류별로 기억해뒀다가, 다음에 그 종류의 창을 열면 같은 위치에 연다. 프로그램을 재시작해도 유지된다(설정 파일에 저장).
- **규칙 5: 관리 리스트의 선택이 바뀌면 열려 있는 속성 창도 그 항목을 따라간다**(2026-08-04 추가, **구현 완료**). 속성 창이 모덜리스로 바뀌면서 이제 그 창을 열어둔 채로 관리 리스트의 다른 행을 클릭할 수 있게 됐는데, 예전에는(모달이던 시절 그대로) 클릭해도 속성 창이 처음 열었던 항목을 계속 보여줬다 — 창을 닫았다가 다시 열어야만 새로 클릭한 항목을 볼 수 있었다. 지금은 클릭 즉시 같은 창이 그 항목으로 갱신된다(규칙 2 "동시 갱신"의 연장선).

**구현 방식**:
- 이 넷은 전부 `ShowDialog()`(모달)가 아니라 `Show()`(모덜리스)로 연다(2026-08-04 변경 — 예전에는 전부 모달이었다). 그래야 서로 다른 종류끼리 동시에 열려 있으면서 각자 독립적으로 상호작용할 수 있다(규칙 3의 "다른 종류는 동시에 가능" 부분).
- **`SingleInstanceWindow<T>.cs`**: 창 종류(T)별로 "현재 열려 있는 인스턴스"를 추적하는 제네릭 static 헬퍼. `SingleInstanceWindow<T>.Show(window)`를 호출하면, 같은 T의 기존 인스턴스가 있으면 먼저 닫고(`Close()`) 새 인스턴스를 등록한 뒤 보여준다. 제네릭 static 클래스라 `T`마다(예: `SingleInstanceWindow<PropertiesWindow>`와 `SingleInstanceWindow<ActorManagerWindow>`) 독립된 저장소를 가지므로, 창 클래스마다 같은 static 필드를 반복해서 둘 필요가 없다(규칙 3 구현).
- **`Owner`는 항상 `MainWindow`로 고정한다** — `PropertiesWindow`↔`ActorManagerWindow` 서로 넘나드는 코드(`PropertiesWindow.OpenActorManagerFor`, `ActorManagerWindow.CreditChip_MouseLeftButtonUp`)에서 `Owner = this`(여는 쪽 창 자신)로 하면, WPF가 창을 닫을 때 그 창이 소유한(`OwnedWindows`) 창까지 자동으로 함께 닫아버리는 기본 동작 때문에, 나중에 그 "여는 쪽 창"이 종류별 단일 인스턴스 규칙으로 다른 창에 밀려 닫힐 때 방금 연 창까지 덩달아 닫혀버려 규칙 1(독립적 상태)이 깨진다. `Owner = Application.Current.MainWindow`로 고정해 이 연쇄 닫힘을 피한다.
- **`PropertiesWindow`의 확인/취소/완전삭제**: 모덜리스 전환으로 `Window.DialogResult`(모달 전용 API)를 더 이상 쓸 수 없어, `_explicitCloseHandled` bool 필드로 대체했다 — "확인"(`Ok_Click`)/"취소"(`Cancel_Click`, 예전에는 `Button.IsCancel="True"`였으나 이것도 모달 전용이라 명시적 핸들러로 교체)/"완전 삭제"(`DeleteButton_Click`) 버튼은 각자 처리를 마치자마자 이 플래그를 세우고 `Close()`를 호출하며, `Window_Closing`은 이 플래그가 없을 때만(제목 표시줄 X, Alt+F4 등으로 버튼을 거치지 않고 닫힌 경우) "확인"과 동일하게 커밋한다. Esc 키로 닫는 동작(`IsCancel`이 대신 해주던 것)은 `Window_PreviewKeyDown`에서 직접 처리한다.
- **`MainWindow`의 각 "창 열기" 핸들러**(`OpenFolderList_Click`/`ManageTags_Click`/`ManageActors_Click`/`OpenPropertiesWindow`)는 예전에 `ShowDialog()` 호출이 반환된 직후 동기적으로 하던 후처리(`_managedView.Refresh()`, `LastFolder`/`PermanentlyDeleted` 반영 등)를 전부 그 창의 `Closed` 이벤트 핸들러로 옮겼다 — `Show()`는 블로킹하지 않으므로 더 이상 "반환된 직후"라는 시점이 없기 때문이다.
- **규칙 4(위치 기억)의 실제 구현**: `WindowPositionMemory.cs`가 창 종류 이름(`typeof(T).Name`)을 키로 위치를 메모리에 들고 있고, `SingleInstanceWindow<T>.Show(window)`가 창을 열기 직전에 기억된 위치가 있으면(그리고 `SystemParameters.VirtualScreenLeft/Top/Width/Height` 기준으로 지금도 화면 안에 있을 만하면 — 모니터 구성이 바뀌어 화면 밖 좌표가 남는 경우를 걸러내기 위함) `WindowStartupLocation`을 `Manual`로 바꾸고 `Left`/`Top`을 적용하며, 창이 닫힐 때 그 시점의 위치를 다시 기억해둔다. 기억된 위치가 없으면(처음 열거나 화면 밖으로 판정된 경우) XAML에 정의된 기본 시작 위치(`CenterOwner`)를 그대로 쓴다. `MainWindow`는 시작 시(`ApplySettings`) `AppSettings.WindowPositions`(키: 창 클래스 이름, 값: `[Left, Top]`)를 `WindowPositionMemory.LoadFrom`으로 불러오고, 설정 저장 시(`SaveSettings`) `WindowPositionMemory.ToDictionary()`로 다시 채워 넣는다 — 컬럼 너비/마지막 선택 항목과 같은 카테고리라 값이 바뀔 때마다 즉시 debounce가 걸리지는 않고, `Ctrl+S`/창 닫기 시점에 그때까지 기억된 값이 저장된다. **적용 범위는 "주요 창" 4개뿐이다** — `MainWindow` 자신의 위치·크기는 이 `WindowPositionMemory`(창 종류별 딕셔너리) 대상이 아니다. 대신 `MainWindow`는 `AppSettings.MainWindowWidth/Height/Left/Top` 4개 필드로 **자기 자신의** 크기/위치를 별도로 저장한다(2026-08-07 추가) — [설정 관리](#설정-관리) 참고. `WindowPositionMemory.IsOnScreen(left, top)`은 이 두 메커니즘이 공유하는 화면 안쪽 판정 로직이라 `public`으로 노출되어 있다.
- **규칙 5(선택 항목 따라가기)의 실제 구현**: `PropertiesWindow.SwitchToItem(newItem)`이 창을 새로 만들지 않고 지금 열려 있는 창 안의 `_item`(그리고 파일 정보/태그 체크박스/배우 콤보박스·칩/썸네일/스크롤 위치)만 새 항목 기준으로 다시 그린다. `MainWindow.ManagedSelection_Changed`가 선택이 바뀔 때마다 `SingleInstanceWindow<PropertiesWindow>.Current?.SwitchToItem(item)`을 호출한다(`SingleInstanceWindow<T>.Current`는 이 기능을 위해 추가한 정적 프로퍼티 — "지금 열려 있는 이 종류의 창"을 다른 코드가 찾아 상호작용할 수 있게 해준다). 전환 전에 이전 항목의 커밋 대기 중인 변경사항(태그/배우 선택 등)을 먼저 커밋한다(`TryCommitFormFields`) — 조용히 버리지 않기 위함이다. **주의**: 이 때문에 속성 창이 지금 보여주는 항목은 창을 처음 열 때 넘긴 항목과 다를 수 있으므로, "완전 삭제" 시 관리 리스트에서 지울 대상은 호출자가 붙잡고 있던 원래 변수가 아니라 `PropertiesWindow.CurrentItem`(지금 보여주는 항목을 노출하는 프로퍼티)이어야 한다 — `MainWindow.OpenPropertiesWindow`/`ActorManagerWindow.CreditChip_MouseLeftButtonUp`의 `Closed` 핸들러 모두 이 프로퍼티를 사용한다.
- **규칙 2(동시 갱신)의 실제 구현**:
  - `MainWindow._managedView`(관리 리스트의 `ICollectionView`)에 **라이브 필터링/정렬**(`ICollectionViewLiveShaping.IsLiveFiltering`/`IsLiveSorting` + `LiveFilteringProperties`/`LiveSortingProperties`, `EnableLiveShaping()`)을 켜뒀다. 필터/정렬에 쓰이는 속성(`IsValid`/`FileName`/`Tags`/`Actors`, 정렬 가능한 모든 컬럼 속성)을 감시 대상으로 등록해두면, 그 속성이 바뀔 때마다(어느 창에서 바꿨든) WPF가 자동으로 그 항목의 필터 통과 여부·정렬 위치를 재평가한다 — `_managedView.Refresh()`를 수동으로 호출할 필요가 없다(다만 창을 닫을 때는 여전히 안전망으로 `Refresh()`도 함께 호출한다).
  - `MainWindow.ManagedItem_PropertyChanged`가 **바뀐 항목이 현재 선택된 항목이면** `UpdateSelectedItemDetails()`를 호출한다 — 하단 "선택된 항목 상세 정보" 패널의 배우 썸네일 목록(`SelectedItemActorsPanel.ItemsSource`)은 `Actors`에 직접 바인딩된 게 아니라 매번 계산해서 채우는 값이라, 이 훅이 없으면 다른 창에서 그 항목의 배우/태그/메모 등을 바꿔도 메인 창 하단 패널이 그 즉시 반영되지 않는다.
  - 이 두 가지 덕분에, 예를 들어 배우 관리 창에서 배우 이름을 바꾸거나 속성 창에서 태그/배우/메모를 바꾸면, 다른 창을 닫지 않고도 메인 창의 목록과 하단 상세정보 패널에 즉시 반영된다.
  - **태그/배우 칩 셀이 창을 옮겨야만 다시 그려지던 문제(2026-08-04 수정, 구현 완료)**: 위 라이브 필터링/정렬은 항목의 필터 통과 여부·정렬 "위치"만 재평가할 뿐, 이미 화면에 그려진 행(row)의 셀 내용을 다시 그리는 것까지 보장하지는 않는다. 태그/배우 칩은 `WrapPanel`이라 내용에 따라 셀 높이가 바뀌는데, 가상화된 `ListView`/`ListBox`가 이 높이 변화를 곧바로 반영하지 않아 데이터는 바뀌었는데도 화면에는 예전 칩이 그대로 남아있다가, 창을 옮기는 등 강제 레이아웃이 있어야 드러나는 문제가 있었다. `MainWindow.ManagedItem_PropertyChanged`에서 `Tags`/`Actors`가 바뀌면 `_managedView.Refresh()`를 호출해(목록 컨테이너를 완전히 다시 생성) 해결했다 — `_managedView.Refresh()`는 `ManagedListView`/`ManagedIconView`가 같은 `ICollectionView` 인스턴스를 공유하므로 양쪽 모두에 적용된다.
- **버그 수정: 속성 창에서 배우/시리즈를 바꿔도 이미 열려 있는 배우 관리/시리즈 관리 창의 Credits 패널이 갱신되지 않던 문제**(2026-08-10 수정, **구현 완료**) — `ActorManagerWindow`/`SeriesManagerWindow`를 열어둔 채로 `PropertiesWindow`에서 배우/시리즈 칩을 추가·제거해도, 이미 열려 있는 관리 창의 Credits 칩 목록이 그 즉시 반영되지 않고 창을 닫았다 다시 열거나(또는 다른 배우/시리즈를 선택했다가 되돌아와야만) 갱신되는 문제가 있었다. `RefreshCreditsPanel`(Credits 칩 목록을 다시 그리는 메서드)을 트리거하는 훅이 전혀 없었던 것이 원인이며, 수정은 **두 층(layer)**으로 이루어진다 — 한 층만으로는 불충분했다(아래 참고).
  - **1층: 관리 리스트 항목의 `Actors`/`Series`가 바뀌면** — `MainWindow.ManagedItems_CollectionChanged`/`ManagedItem_PropertyChanged`와 동일한 패턴으로, `ActorManagerWindow`/`SeriesManagerWindow`도 생성자에서 `_managedItems`의 모든 항목에 `PropertyChanged`를 구독하고(컬렉션 변경 시 구독도 함께 추가/해제) `Actors`(배우 창)/`Series`(시리즈 창)가 바뀌면 `RefreshCreditsPanel`을 호출한다. 창이 닫힐 때 모든 구독을 해제한다.
  - **2층(1층만으로는 불충분해서 추가로 필요했던 부분): 선택된 `ActorItem`/`SeriesItem` 자신의 `Credits`가 바뀌면** — `PropertiesWindow.TryCommitFormFields()`는 `_item.SetActors(...)`(1층이 감지하는 지점)를 먼저 호출하고, 그 **다음에** `ActorCreditSync.OnActorRemovedFromItem`이 실제로 `actor.Credits`에서 품번을 제거한다(시리즈도 `SeriesCreditSync.OnFileRenamed`/`UpdateManagedItemSeriesForCredit`와 같은 이유로 동일한 순서 문제가 있음). 즉 1층 훅이 실행되는 시점에는 `actor.Credits`가 **아직 옛 값 그대로**라 `RefreshCreditsPanel`을 호출해도 화면에 변화가 없었다 — 이게 실제로 재현되어 확인된 버그다. 해결을 위해 `ActorManagerWindow`/`SeriesManagerWindow`가 **현재 선택된 `ActorItem`/`SeriesItem` 자신의 `PropertyChanged`도 직접 구독**해 `Credits`가 바뀌면 `RefreshCreditsPanel`을 호출한다. 선택이 바뀔 때마다 이전 선택 대상 구독을 해제하고 새 선택 대상을 구독하는 "구독 교체" 패턴(`_subscribedCreditsActor`/`_subscribedCreditsSeries` 필드, `RefreshThumbnailPreview`/`RefreshCreditsPanel` 시작 부분에서 처리)을 쓴다 — 선택이 `null`이 되는 경우(시리즈)도 놓치지 않도록 이 구독 교체 로직은 `series is null` 조기 반환보다 먼저 실행된다.
  - **검증**: 실제 사용자 데이터(배우 "Suzumura Airi", 품번 "abp-197")로 재현 — 배우 관리 창을 열어 Suzumura Airi를 선택해 Credits에 `abp-163`/`abp-197`/`abp-383`이 보이는 상태를 만든 뒤, 그 창을 닫지 않고 속성 창에서 abp-197.avi의 배우 칩(Suzumura Airi)을 제거·확인하면, 배우 관리 창을 전혀 건드리지 않았는데도 Credits 목록에서 `abp-197`이 즉시 사라짐을 스크린샷으로 확인함(검증 후 실제 데이터는 원래 상태로 복원함).
- **적용 범위 밖(의도적으로 남겨둔 부분)**: `PropertiesWindow` 자신이 표시 중인 태그 체크박스/배우 콤보박스/배우 칩은, 그 창이 열려 있는 동안 **다른** 창에서 마스터 목록(태그/배우 이름)이나 이 항목의 `Tags`/`Actors`가 외부에서 바뀌어도 자동으로 다시 그려지지 않는다(스냅샷이라 라이브 반영 안 됨) — 동시에 같은 항목을 여러 창에서 편집하는 경우는 드물고, 잘못하면 사용자가 입력 중인 내용을 덮어써버릴 위험이 더 크다고 판단해 범위에서 제외했다. 창을 닫았다 다시 열면(또는 그 창이 "배우 관리 창에서 돌아왔을 때"처럼 자기 자신의 `Closed` 콜백을 갖는 경우는) 항상 최신 상태로 다시 그려진다.

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
- 새 "주요 창"을 추가할 때는 [컨벤션](#컨벤션)에 따라 `SingleInstanceWindow<T>` 등록과 함께 생성자에 `WindowSnapHelper.Attach(this)`도 호출해야 한다.

## 데이터 모델 (관리 리스트 JSON)

관리 리스트의 각 항목은 아래 필드를 가진다.

| 필드 | 설명 |
|---|---|
| `FileName` | 파일명 |
| `SizeBytes` | 파일 크기 (바이트) |
| `ModifiedDate` | 수정일 |
| `FullPath` | 전체 경로 |
| `PlayCount` | 재생횟수 |
| `Tags` | 태그 목록 (문자열 배열). setter가 `private`이라 `[JsonInclude]`를 반드시 붙여야 역직렬화가 된다 — [컨벤션](#컨벤션) 참고 |
| `ThumbnailPath` | 아이콘 보기/썸네일 뷰어에서 사용할 320x240 리사이즈본 이미지 파일 경로 (미지정 시 null, 기본 아이콘 표시) |
| `ThumbnailOriginalPath` | 리사이즈 전 원본 이미지 파일 경로 (미지정 시 null). [원본창](#원본창-originalimagewindow)에서 사용 |
| `Actors` | 배우 이름 목록 (문자열 배열, 배우 마스터 목록의 이름만 참조). `Tags`와 마찬가지로 setter가 `private`이라 `[JsonInclude]` 필요. 여러 명 지정 가능 — [배우 마스터 목록 관리](#배우-마스터-목록-관리-actormanagerwindow) 참고. 예전 버전의 단일 문자열 `Actor` 필드를 대체한 것으로, 예전 `library.json`을 불러오면 `Actor` 값은 무시되고 빈 목록으로 시작한다 |
| `Memo` | 메모 (문자열, 기본값 빈 문자열) — 속성 창에서 편집 |
| `Code` | 코드 (문자열, 기본값 빈 문자열) — 속성 창에서 편집. 비어있을 때는 `ManagedVideoItem.DeriveCode(fileName, fullPath)`로 자동 제안(파일명의 첫 "-" 이전 부분, "-"가 없으면 폴더명), 저장된 값이 있으면 그 값 유지. 파일명을 바꾸면 제안값도 다시 계산됨. "파일 이동" 버튼이 이 값을 폴더명으로 사용 |
| `ReleaseDate` | 출시일 (문자열, 기본값 빈 문자열, 2026-08-02 추가) — 속성 창에서 "코드" 옆에 편집. 자동 제안이나 형식 검증 없이 자유 텍스트 그대로 저장 |
| `Series` | 이 항목이 속한 시리즈 (문자열, 기본값 빈 문자열, 2026-08-09 추가). 시리즈 마스터 목록(`series.json`)의 이름 하나만 참조하며, 태그/배우와 달리 **단일 선택**이다(속성 창의 콤보박스로 선택). `Code`/`ReleaseDate`와 마찬가지로 public setter라 `[JsonInclude]`가 필요 없다 — [시리즈 마스터 목록 관리](#시리즈-마스터-목록-관리-seriesmanagerwindow) 참고 |
| `IsValid` | 사용자가 이 항목을 관리 리스트에 계속 두고 싶어 하는지 여부 (bool, 기본값 true, 2026-08-06 추가 — 예전 `IsArchived`를 대체, 의미는 반대). false면 "제거된" 상태로, 데이터는 유지되지만 기본적으로 화면에는 노출되지 않는다("제거된 항목도 표시" 체크박스로 노출 가능). `IsExist`와 독립적인 축이다 — [제거(Remove) 메커니즘](#제거remove-메커니즘) 참고 |
| `IsExist` | `FullPath`의 실제 파일이 지금 디스크에 있는지 여부 (bool, 기본값 true, 2026-08-06 추가). 시작 시 모든 항목에 대해 다시 판단해 갱신된다(`MainWindow.UpdateFileExistence`). `IsValid`를 자동으로 바꾸지 않으므로, 파일이 일시적으로 없어져도(외장 드라이브 분리 등) 관리 리스트에서 사라지지 않고 화면에서 다른 색(주황빛)으로만 표시된다 — [제거(Remove) 메커니즘](#제거remove-메커니즘) 참고 |
| `IsPlaceholder` | 실제 동영상 파일 없이 "파일 없이 추가" 버튼으로 수동으로 추가된 항목인지 여부 (bool, 기본값 false, 2026-08-06 추가). 이런 항목은 추가되는 즉시 `IsValid = false`도 함께 설정되어(제거된 항목과 동일하게 취급) `library.json`에 저장된다. true인 동안은 시작 시 파일 존재 여부 확인(`UpdateFileExistence`)에서 항상 `IsExist = false`로 취급되며, 속성 창에서 파일이 있어야 동작하는 기능(재생/파일 변경·이동)이 비활성화된다(썸네일은 예외 — 고정 폴더에 저장되어 계속 가능). 나중에 같은 이름(확장자 없이도 매칭)의 실제 파일이 추가되면 재사용 매칭 시 `IsValid`/`IsExist`가 true로, `IsPlaceholder`가 false로 되돌아간다 — [관리 리스트](#2-관리-리스트-사용자가-지속적으로-관리하는-목록)의 "파일 없이 추가" 참고 |
| `AutoThumbnailGenerated` | 썸네일이 자동 생성된 것인지 여부 (bool). [썸네일 관리](#썸네일-관리)의 향후 자동 생성 기능을 위한 필드 — **아직 미구현**, 현재 코드의 `ManagedVideoItem`에는 없음 |

화면에는 `FileName`(고정) + 사용자가 선택한 컬럼(`SizeBytes`, `PlayCount`, `Tags`, `Actor`, `Memo`, `FolderName`, `Code`, `Series`, `ReleaseDate` 중 헤더 우클릭으로 고른 것)을 표시한다. 기본으로는 `SizeBytes`/`PlayCount`/`Tags`가 켜져 있고 `Actor`/`Memo`/`FolderName`/`Code`/`Series`/`ReleaseDate`는 꺼져 있다. "배우" 컬럼은 `Actors`를 쉼표로 이어붙인 `ActorsDisplay`를 표시한다. `FolderName`은 JSON에 저장되지 않는 계산 속성으로, `FullPath`의 마지막 폴더명만 보여준다. (`ThumbnailPath`는 아이콘 보기 모드에서 이미지로 렌더링되어 표시됨)

## 데이터 모델 (태그 마스터 목록, `tags.json`)

태그 마스터 목록은 문자열 배열 형태로 저장한다.

```json
["코미디", "액션", "미시청", "즐겨찾기"]
```

관리 리스트 항목의 `Tags` 필드는 이 마스터 목록에 존재하는 값만 참조하도록 유지한다 (마스터 목록에 없는 태그가 항목에 들어가지 않도록 UI에서 강제).

## 데이터 모델 (배우 마스터 목록, `actors.json`)

배우 마스터 목록은 `ActorItem` 객체 배열 형태로 저장한다.

```json
[
    { "Name": "이즈미 리온", "ThumbnailPath": "C:\\Users\\...\\VideoVault\\actresses\\이즈미 리온.jpg", "BirthYear": 1994, "Height": 158, "BodyInfo": "B83 W58 H85", "Credits": ["SSNI-123", "SSNI-456"] },
    { "Name": "쿠로카와 사리나", "ThumbnailPath": null, "BirthYear": null, "Height": null, "BodyInfo": "", "Credits": [] }
]
```

관리 리스트 항목의 `Actors` 필드는 이 마스터 목록의 `Name` 값만 참조하도록 유지한다 (마스터 목록에 없는 배우가 항목에 들어가지 않도록 UI에서 강제). 썸네일 이미지 파일 자체는 `%LOCALAPPDATA%\VideoVault\actresses\` 폴더에 `{배우명}.jpg`로 저장된다 — [배우 마스터 목록 관리](#배우-마스터-목록-관리-actormanagerwindow) 참고. `Credits`(품번 목록)는 관리 리스트의 `Actors`와 `ActorCreditSync`로 상호 동기화된다 — 같은 절 참고.

## 데이터 모델 (시리즈 마스터 목록, `series.json`)

시리즈 마스터 목록은 `SeriesItem` 객체 배열 형태로 저장한다(2026-08-09 추가).

```json
[
    { "Name": "SS 시리즈", "Credits": ["SSNI-123", "SSNI-456"] },
    { "Name": "미분류 기획", "Credits": [] }
]
```

관리 리스트 항목의 `Series` 필드는 이 마스터 목록의 `Name` 값 하나만 참조하도록 유지한다(마스터 목록에 없는 값이나 여러 개를 동시에 갖지 않도록 속성 창의 콤보박스가 강제) — [시리즈 마스터 목록 관리](#시리즈-마스터-목록-관리-seriesmanagerwindow) 참고. `Credits`(품번 목록)는 배우의 `Credits`와 같은 패턴이지만 자동 태깅은 단방향으로만 동작한다(해당 항목의 `Series`가 비어있을 때만 채움) — 같은 절 참고.

## 컨벤션

- UI는 XAML로, 로직은 code-behind 또는 별도 클래스로 분리한다. 신규 기능은 [아키텍처](#아키텍처) 절의 Repository/Service/ViewModel 계층을 따른다.
- 새 화면/윈도우를 추가할 때는 기존 `MainWindow.xaml` 패턴(XAML + `.xaml.cs`)을 따른다.
- **주요 창**(`MainWindow`에서 직접 열거나 주요 창끼리 서로 넘나드는 창)을 새로 추가할 때는 [창 관리 정책](#창-관리-정책)을 따른다 — `Show()`(모덜리스) + `SingleInstanceWindow<T>.Show(window)`로 열고, `Owner`는 `Application.Current.MainWindow`로 고정하며, `ShowDialog()` 반환 직후 하던 후처리는 `Closed` 이벤트로 옮긴다. 생성자의 `InitializeComponent()` 직후에는 [창 스냅(자석 붙기)](#창-스냅자석-붙기)을 위해 `WindowSnapHelper.Attach(this)`도 호출해야 한다. 반대로 한 창에서 값을 잠깐 입력받고 바로 돌아오는 작은 대화상자(이름 변경/정보 수정 등)는 이 정책 대상이 아니며 기존처럼 `ShowDialog()`를 그대로 쓰면 된다.
- 화면(윈도우) 기본 크기는 일괄된 고정값을 따르지 않고, 창의 용도와 내용물에 맞게 개별적으로 정한다(2026-08-02 정정 — 예전에는 "기본 1920x1080, MainWindow만 예외"라고 되어 있었으나 실제로는 지켜진 적이 없어 문서를 실제 관행에 맞게 고쳤다). 새 창을 추가할 때도 내용에 맞는 크기를 자유롭게 정하면 된다. 참고로 현재 각 창의 실제 크기는 다음과 같다:
  - `MainWindow`: `Height="1200" Width="1720"`
  - `FolderListWindow`: `Height="700" Width="1000"`
  - `PropertiesWindow`: `Height="1200" Width="500"`
  - `ActorManagerWindow`: `Height="1000" Width="1000"`
  - `TagManagerWindow`: `Height="500" Width="400"`
  - `SeriesManagerWindow`: `Height="700" Width="450"`
  - `ActorInfoWindow`: `Height="290" Width="380"`
  - `RenameWindow`: `Height="150" Width="420"`
  - `OriginalImageWindow`: `Height="800" Width="1000"`
- 동영상 파일 확장자 목록은 `ManagedListImporter.VideoExtensions` 배열 하나로 관리한다(2026-08-05 변경 — 예전에는 `FolderListWindow.xaml.cs`에 별도로 있었으나, 관리 리스트 드래그 앤 드롭 추가 기능이 같은 목록을 필요로 해서 공용 위치로 옮겼다). `FolderListWindow`의 폴더 스캔도 이 목록을 참조한다.
- 관리 리스트의 데이터 모델(JSON 직렬화 대상) 및 JSON 파일 읽기/쓰기 로직은 UI 코드(`MainWindow.xaml.cs`)와 분리한 별도 클래스로 관리한다.
- "폴더 목록"(임시 스캔 결과)과 "관리 리스트"(영속 데이터)는 서로 다른 데이터 소스이므로 혼동하지 않도록 변수/컬렉션 이름을 명확히 구분한다.
- tags는 항상 문자열 배열(태그 목록)로 취급하며, 단일 문자열(콤마 구분 등)로 저장하지 않는다.
- 헤더 클릭(정렬)과 헤더 우클릭(표시할 컬럼 선택)은 서로 다른 이벤트이므로 UI/이벤트 핸들러를 명확히 분리한다. 파일명/태그 검색 필터는 헤더가 아니라 리스트 위 상시 UI에서 처리한다 ([관리 리스트](#2-관리-리스트-사용자가-지속적으로-관리하는-목록) 참고).
- 썸네일은 `ThumbnailHelper.CreateThumbnail`을 통해 320x240으로 리사이즈한 뒤 동영상 파일과 같은 폴더에 `{파일명}.thumbnail.jpg`로 저장하고, 그 경로만 `ThumbnailPath`로 JSON에 저장한다 — [썸네일 관리](#썸네일-관리) 참고. (예전에는 원본을 복사하지 않는 정책이었으나 리사이즈+같은 폴더 저장 방식으로 대체됨.)
- 배우 썸네일은 동영상 썸네일과 별도 로직(`ThumbnailHelper.CreateActorThumbnail`)을 쓴다 — 동영상 파일 옆이 아니라 `%LOCALAPPDATA%\VideoVault\actresses\{배우명}.jpg`에 100x100으로, 원본 보관 없이 저장한다 — [배우 마스터 목록 관리](#배우-마스터-목록-관리-actormanagerwindow) 참고.
- 헤더 우클릭(컬럼 선택)/태그 필터 팝업은 WPF `ContextMenu`가 아니라 `Popup`으로 구현한다 (텍스트 입력/체크박스 목록 등 인터랙티브 컨트롤을 담기에 `ContextMenu`보다 다루기 쉬움).
- 리스트 보기의 썸네일/크기/재생횟수/태그/배우/메모 컬럼은 XAML에 고정으로 선언하지 않고, `MainWindow.xaml.cs`의 `BuildManagedColumns()`에서 `GridViewColumn` 객체로 만들어 필드에 보관한 뒤 필요한 것만 `GridView.Columns`에 추가/제거한다 (헤더 우클릭 컬럼 선택 기능을 위함). 파일명 컬럼만 항상 표시되는 고정 컬럼으로 XAML에 남겨둔다. 태그/크기/재생횟수/썸네일 컬럼의 `CellTemplate`은 각각 `Window.Resources`의 `TagsCellTemplate`/`SizeCellTemplate`/`PlayCountCellTemplate`/`HasThumbnailCellTemplate`을 코드에서 참조해서 재사용한다.
- 더블클릭 재생 범위는 리스트 보기/아이콘 보기가 서로 다르다 (**구현 완료**). 아이콘 보기는 파일명 텍스트에만 `MouseLeftButtonDown` + `e.ClickCount == 2` 검사로 구현한다(`FileNameCell_MouseLeftButtonDown`). 리스트 보기는 행 전체에서 인식되도록 `ListViewItem`의 `ItemContainerStyle`에 `EventSetter Event="MouseDoubleClick"`(`ManagedListRow_MouseDoubleClick`)을 건다 — 이 경우 파일명 셀에는 더 이상 별도 클릭 핸들러를 걸지 않는다(중복 재생/재생횟수 이중 증가 방지).
- F1(속성)/F2(이름변경)/Del(삭제/제거)/Ctrl+S(저장) 단축키는 `MainWindow`의 `PreviewKeyDown`에서 전역으로 처리하고, 기존 `PropertiesManaged_Click`/`RenameManaged_Click`/`RemoveFromManagedList_Click`/`SaveNow_Click` 핸들러를 그대로 호출한다 (새 로직을 만들지 않음 — 버튼/메뉴/우클릭 메뉴/단축키가 모두 같은 핸들러를 공유). Del만 예외적으로 `e.OriginalSource is not TextBox` 가드가 있다 (텍스트 상자에서 글자를 지울 때 항목이 삭제되지 않도록).
- 컬럼 표시 상태는 `ApplyVisibleColumns(keys)`(GridView에 반영 + 컬럼 선택 팝업 체크박스 동기화)와 `GetVisibleColumnKeys()`(현재 GridView 상태를 키 목록으로 추출, 저장용) 한 쌍으로 관리한다. 컬럼을 하나 더 추가할 때는 두 메서드와 `ColumnToggle_Click`의 `switch`, 컬럼 선택 팝업의 체크박스를 함께 갱신해야 한다. 파일명보다 앞에 표시해야 하는 컬럼(현재는 썸네일 컬럼)은 `SetColumnVisible(gridView, column, visible, insertAtStart: true)`처럼 `insertAtStart` 인자를 `true`로 넘겨 `GridView.Columns.Insert(0, column)`으로 맨 앞에 삽입한다. 나머지 컬럼은 `insertAtStart` 생략(기본값 `false`, `Columns.Add`).
- 아이콘 보기에서 썸네일 지정은 각 아이템 카드의 썸네일 이미지 영역(정사각형 미리보기 부분) 클릭으로 트리거된다. 파일명/재생횟수 텍스트 영역은 클릭 대상에서 제외된다.
- 리스트 보기에서 태그 칩 영역 클릭으로 속성 창을 여는 것도 같은 패턴이다 — 헤더의 좌클릭(정렬)/우클릭(필터)과는 별개로, 행(item)의 태그 셀 클릭은 항상 `PropertiesWindow`를 연다.
- 상단 `Menu`의 각 `MenuItem`은 새 로직을 만들지 않고 기존 버튼과 동일한 이벤트 핸들러를 재사용한다. 새 기능을 추가할 때도 버튼과 메뉴 항목이 같은 핸들러를 공유하도록 한다 (기능이 두 곳에서 따로 구현되어 어긋나는 것을 방지).
- 관리 리스트 행의 우클릭 팝업 메뉴(`ContextMenu`)는 `Window.Resources`에 `x:Key="ManagedItemContextMenu"`로 한 번만 선언하고, 리스트 보기(`ListView`)/아이콘 보기(`ListBox`) 양쪽의 `ItemContainerStyle`에서 `StaticResource`로 공유한다 (`ItemContainerStyle`의 `Setter.Value`에 `ContextMenu`를 인라인으로 직접 작성하면 XAML 컴파일러가 내부 `MenuItem`의 `Click` 핸들러를 엉뚱한 상위 요소에 연결하는 컴파일 오류가 발생했음 — 반드시 리소스로 분리해서 참조할 것).
- 실제 파일 이름 변경(`RenameHelper`)은 관리 리스트 항목의 표시용 `FileName` 문자열만 바꾸는 게 아니라 디스크의 실제 파일을 `File.Move`로 rename한다. 따라서 대상 경로 존재 여부 확인, 실패 시 사용자 알림이 필수다.
- 관리 리스트 JSON의 기본 저장 경로(`%LOCALAPPDATA%\VideoVault\library.json`)는 하드코딩된 문자열 대신 상수/설정 값으로 관리한다. 하드코딩 문자열은 (도입 시) `Constants.cs`로 분리하며, 저장 경로를 설정값으로 관리해두면 향후 macOS/Linux 등 다른 플랫폼으로 확장할 때도 `AppPaths`만 교체하면 된다.
- 태그 마스터 목록(`tags.json`)과 관리 리스트(`library.json`)는 별도 파일/별도 Repository(`TagRepository`, `ManagedListRepository`)로 분리 관리한다.
- 태그 마스터 목록에서 태그 이름 변경/삭제 시 관리 리스트의 `Tags` 필드와 항상 동기화되도록 한다 (마스터 목록에 없는 태그가 관리 리스트에 남아있지 않도록 함).
- 자동 저장은 항상 기본 저장 위치(`library.json`)를 대상으로 한다. "열기"로 다른 JSON 파일을 불러오면 그 내용이 메모리상의 관리 리스트를 대체하고 이후 변경 시 기본 위치에 자동 저장되지만, 열었던 파일 자체가 자동 저장 대상으로 바뀌지는 않는다. "다른 이름으로 저장"은 기본 저장 위치와 무관한 1회성 내보내기다.
- **CLAUDE.md를 수정하기 전에는 항상 수정 전 내용을 `history` 폴더에 `CLAUDE_yy_mm_dd_#.md`(예: `history/CLAUDE_26_07_31_1.md`, 같은 날 여러 번 백업 시 `#`을 1부터 증가)로 백업한 뒤 수정한다** (2026-08-11부터 — 예전에는 프로젝트 루트에 직접 백업했으나 파일이 너무 많이 쌓여 `history` 폴더로 옮겼다).
- **기능을 추가/변경/수정할 때는 그 작업이 끝나는 시점에 CLAUDE.md도 함께 업데이트한다.** (2026-08-02 명시적으로 규칙화 — 여러 기능을 구현하고도 CLAUDE.md 업데이트를 미룬 적이 있어, 이후에는 코드 변경과 문서 업데이트를 같은 작업 단위로 취급한다.) 새 파일을 추가했으면 "현재 구현된 파일" 목록에, 동작이 바뀌었으면 관련 기능 절에 반영하고, 필요하면 데이터 모델/컨벤션 절도 함께 갱신한다.
- 비동기 메서드는 이름에 `Async` 접미사를 붙인다 (예: `LoadVideoFilesAsync`).
- Nullable reference type을 활성화한다 (`<Nullable>enable</Nullable>`, 이미 csproj에 설정되어 있음).
- 파일 경로 조합은 문자열 연결 대신 `Path.Combine`을 사용한다.
- 하드코딩 문자열(경로, 기본 파일명 등)은 (규모가 커지면) `Constants.cs` 클래스로 분리한다.
- 다국어 등 공용 문자열이 늘어나면 `Resources.resx`로 옮길 수 있도록 문자열을 코드 곳곳에 흩뿌리지 않는다.
- **JSON으로 직렬화되는 모델(`ManagedVideoItem`, `AppSettings` 등)의 속성에 `private set`(또는 setter 없음)을 쓸 때는 반드시 `[JsonInclude]`(`System.Text.Json.Serialization`)를 붙인다.** System.Text.Json은 기본적으로 public setter가 없는 속성을 역직렬화 시 조용히 건너뛴다 — 예외가 발생하지 않아 눈치채기 어렵다. 실제로 `ManagedVideoItem.Tags`가 이 문제로, 저장(직렬화)은 정상 동작하면서(getter만 있으면 됨) 다음 실행 시 불러오기(역직렬화)에서만 태그가 항상 빈 목록이 되는 버그가 있었다(2026-07-31 수정, `[JsonInclude]` 추가). 캡슐화가 필요 없는 필드는 애초에 public setter를 쓰는 것도 방법이다.
- **로컬 파일 경로에서 이미지를 표시할 때는 항상 `ImageLoadHelper.Load`(코드) 또는 `ThumbnailPathConverter`(XAML `{Binding}`)를 거친다. `new BitmapImage(new Uri(path))`나 XAML의 암시적 문자열→`ImageSource` 변환을 캐시 옵션 없이 직접 쓰지 않는다.** 둘 다 파일을 지연 로딩(사실상 계속 열어둠)해서, 그 이미지가 화면에 표시되어 있는 동안 같은 경로에 새 파일을 덮어쓰려 하면 "다른 프로세스가 파일을 사용 중" 오류가 난다 — 실제로 이미 썸네일이 있는 항목에 새 썸네일을 지정하면 이 오류로 실패하는 버그가 있었다(2026-08-01 수정, [썸네일 파일 잠금 문제](#썸네일-파일-잠금-문제-이미-표시-중인-썸네일-덮어쓰기) 참고). `ImageLoadHelper.Load`는 `BitmapCacheOption.OnLoad` + `Freeze()`로 로드 즉시 파일 핸들을 놓고, `BitmapCreateOptions.IgnoreImageCache`로 같은 경로를 다시 읽을 때 WPF의 내부 이미지 캐시에서 예전 픽셀 데이터가 재사용되는 것도 막는다(같은 경로에 덮어쓴 배우 썸네일이 갱신되지 않고 예전 이미지로 계속 보이던 버그, 2026-08-01 수정).

## 로그

디버깅을 위한 로그 정책 (아직 미구현):

- 기록 대상: 프로그램 시작/종료, JSON 저장, JSON 로드, 재생, 태그 변경, 예외 발생
- 저장 위치: `%LOCALAPPDATA%\VideoVault\Logs\` (다른 데이터 파일과 동일한 로컬 데이터 폴더 하위)

## 테스트

향후 강화할 테스트 범위:

- 단위 테스트: JSON 직렬화/역직렬화(`ManagedListRepository`, `TagRepository`), 태그 이름 변경·삭제 시 관리 리스트 동기화 로직, 재생횟수 증가 로직
- UI 자동화 테스트: 폴더 열기 → 관리 리스트 추가 → 정렬/필터 → 저장까지의 골든 패스
