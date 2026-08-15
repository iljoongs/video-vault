using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace VideoVault;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private static readonly TimeSpan AutoSaveDelay = TimeSpan.FromMilliseconds(500);

    private string? _currentFolder;
    private bool _suppressAutoSave;

    private readonly ObservableCollection<ManagedVideoItem> _managedItems = new();
    private readonly ObservableCollection<string> _masterTags = new();
    private readonly ObservableCollection<ActorItem> _masterActors = new();
    private readonly ObservableCollection<SeriesItem> _masterSeries = new();
    private readonly ICollectionView _managedView;

    private readonly DispatcherTimer _libraryAutoSaveTimer;
    private readonly DispatcherTimer _tagsAutoSaveTimer;
    private readonly DispatcherTimer _settingsAutoSaveTimer;
    private readonly DispatcherTimer _actorsAutoSaveTimer;
    private readonly DispatcherTimer _seriesAutoSaveTimer;

    /// <summary>시리즈 필터 콤보박스에서 "전체(필터 없음)"를 나타내는 항목. 실제 시리즈 이름과 겹치지 않도록 목록 맨 앞에 삽입한다.</summary>
    private const string AllSeriesLabel = "(전체)";

    private string? _nameFilter;
    private string? _folderFilter;
    private string? _seriesFilter;
    private HashSet<string> _selectedTags = new(StringComparer.OrdinalIgnoreCase);
    private List<TagCheckItem> _tagCheckItems = new();
    private HashSet<string> _selectedActorFilter = new(StringComparer.OrdinalIgnoreCase);
    private List<ActorFilterCheckItem> _actorFilterCheckItems = new();
    private bool _showRemovedItems;

    private string? _sortProperty;
    private bool _sortAscending = true;

    private GridViewColumn? _hasThumbnailColumn;
    private GridViewColumn? _sizeColumn;
    private GridViewColumn? _playCountColumn;
    private GridViewColumn? _tagsColumn;
    private GridViewColumn? _actorColumn;
    private GridViewColumn? _memoColumn;
    private GridViewColumn? _folderColumn;
    private GridViewColumn? _codeColumn;
    private GridViewColumn? _seriesColumn;
    private GridViewColumn? _releaseDateColumn;

    public MainWindow()
    {
        InitializeComponent();
        WindowSnapHelper.Attach(this);

        AppPaths.EnsureAppDataDirectory();
        LibraryPathText.Text = $"자동 저장 위치: {AppPaths.LibraryPath}";

        BuildManagedColumns();
        ApplyVisibleColumns(DefaultVisibleColumns);

        _managedView = CollectionViewSource.GetDefaultView(_managedItems);
        _managedView.Filter = FilterManagedItem;
        ManagedListView.ItemsSource = _managedView;
        ManagedIconView.ItemsSource = _managedView;
        EnableLiveShaping();

        ManagedListView.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(ManagedListHeader_Click));
        ManagedListView.AddHandler(UIElement.PreviewMouseRightButtonUpEvent, new MouseButtonEventHandler(ManagedListHeader_RightClick), true);

        _managedItems.CollectionChanged += ManagedItems_CollectionChanged;
        _masterTags.CollectionChanged += (_, _) => ScheduleTagsAutoSave();
        _masterActors.CollectionChanged += MasterActors_CollectionChanged;
        _masterSeries.CollectionChanged += MasterSeries_CollectionChanged;

        _libraryAutoSaveTimer = new DispatcherTimer { Interval = AutoSaveDelay };
        _libraryAutoSaveTimer.Tick += (_, _) => { _libraryAutoSaveTimer.Stop(); SaveLibrary(); };

        _tagsAutoSaveTimer = new DispatcherTimer { Interval = AutoSaveDelay };
        _tagsAutoSaveTimer.Tick += (_, _) => { _tagsAutoSaveTimer.Stop(); SaveTags(); };

        _settingsAutoSaveTimer = new DispatcherTimer { Interval = AutoSaveDelay };
        _settingsAutoSaveTimer.Tick += (_, _) => { _settingsAutoSaveTimer.Stop(); SaveSettings(); };

        _actorsAutoSaveTimer = new DispatcherTimer { Interval = AutoSaveDelay };
        _actorsAutoSaveTimer.Tick += (_, _) => { _actorsAutoSaveTimer.Stop(); SaveActors(); };

        _seriesAutoSaveTimer = new DispatcherTimer { Interval = AutoSaveDelay };
        _seriesAutoSaveTimer.Tick += (_, _) => { _seriesAutoSaveTimer.Stop(); SaveSeries(); };

        LoadInitialData();
        RefreshSeriesFilterComboBox();
        UpdateManagedCountDisplay();
    }

    /// <summary>
    /// "# 창" 규칙(모든 창은 데이터가 바뀌면 동시에 갱신된다): 여러 창이 동시에 열려 있을 수 있게 되면서
    /// (예: 배우 관리 창에서 배우를 배우 관리로, 속성 창에서 배우로 서로 넘나드는 동안 둘 다 열려 있음),
    /// 그 창들이 공유하는 `_managedItems`의 항목이 바뀔 때 관리 리스트 화면(정렬/필터)이 창을 닫았다 여는 것
    /// 없이도 즉시 반영되도록 `_managedView`에 라이브 필터링/정렬을 켠다. 정렬/필터 조건에 쓰이는 속성들을
    /// 감시 대상으로 등록해두면, 그 속성이 바뀔 때마다 WPF가 자동으로 해당 항목의 필터/정렬 위치를 재평가한다.
    /// </summary>
    private void EnableLiveShaping()
    {
        if (_managedView is not ICollectionViewLiveShaping liveShaping)
        {
            return;
        }

        if (liveShaping.CanChangeLiveFiltering)
        {
            liveShaping.LiveFilteringProperties.Add(nameof(ManagedVideoItem.IsValid));
            liveShaping.LiveFilteringProperties.Add(nameof(ManagedVideoItem.FileName));
            liveShaping.LiveFilteringProperties.Add(nameof(ManagedVideoItem.FolderName));
            liveShaping.LiveFilteringProperties.Add(nameof(ManagedVideoItem.Tags));
            liveShaping.LiveFilteringProperties.Add(nameof(ManagedVideoItem.Actors));
            liveShaping.LiveFilteringProperties.Add(nameof(ManagedVideoItem.Series));
            liveShaping.IsLiveFiltering = true;
        }

        if (liveShaping.CanChangeLiveSorting)
        {
            liveShaping.LiveSortingProperties.Add(nameof(ManagedVideoItem.FileName));
            liveShaping.LiveSortingProperties.Add(nameof(ManagedVideoItem.SizeBytes));
            liveShaping.LiveSortingProperties.Add(nameof(ManagedVideoItem.PlayCount));
            liveShaping.LiveSortingProperties.Add(nameof(ManagedVideoItem.TagsSortKey));
            liveShaping.LiveSortingProperties.Add(nameof(ManagedVideoItem.ActorsSortKey));
            liveShaping.LiveSortingProperties.Add(nameof(ManagedVideoItem.Memo));
            liveShaping.LiveSortingProperties.Add(nameof(ManagedVideoItem.FolderName));
            liveShaping.LiveSortingProperties.Add(nameof(ManagedVideoItem.HasThumbnail));
            liveShaping.LiveSortingProperties.Add(nameof(ManagedVideoItem.Code));
            liveShaping.LiveSortingProperties.Add(nameof(ManagedVideoItem.Series));
            liveShaping.LiveSortingProperties.Add(nameof(ManagedVideoItem.ReleaseDate));
            liveShaping.IsLiveSorting = true;
        }
    }

    private static readonly string[] DefaultVisibleColumns = { "Size", "PlayCount", "Tags" };

    /// <summary>썸네일 유무/크기/재생횟수/태그/배우/메모 컬럼 객체를 만들어둔다 (아직 GridView에는 추가하지 않음).</summary>
    private void BuildManagedColumns()
    {
        var tagsCellTemplate = (DataTemplate)Resources["TagsCellTemplate"];
        var sizeCellTemplate = (DataTemplate)Resources["SizeCellTemplate"];
        var playCountCellTemplate = (DataTemplate)Resources["PlayCountCellTemplate"];
        var hasThumbnailCellTemplate = (DataTemplate)Resources["HasThumbnailCellTemplate"];

        _hasThumbnailColumn = new GridViewColumn { Header = string.Empty, Width = 32, CellTemplate = hasThumbnailCellTemplate };
        _sizeColumn = new GridViewColumn { Header = "크기", Width = 100, CellTemplate = sizeCellTemplate };
        _playCountColumn = new GridViewColumn { Header = "재생횟수", Width = 90, CellTemplate = playCountCellTemplate };
        _tagsColumn = new GridViewColumn { Header = "태그", Width = 250, CellTemplate = tagsCellTemplate };
        _actorColumn = new GridViewColumn { Header = "배우", Width = 120, DisplayMemberBinding = new Binding(nameof(ManagedVideoItem.ActorsDisplay)) };
        _memoColumn = new GridViewColumn { Header = "메모", Width = 150, DisplayMemberBinding = new Binding(nameof(ManagedVideoItem.Memo)) };
        _folderColumn = new GridViewColumn { Header = "폴더명", Width = 150, DisplayMemberBinding = new Binding(nameof(ManagedVideoItem.FolderName)) };
        _codeColumn = new GridViewColumn { Header = "코드", Width = 100, DisplayMemberBinding = new Binding(nameof(ManagedVideoItem.Code)) };
        _seriesColumn = new GridViewColumn { Header = "시리즈", Width = 120, DisplayMemberBinding = new Binding(nameof(ManagedVideoItem.Series)) };
        _releaseDateColumn = new GridViewColumn { Header = "출시일", Width = 100, DisplayMemberBinding = new Binding(nameof(ManagedVideoItem.ReleaseDate)) };
    }

    /// <summary>주어진 컬럼 키 목록만 GridView에 표시되도록 맞추고, 컬럼 선택 팝업의 체크박스도 동기화한다.</summary>
    private void ApplyVisibleColumns(IEnumerable<string> keys)
    {
        var desired = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        var gridView = (GridView)ManagedListView.View;

        // 썸네일 컬럼은 파일명보다 앞(맨 앞)에 와야 하므로 항상 먼저 처리한다.
        SetColumnVisible(gridView, _hasThumbnailColumn, desired.Contains("Thumbnail"), insertAtStart: true);
        SetColumnVisible(gridView, _sizeColumn, desired.Contains("Size"));
        SetColumnVisible(gridView, _playCountColumn, desired.Contains("PlayCount"));
        SetColumnVisible(gridView, _tagsColumn, desired.Contains("Tags"));
        SetColumnVisible(gridView, _actorColumn, desired.Contains("Actor"));
        SetColumnVisible(gridView, _memoColumn, desired.Contains("Memo"));
        SetColumnVisible(gridView, _folderColumn, desired.Contains("Folder"));
        SetColumnVisible(gridView, _codeColumn, desired.Contains("Code"));
        SetColumnVisible(gridView, _seriesColumn, desired.Contains("Series"));
        SetColumnVisible(gridView, _releaseDateColumn, desired.Contains("ReleaseDate"));

        ThumbnailColumnCheckBox.IsChecked = desired.Contains("Thumbnail");
        SizeColumnCheckBox.IsChecked = desired.Contains("Size");
        PlayCountColumnCheckBox.IsChecked = desired.Contains("PlayCount");
        TagsColumnCheckBox.IsChecked = desired.Contains("Tags");
        ActorColumnCheckBox.IsChecked = desired.Contains("Actor");
        MemoColumnCheckBox.IsChecked = desired.Contains("Memo");
        FolderColumnCheckBox.IsChecked = desired.Contains("Folder");
        CodeColumnCheckBox.IsChecked = desired.Contains("Code");
        SeriesColumnCheckBox.IsChecked = desired.Contains("Series");
        ReleaseDateColumnCheckBox.IsChecked = desired.Contains("ReleaseDate");
    }

    private static void SetColumnVisible(GridView gridView, GridViewColumn? column, bool visible, bool insertAtStart = false)
    {
        if (column is null)
        {
            return;
        }

        if (visible)
        {
            if (!gridView.Columns.Contains(column))
            {
                if (insertAtStart)
                {
                    gridView.Columns.Insert(0, column);
                }
                else
                {
                    gridView.Columns.Add(column);
                }
            }
        }
        else
        {
            gridView.Columns.Remove(column);
        }
    }

    private List<string> GetVisibleColumnKeys()
    {
        var gridView = (GridView)ManagedListView.View;
        var keys = new List<string>();

        if (_hasThumbnailColumn is not null && gridView.Columns.Contains(_hasThumbnailColumn)) keys.Add("Thumbnail");
        if (_sizeColumn is not null && gridView.Columns.Contains(_sizeColumn)) keys.Add("Size");
        if (_playCountColumn is not null && gridView.Columns.Contains(_playCountColumn)) keys.Add("PlayCount");
        if (_tagsColumn is not null && gridView.Columns.Contains(_tagsColumn)) keys.Add("Tags");
        if (_actorColumn is not null && gridView.Columns.Contains(_actorColumn)) keys.Add("Actor");
        if (_memoColumn is not null && gridView.Columns.Contains(_memoColumn)) keys.Add("Memo");
        if (_folderColumn is not null && gridView.Columns.Contains(_folderColumn)) keys.Add("Folder");
        if (_codeColumn is not null && gridView.Columns.Contains(_codeColumn)) keys.Add("Code");
        if (_seriesColumn is not null && gridView.Columns.Contains(_seriesColumn)) keys.Add("Series");
        if (_releaseDateColumn is not null && gridView.Columns.Contains(_releaseDateColumn)) keys.Add("ReleaseDate");

        return keys;
    }

    /// <summary>현재 컬럼 너비를 컬럼 키 기준으로 모은다. 숨겨진 컬럼도 마지막으로 가졌던 너비를 그대로 저장해둔다.</summary>
    private Dictionary<string, double> GetColumnWidths()
    {
        var widths = new Dictionary<string, double>();

        void Add(string key, GridViewColumn? column)
        {
            if (column is not null)
            {
                widths[key] = column.ActualWidth > 0 ? column.ActualWidth : column.Width;
            }
        }

        Add("FileName", FileNameColumn);
        Add("Thumbnail", _hasThumbnailColumn);
        Add("Size", _sizeColumn);
        Add("PlayCount", _playCountColumn);
        Add("Tags", _tagsColumn);
        Add("Actor", _actorColumn);
        Add("Memo", _memoColumn);
        Add("Folder", _folderColumn);
        Add("Code", _codeColumn);
        Add("Series", _seriesColumn);
        Add("ReleaseDate", _releaseDateColumn);

        return widths;
    }

    private void ApplyColumnWidths(Dictionary<string, double> widths)
    {
        void Apply(string key, GridViewColumn? column)
        {
            if (column is not null && widths.TryGetValue(key, out var width) && width > 0)
            {
                column.Width = width;
            }
        }

        Apply("FileName", FileNameColumn);
        Apply("Thumbnail", _hasThumbnailColumn);
        Apply("Size", _sizeColumn);
        Apply("PlayCount", _playCountColumn);
        Apply("Tags", _tagsColumn);
        Apply("Actor", _actorColumn);
        Apply("Memo", _memoColumn);
        Apply("Folder", _folderColumn);
        Apply("Code", _codeColumn);
        Apply("Series", _seriesColumn);
        Apply("ReleaseDate", _releaseDateColumn);
    }

    private void ColumnToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: string columnKey } checkBox)
        {
            return;
        }

        var column = columnKey switch
        {
            "Thumbnail" => _hasThumbnailColumn,
            "Size" => _sizeColumn,
            "PlayCount" => _playCountColumn,
            "Tags" => _tagsColumn,
            "Actor" => _actorColumn,
            "Memo" => _memoColumn,
            "Folder" => _folderColumn,
            "Code" => _codeColumn,
            "Series" => _seriesColumn,
            "ReleaseDate" => _releaseDateColumn,
            _ => null,
        };

        var gridView = (GridView)ManagedListView.View;
        SetColumnVisible(gridView, column, checkBox.IsChecked == true, insertAtStart: columnKey == "Thumbnail");
        ScheduleSettingsAutoSave();
    }

    private void SaveNow_Click(object sender, RoutedEventArgs e) => SaveAllNow();

    /// <summary>디바운스를 기다리지 않고 관리 리스트/태그/배우/설정을 즉시 저장한다. Ctrl+S와 창 닫기(<see cref="MainWindow_Closing"/>)가 공유한다.</summary>
    private void SaveAllNow()
    {
        _libraryAutoSaveTimer.Stop();
        SaveLibrary();

        _tagsAutoSaveTimer.Stop();
        SaveTags();

        _actorsAutoSaveTimer.Stop();
        SaveActors();

        _seriesAutoSaveTimer.Stop();
        SaveSeries();

        _settingsAutoSaveTimer.Stop();
        SaveSettings();
    }

    /// <summary>
    /// 창을 닫을 때 최종 상태를 즉시 저장한다. 컬럼 너비는 드래그로 바뀔 때 자동 저장을 예약하지 않으므로
    /// (변경 이벤트가 없어 매 리사이즈마다 걸기엔 과함), 종료 시점에 한 번 저장하는 것으로 "종료 전 상태 유지"를 보장한다.
    /// </summary>
    private void MainWindow_Closing(object sender, CancelEventArgs e) => SaveAllNow();

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SaveNow_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && e.OriginalSource is not TextBox)
        {
            RemoveFromManagedList_Click(sender, e);
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.F2:
                RenameManaged_Click(sender, e);
                e.Handled = true;
                break;
            case Key.F1:
                PropertiesManaged_Click(sender, e);
                e.Handled = true;
                break;
        }
    }

    private class TagCheckItem
    {
        public string Tag { get; set; } = string.Empty;
        public bool IsSelected { get; set; }
    }

    private class ActorFilterCheckItem
    {
        public string ActorName { get; set; } = string.Empty;
        public bool IsSelected { get; set; }
    }

    // ===================== 시작 시 자동 로딩 =====================

    private void LoadInitialData()
    {
        _suppressAutoSave = true;
        try
        {
            LoadStep("관리 리스트", () =>
            {
                if (File.Exists(AppPaths.LibraryPath))
                {
                    foreach (var item in ManagedListRepository.Load(AppPaths.LibraryPath))
                    {
                        _managedItems.Add(item);
                    }
                }
            });

            // (레거시 마이그레이션, 2026-08-06 추가) 예전 버전이 남긴 removed.json이 있으면 한 번만
            // 병합해서 불러온다 — 그 파일에 있던 항목은 전부 "제거된" 상태였으므로 IsValid = false로
            // 강제한다. 병합 후에는 파일을 삭제해 다음 실행부터는 이 단계가 다시 일어나지 않게 한다
            // (이후로는 활성/제거 항목 모두 library.json 하나에만 저장됨).
            LoadStep("관리 리스트(레거시 제거 항목 병합)", () =>
            {
                if (!File.Exists(AppPaths.LegacyRemovedLibraryPath))
                {
                    return;
                }

                foreach (var item in ManagedListRepository.Load(AppPaths.LegacyRemovedLibraryPath))
                {
                    item.IsValid = false;
                    _managedItems.Add(item);
                }

                File.Delete(AppPaths.LegacyRemovedLibraryPath);
            });

            LoadStep("태그 목록", () =>
            {
                if (File.Exists(AppPaths.TagsPath))
                {
                    foreach (var tag in TagRepository.Load(AppPaths.TagsPath))
                    {
                        _masterTags.Add(tag);
                    }
                }
            });

            LoadStep("배우 목록", () =>
            {
                if (File.Exists(AppPaths.ActorsPath))
                {
                    foreach (var actor in ActorRepository.Load(AppPaths.ActorsPath))
                    {
                        _masterActors.Add(actor);
                    }
                }
            });

            LoadStep("시리즈 목록", () =>
            {
                if (File.Exists(AppPaths.SeriesPath))
                {
                    foreach (var series in SeriesRepository.Load(AppPaths.SeriesPath))
                    {
                        _masterSeries.Add(series);
                    }
                }
            });

            LoadStep("설정", () =>
            {
                if (File.Exists(AppPaths.SettingsPath))
                {
                    ApplySettings(SettingsRepository.Load(AppPaths.SettingsPath));
                }
            });

            LoadStep("파일 존재 여부 확인", UpdateFileExistence);
        }
        finally
        {
            _suppressAutoSave = false;
        }
    }

    /// <summary>
    /// 시작 시 로딩 단계 하나를 실행한다. 한 단계가 손상된 파일 등으로 실패해도 그 단계만 건너뛰고
    /// 나머지 단계(다른 JSON 파일 로딩 등)는 계속 진행하기 위해, 단계별로 독립된 try/catch를 둔다
    /// (예: tags.json이 손상돼도 actors.json/settings.json 로딩과 누락 파일 정리는 그대로 진행됨).
    /// </summary>
    private static void LoadStep(string description, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"{description}을(를) 불러오는 중 오류가 발생했습니다. 이 항목은 건너뛰고 계속 진행합니다.\n{ex.Message}",
                "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 모든 항목(유효/제거 여부와 무관하게)의 <see cref="ManagedVideoItem.IsExist"/>를 실제 파일 존재 여부로
    /// 다시 판단해 갱신한다(2026-08-06 변경 — 예전 `ReconcileMissingFiles`는 활성 항목만 검사해서 파일이
    /// 없어지면 자동으로 "제거" 처리(`IsArchived = true`)했으나, 이제 `IsExist`와 `IsValid`가 독립된
    /// 축이라 파일이 일시적으로(외장 드라이브 분리 등) 없어져도 `IsValid`는 건드리지 않는다 — 관리 리스트에서
    /// 사라지지 않고 계속 활성 상태로 남아있으며, 화면에서 다른 색으로 표시될 뿐이다). "파일 없이 추가"된
    /// 항목(`IsPlaceholder`)은 실제 폴더가 없는 경로이므로 항상 존재하지 않는 것으로 취급한다.
    /// </summary>
    private void UpdateFileExistence()
    {
        var changed = false;

        foreach (var item in _managedItems)
        {
            var exists = !item.IsPlaceholder && File.Exists(item.FullPath);
            if (item.IsExist != exists)
            {
                item.IsExist = exists;
                changed = true;
            }
        }

        if (changed)
        {
            _managedView.Refresh();
        }
    }

    private void ApplySettings(AppSettings settings)
    {
        if (settings.MainWindowWidth is { } width && settings.MainWindowHeight is { } height &&
            settings.MainWindowLeft is { } left && settings.MainWindowTop is { } top &&
            WindowPositionMemory.IsOnScreen(left, top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Width = width;
            Height = height;
            Left = left;
            Top = top;
        }

        IconViewModeRadio.IsChecked = settings.IsIconView;
        ListViewModeRadio.IsChecked = !settings.IsIconView;

        if (settings.SortProperty is not null)
        {
            SetSort(settings.SortProperty, settings.SortAscending);
        }

        _nameFilter = settings.NameFilter;
        NameFilterTextBox.Text = _nameFilter ?? string.Empty;
        _folderFilter = settings.FolderFilter;
        FolderFilterTextBox.Text = _folderFilter ?? string.Empty;
        _seriesFilter = settings.SeriesFilter;
        _selectedTags = new HashSet<string>(settings.SelectedTags, StringComparer.OrdinalIgnoreCase);
        _selectedActorFilter = new HashSet<string>(settings.SelectedActors, StringComparer.OrdinalIgnoreCase);
        _showRemovedItems = settings.ShowRemovedItems;
        ShowRemovedItemsCheckBox.IsChecked = _showRemovedItems;
        _managedView.Refresh();

        ApplyVisibleColumns(settings.VisibleColumns);
        ApplyColumnWidths(settings.ColumnWidths);

        if (settings.SelectedItemPath is not null)
        {
            var selectedItem = _managedItems.FirstOrDefault(m =>
                string.Equals(m.FullPath, settings.SelectedItemPath, StringComparison.OrdinalIgnoreCase));
            if (selectedItem is not null)
            {
                Selector view = IconViewModeRadio.IsChecked == true ? ManagedIconView : ManagedListView;
                view.SelectedItem = selectedItem;
            }
        }

        if (settings.LastFolder is not null && Directory.Exists(settings.LastFolder))
        {
            _currentFolder = settings.LastFolder;
        }

        WindowPositionMemory.LoadFrom(settings.WindowPositions);

        var iconSize = Enum.TryParse<IconSize>(settings.IconSizePreset, out var parsedIconSize) ? parsedIconSize : IconSize.Normal;
        IconSizeSettings.Current.Apply(iconSize);
        IconSizeComboBox.SelectedIndex = iconSize switch
        {
            IconSize.ExtraLarge => 0,
            IconSize.Large => 1,
            IconSize.Small => 3,
            _ => 2,
        };

        IconCardFieldsSettings.Current.ShowSize = settings.IconShowSize;
        IconCardFieldsSettings.Current.ShowPlayCount = settings.IconShowPlayCount;
        IconCardFieldsSettings.Current.ShowTags = settings.IconShowTags;
        IconCardFieldsSettings.Current.ShowSeries = settings.IconShowSeries;
        IconSizeFieldCheckBox.IsChecked = settings.IconShowSize;
        IconPlayCountFieldCheckBox.IsChecked = settings.IconShowPlayCount;
        IconTagsFieldCheckBox.IsChecked = settings.IconShowTags;
        IconSeriesFieldCheckBox.IsChecked = settings.IconShowSeries;
    }

    /// <summary>
    /// 창이 처음 렌더링된 뒤(레이아웃이 준비된 뒤) 시작 시 복원된 선택 항목으로 스크롤한다.
    /// 생성자 시점(<see cref="ApplySettings"/>)에는 아직 레이아웃이 없어 `ScrollIntoView`가 동작하지 않으므로 분리했다.
    /// </summary>
    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var item = GetSelectedManagedItem();
        if (item is null)
        {
            return;
        }

        if (IconViewModeRadio.IsChecked == true)
        {
            ManagedIconView.ScrollIntoView(item);
        }
        else
        {
            ManagedListView.ScrollIntoView(item);
        }
    }

    /// <summary>메뉴바 오른쪽에 현재 창 크기를 실시간으로 보여준다(2026-08-07 추가).</summary>
    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        WindowSizeText.Text = $"{(int)ActualWidth} x {(int)ActualHeight}";
    }

    // ===================== 폴더 목록 (서브 창) =====================

    /// <summary>
    /// 폴더 목록 서브 창(`FolderListWindow`)을 연다. 관리 리스트 컬렉션을 그대로 넘기므로
    /// 그 창에서 추가/재사용한 항목이 즉시 메인창에도 반영된다. 마지막 폴더는 설정에 저장된다.
    /// </summary>
    private void OpenFolderList_Click(object sender, RoutedEventArgs e)
    {
        var window = new FolderListWindow(_managedItems, _currentFolder) { Owner = this };
        window.Closed += (_, _) =>
        {
            if (!string.Equals(_currentFolder, window.LastFolder, StringComparison.OrdinalIgnoreCase))
            {
                _currentFolder = window.LastFolder;
                ScheduleSettingsAutoSave();
            }

            _managedView.Refresh();
        };
        SingleInstanceWindow<FolderListWindow>.Show(window);
    }

    // ===================== 관리 리스트: 자동 저장 =====================

    private void ManagedItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (ManagedVideoItem item in e.NewItems)
            {
                item.PropertyChanged += ManagedItem_PropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (ManagedVideoItem item in e.OldItems)
            {
                item.PropertyChanged -= ManagedItem_PropertyChanged;
            }
        }

        ScheduleAutoSave();

        // 시작 시 LoadInitialData()가 항목을 하나씩 Add할 때마다 전체를 다시 스캔하지 않도록,
        // 로딩 중(_suppressAutoSave)에는 건너뛰고 로딩이 끝난 뒤 한 번만 계산한다(생성자 마지막 호출).
        if (!_suppressAutoSave)
        {
            UpdateManagedCountDisplay();
        }
    }

    private void ManagedItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ScheduleAutoSave();

        if (!_suppressAutoSave)
        {
            UpdateManagedCountDisplay();
        }

        // "# 창" 규칙: 다른 창(배우 관리/속성 창 등)에서 지금 선택된 항목을 바꾸면, 하단 상세정보 패널도
        // 이 창을 다시 선택하지 않아도 즉시 갱신된다(예: 배우 관리 창에서 Actors를 바꾼 경우).
        if (!_suppressAutoSave && ReferenceEquals(sender, GetSelectedManagedItem()))
        {
            UpdateSelectedItemDetails();
        }

        // 태그/배우 칩은 WrapPanel이라 내용에 따라 셀 높이가 바뀌는데, 가상화된 ListView/ListBox가 이
        // 높이 변화를 곧바로 다시 그리지 않아 값은 바뀌었는데도 화면에는 예전 칩이 그대로 남아있다가
        // 창을 옮기는 등 강제 레이아웃이 있어야 드러나는 문제가 있었다(2026-08-04 수정). 라이브
        // 정렬/필터(EnableLiveShaping)는 항목의 위치만 재평가할 뿐 이미 화면에 그려진 셀을 다시 그리게
        // 보장하지는 않으므로, 해당 항목이 화면에 보이고 있을 가능성이 있는 Tags/Actors 변경 시에는
        // `_managedView.Refresh()`로 목록 컨테이너를 완전히 다시 생성해 반영한다.
        if (!_suppressAutoSave && e.PropertyName is nameof(ManagedVideoItem.Tags) or nameof(ManagedVideoItem.Actors))
        {
            _managedView.Refresh();
        }

        // 파일명이 바뀌면 정렬 순서상 위치도 바뀐다(라이브 정렬) — F2/우클릭 "이름변경"뿐 아니라 속성 창의
        // 파일명 텍스트 상자 편집 등 어느 경로로 바뀌었든, 그 항목이 지금 선택돼 있으면 바뀐 위치로 따라간다.
        // 라이브 정렬 재배치 직후 곧바로 호출하면 레이아웃이 아직 갱신되지 않아 ScrollIntoView가 조용히
        // 아무 효과가 없으므로(재현 확인함), 레이아웃이 반영된 뒤로 미룬다.
        if (!_suppressAutoSave && e.PropertyName == nameof(ManagedVideoItem.FileName) &&
            sender is ManagedVideoItem renamedItem && ReferenceEquals(sender, GetSelectedManagedItem()))
        {
            Dispatcher.BeginInvoke(new Action(() => SelectAndScrollToManagedItem(renamedItem)), DispatcherPriority.ContextIdle);
        }
    }

    /// <summary>관리 리스트 제목 옆에 전체/썸네일 있음/제거됨 개수를 표시한다.</summary>
    private void UpdateManagedCountDisplay()
    {
        var total = _managedItems.Count;
        var withThumbnail = _managedItems.Count(m => m.HasThumbnail);
        var removed = _managedItems.Count(m => !m.IsValid);
        ManagedCountText.Text = $"(전체 {total} / 썸네일 {withThumbnail} / 제거됨 {removed})";
    }

    private void MasterActors_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (ActorItem actor in e.NewItems)
            {
                actor.PropertyChanged += MasterActor_PropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (ActorItem actor in e.OldItems)
            {
                actor.PropertyChanged -= MasterActor_PropertyChanged;
            }
        }

        ScheduleActorsAutoSave();
    }

    private void MasterActor_PropertyChanged(object? sender, PropertyChangedEventArgs e) => ScheduleActorsAutoSave();

    private void MasterSeries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (SeriesItem series in e.NewItems)
            {
                series.PropertyChanged += MasterSeriesItem_PropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (SeriesItem series in e.OldItems)
            {
                series.PropertyChanged -= MasterSeriesItem_PropertyChanged;
            }
        }

        ScheduleSeriesAutoSave();
        RefreshSeriesFilterComboBox();
    }

    /// <summary>
    /// 시리즈 선택 필터 콤보박스의 항목을 "(전체)" + 마스터 목록(이름 기준 오름차순)으로 다시 채우고, 현재
    /// <see cref="_seriesFilter"/> 값을 선택 상태로 맞춘다. 시리즈가 추가/삭제될 때(<see cref="MasterSeries_CollectionChanged"/>)
    /// 와 설정을 복원할 때(<see cref="ApplySettings"/>) 모두 이 메서드로 항목/선택 상태를 동기화한다.
    /// </summary>
    private void RefreshSeriesFilterComboBox()
    {
        var items = new List<string> { AllSeriesLabel };
        items.AddRange(_masterSeries.Select(s => s.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        SeriesFilterComboBox.ItemsSource = items;
        SeriesFilterComboBox.SelectedItem = !string.IsNullOrEmpty(_seriesFilter) && items.Contains(_seriesFilter, StringComparer.OrdinalIgnoreCase)
            ? _seriesFilter
            : AllSeriesLabel;
    }

    private void MasterSeriesItem_PropertyChanged(object? sender, PropertyChangedEventArgs e) => ScheduleSeriesAutoSave();

    private void ScheduleAutoSave()
    {
        if (_suppressAutoSave)
        {
            return;
        }

        _libraryAutoSaveTimer.Stop();
        _libraryAutoSaveTimer.Start();
    }

    private void ScheduleTagsAutoSave()
    {
        if (_suppressAutoSave)
        {
            return;
        }

        _tagsAutoSaveTimer.Stop();
        _tagsAutoSaveTimer.Start();
    }

    private void ScheduleSettingsAutoSave()
    {
        if (_suppressAutoSave)
        {
            return;
        }

        _settingsAutoSaveTimer.Stop();
        _settingsAutoSaveTimer.Start();
    }

    private void ScheduleActorsAutoSave()
    {
        if (_suppressAutoSave)
        {
            return;
        }

        _actorsAutoSaveTimer.Stop();
        _actorsAutoSaveTimer.Start();
    }

    private void ScheduleSeriesAutoSave()
    {
        if (_suppressAutoSave)
        {
            return;
        }

        _seriesAutoSaveTimer.Stop();
        _seriesAutoSaveTimer.Start();
    }

    private void SaveLibrary()
    {
        try
        {
            ManagedListRepository.Save(AppPaths.LibraryPath, _managedItems);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"관리 리스트를 저장하지 못했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveTags()
    {
        try
        {
            TagRepository.Save(AppPaths.TagsPath, _masterTags);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"태그 목록을 저장하지 못했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveActors()
    {
        try
        {
            ActorRepository.Save(AppPaths.ActorsPath, _masterActors);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"배우 목록을 저장하지 못했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveSeries()
    {
        try
        {
            SeriesRepository.Save(AppPaths.SeriesPath, _masterSeries);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"시리즈 목록을 저장하지 못했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveSettings()
    {
        try
        {
            var settings = new AppSettings
            {
                IsIconView = IconViewModeRadio.IsChecked == true,
                SortProperty = _sortProperty,
                SortAscending = _sortAscending,
                NameFilter = _nameFilter,
                FolderFilter = _folderFilter,
                SeriesFilter = _seriesFilter,
                MainWindowWidth = (WindowState == WindowState.Normal ? Width : RestoreBounds.Width),
                MainWindowHeight = (WindowState == WindowState.Normal ? Height : RestoreBounds.Height),
                MainWindowLeft = (WindowState == WindowState.Normal ? Left : RestoreBounds.Left),
                MainWindowTop = (WindowState == WindowState.Normal ? Top : RestoreBounds.Top),
                SelectedTags = _selectedTags.ToList(),
                SelectedActors = _selectedActorFilter.ToList(),
                ShowRemovedItems = _showRemovedItems,
                LastFolder = _currentFolder,
                VisibleColumns = GetVisibleColumnKeys(),
                ColumnWidths = GetColumnWidths(),
                SelectedItemPath = GetSelectedManagedItem()?.FullPath,
                WindowPositions = WindowPositionMemory.ToDictionary(),
                IconSizePreset = IconSizeSettings.Current.Preset.ToString(),
                IconShowSize = IconCardFieldsSettings.Current.ShowSize,
                IconShowPlayCount = IconCardFieldsSettings.Current.ShowPlayCount,
                IconShowTags = IconCardFieldsSettings.Current.ShowTags,
                IconShowSeries = IconCardFieldsSettings.Current.ShowSeries,
            };

            SettingsRepository.Save(AppPaths.SettingsPath, settings);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"설정을 저장하지 못했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ===================== 관리 리스트: JSON 파일 관리 (열기 / 다른 이름으로 저장) =====================

    private void OpenManagedList_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "관리 리스트 JSON 파일 열기",
            Filter = "JSON 파일 (*.json)|*.json|모든 파일 (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var loaded = ManagedListRepository.Load(dialog.FileName);
            _managedItems.Clear();
            foreach (var item in loaded)
            {
                _managedItems.Add(item);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"관리 리스트를 불러올 수 없습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveAsManagedList_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "관리 리스트를 다른 이름으로 저장",
            Filter = "JSON 파일 (*.json)|*.json",
            FileName = "library.json",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            ManagedListRepository.Save(dialog.FileName, _managedItems);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"저장하지 못했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ===================== 태그 마스터 목록 관리 =====================

    private void ManageTags_Click(object sender, RoutedEventArgs e)
    {
        var window = new TagManagerWindow(_masterTags, _managedItems) { Owner = this };
        window.Closed += (_, _) => _managedView.Refresh();
        SingleInstanceWindow<TagManagerWindow>.Show(window);
    }

    // ===================== 배우 마스터 목록 관리 =====================

    private void ManageActors_Click(object sender, RoutedEventArgs e)
    {
        var window = new ActorManagerWindow(_masterActors, _masterSeries, _managedItems, _masterTags) { Owner = this };
        window.Closed += (_, _) =>
        {
            _managedView.Refresh();
            UpdateSelectedItemDetails();
        };
        SingleInstanceWindow<ActorManagerWindow>.Show(window);
    }

    /// <summary>
    /// 각 배우의 Credits(품번 목록)를 기준으로, 관리 리스트에서 그 품번과 일치하지만 아직 그 배우가
    /// `Actors`에 지정되어 있지 않은 항목에 배우를 추가한다(2026-08-10 추가, `SyncSeries_Click`과 동일한
    /// 방향의 동기화 — Credits → 관리 리스트). 배우는 시리즈와 달리 여러 명을 동시에 지정할 수 있는
    /// 목록이라, 시리즈 동기화와 달리 "충돌"이라는 개념이 없다 — 기존 배우 지정을 덮어쓰거나 지우지 않고
    /// 그냥 없는 것만 추가한다.
    /// </summary>
    private void SyncActors_Click(object sender, RoutedEventArgs e)
    {
        var byCode = _managedItems
            .GroupBy(i => Path.GetFileNameWithoutExtension(i.FileName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var updated = new List<string>();

        foreach (var actor in _masterActors)
        {
            foreach (var code in actor.Credits)
            {
                if (!byCode.TryGetValue(code, out var matches))
                {
                    continue;
                }

                foreach (var item in matches)
                {
                    if (!item.Actors.Any(a => string.Equals(a, actor.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        var updatedActors = new List<string>(item.Actors) { actor.Name };
                        item.SetActors(updatedActors);
                        updated.Add($"{item.FileName} → '{actor.Name}'");
                    }
                }
            }
        }

        var message = updated.Count == 0
            ? "동기화할 항목이 없습니다. 모든 항목이 이미 최신 상태입니다."
            : BuildSyncResultMessage(updated, new List<string>());

        MessageBox.Show(message, "배우 동기화 결과", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ===================== 시리즈 마스터 목록 관리 =====================

    private void ManageSeries_Click(object sender, RoutedEventArgs e)
    {
        var window = new SeriesManagerWindow(_masterSeries, _masterTags, _masterActors, _managedItems) { Owner = this };
        window.Closed += (_, _) =>
        {
            _managedView.Refresh();
            UpdateSelectedItemDetails();
        };
        SingleInstanceWindow<SeriesManagerWindow>.Show(window);
    }

    /// <summary>
    /// 각 시리즈의 Credits(품번 목록)를 기준으로, 관리 리스트에서 그 품번과 일치하지만 아직 `Series`가
    /// 비어있는 항목에 해당 시리즈를 채워 넣는다(2026-08-10 추가). 이미 다른 시리즈가 지정된 항목은
    /// 충돌로 간주해 건드리지 않고 별도로 보고한다 — 사용자가 직접 지정한 값을 임의로 덮어쓰지 않기 위함이다.
    /// </summary>
    private void SyncSeries_Click(object sender, RoutedEventArgs e)
    {
        var byCode = _managedItems
            .GroupBy(i => Path.GetFileNameWithoutExtension(i.FileName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var updated = new List<string>();
        var conflicts = new List<string>();

        foreach (var series in _masterSeries)
        {
            foreach (var code in series.Credits)
            {
                if (!byCode.TryGetValue(code, out var matches))
                {
                    continue;
                }

                foreach (var item in matches)
                {
                    if (string.IsNullOrEmpty(item.Series))
                    {
                        item.Series = series.Name;
                        updated.Add($"{item.FileName} → '{series.Name}'");
                    }
                    else if (!string.Equals(item.Series, series.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        conflicts.Add($"{item.FileName}: 이미 '{item.Series}' (Credits는 '{series.Name}')");
                    }
                }
            }
        }

        var message = updated.Count == 0 && conflicts.Count == 0
            ? "동기화할 항목이 없습니다. 모든 항목이 이미 최신 상태입니다."
            : BuildSyncResultMessage(updated, conflicts);

        MessageBox.Show(message, "시리즈 동기화 결과", MessageBoxButton.OK,
            conflicts.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private static string BuildSyncResultMessage(List<string> updated, List<string> conflicts)
    {
        const int maxLines = 20;
        var lines = new List<string> { $"업데이트된 항목: {updated.Count}건" };
        lines.AddRange(updated.Take(maxLines));
        if (updated.Count > maxLines)
        {
            lines.Add($"...외 {updated.Count - maxLines}건 더");
        }

        if (conflicts.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"충돌로 건너뜀(이미 다른 시리즈가 지정됨): {conflicts.Count}건");
            lines.AddRange(conflicts.Take(maxLines));
            if (conflicts.Count > maxLines)
            {
                lines.Add($"...외 {conflicts.Count - maxLines}건 더");
            }
        }

        return string.Join("\n", lines);
    }

    // ===================== 관리 리스트: 정렬 =====================

    private void ManagedListHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader { Column: not null } header)
        {
            return;
        }

        var propertyName = HeaderTextToSortProperty(header.Column.Header as string);
        if (propertyName is not null)
        {
            ApplySort(propertyName);
        }
    }

    private static string? HeaderTextToSortProperty(string? headerText) => headerText switch
    {
        "" => nameof(ManagedVideoItem.HasThumbnail),
        "파일명" => nameof(ManagedVideoItem.FileName),
        "크기" => nameof(ManagedVideoItem.SizeBytes),
        "재생횟수" => nameof(ManagedVideoItem.PlayCount),
        "태그" => nameof(ManagedVideoItem.TagsSortKey),
        "배우" => nameof(ManagedVideoItem.ActorsSortKey),
        "메모" => nameof(ManagedVideoItem.Memo),
        "폴더명" => nameof(ManagedVideoItem.FolderName),
        "코드" => nameof(ManagedVideoItem.Code),
        "시리즈" => nameof(ManagedVideoItem.Series),
        "출시일" => nameof(ManagedVideoItem.ReleaseDate),
        _ => null,
    };

    private void ApplySort(string propertyName)
    {
        var ascending = _sortProperty == propertyName ? !_sortAscending : true;
        SetSort(propertyName, ascending);
    }

    private void SetSort(string propertyName, bool ascending)
    {
        _sortProperty = propertyName;
        _sortAscending = ascending;

        var listView = (ListCollectionView)_managedView;

        // 파일명은 문자와 숫자가 섞여있어(예: "abp-2"/"abp-10") 기본 사전식 비교로는 "abp-10"이
        // "abp-2"보다 앞에 오는 문제가 있다. SortDescriptions(기본 비교)와 CustomSort(커스텀 비교)는
        // ListCollectionView에서 동시에 쓸 수 없어(먼저 비운 뒤 다른 쪽을 설정해야 한다), 파일명일 때만
        // CustomSort로 전환하고 나머지 컬럼은 기존처럼 SortDescriptions를 그대로 쓴다.
        if (propertyName == nameof(ManagedVideoItem.FileName))
        {
            listView.SortDescriptions.Clear();
            listView.CustomSort = ascending ? FileNameNaturalComparer.Ascending : FileNameNaturalComparer.Descending;
        }
        else
        {
            listView.CustomSort = null;
            listView.SortDescriptions.Clear();
            listView.SortDescriptions.Add(new SortDescription(
                propertyName, ascending ? ListSortDirection.Ascending : ListSortDirection.Descending));
        }

        ScheduleSettingsAutoSave();
    }

    // ===================== 관리 리스트: 검색(필터) - 리스트 위 상시 UI =====================

    private void NameFilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _nameFilter = string.IsNullOrWhiteSpace(NameFilterTextBox.Text) ? null : NameFilterTextBox.Text.Trim();
        _managedView.Refresh();
        ScheduleSettingsAutoSave();
    }

    private void FolderFilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _folderFilter = string.IsNullOrWhiteSpace(FolderFilterTextBox.Text) ? null : FolderFilterTextBox.Text.Trim();
        _managedView.Refresh();
        ScheduleSettingsAutoSave();
    }

    private void TagFilterButton_Click(object sender, RoutedEventArgs e) => ShowTagFilterPopup((UIElement)sender);

    private void SeriesFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        var selected = SeriesFilterComboBox.SelectedItem as string;
        _seriesFilter = selected is null || selected == AllSeriesLabel ? null : selected;
        _managedView.Refresh();
        ScheduleSettingsAutoSave();
    }

    private void ClearAllFilters_Click(object sender, RoutedEventArgs e)
    {
        _nameFilter = null;
        NameFilterTextBox.Text = string.Empty;
        _folderFilter = null;
        FolderFilterTextBox.Text = string.Empty;
        _seriesFilter = null;
        SeriesFilterComboBox.SelectedItem = AllSeriesLabel;
        _selectedTags.Clear();
        _selectedActorFilter.Clear();
        _managedView.Refresh();
        ScheduleSettingsAutoSave();
    }

    // ===================== 관리 리스트: 헤더 우클릭 - 표시할 컬럼 선택 =====================

    private void ManagedListHeader_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<GridViewColumnHeader>(e.OriginalSource as DependencyObject) is not { Column: not null } header)
        {
            return;
        }

        ColumnChooserPopup.PlacementTarget = header;
        ColumnChooserPopup.IsOpen = true;
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    // ===================== 관리 리스트: 아이콘 보기 빈 공간 우클릭 - 표시할 정보 선택 =====================

    private void ManagedIconView_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        IconInfoChooserPopup.IsOpen = true;
        e.Handled = true;
    }

    private void IconFieldToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: string fieldKey } checkBox)
        {
            return;
        }

        var isChecked = checkBox.IsChecked == true;
        switch (fieldKey)
        {
            case "Size":
                IconCardFieldsSettings.Current.ShowSize = isChecked;
                break;
            case "PlayCount":
                IconCardFieldsSettings.Current.ShowPlayCount = isChecked;
                break;
            case "Tags":
                IconCardFieldsSettings.Current.ShowTags = isChecked;
                break;
            case "Series":
                IconCardFieldsSettings.Current.ShowSeries = isChecked;
                break;
        }
    }

    private void ShowTagFilterPopup(UIElement target)
    {
        _tagCheckItems = _masterTags
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Select(t => new TagCheckItem { Tag = t, IsSelected = _selectedTags.Contains(t) })
            .ToList();

        TagFilterList.ItemsSource = _tagCheckItems;
        TagFilterPopup.PlacementTarget = target;
        TagFilterPopup.IsOpen = true;
    }

    private void TagFilterApply_Click(object sender, RoutedEventArgs e)
    {
        _selectedTags = _tagCheckItems
            .Where(t => t.IsSelected)
            .Select(t => t.Tag)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _managedView.Refresh();
        TagFilterPopup.IsOpen = false;
        ScheduleSettingsAutoSave();
    }

    private void TagFilterClear_Click(object sender, RoutedEventArgs e)
    {
        _selectedTags.Clear();
        _managedView.Refresh();
        TagFilterPopup.IsOpen = false;
        ScheduleSettingsAutoSave();
    }

    private void ActorFilterButton_Click(object sender, RoutedEventArgs e) => ShowActorFilterPopup((UIElement)sender);

    private void ShowActorFilterPopup(UIElement target)
    {
        _actorFilterCheckItems = _masterActors
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(a => new ActorFilterCheckItem { ActorName = a.Name, IsSelected = _selectedActorFilter.Contains(a.Name) })
            .ToList();

        ActorFilterList.ItemsSource = _actorFilterCheckItems;
        ActorFilterPopup.PlacementTarget = target;
        ActorFilterPopup.IsOpen = true;
    }

    private void ActorFilterApply_Click(object sender, RoutedEventArgs e)
    {
        _selectedActorFilter = _actorFilterCheckItems
            .Where(a => a.IsSelected)
            .Select(a => a.ActorName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _managedView.Refresh();
        ActorFilterPopup.IsOpen = false;
        ScheduleSettingsAutoSave();
    }

    private void ActorFilterClear_Click(object sender, RoutedEventArgs e)
    {
        _selectedActorFilter.Clear();
        _managedView.Refresh();
        ActorFilterPopup.IsOpen = false;
        ScheduleSettingsAutoSave();
    }

    private void ShowRemovedItemsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        _showRemovedItems = ShowRemovedItemsCheckBox.IsChecked == true;
        _managedView.Refresh();
        ScheduleSettingsAutoSave();
    }

    private bool FilterManagedItem(object obj)
    {
        if (obj is not ManagedVideoItem item)
        {
            return false;
        }

        if (!item.IsValid && !_showRemovedItems)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(_nameFilter) &&
            !item.FileName.Contains(_nameFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(_folderFilter) &&
            !item.FolderName.Contains(_folderFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_selectedTags.Count > 0 && !item.Tags.Any(t => _selectedTags.Contains(t)))
        {
            return false;
        }

        if (_selectedActorFilter.Count > 0 && !item.Actors.Any(a => _selectedActorFilter.Contains(a)))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(_seriesFilter) && !string.Equals(item.Series, _seriesFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    // ===================== 관리 리스트: 보기 모드 =====================

    private void ViewMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        var iconMode = IconViewModeRadio.IsChecked == true;
        ManagedListView.Visibility = iconMode ? Visibility.Collapsed : Visibility.Visible;
        ManagedIconView.Visibility = iconMode ? Visibility.Visible : Visibility.Collapsed;

        ListViewModeMenuItem.IsChecked = !iconMode;
        IconViewModeMenuItem.IsChecked = iconMode;

        UpdateSelectedItemDetails();
        ScheduleSettingsAutoSave();
    }

    private void IconSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        var size = IconSizeComboBox.SelectedIndex switch
        {
            0 => IconSize.ExtraLarge,
            1 => IconSize.Large,
            3 => IconSize.Small,
            _ => IconSize.Normal,
        };

        IconSizeSettings.Current.Apply(size);
    }

    private void ListViewModeMenuItem_Click(object sender, RoutedEventArgs e) => ListViewModeRadio.IsChecked = true;

    private void IconViewModeMenuItem_Click(object sender, RoutedEventArgs e) => IconViewModeRadio.IsChecked = true;

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private ManagedVideoItem? GetSelectedManagedItem() =>
        (IconViewModeRadio.IsChecked == true ? ManagedIconView.SelectedItem : ManagedListView.SelectedItem) as ManagedVideoItem;

    /// <summary>복구/제거/완전삭제처럼 여러 항목에 동시에 적용 가능한 동작을 위한 다중 선택 목록.</summary>
    private List<ManagedVideoItem> GetSelectedManagedItems()
    {
        var selectedItems = IconViewModeRadio.IsChecked == true ? ManagedIconView.SelectedItems : ManagedListView.SelectedItems;
        return selectedItems.OfType<ManagedVideoItem>().ToList();
    }

    // ===================== 관리 리스트: 드래그 앤 드롭으로 추가 =====================

    /// <summary>탐색기에서 끌어온 파일 중 동영상 확장자가 하나라도 있으면 드롭을 허용한다.</summary>
    private void ManagedList_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) &&
                    ((string[])e.Data.GetData(DataFormats.FileDrop)!).Any(IsVideoFile)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>탐색기에서 관리 리스트 영역으로 동영상 파일을 끌어다 놓으면 바로 추가한다
    /// (폴더 목록을 거치지 않는 지름길 — 재사용 여부 확인 등은 <see cref="ManagedListImporter"/>를 공유한다).</summary>
    private void ManagedList_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        var files = ((string[])e.Data.GetData(DataFormats.FileDrop)!)
            .Where(IsVideoFile)
            .Select(p => new VideoFileItem(new FileInfo(p)))
            .ToList();

        if (files.Count == 0)
        {
            MessageBox.Show("동영상 파일을 드래그해 주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ManagedListImporter.AddFiles(_managedItems, files);
        e.Handled = true;
    }

    private static bool IsVideoFile(string path) =>
        File.Exists(path) && ManagedListImporter.VideoExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    // ===================== 관리 리스트: 선택 항목 상세 정보 =====================

    private void ManagedSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectedItemDetails();

        // "# 관리 리스트" 규칙: 목록에서 다른 파일을 클릭하면, 열려 있는 속성 창이 있을 때 그 창도 따라간다.
        var item = GetSelectedManagedItem();
        if (item is not null)
        {
            SingleInstanceWindow<PropertiesWindow>.Current?.SwitchToItem(item);
        }
    }

    private void UpdateSelectedItemDetails()
    {
        var item = GetSelectedManagedItem();
        SelectedItemDetailsPanel.DataContext = item;
        SelectedItemDetailsPanel.Visibility = item is null ? Visibility.Collapsed : Visibility.Visible;
        NoSelectionHint.Visibility = item is null ? Visibility.Visible : Visibility.Collapsed;

        ThumbnailPanel.DataContext = item;

        SelectedItemActorsPanel.ItemsSource = item is null
            ? null
            : item.Actors
                .Select(name => _masterActors.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
                .OfType<ActorItem>()
                .ToList();
    }

    // ===================== 관리 리스트: 썸네일 =====================

    /// <summary>메인창 썸네일 뷰어 클릭 시 원본 이미지 창을 연다.</summary>
    private void ThumbnailViewer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        OriginalImageWindow.ShowFor(this, GetSelectedManagedItem());

    private void ThumbnailViewer_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropImageHelper.CanAccept(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void ThumbnailViewer_Drop(object sender, DragEventArgs e)
    {
        var item = GetSelectedManagedItem();
        if (item is null)
        {
            return;
        }

        var imagePath = DragDropImageHelper.TryGetImagePath(e.Data);
        if (imagePath is not null)
        {
            ApplyThumbnail(item, imagePath);
        }
        else
        {
            ShowDropFailedMessage(e.Data);
        }
    }

    private static void ShowDropFailedMessage(IDataObject data)
    {
        MessageBox.Show(
            "드롭한 항목에서 이미지를 찾지 못했습니다.\n" +
            $"제공된 데이터 형식: {string.Join(", ", data.GetFormats())}",
            "썸네일 지정 실패",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static void ApplyThumbnail(ManagedVideoItem item, string sourceImagePath)
    {
        try
        {
            var result = item.IsPlaceholder
                ? ThumbnailHelper.CreatePlaceholderThumbnail(sourceImagePath, item.FileName)
                : ThumbnailHelper.CreateThumbnail(sourceImagePath, item.FullPath);
            item.ThumbnailPath = result.ThumbnailPath;
            item.ThumbnailOriginalPath = result.OriginalPath;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"썸네일을 만들 수 없습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ===================== 관리 리스트: 우클릭 팝업 메뉴 =====================

    /// <summary>
    /// 우클릭한 항목이 이미 선택된 다중 선택의 일부이면 선택을 그대로 유지하고(복구/제거/완전삭제를 여러 항목에 적용하기 위함),
    /// 선택되지 않은 항목을 우클릭하면 그 항목 하나만 선택한다(탐색기와 동일한 동작).
    /// </summary>
    private void ManagedListItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListViewItem { DataContext: ManagedVideoItem item } && !ManagedListView.SelectedItems.Contains(item))
        {
            ManagedListView.SelectedItem = item;
        }
    }

    private void ManagedIconItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: ManagedVideoItem item } && !ManagedIconView.SelectedItems.Contains(item))
        {
            ManagedIconView.SelectedItem = item;
        }
    }

    private void RenameManaged_Click(object sender, RoutedEventArgs e)
    {
        var item = GetSelectedManagedItem();
        if (item is null)
        {
            MessageBox.Show("이름을 변경할 항목을 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 바뀐 위치로 스크롤해서 따라가는 것은 ManagedItem_PropertyChanged(FileName 변경 감지)가 처리한다 —
        // F2/우클릭 이 경로든 속성 창의 파일명 편집이든 공유하는 동일한 로직이다.
        RenameHelper.TryRenameManagedItem(this, item, _masterActors, _masterSeries);
    }

    // ===================== 관리 리스트: 속성 / 재생 / 제거 =====================

    private void PropertiesManaged_Click(object sender, RoutedEventArgs e)
    {
        var item = GetSelectedManagedItem();
        if (item is null)
        {
            MessageBox.Show("속성을 볼 항목을 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenPropertiesWindow(item);
    }

    private void TagsCell_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ManagedVideoItem item })
        {
            OpenPropertiesWindow(item);
        }

        e.Handled = true;
    }

    /// <summary>"속성" 버튼 왼쪽의 "파일 없이 추가" 버튼 — 실제 파일 없이 관리 리스트 항목을 미리 만들어두고
    /// (아직 구하지 못한 작품 등), 속성 창에서 파일 정보를 입력받는다. 파일명을 입력하지 않고 닫으면
    /// (`PropertiesWindow`의 새 항목 커밋 로직이) 조용히 추가를 취소한다 — [관리 리스트] "파일 없이 추가" 참고.</summary>
    private void AddPlaceholderItem_Click(object sender, RoutedEventArgs e)
    {
        var newItem = new ManagedVideoItem
        {
            ModifiedDate = DateTime.Now,
            IsPlaceholder = true,
            IsExist = false,
        };

        OpenPropertiesWindow(newItem, isNewItem: true);
    }

    private void OpenPropertiesWindow(ManagedVideoItem item, bool isNewItem = false)
    {
        var dialog = new PropertiesWindow(item, _masterTags, _masterActors, _masterSeries, _managedItems, isNewItem) { Owner = this };
        dialog.Closed += (_, _) =>
        {
            // 관리 리스트 선택이 바뀌면 이 창이 다른 항목으로 전환될 수 있으므로(SwitchToItem), 완전 삭제
            // 대상은 창을 처음 열 때의 item이 아니라 닫히는 시점에 실제로 보여주고 있던 CurrentItem이어야 한다.
            if (dialog.PermanentlyDeleted)
            {
                _managedItems.Remove(dialog.CurrentItem);
            }

            _managedView.Refresh();
            UpdateSelectedItemDetails();

            // "파일 없이 추가"로 새로 만든 항목은 (제거된 항목으로 바로 분류되고) 정렬 순서상 화면 밖에
            // 있을 수 있어, 추가되자마자 선택하고 스크롤해서 바로 보이게 한다 — 그렇지 않으면 실제로는
            // 추가됐는데도(전체 개수는 늘었는데) 스크롤을 해서 찾아야 해서 "목록에 안 보인다"고 오인하기 쉽다.
            if (isNewItem && _managedItems.Contains(dialog.CurrentItem))
            {
                SelectAndScrollToManagedItem(dialog.CurrentItem);
            }
        };
        SingleInstanceWindow<PropertiesWindow>.Show(dialog);
    }

    private void SelectAndScrollToManagedItem(ManagedVideoItem item)
    {
        if (IconViewModeRadio.IsChecked == true)
        {
            ManagedIconView.SelectedItem = item;
            ManagedIconView.ScrollIntoView(item);
        }
        else
        {
            ManagedListView.SelectedItem = item;
            ManagedListView.ScrollIntoView(item);
        }
    }

    private void PlayManaged_Click(object sender, RoutedEventArgs e) => PlaySelectedManagedItem();

    /// <summary>아이콘 보기에서는 파일명 텍스트를 더블클릭했을 때만 재생된다.</summary>
    private void FileNameCell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        if (sender is FrameworkElement { DataContext: ManagedVideoItem item })
        {
            PlayManagedItem(item);
        }
    }

    /// <summary>리스트 보기에서는 행(row)의 어느 영역을 더블클릭해도 재생된다.</summary>
    private void ManagedListRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListViewItem { DataContext: ManagedVideoItem item })
        {
            PlayManagedItem(item);
        }
    }

    private void PlaySelectedManagedItem()
    {
        var item = GetSelectedManagedItem();
        if (item is null)
        {
            MessageBox.Show("재생할 항목을 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        PlayManagedItem(item);
    }

    private void PlayManagedItem(ManagedVideoItem item)
    {
        if (item.IsPlaceholder)
        {
            MessageBox.Show("실제 파일이 없는 항목입니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
            item.PlayCount++;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 열 수 없습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RemoveFromManagedList_Click(object sender, RoutedEventArgs e)
    {
        var items = GetSelectedManagedItems();
        if (items.Count == 0)
        {
            MessageBox.Show("제거할 항목을 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var question = items.Count == 1
            ? $"'{items[0].FileName}' 항목을 관리 리스트에서 제거하시겠습니까?\n"
            : $"선택한 {items.Count}개 항목을 관리 리스트에서 제거하시겠습니까?\n";

        var result = MessageBox.Show(
            question +
            "(실제 동영상 파일은 삭제되지 않습니다. 재생횟수/태그 등 데이터는 관리 리스트에 그대로 보관되며, " +
            "'제거된 항목도 표시' 체크박스로 다시 볼 수 있고, 나중에 같은 파일명으로 다시 추가하면 재사용할 수 있습니다.)",
            "제거 확인",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            foreach (var item in items)
            {
                item.IsValid = false;
            }
            _managedView.Refresh();
        }
    }

    private void RestoreManaged_Click(object sender, RoutedEventArgs e)
    {
        var items = GetSelectedManagedItems();
        if (items.Count == 0)
        {
            MessageBox.Show("복구할 항목을 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var removedItems = items.Where(i => !i.IsValid).ToList();
        if (removedItems.Count == 0)
        {
            MessageBox.Show("이미 활성 상태인 항목입니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (var item in removedItems)
        {
            item.IsValid = true;
        }
        _managedView.Refresh();
    }

    /// <summary>
    /// 선택한 항목(들)의 관리 데이터를 완전히 삭제한다 (`library.json`에 다시 저장되지 않음).
    /// 실제 동영상 파일은 삭제되지 않는다.
    /// </summary>
    private void PermanentlyDeleteManaged_Click(object sender, RoutedEventArgs e)
    {
        var items = GetSelectedManagedItems();
        if (items.Count == 0)
        {
            MessageBox.Show("완전 삭제할 항목을 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var question = items.Count == 1
            ? $"'{items[0].FileName}' 항목의 관리 데이터를 완전히 삭제하시겠습니까?\n"
            : $"선택한 {items.Count}개 항목의 관리 데이터를 완전히 삭제하시겠습니까?\n";

        var result = MessageBox.Show(
            question +
            "재생횟수/태그/배우/메모/썸네일 등 모든 데이터가 사라지며 복구할 수 없습니다.\n" +
            "(실제 동영상 파일은 삭제되지 않습니다.)",
            "완전 삭제 확인",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            foreach (var item in items)
            {
                _managedItems.Remove(item);
            }
            _managedView.Refresh();
            UpdateSelectedItemDetails();
        }
    }
}
