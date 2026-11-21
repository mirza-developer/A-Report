using System.Windows;
using AReport.Designer.Views;

namespace AReport.Designer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        // "AReport.Designer.exe report.art" opens the file directly.
        if (e.Args.Length > 0)
            window.OpenFile(e.Args[0]);
    }
}
