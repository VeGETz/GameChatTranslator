using System.Net.Http;
using System.Runtime.Versioning;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ScreenTranslator.Services.Capture;
using ScreenTranslator.Services.Hotkey;
using ScreenTranslator.Services.Ocr;
using ScreenTranslator.Services.Pipeline;
using ScreenTranslator.Services.Settings;
using ScreenTranslator.Services.Translation;
using ScreenTranslator.ViewModels;
using ScreenTranslator.Views;

namespace ScreenTranslator;

[SupportedOSPlatform("windows10.0.19041.0")]
public partial class App : Application
{
    private IServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();

        // Settings
        services.AddSingleton<SettingsStore>();

        // HttpClient for translators
        services.AddSingleton(sp => new HttpClient(new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        })
        {
            Timeout = TimeSpan.FromSeconds(8),
        });

        // Core services
        services.AddSingleton<IScreenCapturer, GdiScreenCapturer>();
        services.AddSingleton<IOcrService, WindowsOcrService>();

        services.AddSingleton<GoogleFreeTranslator>();
        services.AddSingleton<GoogleCloudTranslator>();
        services.AddSingleton<LlmTranslator>();
        services.AddSingleton<TranslatorFactory>();

        services.AddSingleton<Win32HotkeyService>();
        services.AddSingleton<IHotkeyService>(sp => sp.GetRequiredService<Win32HotkeyService>());

        services.AddSingleton<TranslationLoop>();

        // View models
        services.AddSingleton<OverlayViewModel>();
        services.AddSingleton<ControlPanelViewModel>();

        // Windows
        services.AddSingleton<OverlayWindow>();
        services.AddSingleton<ControlPanelWindow>();

        _services = services.BuildServiceProvider();

        var overlay = _services.GetRequiredService<OverlayWindow>();
        var control = _services.GetRequiredService<ControlPanelWindow>();
        var loop = _services.GetRequiredService<TranslationLoop>();

        // Wire the capture-rect provider to the overlay.
        loop.GetCaptureRect = overlay.GetCaptureRect;

        // Keep overlay visibility synced to settings + view model.
        var settings = _services.GetRequiredService<SettingsStore>();
        var overlayVm = _services.GetRequiredService<OverlayViewModel>();
        if (settings.Current.ShowOverlay)
            overlay.Show();
        overlayVm.PropertyChanged += (_, args) =>
        {
            // Nothing extra — binding updates Locked/TranslatedText directly.
            _ = args;
        };

        // Control panel is the main window (closing it exits the app).
        MainWindow = control;
        control.Show();

        // If translation was left enabled, start immediately.
        if (settings.Current.TranslationEnabled)
            loop.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_services is IDisposable d)
            d.Dispose();
        base.OnExit(e);
    }
}
