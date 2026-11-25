using Microsoft.UI.Xaml.Controls;
using MoxTerm.Services;

namespace MoxTerm.Views;

/// <summary>快捷键说明页：列出终端会话支持的按键组合。</summary>
public sealed partial class ShortcutsPage : Page
{
    /// <summary>界面文案源；x:Bind 绑定到它的属性，语言切换时自动刷新。</summary>
    private Loc Loc => Loc.Instance;

    public ShortcutsPage()
    {
        InitializeComponent();
    }
}
