using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using UmlWizard.App.Services;

namespace UmlWizard.App.Views;

/// <summary>A minimal message box. Closing it without a button returns <see cref="ConfirmResult.Cancel"/>.</summary>
public sealed class MessageWindow : Window
{
    private MessageWindow(string title, string message, IReadOnlyList<(string Label, ConfirmResult Result)> buttons)
    {
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        CanMinimize = false;
        ShowInTaskbar = false;
        MinWidth = 360;
        MaxWidth = 560;

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        for (var i = 0; i < buttons.Count; i++)
        {
            var (label, result) = buttons[i];
            var button = new Button
            {
                Content = label,
                MinWidth = 80,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                IsDefault = i == 0,
                IsCancel = result == ConfirmResult.Cancel || buttons.Count == 1,
            };
            if (i == 0)
            {
                button.Classes.Add("accent");
            }

            button.Click += (_, _) => Close(result);
            buttonPanel.Children.Add(button);
        }

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 20,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                buttonPanel,
            },
        };
    }

    public static async Task<ConfirmResult> ShowAsync(
        Window owner, string title, string message, IReadOnlyList<(string Label, ConfirmResult Result)> buttons) =>
        await new MessageWindow(title, message, buttons).ShowDialog<ConfirmResult>(owner);
}
