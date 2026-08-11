using System.Windows;

namespace VideoVault;

/// <summary>
/// 창 종류(T)별로 동시에 인스턴스가 하나만 열리도록 보장하는 공용 헬퍼("# 창" 규칙: 모든 주요 창은 창
/// 종류별로 최대 1개만 열린다). 같은 종류의 창을 다시 열려고 하면 기존에 열려 있던 인스턴스를 먼저 닫는다.
/// </summary>
/// <remarks>
/// 제네릭 static 클래스이므로 <c>T</c>마다(예: <see cref="PropertiesWindow"/>, <see cref="ActorManagerWindow"/>)
/// 독립된 <c>_current</c> 저장소를 갖는다 — 각 창 클래스에 똑같은 static 필드를 반복해서 두지 않아도 된다.
/// 모덜리스(<see cref="Window.Show"/>)로 띄우므로, 다른 종류의 주요 창들과는 동시에 열려 있을 수 있다
/// (각 창이 독립적인 상태를 갖고, 공유하는 데이터가 바뀌면 서로 즉시 반영되도록 하는 것과는 별개의 문제).
/// </remarks>
public static class SingleInstanceWindow<T> where T : Window
{
    private static T? _current;

    /// <summary>현재 열려 있는 이 종류의 창 인스턴스(없으면 null). 다른 곳에서 "지금 열려 있는 속성 창"처럼
    /// 특정 종류의 열린 창을 찾아 상호작용해야 할 때(예: 관리 리스트 선택이 바뀌면 열려 있는 속성 창을 따라가게 하는 것) 사용한다.</summary>
    public static T? Current => _current;

    /// <summary>
    /// 같은 종류의 기존 창이 열려 있으면 닫고, 이 창을 새로운 "현재 창"으로 등록한 뒤 보여준다.
    /// <see cref="WindowPositionMemory"/>에 이 창 종류가 마지막으로 열려 있던 위치가 기억되어 있으면(그리고
    /// 그 위치가 여전히 화면 안이면) 그 위치에 열고, 없으면 XAML에 정의된 기본 시작 위치(보통 CenterOwner)를
    /// 그대로 쓴다. 창이 닫힐 때는 그 시점의 위치를 다시 기억해둔다.
    /// </summary>
    public static void Show(T window)
    {
        _current?.Close();

        var key = typeof(T).Name;
        if (WindowPositionMemory.TryGetOnScreenPosition(key, out var left, out var top))
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = left;
            window.Top = top;
        }

        _current = window;
        window.Closed += (_, _) =>
        {
            WindowPositionMemory.Remember(key, window.Left, window.Top);

            if (ReferenceEquals(_current, window))
            {
                _current = null;
            }
        };

        window.Show();
        window.Activate();
    }
}
