using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace MoxTerm.Controls;

/// <summary>
/// 给 ListView 增加“按住鼠标拖出矩形框”的多选能力（WinUI 原生只支持 Ctrl/Shift 点选）。
/// 列表本身仍是 Extended 选择模式，单击、Ctrl、Shift 的行为不变。
/// 注意：ListViewItem 会把按下事件标记为已处理且不再向上冒泡，
/// 所以 PointerPressed 必须挂在各个容器上；移动/松开事件能到达 ListView，挂在列表上即可。
/// </summary>
internal sealed class RubberBandSelector
{
    /// <summary>移动超过该距离才判定为拖拽，避免轻微抖动误触发。</summary>
    private const double DragThreshold = 8;

    private readonly ListView _list;
    private readonly Canvas _layer;
    private readonly Rectangle _band;
    private readonly bool _dragItems;
    private readonly HashSet<ListViewItem> _hookedItems = new();
    private Point _anchor;
    private bool _pressed;
    private bool _dragging;

    /// <param name="dragItems">
    /// 列表是否开启了 CanDragItems。开启时按在列表项上交给系统拖拽（拖出去上传），
    /// 框选改为只在列表空白处按下才生效，两种手势互不抢占。
    /// </param>
    public RubberBandSelector(ListView list, Canvas layer, bool dragItems = false)
    {
        _list = list;
        _layer = layer;
        _dragItems = dragItems;
        _band = new Rectangle
        {
            Stroke = new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue),
            StrokeThickness = 1,
            Fill = new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue) { Opacity = 0.15 },
            Visibility = Visibility.Collapsed,
        };
        _layer.Children.Add(_band);

        // 列表项会吃掉按下事件，所以容器上各挂一次；列表本身的那次负责空白处按下。
        _list.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPressed), true);
        _list.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnMoved), true);
        _list.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnReleased), true);
        _list.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnCaptureLost), true);

        _list.ContainerContentChanging += OnContainerContentChanging;
        _list.Loaded += (loadedSender, loadedArgs) => HookRealizedItems();
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is ListViewItem item)
        {
            Hook(item);
        }
    }

    /// <summary>已实现的容器不会再触发 ContainerContentChanging，首次显示时补挂一次。</summary>
    private void HookRealizedItems()
    {
        for (var index = 0; index < _list.Items.Count; index++)
        {
            if (_list.ContainerFromIndex(index) is ListViewItem item)
            {
                Hook(item);
            }
        }
    }

    private void Hook(ListViewItem item)
    {
        if (_hookedItems.Add(item))
        {
            item.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPressed), true);
        }
    }

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_list);
        if (!point.Properties.IsLeftButtonPressed || IsScrollBar(e.OriginalSource))
        {
            return;
        }

        // 开启了列表项拖拽时，按在条目上交给系统拖拽（上传），不从这里开始框选
        if (_dragItems && IsInsideItem(e.OriginalSource))
        {
            return;
        }

        _anchor = point.Position;
        _pressed = true;
        _dragging = false;
    }

    /// <summary>按下的位置是否落在某个列表项容器里。</summary>
    private bool IsInsideItem(object? source)
    {
        var element = source as DependencyObject;

        while (element is not null && !ReferenceEquals(element, _list))
        {
            if (element is ListViewItem)
            {
                return true;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return false;
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_pressed)
        {
            return;
        }

        var point = e.GetCurrentPoint(_list);
        if (!point.Properties.IsLeftButtonPressed)
        {
            EndDrag();
            return;
        }

        var deltaX = point.Position.X - _anchor.X;
        var deltaY = point.Position.Y - _anchor.Y;

        if (!_dragging)
        {
            if (Math.Abs(deltaX) < DragThreshold && Math.Abs(deltaY) < DragThreshold)
            {
                return;
            }

            _dragging = true;
            _list.CapturePointer(e.Pointer);
            _band.Visibility = Visibility.Visible;
        }

        var rect = new Rect(
            Math.Min(_anchor.X, point.Position.X),
            Math.Min(_anchor.Y, point.Position.Y),
            Math.Abs(deltaX),
            Math.Abs(deltaY));

        Canvas.SetLeft(_band, rect.X);
        Canvas.SetTop(_band, rect.Y);
        _band.Width = rect.Width;
        _band.Height = rect.Height;

        SelectIntersecting(rect);
    }

    private void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging)
        {
            _list.ReleasePointerCapture(e.Pointer);
        }

        EndDrag();
    }

    private void OnCaptureLost(object sender, PointerRoutedEventArgs e) => EndDrag();

    private void EndDrag()
    {
        _pressed = false;
        _dragging = false;
        _band.Visibility = Visibility.Collapsed;
    }

    /// <summary>把与矩形相交的已实现容器全部选中（虚拟化外的项目不在可见区，无需处理）。</summary>
    private void SelectIntersecting(Rect rect)
    {
        var selected = new List<object>();

        for (var index = 0; index < _list.Items.Count; index++)
        {
            if (_list.ContainerFromIndex(index) is not ListViewItem container)
            {
                continue;
            }

            var bounds = container
                .TransformToVisual(_list)
                .TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));

            if (Intersects(bounds, rect))
            {
                selected.Add(_list.Items[index]);
            }
        }

        _list.SelectedItems.Clear();
        foreach (var item in selected)
        {
            _list.SelectedItems.Add(item);
        }
    }

    private static bool Intersects(Rect a, Rect b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width &&
        a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    private static bool IsScrollBar(object? source)
    {
        var element = source as DependencyObject;

        while (element is not null)
        {
            if (element is ScrollBar)
            {
                return true;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return false;
    }
}
