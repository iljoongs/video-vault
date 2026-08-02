using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;

namespace VideoVault;

/// <summary>
/// 배우 마스터 목록을 추가/이름변경/삭제하고 배우별 100x100 썸네일을 지정하는 창.
/// 이름 변경/삭제 시 관리 리스트 항목의 Actors에도 반영되어 참조 무결성을 유지한다.
/// </summary>
public partial class ActorManagerWindow : Window
{
    private readonly ObservableCollection<ActorItem> _masterActors;
    private readonly IEnumerable<ManagedVideoItem> _managedItems;
    private readonly ICollectionView _actorsView;

    public ActorManagerWindow(ObservableCollection<ActorItem> masterActors, IEnumerable<ManagedVideoItem> managedItems)
    {
        InitializeComponent();
        _masterActors = masterActors;
        _managedItems = managedItems;

        _actorsView = CollectionViewSource.GetDefaultView(_masterActors);
        _actorsView.SortDescriptions.Add(new SortDescription(nameof(ActorItem.Name), ListSortDirection.Ascending));
        ActorsListBox.ItemsSource = _actorsView;

        RefreshThumbnailPreview();
    }

    private ActorItem? SelectedActor => ActorsListBox.SelectedItem as ActorItem;

    private void ActorsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshThumbnailPreview();

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var name = NormalizeName(NewActorBox.Text);
        if (name is null)
        {
            MessageBox.Show("배우 이름을 입력하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_masterActors.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("이미 존재하는 배우입니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var newActor = new ActorItem { Name = name };
        _masterActors.Add(newActor);
        NewActorBox.Clear();

        ActorsListBox.SelectedItem = newActor;
        ActorsListBox.ScrollIntoView(newActor);
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        var actor = SelectedActor;
        if (actor is null)
        {
            MessageBox.Show("이름을 변경할 배우를 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var newName = NormalizeName(RenameBox.Text);
        if (newName is null)
        {
            MessageBox.Show("새 배우 이름을 입력하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var oldName = actor.Name;
        if (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase) &&
            _masterActors.Any(a => string.Equals(a.Name, newName, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("이미 존재하는 배우 이름입니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        RenameActorAndSync(actor, newName);

        RenameBox.Clear();
        _actorsView.Refresh();
        RefreshThumbnailPreview();
    }

    /// <summary>
    /// 배우 이름을 바꾸고, 이름 기반 썸네일 파일 rename + 관리 리스트 항목들의 Actors 참조 갱신까지 함께 처리한다.
    /// 이름 변경이 발생하는 모든 진입점(인라인 이름 변경 버튼, 배우 정보 수정 대화상자)이 공유한다.
    /// </summary>
    private void RenameActorAndSync(ActorItem actor, string newName)
    {
        var oldName = actor.Name;
        RenameThumbnailFile(actor, newName);
        actor.Name = newName;

        foreach (var item in _managedItems)
        {
            var index = item.Actors.FindIndex(a => string.Equals(a, oldName, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                var updated = new List<string>(item.Actors) { [index] = newName };
                item.SetActors(updated);
            }
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var actor = SelectedActor;
        if (actor is null)
        {
            MessageBox.Show("삭제할 배우를 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            $"'{actor.Name}' 배우를 삭제하시겠습니까?\n이 배우가 지정된 모든 항목에서도 함께 제거됩니다.",
            "삭제 확인",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _masterActors.Remove(actor);

        foreach (var item in _managedItems)
        {
            if (item.Actors.Any(a => string.Equals(a, actor.Name, StringComparison.OrdinalIgnoreCase)))
            {
                var updated = item.Actors.Where(a => !string.Equals(a, actor.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                item.SetActors(updated);
            }
        }

        TryDeleteThumbnailFile(actor.ThumbnailPath);
        RefreshThumbnailPreview();
    }

    private void ActorListItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: ActorItem } item)
        {
            item.IsSelected = true;
        }
    }

    private void EditActorInfo_Click(object sender, RoutedEventArgs e)
    {
        var actor = SelectedActor;
        if (actor is null)
        {
            MessageBox.Show("정보를 수정할 배우를 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new ActorInfoWindow(actor, _masterActors) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (!string.Equals(actor.Name, dialog.NewName, StringComparison.Ordinal))
        {
            RenameActorAndSync(actor, dialog.NewName);
        }

        actor.BirthYear = dialog.BirthYear;
        actor.Height = dialog.HeightCm;
        actor.BodyInfo = dialog.BodyInfo;

        _actorsView.Refresh();
        RefreshThumbnailPreview();
    }

    private void AddThumbnailButton_Click(object sender, RoutedEventArgs e)
    {
        var actor = SelectedActor;
        if (actor is null)
        {
            MessageBox.Show("썸네일을 지정할 배우를 먼저 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "배우 썸네일 이미지 선택",
            Filter = "이미지 파일 (*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif|모든 파일 (*.*)|*.*",
        };

        if (dialog.ShowDialog() == true)
        {
            ApplyThumbnail(actor, dialog.FileName);
        }
    }

    private void DeleteThumbnailButton_Click(object sender, RoutedEventArgs e)
    {
        var actor = SelectedActor;
        if (actor is null)
        {
            MessageBox.Show("썸네일을 삭제할 배우를 먼저 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!actor.HasThumbnail)
        {
            MessageBox.Show("삭제할 썸네일이 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        TryDeleteThumbnailFile(actor.ThumbnailPath);
        actor.ThumbnailPath = null;
        RefreshThumbnailPreview();
    }

    private void ThumbnailViewer_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropImageHelper.CanAccept(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void ThumbnailViewer_Drop(object sender, DragEventArgs e)
    {
        var actor = SelectedActor;
        if (actor is null)
        {
            return;
        }

        var imagePath = DragDropImageHelper.TryGetImagePath(e.Data);
        if (imagePath is not null)
        {
            ApplyThumbnail(actor, imagePath);
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

    private void ApplyThumbnail(ActorItem actor, string sourceImagePath)
    {
        try
        {
            AppPaths.EnsureActorsThumbnailDirectory();
            var destPath = Path.Combine(AppPaths.ActorsThumbnailDir, $"{SanitizeFileName(actor.Name)}.jpg");
            actor.ThumbnailPath = ThumbnailHelper.CreateActorThumbnail(sourceImagePath, destPath);
            RefreshThumbnailPreview();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"썸네일을 만들 수 없습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshThumbnailPreview()
    {
        var actor = SelectedActor;
        ThumbnailPathText.Text = actor?.ThumbnailPath ?? "지정된 썸네일 없음";

        if (actor is not null && actor.HasThumbnail)
        {
            ThumbnailImage.Source = ImageLoadHelper.Load(actor.ThumbnailPath);
            ThumbnailImage.Visibility = Visibility.Visible;
            ThumbnailFallbackIcon.Visibility = Visibility.Collapsed;
        }
        else
        {
            ThumbnailImage.Visibility = Visibility.Collapsed;
            ThumbnailFallbackIcon.Visibility = Visibility.Visible;
        }

        RefreshActorInfoPanel(actor);
    }

    private void RefreshActorInfoPanel(ActorItem? actor)
    {
        if (actor is null)
        {
            ActorNameInfoText.Text = string.Empty;
            ActorBirthYearInfoText.Text = string.Empty;
            ActorHeightInfoText.Text = string.Empty;
            ActorBodyInfoText.Text = string.Empty;
            return;
        }

        ActorNameInfoText.Text = $"이름: {actor.Name}";
        ActorBirthYearInfoText.Text = $"출생년도: {(actor.BirthYear is null ? "-" : actor.BirthYear)}";
        ActorHeightInfoText.Text = $"키: {(actor.Height is null ? "-" : $"{actor.Height}cm")}";
        ActorBodyInfoText.Text = $"신체정보: {(string.IsNullOrEmpty(actor.BodyInfo) ? "-" : actor.BodyInfo)}";
    }

    /// <summary>배우 이름 변경 시 이름 기반 썸네일 파일도 함께 rename한다 (부가 정리, 실패해도 이름 변경 자체는 유지).</summary>
    private static void RenameThumbnailFile(ActorItem actor, string newName)
    {
        if (!actor.HasThumbnail)
        {
            return;
        }

        try
        {
            var newPath = Path.Combine(AppPaths.ActorsThumbnailDir, $"{SanitizeFileName(newName)}.jpg");
            if (string.Equals(Path.GetFullPath(actor.ThumbnailPath!), Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            File.Move(actor.ThumbnailPath!, newPath, overwrite: true);
            actor.ThumbnailPath = newPath;
        }
        catch
        {
            // 썸네일 파일 rename은 부가 정리일 뿐이므로 실패해도 무시한다.
        }
    }

    private static void TryDeleteThumbnailFile(string? path)
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
            // 부가 정리이므로 실패해도 무시한다.
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }

    private static string? NormalizeName(string? raw)
    {
        var trimmed = raw?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
