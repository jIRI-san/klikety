using System.Windows;

using Klikety.Services;

namespace Klikety.Overlay;

public partial class MacroPlaybackOverlay : Window, IMacroPlaybackWindow {
    private int _totalSteps;

    public MacroPlaybackOverlay() {
        InitializeComponent();
    }

    void IMacroPlaybackWindow.Show(string macroName, int totalSteps) {
        _totalSteps = totalSteps;
        TitleText.Text = $"Klikety macro: {macroName}";
        StepText.Text = $"Step 0 / {totalSteps}";
        ProgressBar.Value = 0;
        Show();
    }

    void IMacroPlaybackWindow.UpdateProgress(int completedSteps, int totalSteps) {
        _totalSteps = totalSteps;
        Dispatcher.Invoke(() => {
            StepText.Text = $"Step {completedSteps} / {totalSteps}";
            ProgressBar.Value = totalSteps > 0 ? (double)completedSteps / totalSteps * 100 : 0;
            DelayText.Visibility = Visibility.Collapsed;
        });
    }

    void IMacroPlaybackWindow.UpdateDelay(int remainingMs, string actionType) {
        Dispatcher.Invoke(() => {
            if (remainingMs > 0) {
                DelayText.Text = $"Waiting {remainingMs} ms, then {actionType}";
                DelayText.Visibility = Visibility.Visible;
            } else {
                DelayText.Visibility = Visibility.Collapsed;
            }
        });
    }

    void IMacroPlaybackWindow.Close() {
        Hide();
    }
}
