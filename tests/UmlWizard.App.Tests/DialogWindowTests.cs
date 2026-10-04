using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using UmlWizard.App.Services;
using UmlWizard.App.Views;

namespace UmlWizard.App.Tests;

public sealed class DialogWindowTests
{
    /// <summary>Creates a window of the app, including <see cref="MessageWindow"/>, whose constructor is private.</summary>
    private static Window Create(Type type) => type == typeof(MessageWindow)
        ? (Window)Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: ["Title", "Message", new List<(string, ConfirmResult)> { ("OK", ConfirmResult.Yes) }],
            culture: null)!
        : (Window)Activator.CreateInstance(type)!;

    [AvaloniaFact]
    public void EveryDialogWindow_CannotBeMinimised_ButTheMainWindowCan()
    {
        var dialogs = typeof(MainWindow).Assembly.GetTypes()
            .Where(t => typeof(Window).IsAssignableFrom(t) && !t.IsAbstract && t != typeof(MainWindow) && t != typeof(SplashWindow))
            .ToList();

        // Every dialog of the app: OK/Cancel dialogs, About, message boxes and Preferences. A new dialog window is checked too.
        Assert.Equal(
            [typeof(AboutWindow), typeof(DialogWindow), typeof(MessageWindow), typeof(PreferencesWindow)],
            dialogs.OrderBy(t => t.Name));
        Assert.All(dialogs, type => Assert.False(Create(type).CanMinimize, $"{type.Name} can be minimised"));
        Assert.True(new MainWindow().CanMinimize);
    }
}
