using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VideoVault;

/// <summary>
/// 배우/시리즈의 Credits(품번 목록)에 새 품번을 추가하는 대화상자. 입력하는 동안 관리 리스트에서 일치하는 항목을
/// 미리보기로 보여주지만, 관리 리스트에 없는 품번도 자유롭게 입력해서 추가할 수 있다.
/// "추가" 버튼 또는 Enter 키로 입력한 품번을 즉시 <paramref name="onAddCode"/>로 넘겨 반영하고, 창은 닫지 않고
/// 입력란만 비워 바로 다음 품번을 이어서 입력할 수 있게 한다 — 여러 품번을 연달아 추가하는 용도.
/// </summary>
public partial class AddCreditWindow : Window
{
    private readonly List<ManagedVideoItem> _managedItems;
    private readonly Action<string> _onAddCode;

    public AddCreditWindow(IEnumerable<ManagedVideoItem> managedItems, Action<string> onAddCode)
    {
        InitializeComponent();
        _managedItems = managedItems.ToList();
        _onAddCode = onAddCode;
        Loaded += (_, _) => CodeBox.Focus();
    }

    private void CodeBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshPreview();

    private void CodeBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddCurrentCode();
            e.Handled = true;
        }
    }

    private void AddButton_Click(object sender, RoutedEventArgs e) => AddCurrentCode();

    /// <summary>품번은 대소문자를 가리지 않고 항상 소문자로, 앞뒤 공백은 제거하고 저장한다.</summary>
    private void AddCurrentCode()
    {
        var code = CodeBox.Text.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(code))
        {
            return;
        }

        _onAddCode(code);
        CodeBox.Clear();
        CodeBox.Focus();
    }

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

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private class PreviewItem
    {
        public string Code { get; set; } = string.Empty;
        public string? ThumbnailPath { get; set; }
        public bool HasThumbnail { get; set; }
    }
}
