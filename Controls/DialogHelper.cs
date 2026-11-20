using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MoxTerm.Controls;

/// <summary>
/// ContentDialog 的按钮排布辅助：WinUI 固定按“主按钮在左、关闭按钮在右”排布，
/// 这里把两者对调，让主操作落在右侧，与项目里其它对话框保持一致。
/// </summary>
public static class DialogHelper
{
    public static void SwapButtons(ContentDialog dialog)
    {
        if (FindVisualChild<Button>(dialog, "PrimaryButton") is not { } primary ||
            FindVisualChild<Button>(dialog, "CloseButton") is not { } close ||
            !ReferenceEquals(primary.Parent, close.Parent))
        {
            return;
        }

        switch (close.Parent)
        {
            case Grid { ColumnDefinitions.Count: > 1 } grid:
                var primaryColumn = Grid.GetColumn(primary);
                Grid.SetColumn(primary, Grid.GetColumn(close));
                Grid.SetColumn(close, primaryColumn);
                break;

            case StackPanel panel:
                var primaryIndex = panel.Children.IndexOf(primary);
                var closeIndex = panel.Children.IndexOf(close);
                if (primaryIndex < 0 || closeIndex < 0)
                {
                    return;
                }

                panel.Children.Remove(primary);
                panel.Children.Remove(close);
                panel.Children.Insert(Math.Min(primaryIndex, closeIndex), close);
                panel.Children.Insert(Math.Max(primaryIndex, closeIndex), primary);
                break;
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);

        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);

            if (child is T element && element.Name == name)
            {
                return element;
            }

            if (FindVisualChild<T>(child, name) is { } result)
            {
                return result;
            }
        }

        return null;
    }
}
