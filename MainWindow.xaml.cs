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
    private readonly ICollectionView _managedView;

    private readonly DispatcherTimer _libraryAutoSaveTimer;
    private readonly DispatcherTimer _tagsAutoSaveTimer;
    private readonly DispatcherTimer _settingsAutoSaveTimer;
    private readonly DispatcherTimer _actorsAutoSaveTimer;

    private string? _nameFilter;
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

    public MainWindow()
    {
        InitializeComponent();

        AppPaths.EnsureAppDataDirectory();
        LibraryPathText.Text = $"자동 저장 위치: {AppPaths.LibraryPath}";

        BuildManagedColumns();
        ApplyVisibleColumns(DefaultVisibleColumns);

        _managedView = CollectionViewSource.GetDefaultView(_managedItems);
        _managedView.Filter = FilterManagedItem;
        ManagedListView.ItemsSource = _managedView;
        ManagedIconView.ItemsSource = _managedView;

        ManagedListView.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(ManagedListHeader_Click));
        ManagedListView.AddHandler(UIElement.PreviewMouseRightButtonUpEvent, new MouseButtonEventHandler(ManagedListHeader_RightClick), true);

        _managedItems.CollectionChanged += ManagedItems_CollectionChanged;
        _masterTags.CollectionChanged += (_, _) => ScheduleTagsAutoSave();
        _masterActors.CollectionChanged += MasterActors_CollectionChanged;

        _libraryAutoSaveTimer = new DispatcherTimer { Interval = AutoSaveDelay };
        _libraryAutoSaveTimer.Tick += (_, _) => { _libraryAutoSaveTimer.Stop(); SaveLibrary(); };

        _tagsAutoSaveTimer = new DispatcherTimer { Interval = AutoSaveDelay };
        _tagsAutoSaveTimer.Tick += (_, _) => { _tagsAutoSaveTimer.Stop(); SaveTags(); };

        _settingsAutoSaveTimer = new DispatcherTimer { Interval = AutoSaveDelay };
        _settingsAutoSaveTimer.Tick += (_, _) => { _settingsAutoSaveTimer.Stop(); SaveSettings(); };

        _actorsAutoSaveTimer = new DispatcherTimer { Interval = AutoSaveDelay };
        _actorsAutoSaveTimer.Tick += (_, _) => { _actorsAutoSaveTimer.Stop(); SaveActors(); };

        LoadInitialData();
        UpdateManagedCountDisplay();
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

        ThumbnailColumnCheckBox.IsChecked = desired.Contains("Thumbnail");
        SizeColumnCheckBox.IsChecked = desired.Contains("Size");
        PlayCountColumnCheckBox.IsChecked = desired.Contains("PlayCount");
        TagsColumnCheckBox.IsChecked = desired.Contains("Tags");
        ActorColumnCheckBox.IsChecked = desired.Contains("Actor");
        MemoColumnCheckBox.IsChecked = desired.Contains("Memo");
        FolderColumnCheckBox.IsChecked = desired.Contains("Folder");
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
            if (File.Exists(AppPaths.LibraryPath))
            {
                foreach (var item in ManagedListRepository.Load(AppPaths.LibraryPath))
                {
                    _managedItems.Add(item);
                }
            }

            if (File.Exists(AppPaths.RemovedLibraryPath))
            {
                foreach (var item in ManagedListRepository.Load(AppPaths.RemovedLibraryPath))
                {
                    _managedItems.Add(item);
                }
            }

            if (File.Exists(AppPaths.TagsPath))
            {
                foreach (var tag in TagRepository.Load(AppPaths.TagsPath))
                {
                    _masterTags.Add(tag);
                }
            }

            if (File.Exists(AppPaths.ActorsPath))
            {
                foreach (var actor in ActorRepository.Load(AppPaths.ActorsPath))
                {
                    _masterActors.Add(actor);
                }
            }

            if (File.Exists(AppPaths.SettingsPath))
            {
                ApplySettings(SettingsRepository.Load(AppPaths.SettingsPath));
            }

            ReconcileMissingFiles();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"저장된 데이터를 불러오는 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _suppressAutoSave = false;
        }
    }

    /// <summary>파일이 더 이상 그 자리에 없는(이동/삭제된) 활성 항목을 찾아 보관 상태로 전환한다 (데이터는 유지).</summary>
    private void ReconcileMissingFiles()
    {
        var missing = _managedItems.Where(m => !m.IsArchived && !File.Exists(m.FullPath)).ToList();

        foreach (var item in missing)
        {
            item.IsArchived = true;
        }

        if (missing.Count > 0)
        {
            _managedView.Refresh();
        }
    }

    private void ApplySettings(AppSettings settings)
    {
        IconViewModeRadio.IsChecked = settings.IsIconView;
        ListViewModeRadio.IsChecked = !settings.IsIconView;

        if (settings.SortProperty is not null)
        {
            SetSort(settings.SortProperty, settings.SortAscending);
        }

        _nameFilter = settings.NameFilter;
        NameFilterTextBox.Text = _nameFilter ?? string.Empty;
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

    // ===================== 폴더 목록 (서브 창) =====================

    /// <summary>
    /// 폴더 목록 서브 창(`FolderListWindow`)을 연다. 관리 리스트 컬렉션을 그대로 넘기므로
    /// 그 창에서 추가/재사용한 항목이 즉시 메인창에도 반영된다. 마지막 폴더는 설정에 저장된다.
    /// </summary>
    private void OpenFolderList_Click(object sender, RoutedEventArgs e)
    {
        var window = new FolderListWindow(_managedItems, _currentFolder) { Owner = this };
        window.ShowDialog();

        if (!string.Equals(_currentFolder, window.LastFolder, StringComparison.OrdinalIgnoreCase))
        {
            _currentFolder = window.LastFolder;
            ScheduleSettingsAutoSave();
        }

        _managedView.Refresh();
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
        UpdateManagedCountDisplay();
    }

    private void ManagedItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ScheduleAutoSave();
        UpdateManagedCountDisplay();
    }

    /// <summary>관리 리스트 제목 옆에 전체/썸네일 있음/제거됨 개수를 표시한다.</summary>
    private void UpdateManagedCountDisplay()
    {
        var total = _managedItems.Count;
        var withThumbnail = _managedItems.Count(m => m.HasThumbnail);
        var removed = _managedItems.Count(m => m.IsArchived);
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

    private void SaveLibrary()
    {
        try
        {
            ManagedListRepository.Save(AppPaths.LibraryPath, _managedItems.Where(m => !m.IsArchived));
            ManagedListRepository.Save(AppPaths.RemovedLibraryPath, _managedItems.Where(m => m.IsArchived));
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
                SelectedTags = _selectedTags.ToList(),
                SelectedActors = _selectedActorFilter.ToList(),
                ShowRemovedItems = _showRemovedItems,
                LastFolder = _currentFolder,
                VisibleColumns = GetVisibleColumnKeys(),
                ColumnWidths = GetColumnWidths(),
                SelectedItemPath = GetSelectedManagedItem()?.FullPath,
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
        window.ShowDialog();
        _managedView.Refresh();
    }

    // ===================== 배우 마스터 목록 관리 =====================

    private void ManageActors_Click(object sender, RoutedEventArgs e)
    {
        var window = new ActorManagerWindow(_masterActors, _managedItems) { Owner = this };
        window.ShowDialog();
        _managedView.Refresh();
        UpdateSelectedItemDetails();
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

        _managedView.SortDescriptions.Clear();
        _managedView.SortDescriptions.Add(new SortDescription(
            propertyName, ascending ? ListSortDirection.Ascending : ListSortDirection.Descending));

        ScheduleSettingsAutoSave();
    }

    // ===================== 관리 리스트: 검색(필터) - 리스트 위 상시 UI =====================

    private void NameFilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _nameFilter = string.IsNullOrWhiteSpace(NameFilterTextBox.Text) ? null : NameFilterTextBox.Text.Trim();
        _managedView.Refresh();
        ScheduleSettingsAutoSave();
    }

    private void TagFilterButton_Click(object sender, RoutedEventArgs e) => ShowTagFilterPopup((UIElement)sender);

    private void ClearAllFilters_Click(object sender, RoutedEventArgs e)
    {
        _nameFilter = null;
        NameFilterTextBox.Text = string.Empty;
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

        if (item.IsArchived && !_showRemovedItems)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(_nameFilter) &&
            !item.FileName.Contains(_nameFilter, StringComparison.OrdinalIgnoreCase))
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

    // ===================== 관리 리스트: 선택 항목 상세 정보 =====================

    private void ManagedSelection_Changed(object sender, SelectionChangedEventArgs e) => UpdateSelectedItemDetails();

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

    private void ThumbnailArea_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ManagedVideoItem item })
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "썸네일 이미지 선택",
            Filter = "이미지 파일 (*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif|모든 파일 (*.*)|*.*",
        };

        if (dialog.ShowDialog() == true)
        {
            ApplyThumbnail(item, dialog.FileName);
        }

        e.Handled = true;
    }

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
            var result = ThumbnailHelper.CreateThumbnail(sourceImagePath, item.FullPath);
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

        RenameHelper.TryRenameManagedItem(this, item);
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

    private void OpenPropertiesWindow(ManagedVideoItem item)
    {
        var dialog = new PropertiesWindow(item, _masterTags, _masterActors) { Owner = this };
        var confirmed = dialog.ShowDialog();

        if (dialog.PermanentlyDeleted)
        {
            _managedItems.Remove(item);
            _managedView.Refresh();
            UpdateSelectedItemDetails();
            return;
        }

        if (confirmed == true)
        {
            _managedView.Refresh();
            UpdateSelectedItemDetails();
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
            "(실제 동영상 파일은 삭제되지 않습니다. 재생횟수/태그 등 데이터는 별도 파일(removed.json)에 보관되며, " +
            "'제거된 항목도 표시' 체크박스로 다시 볼 수 있고, 나중에 같은 파일명으로 다시 추가하면 재사용할 수 있습니다.)",
            "제거 확인",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            foreach (var item in items)
            {
                item.IsArchived = true;
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

        var archivedItems = items.Where(i => i.IsArchived).ToList();
        if (archivedItems.Count == 0)
        {
            MessageBox.Show("이미 활성 상태인 항목입니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (var item in archivedItems)
        {
            item.IsArchived = false;
        }
        _managedView.Refresh();
    }

    /// <summary>
    /// 선택한 항목(들)의 관리 데이터를 완전히 삭제한다 (`library.json`/`removed.json` 어디에도 남지 않음).
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
