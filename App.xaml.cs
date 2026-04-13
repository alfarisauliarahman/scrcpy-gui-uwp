using System.Windows;
using Wpf.Ui.Appearance;

namespace ScrcpyGui;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Force dark theme
        ApplicationThemeManager.Apply(ApplicationTheme.Dark);
    }
}
