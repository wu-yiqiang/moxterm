using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MoxTerm.Controls;

/// <summary>分屏方向：左右并排或上下堆叠。</summary>
public enum TerminalSplitDirection
{
    LeftRight,
    TopBottom,
}

/// <summary>终端右键菜单里可执行的分屏操作。</summary>
public enum TerminalPaneAction
{
    SplitLeftRight,
    SplitTopBottom,
    Close,
}

/// <summary>窗格操作事件参数。</summary>
public sealed class TerminalPaneEventArgs(TerminalPaneAction action) : EventArgs
{
    public TerminalPaneAction Action { get; } = action;
}

/// <summary>
/// 承载一个标签页内的全部终端窗格。内部是一棵节点树：叶子是 <see cref="TerminalView"/>，
/// 分支是带可拖动分隔条的 <see cref="Grid"/>，支持任意层级的嵌套分屏。
/// </summary>
public sealed class TerminalPaneHost : Grid
{
    /// <summary>分隔条的可点击宽度，实际只画中间 1px 的线。</summary>
    private const double SplitterHitWidth = 6;

    /// <summary>拖动分隔条时两侧比例的最小值，避免把某一侧拖没。</summary>
    private const double MinRatio = 0.1;

    private readonly List<TerminalView> _views = [];

    private PaneNode? _root;

    public TerminalPaneHost()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
    }

    /// <summary>当前标签页内的全部终端（自上而下、自左而右）。</summary>
    public IReadOnlyList<TerminalView> Views => _views;

    /// <summary>放入第一个窗格（标签页创建时调用一次）。</summary>
    public void Add(TerminalView view)
    {
        _root ??= new LeafNode(view);

        Attach(view);
        UpdateCanClosePane();
        Render();
    }

    /// <summary>在目标窗格旁再开一个同配置的终端。</summary>
    public void Split(TerminalView target, TerminalSplitDirection direction)
    {
        if (FindLeaf(_root, target) is not { } leaf)
        {
            return;
        }

        // 先把插入点记下来：新建的 SplitNode 会把 leaf.Parent 改写到自己身上
        var parent = leaf.Parent;
        var isRoot = ReferenceEquals(_root, leaf);

        var created = new TerminalView
        {
            Profile = target.Profile,
            Backend = target.Backend,
        };
        Attach(created);

        var split = new SplitNode(
            direction == TerminalSplitDirection.LeftRight ? Orientation.Horizontal : Orientation.Vertical,
            leaf,
            new LeafNode(created));

        if (isRoot)
        {
            _root = split;
        }
        else
        {
            split.Parent = parent;

            if (ReferenceEquals(parent!.First, leaf))
            {
                parent.First = split;
            }
            else
            {
                parent.Second = split;
            }
        }

        UpdateCanClosePane();
        Render();
        FocusView(created);
    }

    /// <summary>关闭一个窗格。只剩它自己时忽略，避免把标签页清空。</summary>
    public void Close(TerminalView view)
    {
        if (_views.Count <= 1 || FindLeaf(_root, view) is not { } leaf || leaf.Parent is not { } parent)
        {
            return;
        }

        var sibling = ReferenceEquals(parent.First, leaf) ? parent.Second : parent.First;
        var grand = parent.Parent;
        sibling.Parent = grand;

        if (grand is null)
        {
            _root = sibling;
        }
        else if (ReferenceEquals(grand.First, parent))
        {
            grand.First = sibling;
        }
        else
        {
            grand.Second = sibling;
        }

        Detach(view);
        view.Dispose();
        UpdateCanClosePane();
        Render();
        FocusFirst(sibling);
    }

    /// <summary>结束全部窗格并释放各自的会话。</summary>
    public void Dispose()
    {
        foreach (var view in _views.ToArray())
        {
            Detach(view);
            view.Dispose();
        }

        _views.Clear();
        _root = null;
        Children.Clear();
    }

    private void Attach(TerminalView view)
    {
        _views.Add(view);
        view.PaneActionRequested += OnPaneActionRequested;
    }

    private void Detach(TerminalView view)
    {
        _views.Remove(view);
        view.PaneActionRequested -= OnPaneActionRequested;
    }

    private void OnPaneActionRequested(object? sender, TerminalPaneEventArgs e)
    {
        if (sender is not TerminalView view)
        {
            return;
        }

        switch (e.Action)
        {
            case TerminalPaneAction.SplitLeftRight:
                Split(view, TerminalSplitDirection.LeftRight);
                break;

            case TerminalPaneAction.SplitTopBottom:
                Split(view, TerminalSplitDirection.TopBottom);
                break;

            case TerminalPaneAction.Close:
                Close(view);
                break;
        }
    }

    private void UpdateCanClosePane()
    {
        var canClose = _views.Count > 1;

        foreach (var view in _views)
        {
            view.CanClosePane = canClose;
        }
    }

    private void FocusView(TerminalView view) =>
        DispatcherQueue.TryEnqueue(() => view.Focus(FocusState.Programmatic));

    private void FocusFirst(PaneNode node)
    {
        if (FindFirstView(node) is { } view)
        {
            FocusView(view);
        }
    }

    private static TerminalView? FindFirstView(PaneNode? node) => node switch
    {
        null => null,
        LeafNode leaf => leaf.View,
        SplitNode split => FindFirstView(split.First) ?? FindFirstView(split.Second),
        _ => null,
    };

    private static LeafNode? FindLeaf(PaneNode? node, TerminalView view) => node switch
    {
        null => null,
        LeafNode leaf => ReferenceEquals(leaf.View, view) ? leaf : null,
        SplitNode split => FindLeaf(split.First, view) ?? FindLeaf(split.Second, view),
        _ => null,
    };

    /// <summary>整树重建。控件实例保持不变，会话不会重启。</summary>
    private void Render()
    {
        // 旧的分屏 Grid 里还挂着终端控件，必须先摘下来，否则加入新 Grid 时会因“已有父级”失败
        foreach (var view in _views)
        {
            if (view.Parent is Panel panel)
            {
                panel.Children.Remove(view);
            }
        }

        Children.Clear();

        if (_root is not null)
        {
            Children.Add(Build(_root));
        }
    }

    private FrameworkElement Build(PaneNode node)
    {
        if (node is LeafNode leaf)
        {
            return leaf.View;
        }

        var split = (SplitNode)node;
        var grid = new Grid();
        var splitter = CreateSplitter(grid, split);
        var ratio = Math.Clamp(split.Ratio, MinRatio, 1 - MinRatio);
        var firstLength = new GridLength(ratio, GridUnitType.Star);
        var secondLength = new GridLength(1 - ratio, GridUnitType.Star);

        if (split.Orientation == Orientation.Horizontal)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = firstLength });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SplitterHitWidth) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = secondLength });

            var left = Build(split.First);
            Grid.SetColumn(left, 0);

            Grid.SetColumn(splitter, 1);

            var right = Build(split.Second);
            Grid.SetColumn(right, 2);

            grid.Children.Add(left);
            grid.Children.Add(splitter);
            grid.Children.Add(right);
        }
        else
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = firstLength });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(SplitterHitWidth) });
            grid.RowDefinitions.Add(new RowDefinition { Height = secondLength });

            var top = Build(split.First);
            Grid.SetRow(top, 0);

            Grid.SetRow(splitter, 1);

            var bottom = Build(split.Second);
            Grid.SetRow(bottom, 2);

            grid.Children.Add(top);
            grid.Children.Add(splitter);
            grid.Children.Add(bottom);
        }

        return grid;
    }

    /// <summary>
    /// 自绘分隔条：6px 宽的透明命中区，中间画一条 1px 的线；拖动时只改两侧轨道宽度，
    /// 不重建整棵树，避免终端闪烁。
    /// </summary>
    private FrameworkElement CreateSplitter(Grid grid, SplitNode split)
    {
        var horizontal = split.Orientation == Orientation.Horizontal;
        var dark = ActualTheme == ElementTheme.Dark;

        var line = new Border
        {
            Background = new SolidColorBrush(dark
                ? Color.FromArgb(0xFF, 0x45, 0x45, 0x45)
                : Color.FromArgb(0xFF, 0xD8, 0xD8, 0xD8)),
            Width = horizontal ? 1 : double.NaN,
            Height = horizontal ? double.NaN : 1,
            HorizontalAlignment = horizontal ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
            VerticalAlignment = horizontal ? VerticalAlignment.Stretch : VerticalAlignment.Center,
        };

        var hit = new SplitterElement
        {
            Background = new SolidColorBrush(Colors.Transparent),
            Cursor = InputSystemCursor.Create(horizontal
                ? InputSystemCursorShape.SizeWestEast
                : InputSystemCursorShape.SizeNorthSouth),
        };
        hit.Children.Add(line);

        var dragging = false;

        hit.PointerPressed += (_, e) =>
        {
            dragging = true;
            hit.CapturePointer(e.Pointer);
            e.Handled = true;
        };

        hit.PointerMoved += (_, e) =>
        {
            if (!dragging)
            {
                return;
            }

            var position = e.GetCurrentPoint(grid).Position;
            var total = horizontal ? grid.ActualWidth : grid.ActualHeight;
            var usable = total - SplitterHitWidth;

            if (usable <= 0)
            {
                return;
            }

            var offset = (horizontal ? position.X : position.Y) - SplitterHitWidth / 2;
            var ratio = Math.Clamp(offset / usable, MinRatio, 1 - MinRatio);
            split.Ratio = ratio;

            if (horizontal)
            {
                grid.ColumnDefinitions[0].Width = new GridLength(ratio, GridUnitType.Star);
                grid.ColumnDefinitions[2].Width = new GridLength(1 - ratio, GridUnitType.Star);
            }
            else
            {
                grid.RowDefinitions[0].Height = new GridLength(ratio, GridUnitType.Star);
                grid.RowDefinitions[2].Height = new GridLength(1 - ratio, GridUnitType.Star);
            }
        };

        hit.PointerReleased += (_, e) =>
        {
            dragging = false;
            hit.ReleasePointerCapture(e.Pointer);
        };

        hit.PointerCaptureLost += (_, _) => dragging = false;

        return hit;
    }

    /// <summary>命中区本身是透明的，光标只能通过派生类设置（ProtectedCursor）。</summary>
    private sealed class SplitterElement : Grid
    {
        public InputCursor? Cursor
        {
            set => ProtectedCursor = value;
        }
    }

    private abstract class PaneNode
    {
        public SplitNode? Parent { get; set; }
    }

    private sealed class LeafNode(TerminalView view) : PaneNode
    {
        public TerminalView View { get; } = view;
    }

    private sealed class SplitNode : PaneNode
    {
        public SplitNode(Orientation orientation, PaneNode first, PaneNode second)
        {
            Orientation = orientation;
            First = first;
            Second = second;
            first.Parent = this;
            second.Parent = this;
        }

        public Orientation Orientation { get; }

        public PaneNode First { get; set; }

        public PaneNode Second { get; set; }

        /// <summary>第一侧占的比例，拖动分隔条时更新。</summary>
        public double Ratio { get; set; } = 0.5;
    }
}
