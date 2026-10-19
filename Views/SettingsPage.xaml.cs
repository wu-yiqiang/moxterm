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

            // 配色默认“跟随主题”（不在下拉列表里）：选中当前主题实际落到的那一个具体方案
            var schemeKey = settings.ColorScheme;
            if (schemeKey == TerminalColorScheme.FollowTheme.Key)
            {
                schemeKey = TerminalColorScheme.Resolve(schemeKey, ActualTheme).Key;
            }

            ColorSchemeComboBox.SelectedItem = TerminalColorScheme.FromKey(schemeKey);
            FontFamilyComboBox.SelectedItem = TerminalFont.FromFamily(settings.FontFamily);
            FontSizeBox.Value = settings.FontSize;
            LineHeightBox.Value = settings.LineHeight;

            var shells = DefaultShellComboBox.Items.OfType<ShellProfile>().ToList();
            DefaultShellComboBox.SelectedItem =
                shells.FirstOrDefault(shell => shell.Name == settings.DefaultShell) ?? shells.FirstOrDefault();

            WorkingDirectoryBox.Text = settings.WorkingDirectory ?? string.Empty;
            UseParentDirCheckBox.IsChecked = string.IsNullOrEmpty(settings.WorkingDirectory);
            UpdateWorkingDirectoryState();
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
        if (_loading || UseParentDirCheckBox.IsChecked == true)
        {
            return;
        }

        var text = WorkingDirectoryBox.Text.Trim();

        // 留空时自动切回“使用父进程目录”；填了不存在的目录则保留输入并提示，不写入设置
        if (text.Length == 0)
        {
            UseParentDirCheckBox.IsChecked = true;
            return;
        }

        if (!Directory.Exists(text))
        {
            UpdateWorkingDirectoryHint();
            return;
        }

        App.Settings.Current.WorkingDirectory = text;
        UpdateWorkingDirectoryHint();
        ApplyAndSave();
    }

    /// <summary>根据“使用父进程目录”复选框状态联动启用/禁用输入框与浏览按钮。</summary>
    private void UpdateWorkingDirectoryState()
    {
        var useParent = UseParentDirCheckBox.IsChecked == true;
        WorkingDirectoryBox.IsEnabled = !useParent;
        BrowseButton.IsEnabled = !useParent;
    }

    private void OnUseParentDirToggled(object sender, RoutedEventArgs e)
    {
        UpdateWorkingDirectoryState();

        if (_loading)
        {
            return;
        }

        if (UseParentDirCheckBox.IsChecked == true)
        {
            // 使用父进程目录：清空路径并写入 null
            WorkingDirectoryBox.Text = string.Empty;
            App.Settings.Current.WorkingDirectory = null;
        }
        else
        {
            // 取消勾选：若输入框已有有效目录则写入，否则留空等用户输入
            var text = WorkingDirectoryBox.Text.Trim();
            App.Settings.Current.WorkingDirectory =
                text.Length > 0 && Directory.Exists(text) ? text : null;
        }

        UpdateWorkingDirectoryHint();
        ApplyAndSave();
    }

    /// <summary>折叠/展开起始目录的详细设置区。</summary>
    private void OnWorkingDirHeaderClick(object sender, RoutedEventArgs e)
    {
        var expand = WorkingDirPanel.Visibility == Visibility.Collapsed;
        WorkingDirPanel.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        WorkingDirChevron.Glyph = expand ? "\uE70E" : "\uE70D"; // 展开上箭头 / 收起下箭头
    }

    private void UpdateWorkingDirectoryHint()
    {
        if (UseParentDirCheckBox.IsChecked == true)
        {
            WorkingDirectorySummary.Text = Loc.UseParentDir;
            return;
        }

        var text = WorkingDirectoryBox.Text.Trim();

        if (text.Length == 0)
        {
            WorkingDirectorySummary.Text = Loc.WorkingDirEmpty;
            return;
        }

        WorkingDirectorySummary.Text = Directory.Exists(text)
            ? text
            : Loc.WorkingDirInvalid;
    }

    /// <summary>打开文件夹选择器，用所选目录填充起始目录输入框并保存。</summary>
    private async void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        if (App.Shell is null)
        {
            return;
        }

        var picker = new Windows.Storage.Pickers.FolderPicker();

        // 非打包应用需要手动把选择器关联到窗口句柄，否则会立即返回 null
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Shell);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
        // FolderPicker 仍要求至少一个文件类型过滤器，这是 API 的历史遗留
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        // 选择了具体目录即意味着不再使用父进程目录；
        // 先填入路径再取消勾选，OnUseParentDirToggled 会读取有效路径并保存
        WorkingDirectoryBox.Text = folder.Path;
        UseParentDirCheckBox.IsChecked = false;
        UpdateWorkingDirectoryHint();
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
}
