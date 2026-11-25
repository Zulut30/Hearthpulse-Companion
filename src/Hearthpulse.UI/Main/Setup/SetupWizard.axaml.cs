using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Hearthpulse.UI.Main.Setup;

/// <summary>
/// The setup wizard over the main view's page (its data: <see cref="MainViewModel"/>, <c>MainViewModel.Setup.cs</c>).
/// Arranging the overlay is asked of the view, as on Home.
/// </summary>
public partial class SetupWizard : UserControl
{
    private MainViewModel? _model;

    public SetupWizard()
    {
        InitializeComponent();
        // Shown: the keyboard starts in the wizard (on Skip, its first control), not on the page under it.
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && e.NewValue is true)
            {
                Dispatcher.UIThread.Post(() => SkipButton.Focus(NavigationMethod.Tab), DispatcherPriority.Input);
            }
        };
        DataContextChanged += (_, _) =>
        {
            if (_model is not null)
            {
                _model.PropertyChanged -= OnModelChanged;
            }

            _model = DataContext as MainViewModel;
            if (_model is not null)
            {
                _model.PropertyChanged += OnModelChanged;
            }
        };
    }

    /// <summary>"Arrange panels" on the overlay step.</summary>
    public event Action? ArrangeRequested;

    private void OnArrange(object? sender, RoutedEventArgs e)
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
            Dispatcher.UIThread.Post(KeepFocus, DispatcherPriority.Input);
        }
    }

    private void KeepFocus()
    {
        // Only a focus lost here moves: none (a hidden control gives it up), or a wizard control that is hidden now.
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        var lost = focused switch
        {
            null or TopLevel => true,
            Visual visual => !visual.IsEffectivelyVisible && this.IsVisualAncestorOf(visual),
            _ => false,
        };
        if (IsEffectivelyVisible && lost)
        {
            (NextButton.IsVisible ? NextButton : StartButton).Focus(NavigationMethod.Tab);
        }
    }
}
