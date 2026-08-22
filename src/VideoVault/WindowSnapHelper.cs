using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace VideoVault;

/// <summary>
/// 메인 창과 주요 창(폴더 목록/속성/배우 관리/태그 관리)을 서로 가까이 끌어다 놓으면 자석처럼 가장자리가
/// 딱 붙는 기능(2026-08-07 추가). WPF에는 이런 "창끼리 스냅"을 기본 지원하지 않아서, 드래그 도중 실시간으로
/// 위치를 가로채는 Win32 <c>WM_MOVING</c> 메시지를 후킹해 구현한다(<c>Window.LocationChanged</c>는 이동이
/// 끝난 뒤에만 발생해 드래그 중 실시간 스냅에는 쓸 수 없다).
/// </summary>
public static class WindowSnapHelper
{
    /// <summary>이 거리(DIU, 96 DPI 기준) 안으로 가장자리가 들어오면 딱 붙는다. 너무 크면 스냅된 창을 다시
    /// 떼어내기 어려워지므로(마우스가 이 거리만큼 움직여야 비로소 풀리기 시작함) 작게 잡는다(2026-08-07 축소).</summary>
    private const double SnapDistance = 8;

    private const int WM_MOVING = 0x0216;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

    private static readonly List<Window> TrackedWindows = new();

    /// <summary>창의 "원시" 사각형(GetWindowRect/WM_MOVING 기준)과 실제로 화면에 보이는 프레임 경계
    /// 사이의 여백(물리 픽셀, Left/Top/Right/Bottom). 창 크기가 바뀌어도 테마/DPI가 같으면 값이 그대로라
    /// 창당 한 번만 구해서 캐시해둔다 — <c>WM_MOVING</c>마다(추적 중인 창 수만큼) 매번 Win32 호출을 두 번씩
    /// 하면 응답이 느려져 빠르게 드래그할 때 창 위치가 마우스를 따라가지 못하고 어긋나는 문제가 있었다.</summary>
    private static readonly Dictionary<Window, (int Left, int Top, int Right, int Bottom)> MarginCache = new();

    /// <summary>이 창을 스냅 대상 목록에 등록하고, 드래그 중 다른 등록된 창에 가장자리가 붙도록 후킹한다.
    /// 창이 닫히면 자동으로 등록이 해제된다. 새 주요 창 인스턴스가 열릴 때마다(예: SingleInstanceWindow
    /// 패턴으로 재생성될 때) 생성자에서 한 번 호출하면 된다.</summary>
    public static void Attach(Window window)
    {
        TrackedWindows.Add(window);
        window.Closed += (_, _) =>
        {
            TrackedWindows.Remove(window);
            MarginCache.Remove(window);
        };

        if (PresentationSource.FromVisual(window) is HwndSource hwndSource)
        {
            hwndSource.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                WndProc(window, hwnd, msg, lParam, ref handled));
        }
        else
        {
            window.SourceInitialized += (_, _) =>
            {
                if (PresentationSource.FromVisual(window) is HwndSource source)
                {
                    source.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                        WndProc(window, hwnd, msg, lParam, ref handled));
                }
            };
        }
    }

    private static IntPtr WndProc(Window window, IntPtr hwnd, int msg, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_MOVING)
        {
            return IntPtr.Zero;
        }

        var rect = Marshal.PtrToStructure<RECT>(lParam);
        var dpi = VisualTreeHelper.GetDpi(window);
        var margin = GetCachedMargin(window, hwnd);

        // WM_MOVING의 RECT는 "원시" 사각형(물리 픽셀)이고 Window.Left/Top/ActualWidth/ActualHeight는
        // DIU(96 DPI) 기준이므로, 스냅 계산은 DIU로 변환해서 하고 결과만 다시 물리 픽셀로 되돌린다. margin은
        // Windows 10/11이 창 바깥에 덧붙이는 보이지 않는 그림자 여백으로, 이만큼 안쪽이 실제로 "보이는" 가장자리다.
        var visLeft = (rect.Left + margin.Left) / dpi.DpiScaleX;
        var visTop = (rect.Top + margin.Top) / dpi.DpiScaleY;
        var visRight = (rect.Right - margin.Right) / dpi.DpiScaleX;
        var visBottom = (rect.Bottom - margin.Bottom) / dpi.DpiScaleY;

        var (snappedLeft, snappedTop) = ComputeSnap(window, visLeft, visTop, visRight - visLeft, visBottom - visTop);

        var rawWidth = rect.Right - rect.Left;
        var rawHeight = rect.Bottom - rect.Top;
        rect.Left = (int)Math.Round(snappedLeft * dpi.DpiScaleX) - margin.Left;
        rect.Top = (int)Math.Round(snappedTop * dpi.DpiScaleY) - margin.Top;
        rect.Right = rect.Left + rawWidth;
        rect.Bottom = rect.Top + rawHeight;

        Marshal.StructureToPtr(rect, lParam, true);
        handled = true;
        return IntPtr.Zero;
    }

    /// <summary>다른 등록된 창들의 가장자리 중 <see cref="SnapDistance"/> 안에 있는 것이 있으면 그 값으로
    /// 맞춘 좌표를 반환한다(가로/세로 독립적으로 계산). "안쪽" 정렬(왼쪽끼리/오른쪽끼리 등 같은 쪽 가장자리를
    /// 맞추는 것)은 창이 서로 겹쳐 들어가는 것처럼 보여서 제외하고, **바깥쪽에 인접하게 붙는 경우만** 스냅한다
    /// (2026-08-07 변경 — 예전에는 같은 쪽 정렬도 후보에 있었다). 없으면 원래 위치를 그대로 반환한다.</summary>
    private static (double Left, double Top) ComputeSnap(Window dragging, double left, double top, double width, double height)
    {
        var right = left + width;
        var bottom = top + height;

        double? bestLeft = null;
        var bestLeftDist = SnapDistance;
        double? bestTop = null;
        var bestTopDist = SnapDistance;

        foreach (var other in TrackedWindows)
        {
            if (ReferenceEquals(other, dragging) || !other.IsVisible || other.WindowState != WindowState.Normal)
            {
                continue;
            }

            var (oLeft, oTop, oRight, oBottom) = GetVisibleBounds(other);

            // 가로: 내 왼쪽이 상대 오른쪽 바깥에 인접 / 내 오른쪽이 상대 왼쪽 바깥에 인접
            TryCandidate(Math.Abs(left - oRight), oRight, ref bestLeftDist, ref bestLeft);
            TryCandidate(Math.Abs(right - oLeft), oLeft - width, ref bestLeftDist, ref bestLeft);

            // 세로: 내 위가 상대 아래 바깥에 인접 / 내 아래가 상대 위 바깥에 인접
            TryCandidate(Math.Abs(top - oBottom), oBottom, ref bestTopDist, ref bestTop);
            TryCandidate(Math.Abs(bottom - oTop), oTop - height, ref bestTopDist, ref bestTop);
        }

        return (bestLeft ?? left, bestTop ?? top);
    }

    private static void TryCandidate(double distance, double candidateValue, ref double bestDistance, ref double? best)
    {
        if (distance < bestDistance)
        {
            bestDistance = distance;
            best = candidateValue;
        }
    }

    /// <summary>다른 창의 "보이는" 가장자리(DWM 그림자 여백 제외)를 DIU 기준으로 반환한다.</summary>
    private static (double Left, double Top, double Right, double Bottom) GetVisibleBounds(Window window)
    {
        if (PresentationSource.FromVisual(window) is not HwndSource source)
        {
            return (window.Left, window.Top, window.Left + window.ActualWidth, window.Top + window.ActualHeight);
        }

        var dpi = VisualTreeHelper.GetDpi(window);
        var margin = GetCachedMargin(window, source.Handle);

        // 다른 창은 지금 드래그 중이 아니므로 Window.Left/Top/ActualWidth/ActualHeight(DIU, 이미 최신 값)를
        // 그대로 쓴다 — Win32 GetWindowRect를 매번 새로 호출할 필요가 없어 더 가볍다.
        var left = window.Left + margin.Left / dpi.DpiScaleX;
        var top = window.Top + margin.Top / dpi.DpiScaleY;
        var right = window.Left + window.ActualWidth - margin.Right / dpi.DpiScaleX;
        var bottom = window.Top + window.ActualHeight - margin.Bottom / dpi.DpiScaleY;
        return (left, top, right, bottom);
    }

    /// <summary>창의 DWM 그림자 여백(물리 픽셀)을 처음 필요할 때 한 번만 구해서 캐시해두고 재사용한다.</summary>
    private static (int Left, int Top, int Right, int Bottom) GetCachedMargin(Window window, IntPtr hwnd)
    {
        if (MarginCache.TryGetValue(window, out var cached))
        {
            return cached;
        }

        var margin = ComputeMargin(hwnd);
        MarginCache[window] = margin;
        return margin;
    }

    /// <summary>창의 현재(지금 이 순간, GetWindowRect 기준) "원시" 사각형과 실제로 화면에 보이는 프레임
    /// 경계 사이의 여백(물리 픽셀)을 4방향 각각 반환한다. DWM 조회에 실패하면(드문 경우) 여백 없음(0)으로
    /// 취급한다 — 이 경우 스냅이 딱 맞지 않고 살짝 떨어져 보일 뿐, 기능 자체는 유지된다.</summary>
    private static (int Left, int Top, int Right, int Bottom) ComputeMargin(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var raw) ||
            DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var visible, Marshal.SizeOf<RECT>()) != 0)
        {
            return (0, 0, 0, 0);
        }

        return (visible.Left - raw.Left, visible.Top - raw.Top, raw.Right - visible.Right, raw.Bottom - visible.Bottom);
    }
}
