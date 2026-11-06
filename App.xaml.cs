using System.Configuration;
using System.Data;
using System.Windows;

namespace CIE
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            DispatcherUnhandledException += (_, e) => DiagnosticLogger.WriteError("未处理的界面异常", e.Exception.ToString());
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                DiagnosticLogger.WriteError("未处理的程序异常", e.ExceptionObject?.ToString() ?? "未知异常");
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                DiagnosticLogger.WriteError("后台任务异常", e.Exception.ToString());
                e.SetObserved();
            };
        }
    }

}
