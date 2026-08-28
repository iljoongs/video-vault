using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace VideoVault;

/// <summary>
/// 시리즈 마스터 목록을 추가/삭제하고, 시리즈별 품번(Credits)을 관리하는 창. 배우 관리 창의 Credits 패널과
/// 같은 패턴(관리 리스트 대조로 색 구분, 클릭 시 속성 창, 우클릭 시 Credits에서 제거)을 공유한다. 이름 변경/삭제
/// 시 관리 리스트 항목의 <see cref="ManagedVideoItem.Series"/>에도 반영되어 참조 무결성을 유지한다.
/// </summary>
public partial class SeriesManagerWindow : Window
{
    private readonly ObservableCollection<SeriesItem> _masterSeries;
    private readonly IEnumerable<TagItem> _masterTags;
    private readonly ObservableCollection<ActorItem> _masterActors;
    private readonly ObservableCollection<ManagedVideoItem> _managedItems;
    private readonly ICollectionView _seriesView;

    public SeriesManagerWindow(ObservableCollection<SeriesItem> masterSeries, IEnumerable<TagItem> masterTags, ObservableCollection<ActorItem> masterActors, ObservableCollection<ManagedVideoItem> managedItems)
    {
        InitializeComponent();
        WindowSnapHelper.Attach(this);
        _masterSeries = masterSeries;
        _masterTags = masterTags;
        _masterActors = masterActors;
        _managedItems = managedItems;

        _seriesView = CollectionViewSource.GetDefaultView(_masterSeries);
        _seriesView.SortDescriptions.Add(new SortDescription(nameof(SeriesItem.Name), ListSortDirection.Ascending));
        SeriesListBox.ItemsSource = _seriesView;

        // "작품수" 열은 글자 크기에 맞춰 자동 폭(Width 미지정 = Auto)을 유지하고, "시리즈 이름" 열이 나머지
        // 공간을 채우도록 해서 "작품수" 열이 항상 테이블 오른쪽 끝에 붙어 있게 한다. GridView는 자체적으로
        // Star 크기 조절을 지원하지 않으므로, 리스트 크기 변경(SizeChanged)과 "작품수" 열의 실제 폭 변경
        // (ActualWidth, 자릿수가 바뀌는 경우 등)에 맞춰 코드로 재계산한다.
        ((INotifyPropertyChanged)CreditCountColumn).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "ActualWidth") UpdateSeriesNameColumnWidth();
        };

        RefreshCreditsPanel();

        // "# 창" 규칙(모든 창은 데이터가 바뀌면 동시에 갱신된다): 이 창을 열어둔 채로 다른 창(속성 창 등)에서
        // 관리 리스트 항목의 Series가 바뀌면(예: 속성 창에서 시리즈 콤보박스 변경) Credits 패널도 즉시 따라가도록
        // 한다(2026-08-10 추가, 구현 완료) — 예전에는 이 창을 새로 열거나 시리즈를 다시 선택해야만 반영됐다.
        // `MainWindow.ManagedItems_CollectionChanged`와 동일한 패턴으로 모든 항목의 PropertyChanged를 구독한다.
        foreach (var item in _managedItems)
        {
            item.PropertyChanged += ManagedItem_PropertyChanged;
        }

        _managedItems.CollectionChanged += ManagedItems_CollectionChanged;
        Closed += (_, _) =>
        {
            _managedItems.CollectionChanged -= ManagedItems_CollectionChanged;
            foreach (var item in _managedItems)
            {
                item.PropertyChanged -= ManagedItem_PropertyChanged;
            }

            if (_subscribedCreditsSeries is not null)
            {
                _subscribedCreditsSeries.PropertyChanged -= SelectedSeriesPropertyChanged;
            }
        };
    }

    private void SeriesListBox_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateSeriesNameColumnWidth();

    private void UpdateSeriesNameColumnWidth()
    {
        const double scrollBarAndBorderAllowance = 22;
        var remaining = SeriesListBox.ActualWidth - CreditCountColumn.ActualWidth - scrollBarAndBorderAllowance;
        SeriesNameColumn.Width = Math.Max(60, remaining);
    }

    private void ManagedItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (ManagedVideoItem item in e.NewItems)
            {
                item.PropertyChanged += ManagedItem_PropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (ManagedVideoItem item in e.OldItems)
            {
                item.PropertyChanged -= ManagedItem_PropertyChanged;
            }
        }
    }

    private void ManagedItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ManagedVideoItem.Series))
        {
            RefreshCreditsPanel();
        }
    }

    /// <summary>
    /// 지금 선택된 시리즈의 `Credits`가 구독 대상이다 — <see cref="RefreshCreditsPanel"/>이 선택이 바뀔 때마다
    /// 이 필드를 갱신하며 옛 시리즈 구독을 해제하고 새 시리즈를 구독한다.
    /// </summary>
    private SeriesItem? _subscribedCreditsSeries;

    /// <summary>
    /// 선택된 시리즈의 `Credits`가 (이 창 밖에서, 예: `SeriesCreditSync.OnFileRenamed`) 직접 바뀌면 Credits
    /// 패널도 즉시 따라가도록 한다(2026-08-10 추가, 구현 완료) — `ActorManagerWindow.SelectedActorPropertyChanged`와
    /// 동일한 이유로 필요하다(`ManagedItem_PropertyChanged`만으로는, 예를 들어 속성 창에서 시리즈를 다른 값으로
    /// 바꿔서 `SeriesCreditSync`가 옛 품번을 Credits에서 제거하는 경우를 놓친다).
    /// </summary>
    private void SelectedSeriesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SeriesItem.Credits))
        {
            RefreshCreditsPanel();
        }
    }

    private SeriesItem? SelectedSeries => SeriesListBox.SelectedItem as SeriesItem;

    private class CreditChip
    {
        public string Code { get; set; } = string.Empty;

        /// <summary>이 품번의 파일이 현재 관리 리스트(활성/제거됨 모두)에 실제로 있으면 true → 진한 색으로 표시.</summary>
        public bool HasFile { get; set; }
    }

    private void SeriesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshCreditsPanel();

    private void AddSeries_Click(object sender, RoutedEventArgs e)
    {
        var name = NormalizeName(SeriesBox.Text);
        if (name is null)
        {
            MessageBox.Show("시리즈 이름을 입력하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_masterSeries.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("이미 존재하는 시리즈입니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var newSeries = new SeriesItem { Name = name };
        _masterSeries.Add(newSeries);
        SeriesBox.Clear();

        SeriesListBox.SelectedItem = newSeries;
        SeriesListBox.ScrollIntoView(newSeries);
    }

    /// <summary>시리즈 이름을 바꾸고, 이 시리즈를 참조 중인 관리 리스트 항목들의 <see cref="ManagedVideoItem.Series"/>도
    /// 함께 갱신해 참조 무결성을 유지한다(2026-08-27 추가, 사용자 요청 — 예전에는 추가/삭제만 있고 이름 변경
    /// 기능 자체가 없었다). 배우/태그 관리 창의 이름 변경과 같은 패턴이며, 시리즈는 썸네일 파일이 없으므로
    /// `ActorManagerWindow.RenameActorAndSync`처럼 파일 rename까지 처리할 필요는 없다.</summary>
    private void RenameSeries_Click(object sender, RoutedEventArgs e)
    {
        var series = SelectedSeries;
        if (series is null)
        {
            MessageBox.Show("이름을 변경할 시리즈를 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var newName = NormalizeName(SeriesBox.Text);
        if (newName is null)
        {
            MessageBox.Show("새 시리즈 이름을 입력하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var oldName = series.Name;
        if (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase) &&
            _masterSeries.Any(s => string.Equals(s.Name, newName, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("이미 존재하는 시리즈 이름입니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        series.Name = newName;

        foreach (var item in _managedItems)
        {
            if (string.Equals(item.Series, oldName, StringComparison.OrdinalIgnoreCase))
            {
                item.Series = newName;
            }
        }

        SeriesBox.Clear();
        _seriesView.Refresh();
        RefreshCreditsPanel();
    }

    private void DeleteSeries_Click(object sender, RoutedEventArgs e)
    {
        var series = SelectedSeries;
        if (series is null)
        {
            MessageBox.Show("삭제할 시리즈를 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            $"'{series.Name}' 시리즈를 삭제하시겠습니까?\n이 시리즈가 지정된 모든 항목에서도 함께 제거됩니다.",
            "삭제 확인",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _masterSeries.Remove(series);

        foreach (var item in _managedItems)
        {
            if (string.Equals(item.Series, series.Name, StringComparison.OrdinalIgnoreCase))
            {
                item.Series = string.Empty;
            }
        }

        RefreshCreditsPanel();
    }

    /// <summary>
    /// 선택된 시리즈의 Credits(품번 목록)를 관리 리스트와 대조해서 보여준다. 관리 리스트(활성/제거됨 모두)에
    /// 같은 품번(파일명, 확장자 제외)의 파일이 실제로 있으면 진한 색, 없으면 연한 색으로 표시된다.
    /// 표시하기 전에 관리 리스트를 먼저 찾아서 Credits를 최신 상태로 동기화한다(<see cref="SyncCreditsFromManagedItems"/>).
    /// </summary>
    private void RefreshCreditsPanel()
    {
        var series = SelectedSeries;

        if (!ReferenceEquals(_subscribedCreditsSeries, series))
        {
            if (_subscribedCreditsSeries is not null)
            {
                _subscribedCreditsSeries.PropertyChanged -= SelectedSeriesPropertyChanged;
            }

            _subscribedCreditsSeries = series;

            if (_subscribedCreditsSeries is not null)
            {
                _subscribedCreditsSeries.PropertyChanged += SelectedSeriesPropertyChanged;
            }
        }

        // 품번은 시리즈 하나에만 속해야 한다 — 어느 시리즈를 보고 있든(또는 아무것도 선택하지 않았든) 매번
        // 전체 시리즈를 대상으로 중복을 정리해서, 창을 열자마자도 기존에 쌓인 중복이 즉시 해소되도록 한다.
        SeriesCreditSync.RemoveDuplicateCreditsAcrossSeries(_masterSeries, _managedItems);

        if (series is null)
        {
            CreditsList.ItemsSource = null;
            return;
        }

        SyncCreditsFromManagedItems(series);

        var libraryCodes = _managedItems
            .Select(m => Path.GetFileNameWithoutExtension(m.FileName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        CreditsList.ItemsSource = series.Credits
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .Select(code => new CreditChip { Code = code, HasFile = libraryCodes.Contains(code) })
            .ToList();
    }

    /// <summary>
    /// 관리 리스트에서 이 시리즈가 Series로 지정된 항목들의 품번(파일명, 확장자 제외)을 찾아 Credits에 자동으로
    /// 병합한다 — 관리 리스트에서 이미 이 시리즈를 지정해둔 파일이 있다면, 따로 "작품 추가"를 거치지 않아도
    /// Credits 목록에 반영되도록 하기 위함이다. 이미 Credits에 있는 값은 건드리지 않는다.
    /// </summary>
    private void SyncCreditsFromManagedItems(SeriesItem series)
    {
        var codesFromLibrary = _managedItems
            .Where(m => string.Equals(m.Series, series.Name, StringComparison.OrdinalIgnoreCase))
            .Select(m => Path.GetFileNameWithoutExtension(m.FileName))
            .Where(code => !string.IsNullOrEmpty(code))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var missing = codesFromLibrary
            .Where(code => !series.Credits.Any(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (missing.Count > 0)
        {
            var updated = new List<string>(series.Credits);
            updated.AddRange(missing);
            series.SetCredits(updated);
        }
    }

    private void AddCredit_Click(object sender, RoutedEventArgs e)
    {
        var series = SelectedSeries;
        if (series is null)
        {
            MessageBox.Show("작품을 추가할 시리즈를 먼저 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new AddCreditWindow(_managedItems, code => AddCreditToSeries(series, code)) { Owner = this };
        dialog.ShowDialog();
    }

    private void AddCreditToSeries(SeriesItem series, string code)
    {
        if (series.Credits.Any(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("이미 추가된 품번입니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 다른 시리즈에 이미 같은 품번이 등록되어 있으면, 방금 사용자가 명시적으로 지정한 이 시리즈를 최신
        // 정보로 보고 기존 시리즈의 Credits에서 제거한다(2026-08-16 추가). 실제 파일의 Series가 그 기존
        // 시리즈를 가리키고 있었다면 이 시리즈로 함께 옮겨서, 나중에 기존 시리즈를 다시 열었을 때
        // SyncCreditsFromManagedItems가 방금 제거한 품번을 도로 되살리지 않도록 한다.
        var otherSeriesWithCode = _masterSeries
            .Where(s => !ReferenceEquals(s, series) && s.Credits.Any(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (otherSeriesWithCode.Count > 0)
        {
            // 다른 시리즈에서 조용히 옮기지 않고 먼저 확인을 받는다(2026-08-16 추가) — 실수로 다른 시리즈의
            // 등록을 뺏어오는 것을 막기 위함이다. "아니오"를 고르면 아무것도 바꾸지 않고 추가 자체를 취소한다.
            var otherNames = string.Join(", ", otherSeriesWithCode.Select(s => s.Name));
            var question = $"'{code}' 품번은 이미 '{otherNames}' 시리즈에 등록되어 있습니다.\n" +
                            $"기존 시리즈에서 제거하고 '{series.Name}'(으)로 옮기시겠습니까?";
            var confirm = MessageBox.Show(question, "중복 품번 확인", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }
        }

        foreach (var other in otherSeriesWithCode)
        {
            other.SetCredits(other.Credits.Where(c => !string.Equals(c, code, StringComparison.OrdinalIgnoreCase)).ToList());
        }

        // Credits에 먼저 추가한 뒤에 item.Series를 바꿔야 한다 — item.Series 변경은 ManagedItem_PropertyChanged를
        // 통해 RefreshCreditsPanel()(→ SyncCreditsFromManagedItems)을 동기적으로 즉시 실행시키는데, 그 시점에
        // Credits에 아직 이 품번이 없으면 "누락된 코드"로 오인해 먼저 추가해버리고, 바로 아래의 series.SetCredits가
        // 또 한 번 추가해서 같은 품번이 두 개 등록되는 버그가 있었다(2026-08-16 수정).
        var updated = new List<string>(series.Credits) { code };
        series.SetCredits(updated);

        var matches = _managedItems.Where(m =>
            string.Equals(Path.GetFileNameWithoutExtension(m.FileName), code, StringComparison.OrdinalIgnoreCase));
        foreach (var item in matches)
        {
            if (string.IsNullOrEmpty(item.Series) || otherSeriesWithCode.Any(s => string.Equals(s.Name, item.Series, StringComparison.OrdinalIgnoreCase)))
            {
                item.Series = series.Name;
            }
        }

        RefreshCreditsPanel();
    }

    /// <summary>Credits 목록에 텍스트를 드래그 앤 드롭하면 그 텍스트를 품번으로 바로 추가한다(2026-08-15 추가) —
    /// "작품 추가" 대화상자를 거치지 않는 지름길. `AddCreditWindow`의 품번 정규화 규칙(소문자, 앞뒤 공백 제거)을 그대로 따른다.</summary>
    private void CreditsList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.Text) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void CreditsList_Drop(object sender, DragEventArgs e)
    {
        var series = SelectedSeries;
        if (series is null || e.Data.GetData(DataFormats.Text) is not string text)
        {
            return;
        }

        var code = text.Trim().ToLowerInvariant();
        if (code.Length == 0)
        {
            return;
        }

        AddCreditToSeries(series, code);
        e.Handled = true;
    }

    /// <summary>품번(Credit) 칩을 클릭하면 관리 리스트에서 일치하는 항목의 속성 창을 연다. 일치하는 파일이 없으면 안내만 표시한다.</summary>
    private void CreditChip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string code })
        {
            return;
        }

        var match = _managedItems.FirstOrDefault(m =>
            string.Equals(Path.GetFileNameWithoutExtension(m.FileName), code, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            MessageBox.Show("관리 리스트에 이 품번의 파일이 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Owner는 MainWindow로 고정한다 — `this`(SeriesManagerWindow)로 하면, 나중에 이 창이 종류별 단일
        // 인스턴스 규칙으로 다른 SeriesManagerWindow에 밀려 닫힐 때 WPF가 소유한 창(PropertiesWindow)까지
        // 함께 닫아버려서 "모든 창은 독립적인 상태를 갖는다"는 규칙과 어긋난다.
        var dialog = new PropertiesWindow(match, _masterTags, _masterActors, _masterSeries, _managedItems) { Owner = Application.Current.MainWindow };
        dialog.Closed += (_, _) =>
        {
            if (dialog.PermanentlyDeleted)
            {
                _managedItems.Remove(dialog.CurrentItem);
            }

            RefreshCreditsPanel();
        };
        SingleInstanceWindow<PropertiesWindow>.Show(dialog);
        e.Handled = true;
    }

    private void CreditChip_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string code })
        {
            return;
        }

        var series = SelectedSeries;
        if (series is null)
        {
            return;
        }

        var result = MessageBox.Show(
            $"'{code}' 항목을 Credits에서 제거하시겠습니까?\n실제 파일에 이 시리즈가 지정되어 있다면 그 지정도 함께 해제됩니다.",
            "삭제 확인",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        // 이 품번과 일치하는 실제 파일이 아직 이 시리즈를 가리키고 있으면, Credits에서만 지워도 RefreshCreditsPanel의
        // SyncCreditsFromManagedItems가 곧바로 다시 채워넣어 "삭제가 안 되는 것처럼" 보인다(실제 버그였음) —
        // 파일 쪽 Series도 함께 비워서 재동기화로 되살아나지 않게 한다.
        foreach (var item in _managedItems.Where(m =>
            string.Equals(Path.GetFileNameWithoutExtension(m.FileName), code, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.Series, series.Name, StringComparison.OrdinalIgnoreCase)))
        {
            item.Series = string.Empty;
        }

        var updated = series.Credits.Where(c => !string.Equals(c, code, StringComparison.OrdinalIgnoreCase)).ToList();
        series.SetCredits(updated);
        RefreshCreditsPanel();

        e.Handled = true;
    }

    /// <summary>품번(Credit) 칩에 마우스를 올리면 관리 리스트에서 일치하는 항목의 썸네일을 팝업으로 미리 보여준다
    /// (2026-08-16 추가). 일치하는 파일이 없거나 썸네일이 없으면 조용히 아무 반응도 하지 않는다.</summary>
    private void CreditChip_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string code } chip)
        {
            return;
        }

        var match = _managedItems.FirstOrDefault(m =>
            string.Equals(Path.GetFileNameWithoutExtension(m.FileName), code, StringComparison.OrdinalIgnoreCase));

        if (match is null || !match.HasThumbnail)
        {
            return;
        }

        CreditThumbnailImage.Source = ImageLoadHelper.Load(match.ThumbnailPath, 180);
        CreditThumbnailPopup.PlacementTarget = chip;
        CreditThumbnailPopup.IsOpen = true;
    }

    private void CreditChip_MouseLeave(object sender, MouseEventArgs e) => CreditThumbnailPopup.IsOpen = false;

    private static string? NormalizeName(string? raw)
    {
        var trimmed = raw?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
