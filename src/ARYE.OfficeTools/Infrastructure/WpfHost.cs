using WpfApplication = System.Windows.Application;

namespace ARYE.OfficeTools.Infrastructure
{
    /// <summary>
    /// Revit never creates a System.Windows.Application — it pumps its own message loop. Without
    /// one, WPF's pack://application:,,, URI scheme (used by every XAML window to merge Theme.xaml)
    /// is not reliably resolvable, and a ResourceDictionary.Source lookup fails with a
    /// XamlParseException. App.OnStartup calls this once; every command that opens a XAML window
    /// calls it too, because when the button is hosted by another loader (the pyRevit link buttons
    /// of ARYE Office Tools) OnStartup never runs. OnExplicitShutdown so closing our windows never
    /// tries to shut down Revit's own process.
    /// </summary>
    public static class WpfHost
    {
        public static void EnsureApplication()
        {
            if (WpfApplication.Current == null)
            {
                new WpfApplication { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            }
        }
    }
}
