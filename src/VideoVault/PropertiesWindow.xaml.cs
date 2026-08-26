using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace VideoVault;

/// <summary>
/// 관리 리스트 항목의 속성(파일 정보, 이름 변경, 재생횟수, 태그, 배우)을 보고 편집하는 대화상자.
/// </summary>
public partial class PropertiesWindow : Window
{
    private class TagCheckItem
    {
        public string Tag { get; set; } = string.Empty;
        public bool IsSelected { get; set; }
    }

    /// <summary>시리즈 콤보박스에서 "선택된 시리즈 없음"을 나타내는 항목. 실제 시리즈 이름과 겹치지 않도록 목록 맨 앞에 삽입한다.</summary>
    private const string NoSeriesLabel = "(없음)";

    private ManagedVideoItem _item;
    private readonly IEnumerable<string> _masterTags;
    private readonly ObservableCollection<ActorItem> _masterActors;
    private readonly ObservableCollection<SeriesItem> _masterSeries;
    private readonly ObservableCollection<ManagedVideoItem> _managedItems;
    private readonly ObservableCollection<string> _selectedActors;
    private List<TagCheckItem> _tagItems = new();

    /// <summary>"완전 삭제" 버튼으로 닫힌 경우 true. 호출자(`MainWindow`)가 이 값을 보고 항목을 관리 리스트에서 완전히 제거한다.</summary>
    public bool PermanentlyDeleted { get; private set; }

    /// <summary>
    /// 이 창이 지금 보여주고 있는 항목. <see cref="SwitchToItem"/>으로 다른 항목으로 바뀔 수 있으므로,
    /// 호출자는 창을 처음 열 때 넘긴 항목이 아니라 이 프로퍼티로 "지금" 보여주는 항목을 확인해야 한다
    /// (예: 완전 삭제 시 관리 리스트에서 지워야 할 대상).
    /// </summary>
    public ManagedVideoItem CurrentItem => _item;

    /// <summary>
    /// "확인"/"취소"/"완전 삭제" 버튼이 이미 처리(커밋 또는 폐기)를 마치고 닫는 중이면 true.
    /// 이 창은 모덜리스(<see cref="Window.Show"/>)로 열리므로 더 이상 `DialogResult`로 "버튼을 거쳐 닫혔는지"를
    /// 구분할 수 없어(모달 전용 API), 이 플래그로 <see cref="Window_Closing"/>이 같은 커밋을 중복 실행하지
    /// 않도록 막는다 — 버튼을 거치지 않고 닫힌 경우(제목 표시줄의 X, Alt+F4 등)에만 `Window_Closing`이 직접 커밋한다.
    /// </summary>
    private bool _explicitCloseHandled;

    /// <summary>
    /// "파일 없이 추가" 버튼으로 만든, 아직 `_managedItems`에 들어있지 않은 새 항목을 보여주는 중이면 true
    /// (2026-08-06 추가). 이 상태에서 파일명을 입력한 채 커밋(확인/닫기)되면 그 시점에 `_managedItems`에
    /// 추가되고 이 플래그는 false로 바뀐다 — 파일명 없이 커밋되면(입력이 없으면) 조용히 추가를 취소한다.
    /// </summary>
    private bool _isNewItem;

    public PropertiesWindow(ManagedVideoItem item, IEnumerable<string> masterTags, ObservableCollection<ActorItem> masterActors, ObservableCollection<SeriesItem> masterSeries, ObservableCollection<ManagedVideoItem> managedItems, bool isNewItem = false)
    {
        InitializeComponent();
        WindowSnapHelper.Attach(this);
        _item = item;
        _masterTags = masterTags;
        _masterActors = masterActors;
        _masterSeries = masterSeries;
        _managedItems = managedItems;
        _isNewItem = isNewItem;

        RefreshActorComboBox();
        _selectedActors = new ObservableCollection<string>(item.Actors);
        SelectedActorsList.ItemsSource = _selectedActors;

        RefreshFileInfo();
        RefreshSeriesComboBox();
        BuildTagList();
        RefreshThumbnailPreview();
        RefreshSelectedActorsThumbnails();
        RefreshFileOperationAvailability();

        _item.PropertyChanged += ItemPropertyChanged;
        Closed += (_, _) => _item.PropertyChanged -= ItemPropertyChanged;
    }

    /// <summary>
    /// 실제 파일이 없는 항목(<see cref="ManagedVideoItem.IsPlaceholder"/>)은 파일이 있어야 동작하는
    /// 기능(재생/파일 변경·이동/경로 수정/썸네일)을 비활성화한다. "완전 삭제"는 아직 관리 리스트에
    /// 추가되지 않은 새 항목(<see cref="_isNewItem"/>)일 때만 비활성화한다 — 지울 대상 자체가 없으므로.
    /// </summary>
    private void RefreshFileOperationAvailability()
    {
        var hasRealFile = !_item.IsPlaceholder;
        var tooltip = hasRealFile ? null : "실제 파일이 없는 항목입니다.";

        PlayButton.IsEnabled = hasRealFile;
        PlayButton.ToolTip = tooltip;
        ChangeFileButton.IsEnabled = hasRealFile;
        ChangeFileButton.ToolTip = tooltip;
        MoveToFolderButton.IsEnabled = hasRealFile;
        MoveToFolderButton.ToolTip = tooltip;
        MoveByCodeButton.IsEnabled = hasRealFile;
        MoveByCodeButton.ToolTip = tooltip;

        // 썸네일은 실제 파일이 없어도 지정할 수 있다 — ApplyThumbnail이 IsPlaceholder일 때
        // ThumbnailHelper.CreatePlaceholderThumbnail(고정 폴더)로 자동 분기하므로 재생/파일 이동류와 달리
        // 계속 활성 상태로 둔다.
        DeleteButton.IsEnabled = !_isNewItem;
    }

    /// <summary>
    /// 이 창이 열려 있는 동안 다른 경로(관리 리스트에서 더블클릭 재생 등)로 이 항목의 값이 바뀌면 화면도
    /// 즉시 따라가도록 한다. 이게 없으면, 그 사이 이 창에서 "확인"을 누르거나 다른 항목으로 전환될 때
    /// (<see cref="SwitchToItem"/>) `TryCommitFormFields`가 화면에 남아있던 예전 값을 그대로 다시 써버려서
    /// 외부에서 바뀐 값이 도로 되돌아가는 버그가 있었다 — 재생횟수에서 처음 발견됐지만(방금 늘어난 재생횟수가
    /// 줄어듦), **"시리즈 동기화"/"배우 동기화" 버튼처럼 UI를 거치지 않고 `item.Series`/`item.Actors`를 직접
    /// 바꾸는 일괄 작업에도 똑같이 적용되는 문제였다**(2026-08-10 발견·수정) — 예: `scop-708`을 선택해 속성
    /// 창을 열어둔 채로 "시리즈 동기화"를 실행하면 `item.Series`는 바뀌지만 화면의 시리즈 콤보박스는 그
    /// 변경을 모른 채 예전 선택 상태(주로 "(없음)")를 그대로 유지하고 있다가, 다른 항목을 클릭해 이 창이
    /// `SwitchToItem`으로 전환되는 순간 그 예전 선택값을 `_item.Series`에 다시 덮어써서 방금 동기화된
    /// 시리즈가 초기화됐다. `Series`/`Actors`도 같은 방식으로 구독해 해결한다.
    /// </summary>
    private void ItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ManagedVideoItem.PlayCount):
                PlayCountBox.Text = _item.PlayCount.ToString();
                break;
            case nameof(ManagedVideoItem.ThumbnailPath):
            case nameof(ManagedVideoItem.ThumbnailOriginalPath):
                RefreshThumbnailPreview();
                break;
            case nameof(ManagedVideoItem.Series):
                RefreshSeriesComboBox();
                break;
            case nameof(ManagedVideoItem.Actors):
                SyncSelectedActorsFromItem();
                break;
        }
    }

    /// <summary>
    /// 이미 열려 있는 이 창을 다른 항목으로 재사용한다 — 관리 리스트에서 선택된 항목이 바뀌면
    /// `MainWindow`가 열려 있는 속성 창에 이 메서드를 호출해서, 창을 새로 열지 않고도 그 항목의 속성을
    /// 바로 따라가 보여준다("# 관리 리스트" 규칙: 목록에서 다른 파일을 클릭하면 열려 있는 속성 창도 갱신).
    /// 지금 보여주고 있던 항목에 커밋 대기 중인 변경사항(태그/배우 선택 등)이 있으면 먼저 커밋한다 — 창을
    /// 닫지 않고 넘어가는 것도 "닫기"와 마찬가지로 사용자가 입력한 내용을 조용히 버리면 안 되기 때문이다.
    /// </summary>
    public void SwitchToItem(ManagedVideoItem newItem)
    {
        if (ReferenceEquals(_item, newItem) || !TryCommitFormFields())
        {
            return;
        }

        _item.PropertyChanged -= ItemPropertyChanged;
        _item = newItem;
        _item.PropertyChanged += ItemPropertyChanged;
        _isNewItem = false; // SwitchToItem은 항상 이미 관리 리스트에 있는 항목으로만 전환된다

        RefreshActorComboBox();
        _selectedActors.Clear();
        foreach (var name in newItem.Actors)
        {
            _selectedActors.Add(name);
        }

        RefreshFileInfo();
        RefreshSeriesComboBox();
        BuildTagList();
        RefreshThumbnailPreview();
        RefreshSelectedActorsThumbnails();
        RefreshFileOperationAvailability();
        ContentScrollViewer.ScrollToTop();
    }

    private void RefreshFileInfo()
    {
        FileNameText.Text = Path.GetFileNameWithoutExtension(_item.FileName);
        FileExtensionText.Text = TrimLeadingDot(Path.GetExtension(_item.FileName));
        CodeBox.Text = string.IsNullOrEmpty(_item.Code)
            ? ManagedVideoItem.DeriveCode(_item.FileName, _item.FullPath)
            : _item.Code;
        ReleaseDateBox.Text = _item.ReleaseDate;
        SizeText.Text = _item.SizeDisplay;
        ModifiedText.Text = _item.ModifiedDate.ToString("yyyy-MM-dd HH:mm");
        FullPathText.Text = _item.FullPath;
        PlayCountBox.Text = _item.PlayCount.ToString();
        MemoBox.Text = _item.Memo;
        RefreshProductCodeDisplay();
    }

    /// <summary>썸네일 버튼 줄 왼쪽의 "품번" 표시(파일명에서 확장자를 뺀 부분)를 최신 파일명 기준으로 갱신한다.</summary>
    private void RefreshProductCodeDisplay()
    {
        ProductCodeText.Text = $"품번: {Path.GetFileNameWithoutExtension(_item.FileName)}";
    }

    private static string TrimLeadingDot(string extension) =>
        extension.StartsWith('.') ? extension[1..] : extension;

    private void RefreshSelectedActorsThumbnails()
    {
        SelectedActorsThumbnails.ItemsSource = _selectedActors
            .Select(name => _masterActors.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
            .OfType<ActorItem>()
            .ToList();
    }

    private void RefreshActorComboBox()
    {
        ActorComboBox.ItemsSource = _masterActors.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// 시리즈 콤보박스 항목을 마스터 목록(이름 기준 오름차순) + "선택 안함" 항목으로 채우고, 현재 항목의
    /// <see cref="ManagedVideoItem.Series"/> 값을 선택 상태로 맞춘다. 마스터 목록에서 이미 지워진 값을
    /// 이 항목이 그대로 갖고 있는 경우(다른 곳에서 시리즈가 삭제된 뒤)에도 데이터가 조용히 사라지지 않도록
    /// 목록에 함께 포함시킨다.
    /// </summary>
    private void RefreshSeriesComboBox()
    {
        var names = _masterSeries.Select(s => s.Name).ToList();
        if (!string.IsNullOrEmpty(_item.Series) && !names.Contains(_item.Series, StringComparer.OrdinalIgnoreCase))
        {
            names.Add(_item.Series);
        }

        var items = new List<string> { NoSeriesLabel };
        items.AddRange(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        SeriesComboBox.ItemsSource = items;
        SeriesComboBox.SelectedItem = string.IsNullOrEmpty(_item.Series) ? NoSeriesLabel : _item.Series;
    }

    /// <summary>배우 이름 칩(텍스트)을 클릭하면 배우 관리 창을 연다.</summary>
    private void ActorChipName_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: string name })
        {
            OpenActorManagerFor(_masterActors.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)));
        }

        e.Handled = true;
    }

    /// <summary>배우 썸네일을 클릭하면 배우 관리 창을 연다.</summary>
    private void ActorThumbnail_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ActorItem actor })
        {
            OpenActorManagerFor(actor);
        }

        e.Handled = true;
    }

    /// <summary>배우 관리 창을 열고 해당 배우를 선택 + 스크롤한다. 그 창에서 이름 변경/삭제 등이 있었을 수 있으므로
    /// 돌아오면 배우 콤보박스와 선택된 배우 표시를 최신 상태로 다시 맞춘다.</summary>
    private void OpenActorManagerFor(ActorItem? actor)
    {
        if (actor is null)
        {
            return;
        }

        // Owner는 MainWindow로 고정한다 — `this`(PropertiesWindow)로 하면, 나중에 이 창이 종류별 단일 인스턴스
        // 규칙으로 다른 PropertiesWindow에 밀려 닫힐 때 WPF가 소유한 창(ActorManagerWindow)까지 함께 닫아버려서
        // "모든 창은 독립적인 상태를 갖는다"는 규칙과 어긋난다.
        var dialog = new ActorManagerWindow(_masterActors, _masterSeries, _managedItems, _masterTags, actor) { Owner = Application.Current.MainWindow };
        dialog.Closed += (_, _) =>
        {
            RefreshActorComboBox();
            SyncSelectedActorsFromItem();
        };
        SingleInstanceWindow<ActorManagerWindow>.Show(dialog);
    }

    private void BuildTagList()
    {
        _tagItems = _masterTags
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Select(t => new TagCheckItem { Tag = t, IsSelected = _item.Tags.Contains(t, StringComparer.OrdinalIgnoreCase) })
            .ToList();

        TagsList.ItemsSource = _tagItems;
        NoTagsHint.Visibility = _tagItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 파일명/확장자 텍스트 상자 중 하나에서 포커스가 벗어나면(내용이 바뀐 경우) 두 값을 합쳐 바로 실제
    /// 파일 이름 변경을 적용한다.
    /// </summary>
    private void FileNamePart_LostFocus(object sender, RoutedEventArgs e) => CommitFileNameIfChanged();

    /// <summary>
    /// 이 창이 비활성화될 때(다른 최상위 창을 클릭 등)도 파일명 변경을 커밋한다(2026-08-04 추가). WPF의
    /// `LostFocus`는 포커스 스코프(이 창) 안에서의 논리적 포커스 이동에만 반응하므로, 텍스트 상자 안에서
    /// 커서를 유지한 채(같은 창 안의 다른 컨트롤로 옮기지 않고) 다른 창을 클릭하면 논리적 포커스가 그대로라
    /// `LostFocus`가 발생하지 않아 이름 변경이 적용되지 않는 문제가 있었다. `Window.Deactivated`는 창의
    /// 활성 상태(키보드 입력이 실제로 다른 창으로 넘어감)를 기준으로 하므로 이 경우도 놓치지 않는다.
    /// </summary>
    private void Window_Deactivated(object? sender, EventArgs e) => CommitFileNameIfChanged();

    private void CommitFileNameIfChanged()
    {
        var newName = ComposeFileName();
        if (string.Equals(newName, _item.FileName, StringComparison.Ordinal))
        {
            return;
        }

        if (_item.IsPlaceholder)
        {
            // 실제 파일이 없으므로 rename(File.Move)할 대상 자체가 없다 — 입력값을 그대로 반영만 한다.
            // 단, 이미 관리 중인 다른 항목과 이름이 겹치면(예: 실제로 이미 갖고 있는 파일과 같은 이름으로
            // 바꾸는 경우) 서로 다른 두 항목이 같은 FileName을 갖게 되므로 막는다(2026-08-07 추가).
            if (!TryGuardAgainstDuplicateFileName(newName))
            {
                FileNameText.Text = Path.GetFileNameWithoutExtension(_item.FileName);
                FileExtensionText.Text = TrimLeadingDot(Path.GetExtension(_item.FileName));
                return;
            }

            _item.FileName = newName;
            _item.FullPath = newName;
            RefreshProductCodeDisplay();
            return;
        }

        if (RenameHelper.TryRenameManagedItemTo(_item, newName, _masterActors, _masterSeries))
        {
            FullPathText.Text = _item.FullPath;
            CodeBox.Text = ManagedVideoItem.DeriveCode(_item.FileName, _item.FullPath);
            RefreshThumbnailPreview();
            RefreshProductCodeDisplay();
            SyncSelectedActorsFromItem();
            RefreshSeriesComboBox();
        }
        else
        {
            FileNameText.Text = Path.GetFileNameWithoutExtension(_item.FileName);
            FileExtensionText.Text = TrimLeadingDot(Path.GetExtension(_item.FileName));
        }
    }

    /// <summary>
    /// <paramref name="candidateName"/>과 같은 <see cref="ManagedVideoItem.FileName"/>을 가진 다른 항목이
    /// 이미 관리 리스트에 있으면 경고를 띄우고 false를 반환한다(2026-08-07 추가) — "파일 없이 추가"로 만든
    /// 항목은 실제 파일과 연결되어 있지 않아, 이미 관리 중인 파일과 같은 이름을 갖게 되기 쉽다(신규 생성 시
    /// 커밋 시점, 또는 나중에 파일명을 바꿀 때 모두 이 검사를 거친다).
    /// </summary>
    private bool TryGuardAgainstDuplicateFileName(string candidateName)
    {
        var duplicate = _managedItems.FirstOrDefault(m =>
            !ReferenceEquals(m, _item) && string.Equals(m.FileName, candidateName, StringComparison.Ordinal));

        if (duplicate is null)
        {
            return true;
        }

        MessageBox.Show(
            $"'{candidateName}' 이름의 항목이 이미 관리 리스트에 있습니다" +
            (duplicate.IsValid ? " (활성 상태)." : " (제거된 상태).") +
            "\n다른 파일명을 입력하거나, 기존 항목을 활용하세요.",
            "이미 존재하는 파일명",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
    }

    /// <summary>
    /// rename 시 <see cref="ActorCreditSync.OnFileRenamed"/>가 새 품번과 일치하는 Credits를 가진 배우를
    /// `_item.Actors`에 직접 추가할 수 있으므로, 화면에 보여주는 배우 선택 상태(`_selectedActors`)도 최신으로
    /// 맞춰준다 — 그렇지 않으면 "확인" 시 오래된 `_selectedActors`로 덮어써서 방금 추가된 배우가 사라진다.
    /// </summary>
    private void SyncSelectedActorsFromItem()
    {
        _selectedActors.Clear();
        foreach (var name in _item.Actors)
        {
            _selectedActors.Add(name);
        }

        RefreshSelectedActorsThumbnails();
    }

    private string ComposeFileName()
    {
        var name = FileNameText.Text.Trim();
        var extension = FileExtensionText.Text.Trim().TrimStart('.');
        return extension.Length == 0 ? name : $"{name}.{extension}";
    }

    private void ChangeFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (RenameHelper.TryEditFullPath(this, _item, _masterActors, _masterSeries))
        {
            FileNameText.Text = Path.GetFileNameWithoutExtension(_item.FileName);
            FileExtensionText.Text = TrimLeadingDot(Path.GetExtension(_item.FileName));
            FullPathText.Text = _item.FullPath;
            RefreshThumbnailPreview();
            RefreshProductCodeDisplay();
            SyncSelectedActorsFromItem();
            RefreshSeriesComboBox();
        }
    }

    private void MoveToFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (RenameHelper.TryMoveToFolder(this, _item))
        {
            FullPathText.Text = _item.FullPath;
            RefreshThumbnailPreview();
        }
    }

    /// <summary>
    /// `RenameHelper.LibraryBasePath`(`E:\happy`) 밑의 "코드" 값으로 된 폴더로 파일(및 관련 썸네일/원본 파일)을
    /// 이동한다. 예전에는 파일이 지금 있는 폴더의 바로 위 폴더를 기준으로 삼았으나(2026-08-16 이전), 실제
    /// 라이브러리에는 파일이 여러 단계 깊이 폴더에 들어있는 경우가 많아 항상 `E:\happy`를 기준으로 고정했다.
    /// </summary>
    private void MoveByCodeButton_Click(object sender, RoutedEventArgs e)
    {
        var code = CodeBox.Text.Trim();
        if (string.IsNullOrEmpty(code) || code.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            MessageBox.Show("올바른 코드를 입력하세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var newDirectory = Path.Combine(RenameHelper.LibraryBasePath, code);
        var newFullPath = Path.Combine(newDirectory, _item.FileName);

        if (string.Equals(newFullPath, _item.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("이미 같은 위치입니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"파일을 다음 위치로 이동하시겠습니까?\n\n이전 위치:\n{_item.FullPath}\n\n이동할 위치:\n{newFullPath}",
            "파일 이동 확인",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        if (RenameHelper.TryMoveToSpecificFolder(_item, newDirectory))
        {
            _item.Code = code;
            FullPathText.Text = _item.FullPath;
            RefreshThumbnailPreview();
        }
    }

    private void RefreshThumbnailPreview()
    {
        if (_item.HasThumbnail)
        {
            ThumbnailPathText.Text = $"{_item.ThumbnailPath} ({BuildThumbnailSizeInfo()})";
            ThumbnailImage.Source = ImageLoadHelper.Load(_item.ThumbnailPath);
            ThumbnailImage.Visibility = Visibility.Visible;
            ThumbnailFallbackIcon.Visibility = Visibility.Collapsed;
        }
        else
        {
            ThumbnailPathText.Text = "지정된 썸네일 없음";
            ThumbnailImage.Visibility = Visibility.Collapsed;
            ThumbnailFallbackIcon.Visibility = Visibility.Visible;
        }
    }

    private string BuildThumbnailSizeInfo()
    {
        var thumbnailSize = _item.ThumbnailPath is not null && File.Exists(_item.ThumbnailPath)
            ? FormatUtil.FormatSize(new FileInfo(_item.ThumbnailPath).Length)
            : "-";
        var originalSize = _item.ThumbnailOriginalPath is not null && File.Exists(_item.ThumbnailOriginalPath)
            ? FormatUtil.FormatSize(new FileInfo(_item.ThumbnailOriginalPath).Length)
            : "-";

        return $"{thumbnailSize}, {originalSize}";
    }

    private void AddThumbnailButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "썸네일 이미지 선택",
            Filter = "이미지 파일 (*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif|모든 파일 (*.*)|*.*",
        };

        if (dialog.ShowDialog() == true)
        {
            ApplyThumbnail(dialog.FileName);
        }
    }

    private void ThumbnailViewer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        OriginalImageWindow.ShowFor(this, _item);

    private void ThumbnailViewer_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropImageHelper.CanAccept(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void ThumbnailViewer_Drop(object sender, DragEventArgs e)
    {
        var imagePath = DragDropImageHelper.TryGetImagePath(e.Data);
        if (imagePath is not null)
        {
            ApplyThumbnail(imagePath);
        }
        else
        {
            MessageBox.Show(
                "드롭한 항목에서 이미지를 찾지 못했습니다.\n" +
                $"제공된 데이터 형식: {string.Join(", ", e.Data.GetFormats())}",
                "썸네일 지정 실패",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ApplyThumbnail(string sourceImagePath)
    {
        try
        {
            var result = _item.IsPlaceholder
                ? ThumbnailHelper.CreatePlaceholderThumbnail(sourceImagePath, _item.FileName)
                : ThumbnailHelper.CreateThumbnail(sourceImagePath, _item.FullPath);
            _item.ThumbnailPath = result.ThumbnailPath;
            _item.ThumbnailOriginalPath = result.OriginalPath;
            RefreshThumbnailPreview();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"썸네일을 만들 수 없습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteThumbnailButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_item.HasThumbnail)
        {
            MessageBox.Show("삭제할 썸네일이 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        TryDeleteFile(_item.ThumbnailPath);
        TryDeleteFile(_item.ThumbnailOriginalPath);
        _item.ThumbnailPath = null;
        _item.ThumbnailOriginalPath = null;
        RefreshThumbnailPreview();
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // 파일 삭제 실패(권한 등)해도 참조는 이미 지웠으므로 UI 상태는 "썸네일 없음"으로 유지한다.
        }
    }

    private void PlayCountReset_Click(object sender, RoutedEventArgs e) => PlayCountBox.Text = "0";

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_item.FullPath) { UseShellExecute = true });
            _item.PlayCount++; // PlayCountBox는 ItemPropertyChanged 구독으로 자동으로 따라간다
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 열 수 없습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddActor_Click(object sender, RoutedEventArgs e)
    {
        if (ActorComboBox.SelectedItem is not ActorItem actor)
        {
            MessageBox.Show("추가할 배우를 콤보박스에서 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_selectedActors.Any(a => string.Equals(a, actor.Name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("이미 추가된 배우입니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _selectedActors.Add(actor.Name);
        RefreshSelectedActorsThumbnails();
    }

    /// <summary>
    /// 배우 콤보박스 드롭다운을 열 때마다, 마우스 휠 한 번에 <see cref="SystemParameters.WheelScrollLines"/>
    /// (기본 3줄)만큼 움직이는 기본 동작 대신 한 칸(항목 하나)씩만 움직이도록 내부 <see cref="ScrollViewer"/>에
    /// 휠 이벤트를 직접 처리하는 핸들러를 연결한다(2026-08-26 추가, 사용자 요청) — 썸네일 포함 항목 높이가 커서
    /// 기본 동작대로면 한 번 스크롤할 때 여러 항목이 한꺼번에 넘어가 버린다.
    /// </summary>
    private void ActorComboBox_DropDownOpened(object sender, EventArgs e)
    {
        ActorComboBox.ApplyTemplate();
        if (ActorComboBox.Template.FindName("PART_Popup", ActorComboBox) is not Popup { Child: FrameworkElement popupContent })
        {
            return;
        }

        if (FindVisualChild<ScrollViewer>(popupContent) is not { } scrollViewer)
        {
            return;
        }

        // 팝업을 열 때마다 같은 ScrollViewer 인스턴스가 재사용될 수 있어, 먼저 해제한 뒤 다시 구독해 중복을 막는다.
        scrollViewer.PreviewMouseWheel -= ActorComboBoxScrollViewer_PreviewMouseWheel;
        scrollViewer.PreviewMouseWheel += ActorComboBoxScrollViewer_PreviewMouseWheel;
    }

    /// <summary>
    /// 한 칸(항목 하나)만큼만 <see cref="ScrollViewer.ScrollToVerticalOffset"/>로 직접 옮긴다. 항목 높이는
    /// <c>ExtentHeight / Items.Count</c>(모든 행이 같은 템플릿이라 균일함)로 직접 계산한다.
    /// **왜 <see cref="ScrollViewer.LineUp"/>/<see cref="ScrollViewer.LineDown"/>이 아닌가**: 처음에는
    /// <c>ComboBox.ItemsPanel</c>을 <c>VirtualizingStackPanel ScrollUnit="Item"</c>으로 지정하고
    /// <c>LineUp()</c>/<c>LineDown()</c>을 호출했는데(가상화 패널이라 정확히 한 항목씩 움직임), 스크롤할
    /// 때마다 컨테이너가 재생성되면서 드롭다운이 마우스 캡처를 잃고 저절로 닫혀버리는 부작용이 실제로
    /// 재현됐다(1~2번째 스크롤은 우연히 괜찮아 보이다가 이후 스크롤에서 닫히는 등 간헐적이었음).
    /// <see cref="ScrollViewer.ScrollToVerticalOffset"/>로 바꿔도 가상화 패널을 계속 쓰는 한 같은 문제가
    /// 남아있었다 — 근본 원인은 호출 방식이 아니라 **가상화 자체**(스크롤할 때마다 컨테이너를 새로
    /// 만들고/버리는 것)였다. 그래서 <c>ItemsPanel</c>을 가상화하지 않는 평범한 <c>StackPanel</c>로 바꿔
    /// 컨테이너가 스크롤 중 전혀 재생성되지 않게 했다 — 배우 수가 수백 명 수준이라 비가상화로도 성능
    /// 문제가 없다(다른 창의 단순 콤보박스들도 원래 비가상화).
    /// </summary>
    private void ActorComboBoxScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scrollViewer = (ScrollViewer)sender;
        var itemCount = ActorComboBox.Items.Count;
        if (itemCount == 0 || scrollViewer.ExtentHeight <= 0)
        {
            return;
        }

        var itemHeight = scrollViewer.ExtentHeight / itemCount;
        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + (e.Delta > 0 ? -itemHeight : itemHeight));
        e.Handled = true;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
            {
                return typed;
            }

            if (FindVisualChild<T>(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void RemoveActorChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string actorName })
        {
            _selectedActors.Remove(actorName);
            RefreshSelectedActorsThumbnails();
        }

        e.Handled = true;
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            $"'{_item.FileName}' 항목의 관리 데이터를 완전히 삭제하시겠습니까?\n" +
            "재생횟수/태그/배우/메모/썸네일 등 모든 데이터가 사라지며 복구할 수 없습니다.\n" +
            "(실제 동영상 파일은 삭제되지 않습니다.)",
            "완전 삭제 확인",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        PermanentlyDeleted = true;
        _explicitCloseHandled = true;
        Close();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (TryCommitFormFields())
        {
            _explicitCloseHandled = true;
            Close();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _explicitCloseHandled = true;
        Close();
    }

    /// <summary>Esc 키로 "취소"와 동일하게 닫는다 — 모덜리스 전환으로 `Button.IsCancel`(모달 전용)을 더 이상 쓸 수 없어 직접 처리한다.</summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel_Click(sender, e);
        }
    }

    /// <summary>재생횟수/코드/메모/태그/배우 입력값을 검증하고 `_item`에 반영한다. 실패하면 오류를 안내하고 false를 반환한다.</summary>
    private bool TryCommitFormFields()
    {
        if (!int.TryParse(PlayCountBox.Text, out var playCount) || playCount < 0)
        {
            MessageBox.Show("재생횟수는 0 이상의 숫자여야 합니다.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        // _item.Actors를 덮어쓰기 전에, 화면에서 빠진(사용자가 ✕로 제거한) 배우를 미리 찾아둔다 —
        // 그 배우의 Credits에서도 이 파일의 품번을 제거해야 하므로(ActorCreditSync.OnActorRemovedFromItem).
        var removedActors = _item.Actors
            .Where(a => !_selectedActors.Contains(a, StringComparer.OrdinalIgnoreCase))
            .ToList();

        _item.PlayCount = playCount;
        _item.Code = CodeBox.Text.Trim();
        _item.ReleaseDate = ReleaseDateBox.Text.Trim();
        _item.Series = SeriesComboBox.SelectedItem is string selectedSeries && selectedSeries != NoSeriesLabel
            ? selectedSeries
            : string.Empty;
        _item.Memo = MemoBox.Text.Trim();
        _item.SetTags(_tagItems.Where(t => t.IsSelected).Select(t => t.Tag).ToList());
        _item.SetActors(_selectedActors.ToList());

        foreach (var removedActor in removedActors)
        {
            ActorCreditSync.OnActorRemovedFromItem(_item, removedActor, _masterActors);
        }

        if (_isNewItem)
        {
            // "파일 없이 추가"로 열린, 아직 관리 리스트에 없는 새 항목 — 파일명을 입력했을 때만 실제로
            // 추가한다. 입력이 없으면(파일명이 비어있으면) 조용히 추가를 취소한다(요구사항: 입력 없으면 취소).
            if (!string.IsNullOrWhiteSpace(_item.FileName))
            {
                // 이미 같은 파일명의 항목이 있으면(활성이든 제거된 상태든) 겹치는 중복 데이터가 생기므로
                // 막는다(2026-08-07 추가) — 예: 실제로 이미 관리 중인 "sdmt-261.mp4"를 모른 채 같은
                // 이름으로 "파일 없이 추가"하면 서로 다른 두 항목이 같은 이름을 갖게 되는 문제가 있었다.
                if (!TryGuardAgainstDuplicateFileName(_item.FileName))
                {
                    return false;
                }

                // 실제 파일이 없으므로 "제거된 항목"과 동일하게 처리한다 — 기본 목록에서는 보이지 않고
                // "제거된 항목도 표시" 체크박스로 볼 수 있으며, 나중에 같은 이름의 실제 파일이 추가되면
                // 기존 재사용 메커니즘(ManagedListImporter)이 그대로 데이터를 재활용한다.
                _item.IsValid = false;
                _item.IsExist = false;
                _managedItems.Add(_item);
                _isNewItem = false;
                RefreshFileOperationAvailability();
            }
        }

        return true;
    }

    /// <summary>
    /// 창 닫기(X) 버튼 등으로 대화상자가 닫힐 때 호출된다. "확인"/"취소"/"완전 삭제" 버튼은 이미
    /// `_explicitCloseHandled`를 설정해두므로 그 경우는 그냥 통과시키고, 그 외(제목 표시줄의 X, Alt+F4 등)에는
    /// "확인"과 동일하게 변경사항을 저장한다 — 그래야 실수로 닫아도 입력한 내용이 사라지지 않는다.
    /// </summary>
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_explicitCloseHandled)
        {
            return;
        }

        if (!TryCommitFormFields())
        {
            e.Cancel = true;
            return;
        }

        _explicitCloseHandled = true;
    }
}
