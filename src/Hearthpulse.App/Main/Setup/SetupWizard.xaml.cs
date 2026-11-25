using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Hearthpulse.App.Main.Setup;

/// <summary>
/// The setup wizard over the main window's page (its data: <see cref="MainViewModel"/>, <c>MainViewModel.Setup.cs</c>).
/// Arranging the overlay is asked of the window, as on Home.
/// </summary>
public partial class SetupWizard : UserControl
{
    public SetupWizard()
    {
        InitializeComponent();
        // Shown: the keyboard starts in the wizard (on Skip, its first control), not on the page under it.
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, () => SkipButton.Focus());
            }
        };
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is MainViewModel old)
            {
                old.PropertyChanged -= OnModelChanged;
            }

            if (e.NewValue is MainViewModel model)
            {
                model.PropertyChanged += OnModelChanged;
            }
        };
    }

    /// <summary>"Arrange panels" on the overlay step.</summary>
    public event Action? ArrangeRequested;

    private void OnArrange(object sender, RoutedEventArgs e)
    {
        (DataContext as MainViewModel)?.SetupArranged();
        ArrangeRequested?.Invoke();
    }

    /// <summary>
    /// Next on the next-to-last step, Back on the second and "Turn on logs" hide the very button that was pressed: the
    /// keyboard would be left on nothing. After the step's layout, focus moves to the shown primary button.
    /// </summary>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.SetupStep) or nameof(MainViewModel.SetupCanFixLogs))
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, KeepFocus);
        }
    }

    private void KeepFocus()
    {
        // Only a focus lost here moves: none, or a wizard control that is hidden now; focus elsewhere stays put.
        var lost = Keyboard.FocusedElement switch
        {
            null => true,
            UIElement focused => !focused.IsVisible && IsAncestorOf(focused),
            _ => false,
        };
        if (IsVisible && lost)
        {
            (NextButton.IsVisible ? NextButton : StartButton).Focus();
        }
    }
}
