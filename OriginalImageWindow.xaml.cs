using System.IO;
using System.Windows;
using System.Windows.Input;

namespace VideoVault;

/// <summary>
/// 리사이즈 전 원본 썸네일 이미지를 크게 보여주는 창. 클릭하면 닫힌다.
/// </summary>
public partial class OriginalImageWindow : Window
{
    public OriginalImageWindow(string originalImagePath)
    {
        InitializeComponent();
        OriginalImage.Source = ImageLoadHelper.Load(originalImagePath);
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => Close();

    /// <summary>선택된 항목에 원본 이미지가 있으면 원본창을 연다. 없으면 아무 동작도 하지 않는다.</summary>
    public static void ShowFor(Window owner, ManagedVideoItem? item)
    {
        if (item?.ThumbnailOriginalPath is null || !File.Exists(item.ThumbnailOriginalPath))
        {
            return;
        }

        new OriginalImageWindow(item.ThumbnailOriginalPath) { Owner = owner }.ShowDialog();
    }
}
