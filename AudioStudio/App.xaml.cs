global using Application = System.Windows.Application;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace AudioStudio
{
    public partial class App : Application
    {
        private static readonly object _logLock = new();
        private static void Log(string msg)
        {
            lock (_logLock)
            {
                try
                {
                    File.AppendAllText(@"C:\Temp\audiostream_debug.log", $"{DateTime.Now:HH:mm:ss.fff} [App] {msg}\r\n");
                }
                catch { }
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            FontAssets.Register(this);
            Log("App starting");
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                Log($"AppDomain unhandled: {(args.ExceptionObject as Exception)?.Message}");
                Log($"AppDomain stack: {(args.ExceptionObject as Exception)?.StackTrace}");
            };
            DispatcherUnhandledException += (s, args) =>
            {
                Log($"Dispatcher unhandled: {args.Exception.Message}");
                Log($"Dispatcher stack: {args.Exception.StackTrace}");
                args.Handled = true;
            };
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                Log($"Task unobserved: {args.Exception?.Message}");
                args.SetObserved();
            };
            base.OnStartup(e);
        }
    }
}
