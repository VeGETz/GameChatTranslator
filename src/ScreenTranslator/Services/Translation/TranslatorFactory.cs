using ScreenTranslator.Services.Settings;

namespace ScreenTranslator.Services.Translation;

public sealed class TranslatorFactory
{
    private readonly GoogleFreeTranslator _free;
    private readonly GoogleCloudTranslator _cloud;
    private readonly LlmTranslator _llm;
    private readonly SettingsStore _settings;

    public TranslatorFactory(
        GoogleFreeTranslator free,
        GoogleCloudTranslator cloud,
        LlmTranslator llm,
        SettingsStore settings)
    {
        _free = free;
        _cloud = cloud;
        _llm = llm;
        _settings = settings;
    }

    public ITranslator Current => _settings.Current.Translator switch
    {
        TranslatorKind.GoogleCloud => _cloud,
        TranslatorKind.Llm => _llm,
        _ => _free,
    };
}
