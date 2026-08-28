using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace VideoVault;

/// <summary>
/// 태그 마스터 목록을 추가/이름변경/삭제하고, 태그별 품번(Credits)을 관리하는 창.
/// 이름 변경/삭제 시 관리 리스트 항목의 Tags에도 반영되어 참조 무결성을 유지한다.
/// 품번(Credits) 패널은 배우/시리즈 관리 창과 완전히 동일한 로직/스타일을 쓴다(2026-08-27 추가, 사용자 요청) —
/// 다만 태그는 배우처럼 한 파일에 여러 개가 동시에 붙을 수 있는 다대다 관계라, 시리즈처럼 "다른 태그에서
/// 이 품번을 뺏어온다"는 개념은 없다(배우 관리 창과 같은 원칙).
/// </summary>
public partial class TagManagerWindow : Window
{
    private readonly ObservableCollection<TagItem> _masterTags;
    private readonly ObservableCollection<ActorItem> _masterActors;
    private readonly ObservableCollection<SeriesItem> _masterSeries;
    private readonly ObservableCollection<ManagedVideoItem> _managedItems;
    private readonly ICollectionView _tagsView;

    public TagManagerWindow(ObservableCollection<TagItem> masterTags, ObservableCollection<ActorItem> masterActors, ObservableCollection<SeriesItem> masterSeries, ObservableCollection<ManagedVideoItem> managedItems)
    {
        InitializeComponent();
        WindowSnapHelper.Attach(this);
        _masterTags = masterTags;
        _masterActors = masterActors;
        _masterSeries = masterSeries;
        _managedItems = managedItems;

        _tagsView = CollectionViewSource.GetDefaultView(_masterTags);
        _tagsView.SortDescriptions.Add(new SortDescription(nameof(TagItem.Name), ListSortDirection.Ascending));
        TagsListBox.ItemsSource = _tagsView;

        RefreshCreditsPanel(SelectedTag);

        // "# 창" 규칙(모든 창은 데이터가 바뀌면 동시에 갱신된다): 이 창을 열어둔 채로 다른 창(속성 창 등)에서
        // 관리 리스트 항목의 Tags가 바뀌면(예: 속성 창에서 태그 체크박스 변경) 품번 패널도 즉시 따라가도록
        // 한다 — 배우/시리즈 관리 창과 동일한 패턴으로 모든 항목의 PropertyChanged를 구독한다.
        foreach (var item in _managedItems)
        {
            item.PropertyChanged += ManagedItem_PropertyChanged;
        }

        _managedItems.CollectionChanged += ManagedItems_CollectionChanged;
        Closed += (_, _) =>
        {
            _managedItems.CollectionChanged -= ManagedItems_CollectionChanged;
            foreach (var item in _managedItems)
            {
                item.PropertyChanged -= ManagedItem_PropertyChanged;
            }

            if (_subscribedCreditsTag is not null)
            {
                _subscribedCreditsTag.PropertyChanged -= SelectedTagPropertyChanged;
            }
        };
    }

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
    }

    private void ManagedItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ManagedVideoItem.Tags))
        {
            RefreshCreditsPanel(SelectedTag);
        }
    }

    /// <summary>
    /// 지금 선택된 태그의 `Credits`가 구독 대상이다 — <see cref="RefreshCreditsPanel"/>이 선택이 바뀔 때마다
    /// 이 필드를 갱신하며 옛 태그 구독을 해제하고 새 태그를 구독한다.
    /// </summary>
    private TagItem? _subscribedCreditsTag;

    private void SelectedTagPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TagItem.Credits))
        {
            RefreshCreditsPanel(SelectedTag);
        }
    }

    private TagItem? SelectedTag => TagsListBox.SelectedItem as TagItem;

    /// <summary>상태바 메시지의 종류(2026-08-29 추가, 사용자 요청 — "적당히 이쁘게 꾸며줘") — 종류별로
    /// <see cref="SetStatus"/>가 아이콘과 글자색을 다르게 지정해서 한눈에 구분되게 한다.</summary>
    private enum StatusType { Success, Warning, Info, Error }

    /// <summary>단순 안내/오류/성공 메시지를 대화상자 대신 하단 상태바에 표시한다(2026-08-29 추가, 사용자 요청) —
    /// 실제 사용자 결정이 필요한 Yes/No 확인은 여전히 MessageBox.Show를 그대로 쓴다.</summary>
    private void SetStatus(string message, StatusType type)
    {
        StatusText.Text = message;
        (StatusIcon.Text, var color) = type switch
        {
            StatusType.Success => ("✓", "#2E7D32"),
            StatusType.Warning => ("⚠", "#B26A00"),
            StatusType.Error => ("✕", "#C62828"),
            _ => ("ℹ", "#1565C0"),
        };
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        StatusIcon.Foreground = brush;
        StatusText.Foreground = brush;
    }

    private class CreditChip
    {
        public string Code { get; set; } = string.Empty;

        /// <summary>이 품번의 파일이 현재 관리 리스트(활성/제거됨 모두)에 실제로 있으면 true → 진한 색으로 표시.</summary>
        public bool HasFile { get; set; }
    }

    private void TagsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshCreditsPanel(SelectedTag);

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var name = NormalizeTagName(TagBox.Text);
        if (name is null)
        {
            SetStatus("태그 이름을 입력하세요.", StatusType.Warning);
            return;
        }

        if (_masterTags.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus("이미 존재하는 태그입니다.", StatusType.Warning);
            return;
        }

        var newTag = new TagItem { Name = name };
        _masterTags.Add(newTag);
        TagBox.Clear();

        TagsListBox.SelectedItem = newTag;
        TagsListBox.ScrollIntoView(newTag);
        SetStatus($"'{name}' 태그를 추가했습니다.", StatusType.Success);
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        var tag = SelectedTag;
        if (tag is null)
        {
            SetStatus("이름을 변경할 태그를 선택하세요.", StatusType.Warning);
            return;
        }

        var newName = NormalizeTagName(TagBox.Text);
        if (newName is null)
        {
            SetStatus("새 태그 이름을 입력하세요.", StatusType.Warning);
            return;
        }

        var oldName = tag.Name;
        if (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase) &&
            _masterTags.Any(t => string.Equals(t.Name, newName, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus("이미 존재하는 태그 이름입니다.", StatusType.Warning);
            return;
        }

        tag.Name = newName;

        foreach (var item in _managedItems)
        {
            var tagIndex = item.Tags.FindIndex(t => string.Equals(t, oldName, StringComparison.OrdinalIgnoreCase));
            if (tagIndex >= 0)
            {
                var updated = new List<string>(item.Tags) { [tagIndex] = newName };
                item.SetTags(updated);
            }
        }

        TagBox.Clear();
        _tagsView.Refresh();
        SetStatus($"'{oldName}' 태그를 '{newName}'(으)로 변경했습니다.", StatusType.Success);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var tag = SelectedTag;
        if (tag is null)
        {
            SetStatus("삭제할 태그를 선택하세요.", StatusType.Warning);
            return;
        }

        var result = MessageBox.Show(
            $"'{tag.Name}' 태그를 삭제하시겠습니까?\n이 태그를 사용 중인 모든 항목에서도 함께 제거됩니다.",
            "삭제 확인",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _masterTags.Remove(tag);

        foreach (var item in _managedItems)
        {
            if (item.Tags.Any(t => string.Equals(t, tag.Name, StringComparison.OrdinalIgnoreCase)))
            {
                var updated = item.Tags.Where(t => !string.Equals(t, tag.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                item.SetTags(updated);
            }
        }

        RefreshCreditsPanel(SelectedTag);
        SetStatus($"'{tag.Name}' 태그를 삭제했습니다.", StatusType.Success);
    }

    /// <summary>
    /// 선택된 태그의 Credits(품번 목록)를 관리 리스트와 대조해서 보여준다. 관리 리스트(활성/제거됨 모두)에
    /// 같은 품번(파일명, 확장자 제외)의 파일이 실제로 있으면 진한 색, 없으면 연한 색으로 표시된다.
    /// 표시하기 전에 관리 리스트를 먼저 찾아서 Credits를 최신 상태로 동기화한다(<see cref="SyncCreditsFromManagedItems"/>).
    /// </summary>
    private void RefreshCreditsPanel(TagItem? tag)
    {
        if (!ReferenceEquals(_subscribedCreditsTag, tag))
        {
            if (_subscribedCreditsTag is not null)
            {
                _subscribedCreditsTag.PropertyChanged -= SelectedTagPropertyChanged;
            }

            _subscribedCreditsTag = tag;

            if (_subscribedCreditsTag is not null)
            {
                _subscribedCreditsTag.PropertyChanged += SelectedTagPropertyChanged;
            }
        }

        if (tag is null)
        {
            CreditsList.ItemsSource = null;
            return;
        }

        SyncCreditsFromManagedItems(tag);

        var libraryCodes = _managedItems
            .Select(m => Path.GetFileNameWithoutExtension(m.FileName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        CreditsList.ItemsSource = tag.Credits
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .Select(code => new CreditChip { Code = code, HasFile = libraryCodes.Contains(code) })
            .ToList();
    }

    /// <summary>
    /// 관리 리스트에서 이 태그가 Tags로 지정된 항목들의 품번(파일명, 확장자 제외)을 찾아 Credits에 자동으로
    /// 병합한다 — 이미 관리 리스트에서 이 태그를 붙여둔 파일이 있다면, 따로 "추가"를 거치지 않아도 품번
    /// 목록에 반영되도록 하기 위함이다. 이미 Credits에 있는 값은 건드리지 않는다.
    /// </summary>
    private void SyncCreditsFromManagedItems(TagItem tag)
    {
        var codesFromLibrary = _managedItems
            .Where(m => m.Tags.Any(t => string.Equals(t, tag.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(m => Path.GetFileNameWithoutExtension(m.FileName))
            .Where(code => !string.IsNullOrEmpty(code))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var missing = codesFromLibrary
            .Where(code => !tag.Credits.Any(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (missing.Count > 0)
        {
            var updated = new List<string>(tag.Credits);
            updated.AddRange(missing);
            tag.SetCredits(updated);
        }
    }

    /// <summary>
    /// 새로 추가한 품번이 관리 리스트에 실제로 있는 파일이면, 그 항목의 Tags에도 이 태그를 추가해서
    /// (이미 지정돼 있지 않은 경우에만) 태그 정보를 최신 상태로 맞춘다.
    /// </summary>
    private void UpdateManagedItemTagsForCredit(TagItem tag, string code)
    {
        var matches = _managedItems.Where(m =>
            string.Equals(Path.GetFileNameWithoutExtension(m.FileName), code, StringComparison.OrdinalIgnoreCase));

        foreach (var item in matches)
        {
            if (!item.Tags.Any(t => string.Equals(t, tag.Name, StringComparison.OrdinalIgnoreCase)))
            {
                var updatedTags = new List<string>(item.Tags) { tag.Name };
                item.SetTags(updatedTags);
            }
        }
    }

    private void AddCredit_Click(object sender, RoutedEventArgs e)
    {
        var tag = SelectedTag;
        if (tag is null)
        {
            SetStatus("품번을 추가할 태그를 먼저 선택하세요.", StatusType.Warning);
            return;
        }

        var dialog = new AddCreditWindow(_managedItems, code => AddCreditToTag(tag, code)) { Owner = this };
        dialog.ShowDialog();
    }

    private void AddCreditToTag(TagItem tag, string code)
    {
        if (tag.Credits.Any(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus($"이미 추가된 품번입니다: '{code}'", StatusType.Warning);
            return;
        }

        var updated = new List<string>(tag.Credits) { code };
        tag.SetCredits(updated);
        UpdateManagedItemTagsForCredit(tag, code);
        RefreshCreditsPanel(tag);
        SetStatus($"'{code}' 품번을 추가했습니다.", StatusType.Success);
    }

    /// <summary>품번 목록에 텍스트를 드래그 앤 드롭하면 그 텍스트를 품번으로 바로 추가한다 —
    /// "추가" 대화상자를 거치지 않는 지름길. `AddCreditWindow`의 품번 정규화 규칙(소문자, 앞뒤 공백 제거)을 그대로 따른다.</summary>
    private void CreditsList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.Text) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void CreditsList_Drop(object sender, DragEventArgs e)
    {
        var tag = SelectedTag;
        if (tag is null || e.Data.GetData(DataFormats.Text) is not string text)
        {
            return;
        }

        var code = text.Trim().ToLowerInvariant();
        if (code.Length == 0)
        {
            return;
        }

        AddCreditToTag(tag, code);
        e.Handled = true;
    }

    /// <summary>품번(Credit) 칩을 클릭하면 관리 리스트에서 일치하는 항목의 속성 창을 연다. 일치하는 파일이 없으면 안내만 표시한다.</summary>
    private void CreditChip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string code })
        {
            return;
        }

        var match = _managedItems.FirstOrDefault(m =>
            string.Equals(Path.GetFileNameWithoutExtension(m.FileName), code, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            SetStatus("관리 리스트에 이 품번의 파일이 없습니다.", StatusType.Info);
            return;
        }

        // Owner는 MainWindow로 고정한다 — `this`(TagManagerWindow)로 하면, 나중에 이 창이 종류별 단일
        // 인스턴스 규칙으로 다른 TagManagerWindow에 밀려 닫힐 때 WPF가 소유한 창(PropertiesWindow)까지
        // 함께 닫아버려서 "모든 창은 독립적인 상태를 갖는다"는 규칙과 어긋난다.
        var dialog = new PropertiesWindow(match, _masterTags, _masterActors, _masterSeries, _managedItems) { Owner = Application.Current.MainWindow };
        dialog.Closed += (_, _) =>
        {
            if (dialog.PermanentlyDeleted)
            {
                _managedItems.Remove(dialog.CurrentItem);
            }

            RefreshCreditsPanel(SelectedTag);
        };
        SingleInstanceWindow<PropertiesWindow>.Show(dialog);
        e.Handled = true;
    }

    private void CreditChip_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string code })
        {
            return;
        }

        var tag = SelectedTag;
        if (tag is null)
        {
            return;
        }

        var result = MessageBox.Show(
            $"'{code}' 항목을 Credits에서 제거하시겠습니까?\n실제 파일에 이 태그가 지정되어 있다면 그 지정도 함께 해제됩니다.",
            "삭제 확인",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        // 이 품번과 일치하는 실제 파일이 아직 이 태그를 Tags로 갖고 있으면, Credits에서만 지워도 RefreshCreditsPanel의
        // SyncCreditsFromManagedItems가 곧바로 다시 채워넣어 "삭제가 안 되는 것처럼" 보인다(배우/시리즈 관리 창과
        // 동일한 원인) — 파일 쪽 Tags에서도 이 태그를 함께 제거해서 재동기화로 되살아나지 않게 한다.
        foreach (var item in _managedItems.Where(m =>
            string.Equals(Path.GetFileNameWithoutExtension(m.FileName), code, StringComparison.OrdinalIgnoreCase) &&
            m.Tags.Any(t => string.Equals(t, tag.Name, StringComparison.OrdinalIgnoreCase))))
        {
            var updatedTags = item.Tags.Where(t => !string.Equals(t, tag.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            item.SetTags(updatedTags);
        }

        var updated = tag.Credits.Where(c => !string.Equals(c, code, StringComparison.OrdinalIgnoreCase)).ToList();
        tag.SetCredits(updated);
        RefreshCreditsPanel(tag);
        SetStatus($"'{code}' 품번을 삭제했습니다.", StatusType.Success);

        e.Handled = true;
    }

    /// <summary>품번(Credit) 칩에 마우스를 올리면 관리 리스트에서 일치하는 항목의 썸네일을 팝업으로 미리 보여준다.
    /// 일치하는 파일이 없거나 썸네일이 없으면 조용히 아무 반응도 하지 않는다.</summary>
    private void CreditChip_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string code } chip)
        {
            return;
        }

        var match = _managedItems.FirstOrDefault(m =>
            string.Equals(Path.GetFileNameWithoutExtension(m.FileName), code, StringComparison.OrdinalIgnoreCase));

        if (match is null || !match.HasThumbnail)
        {
            return;
        }

        CreditThumbnailImage.Source = ImageLoadHelper.Load(match.ThumbnailPath, 180);
        CreditThumbnailPopup.PlacementTarget = chip;
        CreditThumbnailPopup.IsOpen = true;
    }

    private void CreditChip_MouseLeave(object sender, MouseEventArgs e) => CreditThumbnailPopup.IsOpen = false;

    private static string? NormalizeTagName(string? raw)
    {
        var trimmed = raw?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
