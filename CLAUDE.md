# VideoVault

C# WPF (.NET 8) 데스크톱 애플리케이션. 동영상 파일을 관리하는 GUI 프로그램이다.

## 프로젝트 목적

- 특정 폴더(하위 폴더 포함)를 열어 동영상 파일 목록을 스캔한다 (**폴더 목록**).
- 사용자가 원하는 파일을 별도의 **관리 리스트**에 추가하여 지속적으로 관리한다 (JSON 파일로 저장).
- 관리 리스트에서 파일명, 크기, 재생횟수, tags(태그 형태의 사용자 정의 속성)를 확인하고 정렬·필터링할 수 있다.
- 태그는 별도의 **태그 마스터 목록**(JSON 파일)으로 관리되며, 사용자는 이 마스터 목록에서 태그를 추가/수정/삭제할 수 있다. 관리 리스트 항목에 태그를 붙일 때는 자유 입력이 아니라 마스터 목록에서 선택하는 방식이다.
- 관리 리스트는 리스트 보기 / 아이콘(썸네일) 보기 두 가지 방식으로 볼 수 있으며, 각 항목에 사용자가 직접 이미지 파일을 썸네일로 지정할 수 있다.
- 배우 마스터 목록은 배우가 출연한 작품(품번) 목록인 **Credits**를 함께 관리하며, 관리 리스트의 파일-배우 태깅과 서로 자동으로 동기화된다.

## 기술 스택

- .NET 8 SDK (WPF, `net8.0-windows`)
- XAML + code-behind, MVVM으로 점진 전환 중 (자세한 방향은 [아키텍처](#아키텍처) 참고)

## 빌드 / 실행

2026-08-22부터 프로젝트 코드가 `src/VideoVault/` 밑으로 이동했다(아래 [프로젝트 구조](#프로젝트-구조) 참고). 저장소 루트의 `video-vault.sln`이 그 프로젝트 하나만 가리키므로, `dotnet build`는 루트에서 인자 없이 그대로 실행하면 된다(같은 폴더에 `.sln`이 하나만 있으면 `dotnet` CLI가 자동으로 찾음). `dotnet run`은 `.sln`으로는 동작하지 않아 프로젝트 경로를 직접 지정해야 한다.

```
dotnet build
dotnet run --project src/VideoVault
```

## 코드 서명 (로컬 개발용)

이 PC는 Windows **Smart App Control(SAC)**이 평가 모드로 켜져 있어서, 서명되지 않은 새 빌드 실행 파일을 "신뢰할 수 없음"으로 판단해 실행을 차단한다(코드 무결성 로그에 "Enterprise signing level requirements" 오류로 나타남 — Defender 위협 탐지 로그는 비어 있어 악성코드 탐지가 아니라 순수 서명/평판 정책임을 확인함). 이를 우회하기 위해 **자체 서명(self-signed) 인증서로 빌드 결과물에 자동 서명**하도록 구성했다 — **구현 완료**.

- **인증서**: `CN=VideoVault Dev Signing` (CurrentUser\My 저장소, 5년 유효). 신뢰를 위해 같은 인증서(공개키)를 `CurrentUser\Root`와 `CurrentUser\TrustedPublisher`에도 등록해뒀다. 개인키는 이 PC의 사용자 계정에만 존재하며 저장소/파일로 커밋하지 않는다.
- **자동 서명**: `src/VideoVault/VideoVault.csproj`에 `AfterTargets="Build"` 타겟이 있어, 빌드가 끝날 때마다 같은 폴더의 `Sign-Build.ps1`이 `$(TargetDir)`의 `.exe`와 `.dll`을 모두 서명한다. 인증서를 찾을 수 없는 다른 PC에서는(예: 다른 개발자의 PC) 오류 없이 조용히 서명을 건너뛴다.
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

exe 아이콘(탐색기/작업 표시줄)과 `MainWindow` 타이틀바 아이콘 모두 `AppIcon.ico`(2026-08-02 추가, **구현 완료**, 2026-08-22 `src/VideoVault/Assets/`로 위치 이동, 2026-08-22 아이콘 원본 자체를 교체)를 사용한다.

- **원본 (2026-08-22 교체)**: 저장소 루트의 `video-vault.png`(512x512, 알파 채널 포함 flat-style 미디어 플레이어 아이콘)를 소스로 쓴다. `System.Drawing`으로 16/32/48/256px 각 크기로 고품질 리샘플링(`InterpolationMode.HighQualityBicubic`, 알파 유지)한 뒤 PNG로 인코딩하고, 이 PNG들을 담은 `.ico`(PNG-압축 아이콘 항목, Vista 이상에서 지원)를 직접 조립하는 1회성 PowerShell 스크립트로 변환했다(프로젝트에는 스크립트 자체를 포함하지 않음). **이전(2026-08-02~2026-08-22)에는** 저장소 루트의 `media-player-interface-symbol-svgrepo-com.svg`(재생 버튼이 있는 미디어 플레이어 창 모양 아이콘)를 WPF `Geometry.Parse` + `DrawingVisual`/`RenderTargetBitmap`로 래스터화하는 방식을 썼으나, `video-vault.png`로 교체되며 이 방식은 더 이상 쓰이지 않는다(SVG 파일 자체는 참고용으로 루트에 남겨둠).
- **적용 위치**:
  - `src/VideoVault/VideoVault.csproj`: `<ApplicationIcon>Assets\AppIcon.ico</ApplicationIcon>`로 exe 자체의 아이콘(탐색기/작업 표시줄, 앱을 실행하지 않은 상태에서도 보임)을 지정. `<Resource Include="Assets\AppIcon.ico" />`로 함께 포함해 런타임에도 참조 가능하게 함.
  - `MainWindow.xaml`: `Icon="Assets/AppIcon.ico"`로 타이틀바/Alt+Tab 아이콘을 지정. 서브 창(`FolderListWindow` 등)은 별도로 지정하지 않았다(자식 창은 보통 별도 작업 표시줄 항목을 갖지 않으므로).
- **검증**: 빌드된 exe에서 `System.Drawing.Icon.ExtractAssociatedIcon`으로 아이콘을 실제로 추출해 기본 아이콘이 아닌 지정한 미디어 플레이어 아이콘이 임베드되었음을 확인했다.

## 프로젝트 구조

### 폴더 구조 (요약, 2026-08-22 `src/` 레이아웃으로 재구성)

다른 프로젝트(`text-readers` 등)와 형태를 맞추기 위해 모든 소스 코드를 `src/VideoVault/` 밑으로 옮겼다. `CLAUDE.md`/`doc/`처럼 코드가 아닌 것은 저장소 루트에 그대로 둔다. 아래 "현재 구현된 파일" 목록의 파일명은 **모두 `src/VideoVault/` 기준 상대경로**다(명시적으로 다른 경로를 적은 항목 제외).

```
video-vault/
├── CLAUDE.md
├── video-vault.sln
├── .gitignore
├── video-vault.png                                 # 앱 아이콘 원본 PNG(현재 사용, 코드 아님, 루트 유지) — [앱 아이콘](#앱-아이콘) 참고
├── media-player-interface-symbol-svgrepo-com.svg   # 앱 아이콘 구 원본 SVG(2026-08-22부터 미사용, 참고용으로만 유지)
├── doc/                       # 기능별 상세 문서 → 아래 "문서 구성" 참고
└── src/
    └── VideoVault/            # WPF 프로젝트 본체
        ├── VideoVault.csproj
        ├── Sign-Build.ps1
        ├── Assets/
        │   └── AppIcon.ico
        ├── Models/            # 현재 비어있음 — MVVM 전환 시 데이터 모델 이동 예정, .gitkeep만 존재
        ├── Services/          # 현재 비어있음 — 계획된 서비스 계층, .gitkeep만 존재 → 아래 "아키텍처" 참고
        ├── ViewModels/        # 현재 비어있음 — 계획된 ViewModel 계층, .gitkeep만 존재 → 아래 "아키텍처" 참고
        ├── Views/             # 현재 비어있음 — 향후 XAML 재배치용, .gitkeep만 존재
        └── (그 외 모든 .cs/.xaml 파일 — 아래 목록 참고, 아직 Models/Services/ViewModels/Views로 분류하지 않고 평평하게 둠)
```

### 현재 구현된 파일

- `VideoVault.csproj` — 프로젝트 파일 (빌드 후 자동 서명 타겟 포함 — [코드 서명](#코드-서명-로컬-개발용) 참고, 앱 아이콘(`ApplicationIcon`) 지정 포함 — [앱 아이콘](#앱-아이콘) 참고)
- `Sign-Build.ps1` — 빌드 결과물(exe/dll)에 로컬 개발용 인증서로 서명하는 스크립트
- `Assets/AppIcon.ico` — 앱 아이콘 (16/32/48/256px PNG-압축 아이콘을 담은 `.ico`) — [앱 아이콘](#앱-아이콘) 참고
- `App.xaml` / `App.xaml.cs` — 애플리케이션 진입점. 앱 단일 인스턴스(작업표시줄 재실행 시 기존 창 활성화) 처리도 여기 있다 → [공통 관리](doc/common-management.md)
- `MainWindow.xaml` / `MainWindow.xaml.cs` — 메인 윈도우 (관리 리스트 UI, 정렬/필터/썸네일 로직). 폴더 목록은 `FolderListWindow` 서브 창으로 분리되어 있다 → 상세: [동영상 파일 관리](doc/video-file-management.md)
- `FolderListWindow.xaml` / `FolderListWindow.xaml.cs` — 폴더를 열어 동영상 파일을 스캔하고 관리 리스트에 추가하는 서브 창 → [동영상 파일 관리](doc/video-file-management.md)
- `VideoFileItem.cs` — 폴더 목록(임시 스캔 결과) 항목 모델 → [동영상 파일 관리](doc/video-file-management.md)
- `ManagedListImporter.cs` — 동영상 파일들을 관리 리스트에 추가하는 공용 로직 → [동영상 파일 관리](doc/video-file-management.md)
- `ManagedVideoItem.cs` — 관리 리스트 항목 모델 (JSON 직렬화 대상) → [동영상 파일 관리](doc/video-file-management.md)
- `ManagedListRepository.cs` — 관리 리스트 JSON 파일 읽기/쓰기 로직 → [동영상 파일 관리](doc/video-file-management.md)
- `TagItem.cs` — 태그 마스터 목록 항목 모델 (`Name` + `Credits`, `ActorItem`/`SeriesItem`과 동일한 모양, 2026-08-27 추가 — 이전에는 단순 문자열이었음) → [태그 관리](doc/tag-management.md)
- `TagRepository.cs` — 태그 마스터 목록(`tags.json`) 읽기/쓰기 로직 (구버전 문자열 배열 자동 마이그레이션 포함) → [태그 관리](doc/tag-management.md)
- `TagManagerWindow.xaml` / `TagManagerWindow.xaml.cs` — 태그 마스터 목록 관리 화면 → [태그 관리](doc/tag-management.md)
- `ActorItem.cs` — 배우 마스터 목록 항목 모델 → [배우 관리](doc/actor-management.md)
- `ActorRepository.cs` — 배우 마스터 목록(`actors.json`) 읽기/쓰기 로직 → [배우 관리](doc/actor-management.md)
- `ActorManagerWindow.xaml` / `ActorManagerWindow.xaml.cs` — 배우 마스터 목록 관리 화면 → [배우 관리](doc/actor-management.md)
- `SeriesItem.cs` — 시리즈 마스터 목록 항목 모델 → [시리즈 관리](doc/series-management.md)
- `SeriesRepository.cs` — 시리즈 마스터 목록(`series.json`) 읽기/쓰기 로직 → [시리즈 관리](doc/series-management.md)
- `SeriesManagerWindow.xaml` / `SeriesManagerWindow.xaml.cs` — 시리즈 마스터 목록 관리 화면 → [시리즈 관리](doc/series-management.md)
- `AddCreditWindow.xaml` / `AddCreditWindow.xaml.cs` — 배우/시리즈의 Credits에 새 품번을 추가하는 대화상자(배우/시리즈 관리 창이 공유) → [배우 관리](doc/actor-management.md)
- `ActorCreditSync.cs` — 관리 리스트 항목의 `Actors`와 배우 마스터 목록의 `Credits`를 상호 동기화 → [배우 관리](doc/actor-management.md)
- `SeriesCreditSync.cs` — 관리 리스트 항목의 `Series`와 시리즈 마스터 목록의 `Credits`를 상호 동기화 → [시리즈 관리](doc/series-management.md)
- `ActorInfoWindow.xaml` / `ActorInfoWindow.xaml.cs` — 배우의 이름/출생년도/키/신체정보 편집 대화상자 → [배우 관리](doc/actor-management.md)
- `ImageLoadHelper.cs` — 로컬 파일 경로에서 `BitmapImage`를 즉시 전부 읽어들이는 공용 로직(파일 잠금 회피) → [공통 관리](doc/common-management.md)
- `WindowsIconHelper.cs` — Windows 셸 아이콘을 가져오는 헬퍼 → [공통 관리](doc/common-management.md)
- `ThumbnailPathConverter.cs` — 썸네일 경로 문자열을 `ImageLoadHelper.Load`로 변환하는 `IValueConverter` → [공통 관리](doc/common-management.md)
- `PropertiesWindow.xaml` / `PropertiesWindow.xaml.cs` — 관리 리스트 항목의 속성 대화상자 → [속성 관리](doc/properties-management.md)
- `RenameWindow.xaml` / `RenameWindow.xaml.cs` — 새 파일명을 입력받는 대화상자 → [동영상 파일 관리](doc/video-file-management.md)
- `RenameHelper.cs` — 관리 리스트 항목의 실제 파일 rename/이동 로직 → [동영상 파일 관리](doc/video-file-management.md)
- `FormatUtil.cs` — 파일 크기 표시 등 공용 포맷 유틸리티 → [공통 관리](doc/common-management.md)
- `ThumbnailHelper.cs` — 원본/리사이즈 썸네일 저장 공용 로직(동영상/배우/플레이스홀더 공용) → [공통 관리](doc/common-management.md)
- `OriginalImageWindow.xaml` / `OriginalImageWindow.xaml.cs` — 원본(리사이즈 전) 썸네일 이미지를 크게 보여주는 창 → [공통 관리](doc/common-management.md)
- `DragDropImageHelper.cs` — 드래그 앤 드롭된 데이터에서 이미지를 꺼내 임시 파일로 저장하는 공용 로직 → [공통 관리](doc/common-management.md)
- `AppPaths.cs` — 각종 JSON 파일/폴더의 기본 저장 경로 상수 → [공통 관리](doc/common-management.md)
- `AppSettings.cs` — 마지막 보기 모드/정렬/필터/폴더 상태 모델 → [공통 관리](doc/common-management.md)
- `SettingsRepository.cs` — 설정 JSON 파일 읽기/쓰기 로직 → [공통 관리](doc/common-management.md)
- `SingleInstanceWindow.cs` — 창 종류별 단일 인스턴스 보장 헬퍼 → [공통 관리](doc/common-management.md)
- `WindowPositionMemory.cs` — 주요 창의 마지막 화면 위치를 기억하는 저장소 → [공통 관리](doc/common-management.md)
- `WindowSnapHelper.cs` — 창 스냅(자석 붙기) 기능 → [공통 관리](doc/common-management.md)
- `IconSizeSettings.cs` — 관리 리스트 아이콘 보기의 카드/썸네일/폰트 크기 프리셋 관리 → [동영상 파일 관리](doc/video-file-management.md)
- `IconCardFieldsSettings.cs` — 아이콘 보기 카드에 표시할 정보 선택 상태 관리 → [동영상 파일 관리](doc/video-file-management.md)
- `FileNameNaturalComparer.cs` — 파일명 자연 정렬 비교자 → [동영상 파일 관리](doc/video-file-management.md)
- `VirtualizingWrapPanel.cs` — 아이콘 보기용 커스텀 UI 가상화 패널 (`VirtualizingPanel` + `IScrollInfo`) → [공통 관리](doc/common-management.md)

### 계획된 추가 파일 (아직 미구현)

아래는 [아키텍처](#아키텍처)와 [공통 관리](doc/common-management.md)의 로그 절에서 요구하는 기능을 구현할 때 추가될 예정인 파일이다. `Services/`/`ViewModels/` **폴더 자체는 2026-08-22 `src/` 재구성 때 미리 만들어뒀지만**(`.gitkeep`만 있는 빈 폴더), 아래 파일들은 아직 어느 것도 작성되지 않았다.

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

## 문서 구성

기능 상세 스펙은 아래 6개 문서로 나뉘어 있다(모두 `doc/` 폴더). **작업하려는 기능에 해당하는 문서를 먼저 읽고 시작한다** — 이 메인 문서는 프로젝트 개요와 각 문서로 가는 안내 역할만 한다.

| 문서 | 다루는 범위 |
|---|---|
| [video-file-management.md](doc/video-file-management.md) — 동영상 파일 관리 | 폴더 스캔, 관리 리스트에 파일 추가/제거/복구/완전삭제, 재생, 이름변경/이동, 정렬·필터·컬럼 선택, 리스트/아이콘 보기, `library.json` 저장, `ManagedVideoItem` 데이터 모델 |
| [properties-management.md](doc/properties-management.md) — 속성 관리 | `PropertiesWindow`(파일 정보, 코드/출시일, 메모, 재생횟수, 완전 삭제, "파일 없이 추가" 새 항목 모드) |
| [tag-management.md](doc/tag-management.md) — 태그 관리 | `TagManagerWindow`, `tags.json`, 태그 마스터 목록 동기화 규칙 |
| [actor-management.md](doc/actor-management.md) — 배우 관리 | `ActorManagerWindow`, `actors.json`, Credits, `ActorCreditSync`, 배우 썸네일, `AddCreditWindow` |
| [series-management.md](doc/series-management.md) — 시리즈 관리 | `SeriesManagerWindow`, `series.json`, Credits, `SeriesCreditSync` |
| [common-management.md](doc/common-management.md) — 공통 관리 | 설정 저장(`settings.json`), 오류 처리, 성능, 확장성, 창 관리 정책·창 스냅, 썸네일 공통 인프라(파일 잠금 버그, 인터넷 이미지 드래그 앤 드롭), 전역 컨벤션, 로그, 테스트 |

각 문서는 서로의 기능을 참조할 때 "→ [OO 관리](파일명.md) 참고" 형식으로 링크한다. 한 기능이 여러 문서에 걸쳐 있을 때(예: 배우 태깅은 [속성 관리](doc/properties-management.md)의 UI + [배우 관리](doc/actor-management.md)의 동기화 로직) 중복 서술 대신 상호 참조를 우선한다.

## 아키텍처

- `MainWindow.xaml.cs`에 모든 로직이 집중되지 않도록, MVVM을 초기 단계부터 도입하는 방향으로 전환한다.
  - **현재 상태**: 아직 code-behind 중심(`MainWindow.xaml.cs`)으로 구현되어 있으며 MVVM 전환은 완료되지 않았다. 새로 추가하는 기능부터 아래 계층 구조를 따르고, 기존 code-behind 로직은 점진적으로 옮긴다.
- 계층 구조:
  - **Repository** (`ManagedListRepository`, `TagRepository`, 추가될 `SettingsRepository`): 파일 읽기/쓰기만 담당, 비즈니스 로직 없음
  - **Service** (계획: `VideoLibraryService`, `TagService`, `ThumbnailService`): 정렬/필터/태그 동기화 등 비즈니스 로직
  - **ViewModel** (계획: `MainWindowViewModel`, `TagManagerViewModel`): View에 바인딩할 상태 및 커맨드 노출
  - **View** (`MainWindow.xaml` 등): UI만 담당, ViewModel에 바인딩

## 문서 관리 규칙

- **문서 수정 전 `history` 폴더 백업 관행은 2026-08-16부터 중단한다.** 지시서가 여러 파일로 나뉘면서 매번 전체를 복사해두는 방식의 효용이 떨어졌고, 이제 git 커밋 이력이 변경 기록 역할을 대신한다 — 문서를 고칠 때는 별도 백업 없이 바로 수정하고, 의미 있는 단위로 git commit을 남긴다.
- **기능을 추가/변경/수정할 때는 그 작업이 끝나는 시점에 해당 문서도 함께 업데이트한다.** 새 파일을 추가했으면 이 문서의 "현재 구현된 파일" 목록에, 동작이 바뀌었으면 관련 기능 문서(위 [문서 구성](#문서-구성) 표 참고)에 반영하고, 필요하면 데이터 모델/컨벤션 절도 함께 갱신한다. 여러 문서에 걸친 변경이면 관련된 모든 문서를 같은 작업 단위로 갱신한다.
