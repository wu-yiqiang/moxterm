using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using MoxTerm.Services;

namespace MoxTerm.Views;

/// <summary>
/// 规划中功能的统一占位页：按导航传入的 Tag 从 <see cref="FeatureCatalog"/> 取内容渲染。
/// </summary>
public sealed partial class FeaturePage : Page
{
    private FeatureInfo? _info;
    private string _title = string.Empty;

    /// <summary>语言切换回调；页面不缓存，离开时必须退订，避免 Loc 持有已废弃页面。</summary>
    private readonly EventHandler _onLocaleChanged;

    /// <summary>界面文案源；x:Bind 绑定到它的属性，语言切换时自动刷新。</summary>
    private Loc Loc => Loc.Instance;

    public FeaturePage()
    {
        InitializeComponent();
        _onLocaleChanged = (_, _) => Render();
        Loc.Changed += _onLocaleChanged;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Loc.Changed -= _onLocaleChanged;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var key = e.Parameter as string;
        _info = FeatureCatalog.Get(key);

        _title = key switch
        {
            "keygen" => Loc.NavKeyGen,
            _ => Loc.NavXServer,
        };

        Render();
    }

    private void Render()
    {
        if (_info is null)
        {
            return;
        }

        FeatureIcon.Symbol = _info.Icon;
        FeatureTitle.Text = _title;
        FeatureDesc.Text = _info.Description();
        FeatureItems.ItemsSource = _info.Items();
    }
}
