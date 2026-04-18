using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Media;
using ScreenTranslator.Services.Ocr;

namespace ScreenTranslator.Views;

[SupportedOSPlatform("windows10.0.19041.0")]
public partial class AddOcrLanguageDialog : Window
{
    public ObservableCollection<LanguageRow> Rows { get; } = new();

    public AddOcrLanguageDialog()
    {
        InitializeComponent();
        LanguagesList.ItemsSource = Rows;
        Loaded += (_, _) => RefreshRows();
    }

    private void RefreshRows()
    {
        Rows.Clear();
        foreach (var (display, tag, cap) in OcrLanguageHelper.CommonOcrLanguages)
        {
            Rows.Add(new LanguageRow
            {
                DisplayName = display,
                Tag = tag,
                CapabilityName = cap,
                // Authoritative check via TryCreateFromLanguage + primary-subtag fallback.
                IsInstalled = OcrLanguageHelper.IsOcrAvailable(tag),
            });
        }

        // Diagnostic line: shows what Windows actually reports so mismatches are visible.
        var detected = OcrLanguageHelper.GetInstalled();
        var tagList = detected.Count == 0
            ? "(none)"
            : string.Join(", ", detected.Select(l => l.Tag));
        DetectedTagsText.Text = $"Detected OCR engines: {tagList}";

        UpdateInstallButtonState();
    }

    private void UpdateInstallButtonState()
    {
        var selected = LanguagesList.SelectedItem as LanguageRow;
        InstallButton.IsEnabled = selected is not null && !selected.IsInstalled;
    }

    private void LanguagesList_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateInstallButtonState();
    }

    private void RefreshButton_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshRows();
        StatusText.Text = "Refreshed.";
    }

    private async void InstallButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (LanguagesList.SelectedItem is not LanguageRow row) return;
        if (row.IsInstalled)
        {
            StatusText.Text = $"{row.DisplayName} is already installed.";
            return;
        }

        // Build a PowerShell command that installs, prints result, and waits so the user sees it.
        var psCommand =
            $"Write-Host 'Installing {row.DisplayName} ({row.Tag}) ...' -ForegroundColor Cyan; " +
            $"try {{ " +
            $"  $r = Add-WindowsCapability -Online -Name '{row.CapabilityName}'; " +
            $"  Write-Host 'Done.' -ForegroundColor Green; " +
            $"  $r | Format-List; " +
            $"}} catch {{ " +
            $"  Write-Host ('Failed: ' + $_.Exception.Message) -ForegroundColor Red; " +
            $"}} " +
            $"Write-Host ''; Read-Host 'Press Enter to close this window'";

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCommand.Replace("\"", "\\\"")}\"",
            UseShellExecute = true,   // required for Verb=runas
            Verb = "runas",            // triggers UAC elevation prompt
        };

        StatusText.Text = "Waiting for administrator approval…";
        InstallButton.IsEnabled = false;
        RefreshButton.IsEnabled = false;

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                StatusText.Text = "Could not launch PowerShell.";
                return;
            }
            StatusText.Text = $"Installing {row.DisplayName}… a PowerShell window is open. Leave it running until it says 'Done'.";
            await proc.WaitForExitAsync();
            StatusText.Text = $"PowerShell closed. Refreshing installed-language list…";
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // UAC cancelled by user.
            StatusText.Text = "Cancelled by user (UAC declined).";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            RefreshRows();
            RefreshButton.IsEnabled = true;

            var updated = Rows.FirstOrDefault(r => r.Tag == row.Tag);
            if (updated is not null && updated.IsInstalled)
                StatusText.Text = $"{row.DisplayName} is now installed. You can close this dialog.";
        }
    }
}

public sealed class LanguageRow : INotifyPropertyChanged
{
    private bool _isInstalled;

    public required string DisplayName { get; init; }
    public required string Tag { get; init; }
    public required string CapabilityName { get; init; }

    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (_isInstalled == value) return;
            _isInstalled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsInstalled)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusBrush)));
        }
    }

    public string StatusText => IsInstalled ? "Installed" : "Not installed";
    public Brush StatusBrush => IsInstalled ? Brushes.LightGreen : Brushes.Orange;

    public event PropertyChangedEventHandler? PropertyChanged;
}
