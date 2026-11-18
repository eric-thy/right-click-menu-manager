using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;

namespace Cmm
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                CultureInfo culture = new CultureInfo("zh-CN");
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                FrameworkElement.LanguageProperty.OverrideMetadata(
                    typeof(FrameworkElement),
                    new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("zh-CN")));
            }
            catch
            {
            }

            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

            string category = null;
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--category", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                        category = args[i + 1];
                }
            }

            try
            {
                var window = new MainWindow(category);
                app.Run(window);
            }
            catch (Exception ex)
            {
                WriteCrashLog(ex);
                MessageBox.Show("启动失败：\n" + ex.Message + "\n\n详细信息已写入日志。",
                    "右键菜单管理器", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            WriteCrashLog(e.Exception);
            MessageBox.Show("程序遇到错误：\n" + e.Exception.Message + "\n\n详细信息已写入日志。",
                "右键菜单管理器", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            WriteCrashLog(ex);
        }

        private static void WriteCrashLog(Exception ex)
        {
            try
            {
                AppPaths.Ensure();
                string file = Path.Combine(AppPaths.DataDir, "crash.log");
                var sb = new StringBuilder();
                sb.AppendLine("==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====");
                if (ex != null) sb.AppendLine(ex.ToString());
                else sb.AppendLine("未知错误");
                sb.AppendLine();
                File.AppendAllText(file, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
            }
        }
    }
}
