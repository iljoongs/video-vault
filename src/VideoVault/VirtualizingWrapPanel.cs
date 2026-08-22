using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace VideoVault;

/// <summary>
/// 아이콘 보기 전용 가상화 WrapPanel. WPF에는 기본 제공되는 가상화 WrapPanel이 없어 직접 구현했다 —
/// 화면에 실제로 보이는 행(row)의 카드만 컨테이너를 만들고, 화면 밖 카드는 컨테이너 자체를 만들지 않는다.
/// 그래서 항목 수가 늘어나도(2900개+) 렌더링 비용이 "전체 항목 수"가 아니라 "화면에 보이는 카드 수"에만
/// 비례한다.
/// </summary>
/// <remarks>
/// 모든 카드가 <see cref="IconSizeSettings.Current"/>를 통해 같은 크기를 공유한다는(균일 크기) 전제로
/// 구현을 단순화했다 — 일반적인 가변 크기 WrapPanel(아이템마다 다른 크기)보다 훨씬 단순하게 구현할 수
/// 있고, 실제로 이 앱의 아이콘 카드는 전부 같은 크기이므로 이 전제가 항상 성립한다. 표준 방식(Standard,
/// <see cref="ItemContainerGenerator.Remove"/> 기반)으로 가상화하며, 컨테이너 재사용(Recycling)까지는
/// 하지 않는다 — 화면 밖 컨테이너를 만들지 않는 것만으로도 이미 "전체 항목 수만큼 렌더링"하던 문제의
/// 핵심을 해결하므로, 재사용은 향후 추가 최적화로 남겨둔다.
/// </remarks>
public class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    private Size _extent;
    private Size _viewport;
    private Point _offset;
    private Size _itemSize = new(1, 1);
    private int _itemsPerRow = 1;

    public VirtualizingWrapPanel()
    {
        // 아이콘 크기 프리셋이 바뀌면(예: "보통 아이콘" → "큰 아이콘") 카드 크기가 바뀌므로 다시 측정해야 한다.
        IconSizeSettings.Current.PropertyChanged += (_, _) =>
        {
            _itemSize = new Size(1, 1);
            InvalidateMeasure();
        };
    }

    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; }
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => _offset.X;
    public double VerticalOffset => _offset.Y;
    public ScrollViewer? ScrollOwner { get; set; }

    private double RowHeight => _itemSize.Height > 0 ? _itemSize.Height : 1;

    public void LineUp() => SetVerticalOffset(_offset.Y - RowHeight);
    public void LineDown() => SetVerticalOffset(_offset.Y + RowHeight);
    public void LineLeft() { }
    public void LineRight() { }
    public void MouseWheelUp() => SetVerticalOffset(_offset.Y - RowHeight);
    public void MouseWheelDown() => SetVerticalOffset(_offset.Y + RowHeight);
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }
    public void PageUp() => SetVerticalOffset(_offset.Y - _viewport.Height);
    public void PageDown() => SetVerticalOffset(_offset.Y + _viewport.Height);
    public void PageLeft() { }
    public void PageRight() { }
    public void SetHorizontalOffset(double offset) { }

    public void SetVerticalOffset(double offset)
    {
        offset = Math.Max(0, Math.Min(offset, Math.Max(0, _extent.Height - _viewport.Height)));
        if (Math.Abs(offset - _offset.Y) > 0.01)
        {
            _offset.Y = offset;
            ScrollOwner?.InvalidateScrollInfo();
            InvalidateMeasure();
        }
    }

    /// <summary>선택/포커스 이동 등으로 특정 컨테이너를 화면에 보이게 해야 할 때 WPF가 호출한다.</summary>
    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        // Panel.ItemContainerGenerator는 IItemContainerGenerator(인덱스↔컨테이너 조회용 메서드가 없음)로
        // 노출되므로, 그 조회가 필요할 때는 ItemsControl 쪽의 구체 타입 ItemContainerGenerator를 쓴다
        // (같은 생성기 인스턴스를 가리킨다).
        if (visual is DependencyObject container)
        {
            var index = ItemsControl.GetItemsOwner(this)?.ItemContainerGenerator.IndexFromContainer(container) ?? -1;
            if (index >= 0)
            {
                BringIndexIntoView(index);
            }
        }

        return rectangle;
    }

    /// <summary>
    /// 가상화된 항목(아직 컨테이너가 없는 항목)을 화면에 보이게 해야 할 때 WPF가 호출한다
    /// (예: <c>ListBox.ScrollIntoView</c>, 키보드 방향키로 화면 밖 항목까지 선택 이동).
    /// </summary>
    protected override void BringIndexIntoView(int index)
    {
        if (_itemsPerRow <= 0 || _itemSize.Height <= 0 || index < 0)
        {
            return;
        }

        var row = index / _itemsPerRow;
        var top = row * _itemSize.Height;
        var bottom = top + _itemSize.Height;

        if (top < _offset.Y)
        {
            SetVerticalOffset(top);
        }
        else if (bottom > _offset.Y + _viewport.Height)
        {
            SetVerticalOffset(bottom - _viewport.Height);
        }
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        // Add 이외에는(Remove/Replace/Move/Reset) 이미 realize된 컨테이너가 어떤 항목을 가리키는지
        // 안전하게 보장할 수 없으므로, 지금 갖고 있는 시각적 자식을 전부 비우고 다음 레이아웃 패스에서
        // 현재 상태 기준으로 새로 생성한다 — generator 위치를 직접 조작하는 것보다 훨씬 안전하다.
        if (args.Action != NotifyCollectionChangedAction.Add && InternalChildren.Count > 0)
        {
            RemoveInternalChildRange(0, InternalChildren.Count);
        }

        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // IsItemsHost 훅업이 아직 끝나지 않은 초기 레이아웃 패스(예: Visibility가 Collapsed→Visible로
        // 바뀌어 처음 화면에 나타나는 시점)에서는 ItemContainerGenerator가 null일 수 있다(실제로 재현되어
        // 확인된 크래시였음). 이때는 아무 것도 하지 않고 빈 크기를 반환하되, 훅업이 끝난 뒤 자동으로 다시
        // 측정되도록 다음 디스패처 사이클에 InvalidateMeasure를 예약한다 — 그렇지 않으면 훅업이 완료된
        // 뒤에도 아무도 다시 측정을 요청하지 않아 빈 화면으로 계속 남는 문제가 있었다(실제로 재현 확인함).
        if (ItemContainerGenerator is null)
        {
            Dispatcher.BeginInvoke(new Action(InvalidateMeasure), DispatcherPriority.Loaded);
            return new Size(0, 0);
        }

        var itemsControl = ItemsControl.GetItemsOwner(this);
        var itemCount = itemsControl?.Items.Count ?? 0;

        EnsureItemSize();

        var availableWidth = double.IsInfinity(availableSize.Width) ? _itemSize.Width : availableSize.Width;
        _itemsPerRow = _itemSize.Width > 0 ? Math.Max(1, (int)Math.Floor(availableWidth / _itemSize.Width)) : 1;

        var totalRows = itemCount == 0 ? 0 : (int)Math.Ceiling(itemCount / (double)_itemsPerRow);
        var extentHeight = totalRows * _itemSize.Height;
        var viewportHeight = double.IsInfinity(availableSize.Height) ? extentHeight : availableSize.Height;

        _extent = new Size(availableWidth, extentHeight);
        _viewport = new Size(availableWidth, viewportHeight);

        var maxOffset = Math.Max(0, extentHeight - viewportHeight);
        if (_offset.Y > maxOffset)
        {
            _offset.Y = maxOffset;
        }

        ScrollOwner?.InvalidateScrollInfo();

        RealizeVisibleItems(itemCount);

        var desiredHeight = double.IsInfinity(availableSize.Height) ? extentHeight : Math.Min(extentHeight, viewportHeight);
        return new Size(availableWidth, desiredHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_itemsPerRow <= 0)
        {
            return finalSize;
        }

        var itemsOwner = ItemsControl.GetItemsOwner(this);
        if (itemsOwner is null)
        {
            return finalSize;
        }

        foreach (UIElement child in InternalChildren)
        {
            var index = itemsOwner.ItemContainerGenerator.IndexFromContainer(child);
            if (index < 0)
            {
                continue;
            }

            var row = index / _itemsPerRow;
            var col = index % _itemsPerRow;
            var x = col * _itemSize.Width;
            var y = row * _itemSize.Height - _offset.Y;

            child.Arrange(new Rect(x, y, _itemSize.Width, _itemSize.Height));
        }

        return finalSize;
    }

    /// <summary>카드 하나를 실제로 생성해 측정하고, 그 크기(마진 포함)를 이후 레이아웃 계산에 사용한다.
    /// <see cref="IconSizeSettings"/>가 바뀌면 생성자에서 구독한 핸들러가 이 값을 리셋해 다시 측정하게 한다.</summary>
    private void EnsureItemSize()
    {
        if (_itemSize.Width > 1 && _itemSize.Height > 1)
        {
            return;
        }

        var itemsControl = ItemsControl.GetItemsOwner(this);
        if (itemsControl is null || itemsControl.Items.Count == 0)
        {
            return;
        }

        var generator = ItemContainerGenerator;
        var startPos = generator.GeneratorPositionFromIndex(0);
        using (generator.StartAt(startPos, GeneratorDirection.Forward, true))
        {
            if (generator.GenerateNext(out var isNewlyRealized) is not UIElement child)
            {
                return;
            }

            if (isNewlyRealized)
            {
                InsertInternalChild(0, child);
                generator.PrepareItemContainer(child);
            }

            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (child.DesiredSize.Width > 0 && child.DesiredSize.Height > 0)
            {
                _itemSize = child.DesiredSize;
            }
        }
    }

    private void RealizeVisibleItems(int itemCount)
    {
        if (itemCount == 0 || _itemsPerRow <= 0)
        {
            if (InternalChildren.Count > 0)
            {
                ItemContainerGenerator.Remove(new GeneratorPosition(0, 0), InternalChildren.Count);
                RemoveInternalChildRange(0, InternalChildren.Count);
            }

            return;
        }

        var firstVisibleRow = Math.Max(0, (int)Math.Floor(_offset.Y / RowHeight));
        var visibleRowCount = (int)Math.Ceiling(_viewport.Height / RowHeight) + 1;
        var lastVisibleRow = firstVisibleRow + visibleRowCount;

        var firstIndex = Math.Min(itemCount - 1, firstVisibleRow * _itemsPerRow);
        var lastIndex = Math.Min(itemCount - 1, ((lastVisibleRow + 1) * _itemsPerRow) - 1);
        if (firstIndex < 0)
        {
            firstIndex = 0;
        }

        if (lastIndex < firstIndex)
        {
            lastIndex = firstIndex;
        }

        CleanupOutsideRange(firstIndex, lastIndex);

        var generator = ItemContainerGenerator;
        var startPos = generator.GeneratorPositionFromIndex(firstIndex);
        var childIndex = startPos.Offset == 0 ? startPos.Index : startPos.Index + 1;

        using (generator.StartAt(startPos, GeneratorDirection.Forward, true))
        {
            for (var i = firstIndex; i <= lastIndex; i++, childIndex++)
            {
                if (generator.GenerateNext(out var isNewlyRealized) is not UIElement child)
                {
                    continue;
                }

                if (isNewlyRealized)
                {
                    if (childIndex >= InternalChildren.Count)
                    {
                        AddInternalChild(child);
                    }
                    else
                    {
                        InsertInternalChild(childIndex, child);
                    }

                    generator.PrepareItemContainer(child);
                }

                child.Measure(_itemSize);
            }
        }
    }

    private void CleanupOutsideRange(int minIndex, int maxIndex)
    {
        var generator = ItemContainerGenerator;

        for (var childIndex = InternalChildren.Count - 1; childIndex >= 0; childIndex--)
        {
            var position = new GeneratorPosition(childIndex, 0);
            var itemIndex = generator.IndexFromGeneratorPosition(position);
            if (itemIndex < 0 || itemIndex < minIndex || itemIndex > maxIndex)
            {
                generator.Remove(position, 1);
                RemoveInternalChildRange(childIndex, 1);
            }
        }
    }
}
