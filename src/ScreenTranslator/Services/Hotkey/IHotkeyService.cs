namespace ScreenTranslator.Services.Hotkey;

public interface IHotkeyService : IDisposable
{
    event Action? Pressed;

    /// <summary>Register or re-register the global hotkey. Returns false if registration failed (e.g. already in use).</summary>
    bool Register(uint modifiers, uint virtualKey);

    void Unregister();
}
