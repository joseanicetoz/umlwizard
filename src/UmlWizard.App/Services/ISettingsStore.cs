namespace UmlWizard.App.Services;

/// <summary>Persists the <see cref="AppSettings"/>.</summary>
public interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}
