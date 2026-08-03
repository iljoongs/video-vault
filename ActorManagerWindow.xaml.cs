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
    private readonly ObservableCollection<ManagedVideoItem> _managedItems;
    private readonly IEnumerable<string> _masterTags;
    private readonly ICollectionView _actorsView;

    public ActorManagerWindow(ObservableCollection<ActorItem> masterActors, ObservableCollection<ManagedVideoItem> managedItems, IEnumerable<string> masterTags)
    {
        InitializeComponent();
        _masterActors = masterActors;
        _managedItems = managedItems;
        _masterTags = masterTags;

        _actorsView = CollectionViewSource.GetDefaultView(_masterActors);
        _actorsView.SortDescriptions.Add(new SortDescription(nameof(ActorItem.Name), ListSortDirection.Ascending));
        ActorsListBox.ItemsSource = _actorsView;

        RefreshThumbnailPreview();
    }

    private ActorItem? SelectedActor => ActorsListBox.SelectedItem as ActorItem;

    private class CreditChip
    {
        public string Code { get; set; } = string.Empty;

        /// <summary>이 품번의 파일이 현재 관리 리스트(활성/제거됨 모두)에 실제로 있으면 true → 진한 파란색으로 표시.</summary>
        public bool HasFile { get; set; }
    }

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
        RefreshCreditsPanel(actor);
    }

    /// <summary>
    /// 선택된 배우의 Credits(품번 목록)를 관리 리스트와 대조해서 보여준다. 관리 리스트(활성/제거됨 모두)에
    /// 같은 품번(파일명, 확장자 제외)의 파일이 실제로 있으면 진한 파란색, 없으면 연한 파란색으로 표시된다.
    /// 표시하기 전에 관리 리스트를 먼저 찾아서 Credits를 최신 상태로 동기화한다(<see cref="SyncCreditsFromManagedItems"/>).
    /// </summary>
    private void RefreshCreditsPanel(ActorItem? actor)
    {
        if (actor is null)
        {
            CreditsList.ItemsSource = null;
            return;
        }

        SyncCreditsFromManagedItems(actor);

        var libraryCodes = _managedItems
            .Select(m => Path.GetFileNameWithoutExtension(m.FileName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        CreditsList.ItemsSource = actor.Credits
            .Select(code => new CreditChip { Code = code, HasFile = libraryCodes.Contains(code) })
            .ToList();
    }

    /// <summary>
    /// 관리 리스트에서 이 배우가 Actors로 지정된 항목들의 품번(파일명, 확장자 제외)을 찾아 Credits에 자동으로
    /// 병합한다 — 이미 관리 리스트에서 이 배우를 지정해둔 파일이 있다면, 따로 "작품 추가"를 거치지 않아도
    /// Credits 목록에 반영되도록 하기 위함이다. 이미 Credits에 있는 값은 건드리지 않는다.
    /// </summary>
    private void SyncCreditsFromManagedItems(ActorItem actor)
    {
        var codesFromLibrary = _managedItems
            .Where(m => m.Actors.Any(a => string.Equals(a, actor.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(m => Path.GetFileNameWithoutExtension(m.FileName))
            .Where(code => !string.IsNullOrEmpty(code))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var missing = codesFromLibrary
            .Where(code => !actor.Credits.Any(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (missing.Count > 0)
        {
            var updated = new List<string>(actor.Credits);
            updated.AddRange(missing);
            actor.SetCredits(updated);
        }
    }

    /// <summary>
    /// 새로 추가한 품번이 관리 리스트에 실제로 있는 파일이면, 그 항목의 Actors에도 이 배우를 추가해서
    /// (이미 지정돼 있지 않은 경우에만) 배우 정보를 최신 상태로 맞춘다.
    /// </summary>
    private void UpdateManagedItemActorsForCredit(ActorItem actor, string code)
    {
        var matches = _managedItems.Where(m =>
            string.Equals(Path.GetFileNameWithoutExtension(m.FileName), code, StringComparison.OrdinalIgnoreCase));

        foreach (var item in matches)
        {
            if (!item.Actors.Any(a => string.Equals(a, actor.Name, StringComparison.OrdinalIgnoreCase)))
            {
                var updatedActors = new List<string>(item.Actors) { actor.Name };
                item.SetActors(updatedActors);
            }
        }
    }

    private void AddCredit_Click(object sender, RoutedEventArgs e)
    {
        var actor = SelectedActor;
        if (actor is null)
        {
            MessageBox.Show("작품을 추가할 배우를 먼저 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new AddCreditWindow(_managedItems) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (actor.Credits.Any(c => string.Equals(c, dialog.ProductCode, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("이미 추가된 품번입니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var updated = new List<string>(actor.Credits) { dialog.ProductCode };
        actor.SetCredits(updated);
        UpdateManagedItemActorsForCredit(actor, dialog.ProductCode);
        RefreshCreditsPanel(actor);
    }

    /// <summary>품명(Credit) 칩을 클릭하면 관리 리스트에서 일치하는 항목의 속성 창을 연다. 일치하는 파일이 없으면 안내만 표시한다.</summary>
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
            MessageBox.Show("관리 리스트에 이 품번의 파일이 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new PropertiesWindow(match, _masterTags, _masterActors) { Owner = this };
        dialog.ShowDialog();

        if (dialog.PermanentlyDeleted)
        {
            _managedItems.Remove(match);
        }

        RefreshCreditsPanel(SelectedActor);
        e.Handled = true;
    }

    private void CreditChip_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string code })
        {
            return;
        }

        var actor = SelectedActor;
        if (actor is null)
        {
            return;
        }

        var result = MessageBox.Show(
            $"'{code}' 항목을 Credits에서 제거하시겠습니까?",
            "삭제 확인",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var updated = actor.Credits.Where(c => !string.Equals(c, code, StringComparison.OrdinalIgnoreCase)).ToList();
        actor.SetCredits(updated);
        RefreshCreditsPanel(actor);

        e.Handled = true;
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
