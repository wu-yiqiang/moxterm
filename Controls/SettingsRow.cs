using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MoxTerm.Controls;

/// <summary>
/// 设置页的一行：左侧图标与标题/说明，右侧放具体的设置控件。
/// <para>
/// 右侧控件通过 <see cref="ContentControl.Content"/> 传入。视觉结构（分隔线、图标列、说明列、
/// 内容列）定义在 SettingsPage.xaml 的隐式样式里。
/// </para>
/// </summary>
public sealed class SettingsRow : ContentControl
{
    /// <summary>左侧图标。</summary>
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph),
        typeof(Symbol),
        typeof(SettingsRow),
        new PropertyMetadata(Symbol.Setting));

    /// <summary>主标题。</summary>
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(SettingsRow),
        new PropertyMetadata(string.Empty));

    /// <summary>标题下方的说明文字，留空时该行只有标题。</summary>
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description),
        typeof(string),
        typeof(SettingsRow),
        new PropertyMetadata(string.Empty));

    /// <summary>
    /// 顶部分隔线的可见性。同一张卡片里除第一行外都保留分隔线，
    /// 第一行设为 <see cref="Visibility.Collapsed"/>，避免与卡片边框重叠。
    /// </summary>
    public static readonly DependencyProperty DividerVisibilityProperty = DependencyProperty.Register(
        nameof(DividerVisibility),
        typeof(Visibility),
        typeof(SettingsRow),
        new PropertyMetadata(Visibility.Visible));

    public Symbol Glyph
    {
        get => (Symbol)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public Visibility DividerVisibility
    {
        get => (Visibility)GetValue(DividerVisibilityProperty);
        set => SetValue(DividerVisibilityProperty, value);
    }
}
