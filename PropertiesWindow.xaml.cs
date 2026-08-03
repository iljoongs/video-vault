using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
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

    private readonly ManagedVideoItem _item;
    private readonly IEnumerable<string> _masterTags;
    private readonly IEnumerable<ActorItem> _masterActors;
    private readonly ObservableCollection<string> _selectedActors;
    private List<TagCheckItem> _tagItems = new();

    /// <summary>"완전 삭제" 버튼으로 닫힌 경우 true. 호출자(`MainWindow`)가 이 값을 보고 항목을 관리 리스트에서 완전히 제거한다.</summary>
    public bool PermanentlyDeleted { get; private set; }

    public PropertiesWindow(ManagedVideoItem item, IEnumerable<string> masterTags, IEnumerable<ActorItem> masterActors)
    {
        InitializeComponent();
        _item = item;
        _masterTags = masterTags;
        _masterActors = masterActors;

        ActorComboBox.ItemsSource = masterActors.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
        _selectedActors = new ObservableCollection<string>(item.Actors);
        SelectedActorsList.ItemsSource = _selectedActors;

        RefreshFileInfo();
        BuildTagList();
        RefreshThumbnailPreview();
        RefreshSelectedActorsThumbnails();
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
    private void FileNamePart_LostFocus(object sender, RoutedEventArgs e)
    {
        var newName = ComposeFileName();
        if (string.Equals(newName, _item.FileName, StringComparison.Ordinal))
        {
            return;
        }

        if (RenameHelper.TryRenameManagedItemTo(_item, newName, _masterActors))
        {
            FullPathText.Text = _item.FullPath;
            CodeBox.Text = ManagedVideoItem.DeriveCode(_item.FileName, _item.FullPath);
            RefreshThumbnailPreview();
            RefreshProductCodeDisplay();
            SyncSelectedActorsFromItem();
        }
        else
        {
            FileNameText.Text = Path.GetFileNameWithoutExtension(_item.FileName);
            FileExtensionText.Text = TrimLeadingDot(Path.GetExtension(_item.FileName));
        }
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
        if (RenameHelper.TryEditFullPath(this, _item, _masterActors))
        {
            FileNameText.Text = Path.GetFileNameWithoutExtension(_item.FileName);
            FileExtensionText.Text = TrimLeadingDot(Path.GetExtension(_item.FileName));
            FullPathText.Text = _item.FullPath;
            RefreshThumbnailPreview();
            RefreshProductCodeDisplay();
            SyncSelectedActorsFromItem();
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

    /// <summary>맨 마지막 폴더명을 "코드" 값으로 바꾼 위치로 파일(및 관련 썸네일/원본 파일)을 이동한다.</summary>
    private void MoveByCodeButton_Click(object sender, RoutedEventArgs e)
    {
        var code = CodeBox.Text.Trim();
        if (string.IsNullOrEmpty(code) || code.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            MessageBox.Show("올바른 코드를 입력하세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var currentDirectory = Path.GetDirectoryName(_item.FullPath);
        var parentDirectory = currentDirectory is null ? null : Path.GetDirectoryName(currentDirectory);
        if (string.IsNullOrEmpty(parentDirectory))
        {
            MessageBox.Show("상위 폴더를 확인할 수 없습니다.", "파일 이동 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var newDirectory = Path.Combine(parentDirectory, code);
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
        ThumbnailPathText.Text = _item.ThumbnailPath ?? "지정된 썸네일 없음";

        if (_item.HasThumbnail)
        {
            ThumbnailImage.Source = ImageLoadHelper.Load(_item.ThumbnailPath);
            ThumbnailImage.Visibility = Visibility.Visible;
            ThumbnailFallbackIcon.Visibility = Visibility.Collapsed;
        }
        else
        {
            ThumbnailImage.Visibility = Visibility.Collapsed;
            ThumbnailFallbackIcon.Visibility = Visibility.Visible;
        }
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
            var result = ThumbnailHelper.CreateThumbnail(sourceImagePath, _item.FullPath);
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
            _item.PlayCount++;
            PlayCountBox.Text = _item.PlayCount.ToString();
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
        DialogResult = false;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (TryCommitFormFields())
        {
            DialogResult = true;
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
        _item.Memo = MemoBox.Text.Trim();
        _item.SetTags(_tagItems.Where(t => t.IsSelected).Select(t => t.Tag).ToList());
        _item.SetActors(_selectedActors.ToList());

        foreach (var removedActor in removedActors)
        {
            ActorCreditSync.OnActorRemovedFromItem(_item, removedActor, _masterActors);
        }

        return true;
    }

    /// <summary>
    /// 창 닫기(X) 버튼 등으로 대화상자가 닫힐 때 호출된다. "확인"/"취소"/"완전 삭제" 버튼은 이미 `DialogResult`를
    /// 설정해두므로 그 경우는 그냥 통과시키고, 그 외(제목 표시줄의 X, Alt+F4 등)에는 "확인"과 동일하게
    /// 변경사항을 저장한다 — 그래야 실수로 닫아도 입력한 내용이 사라지지 않는다.
    /// </summary>
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (DialogResult.HasValue)
        {
            return;
        }

        if (!TryCommitFormFields())
        {
            e.Cancel = true;
            return;
        }

        DialogResult = true;
    }
}
