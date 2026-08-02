using System.Windows;

namespace VideoVault;

/// <summary>
/// 배우의 이름/출생년도/키/신체정보를 추가·수정·삭제(빈 값으로 저장)하는 대화상자.
/// 이름 변경 시 중복 검사만 이 창에서 수행하고, 실제 rename 동기화(썸네일/관리 리스트 참조 갱신)는
/// 호출자(`ActorManagerWindow`)가 처리한다.
/// </summary>
public partial class ActorInfoWindow : Window
{
    private readonly ActorItem _actor;
    private readonly IEnumerable<ActorItem> _allActors;

    public string NewName { get; private set; } = string.Empty;
    public int? BirthYear { get; private set; }
    public int? HeightCm { get; private set; }
    public string BodyInfo { get; private set; } = string.Empty;

    public ActorInfoWindow(ActorItem actor, IEnumerable<ActorItem> allActors)
    {
        InitializeComponent();
        _actor = actor;
        _allActors = allActors;

        NameBox.Text = actor.Name;
        BirthYearBox.Text = actor.BirthYear?.ToString() ?? string.Empty;
        HeightBox.Text = actor.Height?.ToString() ?? string.Empty;
        BodyInfoBox.Text = actor.BodyInfo;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("이름을 입력하세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!string.Equals(name, _actor.Name, StringComparison.OrdinalIgnoreCase) &&
            _allActors.Any(a => a != _actor && string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("이미 존재하는 배우 이름입니다.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryParseOptionalInt(BirthYearBox.Text, out var birthYear))
        {
            MessageBox.Show("출생년도는 0 이상의 숫자로 입력하세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryParseOptionalInt(HeightBox.Text, out var height))
        {
            MessageBox.Show("키는 0 이상의 숫자로 입력하세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        NewName = name;
        BirthYear = birthYear;
        HeightCm = height;
        BodyInfo = BodyInfoBox.Text.Trim();

        DialogResult = true;
    }

    private static bool TryParseOptionalInt(string raw, out int? value)
    {
        var trimmed = raw.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            value = null;
            return true;
        }

        if (int.TryParse(trimmed, out var parsed) && parsed >= 0)
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }
}
