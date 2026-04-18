using System.Runtime.Versioning;
using System.Windows.Interop;
using ScreenTranslator.Infrastructure;

namespace ScreenTranslator.Services.Hotkey;

/// <summary>
/// Global hotkey via Win32 RegisterHotKey. Hooks into a WPF window's HwndSource to receive WM_HOTKEY.
/// Call <see cref="Attach"/> after the control panel window is source-initialized.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Win32HotkeyService : IHotkeyService
{
    private const int HotkeyId = 0xBEEF;

    private HwndSource? _source;
    private IntPtr _hwnd;
    private bool _registered;

    public event Action? Pressed;

    public void Attach(HwndSource source)
    {
        if (_source is not null) return;
        _source = source;
        _hwnd = source.Handle;
        source.AddHook(WndProc);
    }

    public bool Register(uint modifiers, uint virtualKey)
    {
        if (_hwnd == IntPtr.Zero) return false;
        Unregister();
        var ok = WinInterop.RegisterHotKey(_hwnd, HotkeyId, modifiers | WinInterop.MOD_NOREPEAT, virtualKey);
        _registered = ok;
        return ok;
    }

    public void Unregister()
    {
        if (_hwnd == IntPtr.Zero || !_registered) return;
        WinInterop.UnregisterHotKey(_hwnd, HotkeyId);
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WinInterop.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }
    }
}
