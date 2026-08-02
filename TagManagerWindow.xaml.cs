using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;

namespace VideoVault;

/// <summary>
/// 태그 마스터 목록을 추가/이름변경/삭제하는 창.
/// 이름 변경/삭제 시 관리 리스트 항목의 Tags에도 반영되어 참조 무결성을 유지한다.
/// </summary>
public partial class TagManagerWindow : Window
{
    private readonly ObservableCollection<string> _masterTags;
    private readonly IEnumerable<ManagedVideoItem> _managedItems;
    private readonly ICollectionView _tagsView;

    public TagManagerWindow(ObservableCollection<string> masterTags, IEnumerable<ManagedVideoItem> managedItems)
    {
        InitializeComponent();
        _masterTags = masterTags;
        _managedItems = managedItems;

        _tagsView = CollectionViewSource.GetDefaultView(_masterTags);
        _tagsView.SortDescriptions.Add(new SortDescription(string.Empty, ListSortDirection.Ascending));
        TagsListBox.ItemsSource = _tagsView;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var name = NormalizeTagName(NewTagBox.Text);
        if (name is null)
        {
            MessageBox.Show("태그 이름을 입력하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_masterTags.Any(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("이미 존재하는 태그입니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _masterTags.Add(name);
        NewTagBox.Clear();

        TagsListBox.SelectedItem = name;
        TagsListBox.ScrollIntoView(name);
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (TagsListBox.SelectedItem is not string oldName)
        {
            MessageBox.Show("이름을 변경할 태그를 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var newName = NormalizeTagName(RenameBox.Text);
        if (newName is null)
        {
            MessageBox.Show("새 태그 이름을 입력하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase) &&
            _masterTags.Any(t => string.Equals(t, newName, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("이미 존재하는 태그 이름입니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var index = _masterTags.IndexOf(oldName);
        _masterTags[index] = newName;

        foreach (var item in _managedItems)
        {
            var tagIndex = item.Tags.FindIndex(t => string.Equals(t, oldName, StringComparison.OrdinalIgnoreCase));
            if (tagIndex >= 0)
            {
                var updated = new List<string>(item.Tags) { [tagIndex] = newName };
                item.SetTags(updated);
            }
        }

        RenameBox.Clear();
        _tagsView.Refresh();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (TagsListBox.SelectedItem is not string tag)
        {
            MessageBox.Show("삭제할 태그를 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            $"'{tag}' 태그를 삭제하시겠습니까?\n이 태그를 사용 중인 모든 항목에서도 함께 제거됩니다.",
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
            if (item.Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)))
            {
                var updated = item.Tags.Where(t => !string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)).ToList();
                item.SetTags(updated);
            }
        }
    }

    private static string? NormalizeTagName(string? raw)
    {
        var trimmed = raw?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
