using System.Windows;
using HdrTracer.Core;
using Loc = HdrTracer.Core.Localization;

namespace HdrTracer.App;

public partial class SettingsWindow : Window
{
    public AppSettings Settings { get; }
    public bool RemovableChanged { get; private set; }
    public bool HiddenSystemChanged { get; private set; }

    private readonly bool _initialRemovable;
    private readonly bool _initialShowHidden;
    private readonly bool _initialAutoStart;
    private readonly string _initialExcluded;
    private readonly bool _initialHotkey;

    public bool HotkeyChanged { get; private set; }

    public bool ExcludedChanged { get; private set; }

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        Settings = settings;
        _initialRemovable = settings.IndexRemovableDrives;
        _initialShowHidden = settings.ShowHiddenSystemItems;
        RemovableCheckBox.IsChecked = settings.IndexRemovableDrives;
        _initialAutoStart = AutoStartManager.IsRegistered();   
        AutoStartCheckBox.IsChecked = _initialAutoStart;
        ExcludedBox.Text = string.Join("; ", settings.ExcludedFolders);
        _initialExcluded = NormalizeExcluded(ExcludedBox.Text);
        _initialHotkey = settings.GlobalHotkeyEnabled;
        HotkeyCheckBox.IsChecked = _initialHotkey;
        RestoreSearchCheckBox.IsChecked = settings.RestoreLastSearch;
        TrayCheckBox.IsChecked = settings.MinimizeToTrayOnClose;
        HiddenSystemCheckBox.IsChecked = settings.ShowHiddenSystemItems;

        ApplyTexts();

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                DialogResult = false;
                Close();
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Enter)
            {
                OkButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        };
    }

    private void ApplyTexts()
    {
        Title          = Loc.T("settings.title");
        TitleText.Text = Loc.T("settings.title");
        SecIndexing.Text = Loc.T("settings.indexing");
        UsbTitle.Text  = Loc.T("settings.usb");
        UsbDesc.Text   = Loc.T("settings.usb.desc");
        TrayTitle.Text = Loc.T("settings.tray");
        TrayDesc.Text  = Loc.T("settings.tray.desc");
        HiddenTitle.Text = Loc.T("settings.hidden");
        HiddenDesc.Text  = Loc.T("settings.hidden.desc");
        AutoStartTitle.Text = Loc.T("settings.autostart");
        AutoStartDesc.Text  = Loc.T("settings.autostart.desc");
        ExcludedTitle.Text  = Loc.T("settings.excluded");
        ExcludedDesc.Text   = Loc.T("settings.excluded.desc");
        HotkeyTitle.Text    = Loc.T("settings.hotkey");
        HotkeyDesc.Text     = Loc.T("settings.hotkey.desc");
        RestoreSearchTitle.Text = Loc.T("settings.restoreSearch");
        RestoreSearchDesc.Text  = Loc.T("settings.restoreSearch.desc");
        OkButton.Content = Loc.T("settings.ok");
    }

    private void RemovableCheckBox_Click(object sender, RoutedEventArgs e)
    {
        bool newValue = RemovableCheckBox.IsChecked ?? false;
        if (newValue && !_initialRemovable)
            StatusText.Text = Loc.T("settings.usbOn");
        else if (!newValue && _initialRemovable)
            StatusText.Text = Loc.T("settings.usbOff");
        else
            StatusText.Text = "";
    }

    private static string NormalizeExcluded(string raw)
    {
        var parts = new List<string>();
        foreach (var p in raw.Split(';'))
        {
            var n = p.Trim().ToLowerInvariant();
            if (n.Length > 0) parts.Add(n);
        }
        parts.Sort(StringComparer.Ordinal);
        return string.Join(";", parts);
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        bool newValue = RemovableCheckBox.IsChecked ?? false;
        if (newValue != _initialRemovable)
        {
            Settings.IndexRemovableDrives = newValue;
            Settings.Save();
            RemovableChanged = true;
        }

        bool showHidden = HiddenSystemCheckBox.IsChecked ?? false;
        if (showHidden != _initialShowHidden)
        {
            Settings.ShowHiddenSystemItems = showHidden;
            HiddenSystemChanged = true;
        }

        Settings.MinimizeToTrayOnClose = TrayCheckBox.IsChecked ?? false;

        var excluded = new List<string>();
        foreach (var part in ExcludedBox.Text.Split(';'))
        {
            var n = part.Trim();
            if (n.Length > 0) excluded.Add(n);
        }
        Settings.ExcludedFolders = excluded;
        ExcludedChanged = NormalizeExcluded(ExcludedBox.Text) != _initialExcluded;

        bool hk = HotkeyCheckBox.IsChecked ?? false;
        HotkeyChanged = hk != _initialHotkey;
        Settings.GlobalHotkeyEnabled = hk;
        Settings.RestoreLastSearch = RestoreSearchCheckBox.IsChecked ?? false;

        Settings.Save();

        bool autoStart = AutoStartCheckBox.IsChecked ?? false;
        if (autoStart != _initialAutoStart)
        {
            bool ok = autoStart ? AutoStartManager.Register() : AutoStartManager.Unregister();
            if (!ok)
                InfoDialog.Show(this, Loc.T("common.error"), Loc.T("settings.autostart.fail"));
        }

        DialogResult = true;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
