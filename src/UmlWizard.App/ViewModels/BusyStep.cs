namespace UmlWizard.App.ViewModels;

/// <summary>How far a long task is, as the busy overlay shows it: what it does, and the share done when that is known.</summary>
/// <param name="Text">What the task does now, e.g. "Reading 120 of 3,400 source files…".</param>
/// <param name="Progress">The share done, from 0 to 1; <c>null</c> when it cannot be told, which shows a moving bar.</param>
public sealed record BusyStep(string Text, double? Progress = null)
{
    /// <summary>Passes a task's own progress reports on to <paramref name="target"/> as busy steps.</summary>
    public static IProgress<T> Map<T>(IProgress<BusyStep> target, Func<T, BusyStep> map) => new Mapped<T>(target, map);

    private sealed class Mapped<T>(IProgress<BusyStep> target, Func<T, BusyStep> map) : IProgress<T>
    {
        public void Report(T value) => target.Report(map(value));
    }
}
