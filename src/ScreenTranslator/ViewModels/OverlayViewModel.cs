using CommunityToolkit.Mvvm.ComponentModel;

namespace ScreenTranslator.ViewModels;

public sealed partial class OverlayViewModel : ObservableObject
{
    [ObservableProperty]
    private string translatedText = string.Empty;

    [ObservableProperty]
    private bool locked;

    [ObservableProperty]
    private bool translationEnabled;

    [ObservableProperty]
    private bool visible = true;
}
