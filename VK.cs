using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using CodeeloUI.Controls;
using Microsoft.Extensions.DependencyInjection;
using VkNet;
using VkNet.Abstractions;
using VkNet.AudioBypassService.Extensions;
using VkNet.Model;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;

namespace VK_Music
{
    public class VK
    {
        private static readonly IVkApi? api;
        private static readonly string TokenFilePath = "Token.txt";
        public static bool IsAuth => api.IsAuthorized;
        private static LoginForm loginForm = null;
        
        static VK()
        {
#if DEBUG
            Logger.Initialize();
            Logger.Info("Инициализация VK API");
#endif
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddAudioBypass();
            api = new VkApi(serviceCollection);
#if DEBUG
            Logger.Info("VK API инициализирован");
#endif
        }

        private static byte[] ProtectToken(string token)
        {
            byte[] tokenBytes = Encoding.UTF8.GetBytes(token);
            return ProtectedData.Protect(tokenBytes, null, DataProtectionScope.CurrentUser);
        }

        private static string UnprotectToken(byte[] protectedToken)
        {
            byte[] tokenBytes = ProtectedData.Unprotect(protectedToken, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(tokenBytes);
        }

        public static bool CheckToken()
        {
#if DEBUG
            Logger.Info("Проверка токена авторизации");
#endif
            if(File.Exists(TokenFilePath))
            {
#if DEBUG
                Logger.LogDebug($"Файл токена найден: {TokenFilePath}");
#endif
                try
                {
                    byte[] protectedToken = File.ReadAllBytes(TokenFilePath);
                    string token = UnprotectToken(protectedToken);
                    
                    api?.Authorize(new ApiAuthParams
                    {
                        AccessToken = token
                    });
#if DEBUG
                    Logger.Info("Авторизация по токену успешна");
#endif
                    return true;
                }
                catch (CryptographicException ex)
                {
                    // Token is corrupted or from different user
#if DEBUG
                    Logger.Warning($"Токен поврежден или принадлежит другому пользователю: {ex.Message}");
#endif
                    File.Delete(TokenFilePath);
                    return false;
                }
                catch (Exception ex)
                {
#if DEBUG
                    Logger.Exception(ex, "Ошибка при проверке токена");
#endif
                    return false;
                }
            }
#if DEBUG
            Logger.Info("Файл токена не найден, требуется авторизация");
#endif
            return false;
        }

        public static async Task SignInAsync(LoginForm loginForm, string login, string password)
        {
#if DEBUG
            Logger.Info($"Попытка авторизации для пользователя: {login}");
#endif
            if (string.IsNullOrEmpty(login))
            {
#if DEBUG
                Logger.Error("Ошибка авторизации: логин не может быть пустым");
#endif
                throw new ArgumentException($"\"{nameof(login)}\" не может быть неопределенным или пустым.", nameof(login));
            }

            if (string.IsNullOrEmpty(password))
            {
#if DEBUG
                Logger.Error("Ошибка авторизации: пароль не может быть пустым");
#endif
                throw new ArgumentException($"\"{nameof(password)}\" не может быть неопределенным или пустым.", nameof(password));
            }

            if (api == null)
            {
#if DEBUG
                Logger.Error("Ошибка авторизации: API не инициализирован");
#endif
                return;
            }
            
            try
            {
#if DEBUG
                Logger.Info("Отправка запроса на авторизацию");
#endif
                await Task.Run(() => api.Authorize(new ApiAuthParams
                {
                    Login = login,
                    Password = password,
                    TwoFactorAuthorization = () => 
                    { 
#if DEBUG
                        Logger.Info("Запрос кода двухфакторной аутентификации");
#endif
                        // Получаем код 2FA от пользователя через форму логина асинхронно
                        // Используем блокирующий вызов для получения результата асинхронной операции
                        string code = loginForm.GetTwoAuthAsync().GetAwaiter().GetResult();
                        
                        // Проверяем, был ли получен код или операция была отменена
                        if (string.IsNullOrEmpty(code))
                        {
#if DEBUG
                            Logger.Warning("Операция двухфакторной аутентификации была отменена или превышено время ожидания");
#endif
                            // Выбрасываем исключение, чтобы прервать процесс авторизации
                            throw new OperationCanceledException("Операция двухфакторной аутентификации была отменена");
                        }
                        
#if DEBUG
                        Logger.Info("Код двухфакторной аутентификации получен");
#endif
                        return code; 
                    }
                }));
                
                if(IsAuth)
                {
#if DEBUG
                    Logger.Info("Авторизация успешна, сохранение токена");
#endif
                    byte[] protectedToken = ProtectToken(api.Token);
                    File.WriteAllBytes(TokenFilePath, protectedToken);
#if DEBUG
                    Logger.Info("Токен успешно сохранен");
#endif
                }
                else
                {
#if DEBUG
                    Logger.Warning("Авторизация не удалась");
#endif
                }
            }
            catch (OperationCanceledException ex)
            {
#if DEBUG
                Logger.Info($"Авторизация отменена пользователем: {ex.Message}");
#endif
                // Не пробрасываем исключение дальше, так как это ожидаемое поведение при отмене
                return;
            }
            catch (Exception ex)
            {
#if DEBUG
                Logger.Exception(ex, "Ошибка при авторизации");
#endif
                throw;
            }
        }
        
        // Для обратной совместимости
        public static void SignIn(LoginForm loginForm, string login, string password)
        {
#if DEBUG
            Logger.Info($"Вызов синхронного метода авторизации для пользователя: {login}");
#endif
            try
            {
                // Запускаем асинхронный метод синхронно
                Task.Run(() => SignInAsync(loginForm, login, password)).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
#if DEBUG
                Logger.Exception(ex, "Ошибка при синхронном вызове асинхронного метода авторизации");
#endif
                throw; // Пробрасываем исключение дальше
            }
        }
        
        public static List<Track> GetListOfTracks()
        {
            var list = new List<Track>();
            if (api == null)
            {
#if DEBUG
                Logger.Error("VK API не инициализирован");
#endif
                return null;
            }
            try
            {
                var audioList = api.Audio.Get(new AudioGetParams { Count = 10 });
                if (audioList == null)
                {
#if DEBUG
                    Logger.Warning("Список аудиозаписей не получен или пуст");
#endif
                    return list;
                }
                foreach (var item in audioList)
                {
                    if (item == null || item.Url == null)
                    {
#if DEBUG
                        Logger.Warning("Некорректный элемент аудиозаписи или отсутствует URL");
#endif
                        continue;
                    }
                    var newTrack = new Track
                    {
                        Artist = item.Artist,
                        Title = item.Title,
                        Duration = (item.Duration / 60).ToString() + ":" + (item.Duration % 60 < 10 ? "0" + item.Duration % 60 : item.Duration % 60),
                        Url = item.Url.AbsoluteUri,
                        Aid = item.Id.HasValue ? item.Id.Value : 0,
                        Owner_id = item.OwnerId.HasValue ? item.OwnerId.Value : 0,
                    };
                    list.Add(newTrack);
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                Logger.Exception(ex, "Ошибка при получении списка треков");
#endif
            }
            return list;
        }

        public static async Task<List<Track>> GetListOfTracksAsync()
        {
            var list = new List<Track>();
            if (api == null)
            {
#if DEBUG
                Logger.Error("VK API не инициализирован");
#endif
                return null;
            }
            try
            {
                var audioList = await Task.Run(() => api.Audio.Get(new AudioGetParams { Count = 10 }));
                if (audioList == null)
                {
#if DEBUG
                    Logger.Warning("Список аудиозаписей не получен или пуст");
#endif
                    return list;
                }
                foreach (var item in audioList)
                {
                    if (item == null || item.Url == null)
                    {
#if DEBUG
                        Logger.Warning("Некорректный элемент аудиозаписи или отсутствует URL");
#endif
                        continue;
                    }
                    var newTrack = new Track
                    {
                        Artist = item.Artist,
                        Title = item.Title,
                        Duration = (item.Duration / 60).ToString() + ":" + (item.Duration % 60 < 10 ? "0" + item.Duration % 60 : item.Duration % 60),
                        Url = item.Url.AbsoluteUri,
                        Aid = item.Id.HasValue ? item.Id.Value : 0,
                        Owner_id = item.OwnerId.HasValue ? item.OwnerId.Value : 0,
                    };
                    list.Add(newTrack);
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                Logger.Exception(ex, "Ошибка при асинхронном получении списка треков");
#endif
            }
            return list;
        }
    }
}
