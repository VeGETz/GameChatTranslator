using System.ComponentModel;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using ScreenTranslator.Infrastructure;
using ScreenTranslator.Services.Settings;
using ScreenTranslator.ViewModels;

namespace ScreenTranslator.Views;

[SupportedOSPlatform("windows")]
public partial class OverlayWindow : Window
{
    private readonly OverlayViewModel _vm;
    private readonly SettingsStore _settings;

    public OverlayWindow(OverlayViewModel vm, SettingsStore settings)
    {
        InitializeComponent();
        _vm = vm;
        _settings = settings;
        DataContext = vm;

        var s = _settings.Current;
        Left = s.OverlayLeft;
        Top = s.OverlayTop;
        Width = s.OverlayWidth;
        Height = s.OverlayHeight;
        CaptureRow.Height = new GridLength(Math.Max(40, s.OverlayHeight - s.TranslationPanelHeight));

        _vm.PropertyChanged += OnVmPropertyChanged;
        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
        ApplyLocked(_vm.Locked);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // Apply the initial click-through style once the HWND is live.
        ApplyLocked(_vm.Locked);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverlayViewModel.Locked))
            ApplyLocked(_vm.Locked);
        else if (e.PropertyName == nameof(OverlayViewModel.Visible))
        {
            if (_vm.Visible) Show();
            else Hide();
        }
    }

    /// <summary>
    /// Returns the screen rectangle (in desktop pixels) of the capture region.
    /// Called from the translation loop on a background thread, so it marshals to the UI thread.
    /// </summary>
    public Rectangle? GetCaptureRect()
    {
        if (!Dispatcher.CheckAccess())
            return Dispatcher.Invoke(GetCaptureRect);

        if (!IsVisible) return null;

        // Convert DIP coordinates to physical pixels using the visual's DPI.
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null) return null;
        var transform = source.CompositionTarget.TransformToDevice;

        var topLeftDip = PointToScreen(new System.Windows.Point(0, 0));
        var captureHeightDip = CaptureRow.ActualHeight;
        var widthDip = ActualWidth;

        var physW = (int)Math.Round(widthDip * transform.M11);
        var physH = (int)Math.Round(captureHeightDip * transform.M22);
        var physX = (int)Math.Round(topLeftDip.X);
        var physY = (int)Math.Round(topLeftDip.Y);

        if (physW <= 0 || physH <= 0) return null;
        return new Rectangle(physX, physY, physW, physH);
    }

    // ---------- Drag / resize handlers ----------

    private void CaptureArea_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm.Locked) return;
        try { DragMove(); } catch { /* ignore re-entrancy */ }
        PersistLayout();
    }

    private void ResizeThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_vm.Locked) return;
        var newW = Math.Max(80, Width + e.HorizontalChange);
        var newCapH = Math.Max(40, CaptureRow.ActualHeight + e.VerticalChange);
        Width = newW;
        CaptureRow.Height = new GridLength(newCapH);
        // Total window height = capture + translation panel (fixed from settings)
        Height = newCapH + Math.Max(40, _settings.Current.TranslationPanelHeight);
        PersistLayout();
    }

    private void SplitterThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_vm.Locked) return;
        var newCapH = Math.Max(40, CaptureRow.ActualHeight + e.VerticalChange);
        CaptureRow.Height = new GridLength(newCapH);
        // Keep window total height constant; translation panel shrinks/grows to fill.
        PersistLayout();
    }

    private void PersistLayout()
    {
        var s = _settings.Current;
        s.OverlayLeft = Left;
        s.OverlayTop = Top;
        s.OverlayWidth = Width;
        s.OverlayHeight = Height;
        s.TranslationPanelHeight = Math.Max(40, Height - CaptureRow.ActualHeight);
        _settings.Save();
    }

    // ---------- Click-through lock ----------

    private void ApplyLocked(bool locked)
    {
        var helper = new WindowInteropHelper(this);
        var hwnd = helper.Handle;
        if (hwnd == IntPtr.Zero) return;

        var ex = WinInterop.GetWindowLong(hwnd, WinInterop.GWL_EXSTYLE);
        // Always toolwindow + no-activate so we don't steal focus.
        ex |= WinInterop.WS_EX_TOOLWINDOW | WinInterop.WS_EX_NOACTIVATE | WinInterop.WS_EX_LAYERED;
        if (locked)
            ex |= WinInterop.WS_EX_TRANSPARENT;
        else
            ex &= ~WinInterop.WS_EX_TRANSPARENT;
        WinInterop.SetWindowLong(hwnd, WinInterop.GWL_EXSTYLE, ex);

        ResizeThumb.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
        SplitterThumb.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
        CaptureBorder.BorderThickness = new Thickness(locked ? 0 : 2);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        PersistLayout();
    }
}
