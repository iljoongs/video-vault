using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace VideoVault;

/// <summary>
/// 폴더를 열어 동영상 파일을 스캔하고, 선택한 파일을 관리 리스트에 추가하는 서브 창.
/// `_managedItems`는 `MainWindow`와 같은 컬렉션을 그대로 참조하므로, 여기서 추가/변경한 내용이
/// 즉시 메인창에도 반영된다(같은 `ObservableCollection` 인스턴스를 공유하기 때문).
/// </summary>
public partial class FolderListWindow : Window
{
    private readonly ObservableCollection<ManagedVideoItem> _managedItems;
    private string? _currentFolder;

    /// <summary>이 창에서 마지막으로 열었던(또는 초기화로 비운) 폴더. 호출자가 설정 저장에 사용한다.</summary>
    public string? LastFolder => _currentFolder;

    public FolderListWindow(ObservableCollection<ManagedVideoItem> managedItems, string? initialFolder)
    {
        InitializeComponent();
        WindowSnapHelper.Attach(this);
        _managedItems = managedItems;

        if (initialFolder is not null && Directory.Exists(initialFolder))
        {
            _currentFolder = initialFolder;
            FolderPathText.Text = _currentFolder;
            LoadVideoFiles();
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "동영상 폴더 선택" };

        if (dialog.ShowDialog() == true)
        {
            _currentFolder = dialog.FolderName;
            FolderPathText.Text = _currentFolder;
            LoadVideoFiles();
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFolder is null)
        {
            MessageBox.Show("먼저 폴더를 여세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        LoadVideoFiles();
    }

    private void ResetFolderList_Click(object sender, RoutedEventArgs e)
    {
        _currentFolder = null;
        FolderPathText.Text = "폴더를 선택하세요.";
        FolderListView.ItemsSource = null;
        FolderFileCountText.Text = string.Empty;
    }

    private void LoadVideoFiles()
    {
        if (_currentFolder is null || !Directory.Exists(_currentFolder))
        {
            return;
        }

        var items = new DirectoryInfo(_currentFolder)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(f => ManagedListImporter.VideoExtensions.Contains(f.Extension.ToLowerInvariant()))
            .OrderBy(f => f.Name)
            .Select(f => new VideoFileItem(f))
            .ToList();

        FolderListView.ItemsSource = items;
        FolderFileCountText.Text = $"파일 {items.Count}개";
    }

    private void DeleteFile_Click(object sender, RoutedEventArgs e)
    {
        var items = FolderListView.SelectedItems.Cast<VideoFileItem>().ToList();
        if (items.Count == 0)
        {
            MessageBox.Show("삭제할 파일을 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var question = items.Count == 1
            ? $"'{items[0].FileName}' 파일을 실제로 삭제하시겠습니까?\n이 작업은 되돌릴 수 없습니다."
            : $"선택한 {items.Count}개 파일을 실제로 삭제하시겠습니까?\n이 작업은 되돌릴 수 없습니다.";

        var result = MessageBox.Show(question, "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var failed = new List<string>();
        foreach (var item in items)
        {
            try
            {
                File.Delete(item.FullPath);
            }
            catch (Exception ex)
            {
                failed.Add($"{item.FileName}: {ex.Message}");
            }
        }

        LoadVideoFiles();

        if (failed.Count > 0)
        {
            MessageBox.Show($"일부 파일을 삭제하지 못했습니다.\n{string.Join("\n", failed)}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddToManagedList_Click(object sender, RoutedEventArgs e)
    {
        if (FolderListView.SelectedItems.Count == 0)
        {
            MessageBox.Show("관리 리스트에 추가할 파일을 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ManagedListImporter.AddFiles(_managedItems, FolderListView.SelectedItems.Cast<VideoFileItem>());
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
