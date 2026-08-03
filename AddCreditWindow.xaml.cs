using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VideoVault;

/// <summary>
/// 배우의 Credits(품번 목록)에 새 품번을 추가하는 대화상자. 입력하는 동안 관리 리스트에서 일치하는 항목을
/// 미리보기로 보여주지만, 관리 리스트에 없는 품번도 자유롭게 입력해서 추가할 수 있다.
/// </summary>
public partial class AddCreditWindow : Window
{
    private readonly List<ManagedVideoItem> _managedItems;

    public string ProductCode { get; private set; } = string.Empty;

    public AddCreditWindow(IEnumerable<ManagedVideoItem> managedItems)
    {
        InitializeComponent();
        _managedItems = managedItems.ToList();
        Loaded += (_, _) => CodeBox.Focus();
    }

    private void CodeBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshPreview();

    private void RefreshPreview()
    {
        var query = CodeBox.Text.Trim();

        if (string.IsNullOrEmpty(query))
        {
            PreviewList.ItemsSource = null;
            EmptyQueryHint.Visibility = Visibility.Visible;
            NoMatchHint.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyQueryHint.Visibility = Visibility.Collapsed;

        var matches = _managedItems
            .Select(m => new PreviewItem
            {
                Code = Path.GetFileNameWithoutExtension(m.FileName),
                ThumbnailPath = m.ThumbnailPath,
                HasThumbnail = m.HasThumbnail,
            })
            .Where(p => p.Code.Contains(query, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(p => p.Code, StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p.Code, StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToList();

        PreviewList.ItemsSource = matches;
        NoMatchHint.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PreviewList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PreviewList.SelectedItem is PreviewItem item)
        {
            CodeBox.Text = item.Code;
            CodeBox.CaretIndex = CodeBox.Text.Length;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var code = CodeBox.Text.Trim();
        if (string.IsNullOrEmpty(code))
        {
            MessageBox.Show("품번을 입력하세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ProductCode = code;
        DialogResult = true;
    }

    private class PreviewItem
    {
        public string Code { get; set; } = string.Empty;
        public string? ThumbnailPath { get; set; }
        public bool HasThumbnail { get; set; }
    }
}
