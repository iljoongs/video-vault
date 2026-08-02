using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VideoVault;

/// <summary>Windows 셸에서 특정 확장자에 연결된 표준 아이콘을 가져오는 헬퍼.</summary>
public static class WindowsIconHelper
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_SMALLICON = 0x1;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    private static ImageSource? _pngFileIcon;

    /// <summary>
    /// Windows 탐색기가 ".png" 파일에 표시하는 표준 아이콘을 반환한다(셸에서 직접 조회, 프로세스 내에서 한 번만
    /// 조회하고 캐시). 조회에 실패하면(드문 환경) null을 반환한다.
    /// </summary>
    public static ImageSource? PngFileIcon
    {
        get
        {
            if (_pngFileIcon is not null)
            {
                return _pngFileIcon;
            }

            var shfi = new SHFILEINFO();
            var result = SHGetFileInfo(".png", FILE_ATTRIBUTE_NORMAL, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(),
                SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);

            if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var bitmap = Imaging.CreateBitmapSourceFromHIcon(shfi.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bitmap.Freeze();
                _pngFileIcon = bitmap;
                return _pngFileIcon;
            }
            finally
            {
                DestroyIcon(shfi.hIcon);
            }
        }
    }
}
