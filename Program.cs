using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace VK_Music
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]



        static void Main()
        {
            // Подписываемся на глобальные исключения
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                try
                {
                    var ex = e.ExceptionObject as Exception;
                    string errorMsg = ex != null ?
                        $"Необработанное исключение: {ex.Message}\n{ex.StackTrace}" :
                        "Необработанное исключение (нет объекта исключения)";
                    
#if DEBUG
                    Logger.Error(errorMsg);
                    if (ex != null) Logger.Exception(ex, "CRITICAL UNHANDLED EXCEPTION");
#endif
                    MessageBox.Show(errorMsg, "Критическая ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch
                {
                    // Игнорируем ошибки внутри обработчика ошибок
                }
            };

            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                try
                {
#if DEBUG
                    Logger.Exception(e.Exception, "Unobserved Task Exception");
#endif
                    e.SetObserved(); // Предотвращаем падение приложения
                }
                catch { }
            };

#if DEBUG
            // Инициализируем логгер при запуске приложения
            Logger.Initialize();
            Logger.Info("Приложение запущено");
#endif

            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            
            // Эти вызовы уже включены в ApplicationConfiguration.Initialize()
            // Application.EnableVisualStyles();
            // Application.SetCompatibleTextRenderingDefault(false);
           
           var mainForm = new MainForm();
		   var loginForm = new LoginForm();
		   
		   bool isTokenExist = VK.CheckToken();
#if DEBUG
            Logger.Info($"Проверка токена: {(isTokenExist ? "токен найден" : "токен не найден")}");
#endif
            if (isTokenExist)
            {
#if DEBUG
                Logger.Info("Запуск главной формы");
#endif
                Application.Run(mainForm);
            }
            else
            {
#if DEBUG
                Logger.Info("Запуск формы авторизации");
#endif
                Application.Run(loginForm);
            }

        }
    }
}