using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MoxTerm.Services;

namespace MoxTerm.Views;

/// <summary>
/// 设置页：外观、终端与输入行为。改动即时生效并写入本地配置。
/// <para>
/// 页面订阅了 <see cref="SettingsService.Changed"/>，因此终端工具栏等其它入口改动设置后，
/// 这里的控件也会同步刷新。
/// </para>
/// </summary>
public sealed partial class SettingsPage : Page
{
    /// <summary>装载控件值时置位，用于屏蔽控件事件回调，避免把“回填”当成用户改动。</summary>
    private bool _loading;

    /// <summary>界面文案源；x:Bind 绑定到它的属性，语言切换时自动刷新。</summary>
    private Loc Loc => Loc.Instance;

    public SettingsPage()
    {
        InitializeComponent();

        DefaultShellComboBox.ItemsSource = ShellProfile.Discover();
        DefaultShellComboBox.DisplayMemberPath = nameof(ShellProfile.Name);

        ColorSchemeComboBox.DisplayMemberPath = nameof(TerminalColorScheme.Name);
        FontFamilyComboBox.DisplayMemberPath = nameof(TerminalFont.Name);
        CursorStyleComboBox.DisplayMemberPath = nameof(TerminalCursorOption.Name);
        BackendComboBox.DisplayMemberPath = nameof(TerminalBackend.Name);

        App.Settings.Changed += OnSettingsChanged;
        Loc.Changed += OnLocChanged;

        // 语言相关的下拉项与动态描述
        ReloadLocalizedItems();
        LoadFromSettings();
    }

    /// <summary>语言切换后重建所有带显示名的下拉项（显示名只在构造时解析，必须重填才能换语言）。</summary>
    private void OnLocChanged(object? sender, EventArgs e)
    {
        ReloadLocalizedItems();
        LoadFromSettings();
    }

    private void ReloadLocalizedItems()
    {
        _loading = true;
        try
        {
            LanguageComboBox.ItemsSource = new[] { Loc.LangSystem, "中文", "English" };
            ThemeComboBox.ItemsSource = new[] { Loc.ThemeSystem, Loc.ThemeLight, Loc.ThemeDark };
            ColorSchemeComboBox.ItemsSource = TerminalColorScheme.All;
            FontFamilyComboBox.ItemsSource = TerminalFont.Discover();
            CursorStyleComboBox.ItemsSource = TerminalCursorOption.All;
            BackendComboBox.ItemsSource = TerminalBackend.All;

            AboutRow.Description =
                Loc.AboutVersion(typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");
        }
        finally
        {
            _loading = false;
        }
    }

    private void LoadFromSettings()
    {
        var settings = App.Settings.Current;

        _loading = true;
        try
        {
            LanguageComboBox.SelectedIndex = settings.Language switch
            {
                "zh-CN" => 1,
                "en-US" => 2,
                _ => 0,
            };

            ThemeComboBox.SelectedIndex = settings.Theme switch
            {
                "Light" => 1,
                "Dark" => 2,
                _ => 0,
            };

            ColorSchemeComboBox.SelectedItem = TerminalColorScheme.FromKey(settings.ColorScheme);
            FontFamilyComboBox.SelectedItem = TerminalFont.FromFamily(settings.FontFamily);
            FontSizeBox.Value = settings.FontSize;
            LineHeightBox.Value = settings.LineHeight;

            var shells = DefaultShellComboBox.Items.OfType<ShellProfile>().ToList();
            DefaultShellComboBox.SelectedItem =
                shells.FirstOrDefault(shell => shell.Name == settings.DefaultShell) ?? shells.FirstOrDefault();

            WorkingDirectoryBox.Text = settings.WorkingDirectory ?? string.Empty;
            UpdateWorkingDirectoryHint();

            ScrollbackBox.Value = settings.ScrollbackLines;
            CursorStyleComboBox.SelectedItem = TerminalCursorOption.FromName(settings.CursorStyle);
            CursorBlinkSwitch.IsOn = settings.CursorBlink;

            BackendComboBox.SelectedItem = TerminalBackend.FromName(settings.Backend);

            ConfirmPasteSwitch.IsOn = settings.ConfirmMultilinePaste;
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => LoadFromSettings();

    /// <summary>持久化并即时应用到所有已打开的会话。</summary>
    private static void ApplyAndSave()
    {
        App.Settings.Save();
        TerminalPage.Current?.ApplySettings();
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        App.Settings.Current.Language = LanguageComboBox.SelectedIndex switch
        {
            1 => "zh-CN",
            2 => "en-US",
            _ => "Default",
        };

        // Save 触发 Settings.Changed，App 里挂的处理器会调用 Loc.Apply 完成切换
        ApplyAndSave();
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        App.Settings.Current.Theme = ThemeComboBox.SelectedIndex switch
        {
            1 => "Light",
            2 => "Dark",
            _ => "Default",
        };

        App.Shell?.ApplyTheme(App.Settings.ResolveTheme());
        ApplyAndSave();
    }

    private void OnColorSchemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ColorSchemeComboBox.SelectedItem is not TerminalColorScheme scheme)
        {
            return;
        }

        App.Settings.Current.ColorScheme = scheme.Key;
        ApplyAndSave();
    }

    private void OnFontFamilyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || FontFamilyComboBox.SelectedItem is not TerminalFont font)
        {
            return;
        }

        App.Settings.Current.FontFamily = font.Family;
        ApplyAndSave();
    }

    private void OnFontSizeChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(args.NewValue))
        {
            return;
        }

        App.Settings.Current.FontSize = args.NewValue;
        ApplyAndSave();
    }

    private void OnLineHeightChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(args.NewValue))
        {
            return;
        }

        App.Settings.Current.LineHeight = args.NewValue;
        ApplyAndSave();
    }

    private void OnDefaultShellChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || DefaultShellComboBox.SelectedItem is not ShellProfile profile)
        {
            return;
        }

        App.Settings.Current.DefaultShell = profile.Name;
        ApplyAndSave();
    }

    private void OnWorkingDirectoryLostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var text = WorkingDirectoryBox.Text.Trim();

        // 留空表示沿用进程当前目录；填了不存在的目录则保留输入并提示，不写入设置
        if (text.Length > 0 && !Directory.Exists(text))
        {
            UpdateWorkingDirectoryHint();
            return;
        }

        App.Settings.Current.WorkingDirectory = text.Length == 0 ? null : text;
        UpdateWorkingDirectoryHint();
        ApplyAndSave();
    }

    private void UpdateWorkingDirectoryHint()
    {
        var text = WorkingDirectoryBox.Text.Trim();

        if (text.Length == 0)
        {
            WorkingDirectoryRow.Description = Loc.WorkingDirEmpty;
            return;
        }

        WorkingDirectoryRow.Description = Directory.Exists(text)
            ? Loc.WorkingDirValid
            : Loc.WorkingDirInvalid;
    }

    private void OnScrollbackChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(args.NewValue))
        {
            return;
        }

        App.Settings.Current.ScrollbackLines = (int)args.NewValue;
        ApplyAndSave();
    }

    private void OnCursorStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CursorStyleComboBox.SelectedItem is not TerminalCursorOption option)
        {
            return;
        }

        App.Settings.Current.CursorStyle = option.Style.ToString();
        ApplyAndSave();
    }

    private void OnCursorBlinkToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        App.Settings.Current.CursorBlink = CursorBlinkSwitch.IsOn;
        ApplyAndSave();
    }

    private void OnBackendChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BackendComboBox.SelectedItem is not TerminalBackend backend)
        {
            return;
        }

        BackendRow.Description = backend.Description;

        if (_loading)
        {
            return;
        }

        App.Settings.Current.Backend = backend.Kind.ToString();
        ApplyAndSave();
    }

    private void OnConfirmPasteToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        App.Settings.Current.ConfirmMultilinePaste = ConfirmPasteSwitch.IsOn;
        ApplyAndSave();
    }

    private async void OnResetClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.Reset,
            Content = Loc.ResetDialogContent,
            PrimaryButtonText = Loc.Reset,
            CloseButtonText = Loc.Cancel,
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        // Reset 会触发 Changed，界面随之回填默认值
        App.Settings.Reset();
        App.Shell?.ApplyTheme(App.Settings.ResolveTheme());
        TerminalPage.Current?.ApplySettings();
    }
}
