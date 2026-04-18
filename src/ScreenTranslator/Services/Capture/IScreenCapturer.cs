using System.Drawing;

namespace ScreenTranslator.Services.Capture;

public interface IScreenCapturer : IDisposable
{
    /// <summary>
    /// Captures the given screen rectangle into a reusable backing bitmap and returns it.
    /// The returned bitmap is owned by the capturer and reused on the next call — do not dispose it,
    /// and do not keep a reference past the next call.
    /// </summary>
    Bitmap Capture(Rectangle screenRect);
}
