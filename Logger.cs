using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace VK_Music
{
    /// <summary>
    /// Класс для логирования сообщений в консоль и файл (только в Debug-сборке)
    /// </summary>
    public static class Logger
    {
        private static readonly object _lockObject = new object();
        private static readonly string _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_log.txt");
        private static bool _isInitialized = false;

        /// <summary>
        /// Уровни логирования
        /// </summary>
        public enum LogLevel
        {
            Debug,
            Info,
            Warning,
            Error
        }

        /// <summary>
        /// Инициализация логгера
        /// </summary>
        public static void Initialize()
        {
#if DEBUG
            if (!_isInitialized)
            {
                try
                {
                    // Создаем консоль для отладочной версии, если она еще не создана
                    if (!AttachConsole(-1)) // Attach to parent process console
                    {
                        AllocConsole(); // Create new console if can't attach
                    }

                    // Очищаем лог-файл при запуске
                    File.WriteAllText(_logFilePath, string.Empty);
                    
                    Info("Логгер инициализирован");
                    _isInitialized = true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Ошибка при инициализации логгера: {ex.Message}");
                }
            }
#endif
        }

        /// <summary>
        /// Логирование отладочной информации
        /// </summary>
        public static void LogDebug(string message)
        {
            Log(message, LogLevel.Debug);
        }

        /// <summary>
        /// Логирование информационного сообщения
        /// </summary>
        public static void Info(string message)
        {
            Log(message, LogLevel.Info);
        }

        /// <summary>
        /// Логирование предупреждения
        /// </summary>
        public static void Warning(string message)
        {
            Log(message, LogLevel.Warning);
        }

        /// <summary>
        /// Логирование ошибки
        /// </summary>
        public static void Error(string message)
        {
            Log(message, LogLevel.Error);
        }

        /// <summary>
        /// Логирование исключения с трассировкой стека
        /// </summary>
        public static void Exception(Exception ex, string additionalInfo = "")
        {
#if DEBUG
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("========== ИСКЛЮЧЕНИЕ ==========");
            if (!string.IsNullOrEmpty(additionalInfo))
            {
                sb.AppendLine($"Дополнительная информация: {additionalInfo}");
            }
            sb.AppendLine($"Сообщение: {ex.Message}");
            sb.AppendLine($"Тип: {ex.GetType().FullName}");
            sb.AppendLine($"Трассировка стека: {ex.StackTrace}");

            if (ex.InnerException != null)
            {
                sb.AppendLine("Внутреннее исключение:");
                sb.AppendLine($"Сообщение: {ex.InnerException.Message}");
                sb.AppendLine($"Тип: {ex.InnerException.GetType().FullName}");
                sb.AppendLine($"Трассировка стека: {ex.InnerException.StackTrace}");
            }
            sb.AppendLine("================================");

            Log(sb.ToString(), LogLevel.Error);
#endif
        }

        /// <summary>
        /// Основной метод логирования
        /// </summary>
        private static void Log(string message, LogLevel level)
        {
#if DEBUG
            try
            {
                string formattedMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
                
                // Вывод в консоль с цветом в зависимости от уровня логирования
                lock (_lockObject)
                {
                    ConsoleColor originalColor = Console.ForegroundColor;
                    
                    switch (level)
                    {
                        case LogLevel.Debug:
                            Console.ForegroundColor = ConsoleColor.Gray;
                            break;
                        case LogLevel.Info:
                            Console.ForegroundColor = ConsoleColor.White;
                            break;
                        case LogLevel.Warning:
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            break;
                        case LogLevel.Error:
                            Console.ForegroundColor = ConsoleColor.Red;
                            break;
                    }
                    
                    Console.WriteLine(formattedMessage);
                    Console.ForegroundColor = originalColor;
                    
                    // Запись в файл
                    File.AppendAllText(_logFilePath, formattedMessage + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при логировании: {ex.Message}");
            }
#endif
        }

        // Импорт функций Windows API для работы с консолью
        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);
    }
}