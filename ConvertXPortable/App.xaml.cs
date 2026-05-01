using ConvertXPortable.Services;

namespace ConvertXPortable;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        ThemeManager.Initialize(AppTheme.Light);
        base.OnStartup(e);
    }
}
