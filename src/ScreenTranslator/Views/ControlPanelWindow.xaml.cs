using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using ScreenTranslator.Services.Hotkey;
using ScreenTranslator.Services.Settings;
using ScreenTranslator.ViewModels;

namespace ScreenTranslator.Views;

[SupportedOSPlatform("windows10.0.19041.0")]
public partial class ControlPanelWindow : Window
{
    private readonly ControlPanelViewModel _vm;
    private readonly Win32HotkeyService _hotkey;

    public ControlPanelWindow(ControlPanelViewModel vm, Win32HotkeyService hotkey, SettingsStore settings)
    {
        InitializeComponent();
        _vm = vm;
        _hotkey = hotkey;
        DataContext = vm;

        SourceInitialized += (_, _) =>
        {
            var source = (HwndSource)PresentationSource.FromVisual(this)!;
            _hotkey.Attach(source);
            var s = settings.Current;
            _hotkey.Register(s.HotkeyModifiers, s.HotkeyVirtualKey);
        };
    }

    private void HotkeyBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return; // ignore modifier-only presses

        uint mods = 0;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) mods |= WinModifiers.Control;
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) mods |= WinModifiers.Alt;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) mods |= WinModifiers.Shift;
        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) mods |= WinModifiers.Win;

        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        _vm.CaptureHotkey(mods, vk);
    }

    private void AddOcrLanguage_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new AddOcrLanguageDialog { Owner = this };
        dialog.ShowDialog();

        // After the dialog closes, re-enumerate installed OCR languages so the main dropdown shows any new ones.
        _vm.RefreshOcrLanguagesCommand.Execute(null);
    }
}
