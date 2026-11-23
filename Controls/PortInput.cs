using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Globalization.NumberFormatting;

namespace MoxTerm.Controls;

/// <summary>
/// 端口输入框的共用工厂与读取逻辑，供隧道、Wake On Lan、网络服务等对话框复用。
/// </summary>
public static class PortInput
{
    /// <summary>创建 1–65535 的整数端口输入框，带步进箭头。</summary>
    public static NumberBox Create(string header, double value) => new()
    {
        Header = header,
        Minimum = 1,
        Maximum = 65535,
        SmallChange = 1,
        LargeChange = 10,
        AcceptsExpression = false,
        Value = value,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        NumberFormatter = new DecimalFormatter
        {
            FractionDigits = 0,
            IntegerDigits = 1,
        },
    };

    /// <summary>
    /// 读取端口：NumberBox 的 Value 与 Text 都要等到输入提交（失焦 / 回车）才更新，
    /// 光标还停在输入框里就点按钮时会读到旧值，所以以模板里输入框的当前文本为准。
    /// </summary>
    public static int Read(NumberBox box)
    {
        if (FindInputTextBox(box) is { } input)
        {
            return int.TryParse(input.Text?.Trim(), out var current) ? current : 0;
        }

        return int.TryParse(box.Text?.Trim(), out var parsed)
            ? parsed
            : double.IsNaN(box.Value) ? 0 : (int)box.Value;
    }

    private static TextBox? FindInputTextBox(DependencyObject parent)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);

        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);

            if (child is TextBox { Name: "InputBox" } input)
            {
                return input;
            }

            if (FindInputTextBox(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
