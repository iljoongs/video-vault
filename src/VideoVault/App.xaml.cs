using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace VideoVault;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    /// <summary>프로세스 전역에서 하나만 있어야 하는 이름 있는 뮤텍스. 앱이 종료될 때까지 참조를 들고 있어야
    /// (필드로 보관) GC로 조기 해제되어 뮤텍스가 사라지는 일이 없다.</summary>
    private const string MutexName = "VideoVault_SingleInstance_2E1B7F3A";
    private Mutex? _singleInstanceMutex;

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// 작업표시줄(핀 고정 등)에서 아이콘을 다시 클릭해 앱을 재실행하면, 새 창을 또 띄우는 대신 이미 실행
    /// 중인 인스턴스의 메인 창을 앞으로 가져와 활성화한다(2026-08-22 추가, 사용자 요청). 이름 있는 뮤텍스로
    /// 중복 실행을 감지하고, 중복이면 기존 프로세스의 메인 창 핸들을 찾아 복원+포그라운드 전환한 뒤 이 새
    /// 프로세스는 창을 만들지 않고 즉시 종료한다. 새로 실행된 프로세스는 사용자가 방금 직접 클릭해서
    /// 띄운 것이라 Windows가 포그라운드 전환 권한을 주므로, 다른 프로세스의 창이라도 <see cref="SetForegroundWindow"/>가
    /// 막히지 않는다.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out var isNewInstance);
        if (!isNewInstance)
        {
            ActivateExistingInstance();
            Environment.Exit(0);
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void ActivateExistingInstance()
    {
        var currentProcess = Process.GetCurrentProcess();
        var existing = Process.GetProcessesByName(currentProcess.ProcessName)
            .FirstOrDefault(p => p.Id != currentProcess.Id);

        if (existing?.MainWindowHandle is { } handle && handle != IntPtr.Zero)
        {
            ShowWindowAsync(handle, SW_RESTORE);
            SetForegroundWindow(handle);
        }
    }
}

