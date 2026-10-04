namespace UmlWizard.App.Services;

/// <summary>Keeps the settings in memory only (the default when no store is given, e.g. in tests).</summary>
public sealed class InMemorySettingsStore(AppSettings? settings = null) : ISettingsStore
{
    private AppSettings _settings = settings ?? new AppSettings();

    public AppSettings Load() => _settings;

    public void Save(AppSettings settings) => _settings = settings;
}
