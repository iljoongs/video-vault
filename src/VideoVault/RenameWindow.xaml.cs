using System.IO;
using System.Windows;

namespace VideoVault;

/// <summary>
/// 새 파일명을 입력받는 간단한 대화상자.
/// </summary>
public partial class RenameWindow : Window
{
    public string NewFileName { get; private set; } = string.Empty;

    public RenameWindow(string currentFileName)
    {
        InitializeComponent();
        NameBox.Text = currentFileName;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            MessageBox.Show("올바른 파일명을 입력하세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        NewFileName = name;
        DialogResult = true;
    }
}
