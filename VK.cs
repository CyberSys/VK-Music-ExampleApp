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
        public static bool IsAuth => api?.IsAuthorized ?? false;
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
        
        /// <summary>
        /// Синхронно получает список аудиозаписей для авторизованного пользователя.
        /// </summary>
        /// <returns>Список объектов Track. Возвращает пустой список при ошибке или если треки не найдены. Возвращает null, если API не инициализирован.</returns>
        public static List<Track> GetListOfTracks()
        {
#if DEBUG
            Logger.Info("Попытка синхронного получения списка треков");
#endif
            var list = new List<Track>();
            if (api == null)
            {
#if DEBUG
                Logger.Error("VK API не инициализирован. Невозможно получить треки.");
#endif
                return null; // Возвращаем null, чтобы указать на ошибку инициализации API
            }

            if (!api.IsAuthorized)
            {
#if DEBUG
                Logger.Error("Пользователь не авторизован. Невозможно получить треки.");
#endif
                return list; // Возвращаем пустой список, если пользователь не авторизован
            }

            try
            {
                // Получаем аудиозаписи с помощью VK API
                // Увеличиваем количество получаемых треков до 50 для лучшего пользовательского опыта
                var audioGetParams = new AudioGetParams { Count = 50 };
                
#if DEBUG
                Logger.LogDebug($"Отправка запроса на получение аудиозаписей: Count={audioGetParams.Count}");
#endif
                var audioList = api.Audio.Get(audioGetParams);

                if (audioList == null || audioList.Count == 0)
                {
#if DEBUG
                    Logger.Warning("Список аудиозаписей не получен или пуст.");
#endif
                    return list; // Возвращаем пустой список, если треки не найдены
                }

#if DEBUG
                Logger.Info($"Получено {audioList.Count} аудиозаписей от API.");
#endif
                foreach (var item in audioList)
                {
                    if (item == null)
                    {
#if DEBUG
                        Logger.Warning("Обнаружен null элемент в списке аудиозаписей.");
#endif
                        continue;
                    }
                    
                    // Проверяем наличие URL и его валидность
                    if (item.Url == null || string.IsNullOrEmpty(item.Url.AbsoluteUri))
                    {
#if DEBUG
                        Logger.Warning($"Аудиозапись '{item.Artist} - {item.Title}' (ID: {item.Id}) имеет недействительный URL. Пропускаем.");
#endif
                        continue;
                    }

                    // Проверяем, что URL начинается с http или https
                    string url = item.Url.AbsoluteUri;
                    if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                    {
#if DEBUG
                        Logger.Warning($"Аудиозапись '{item.Artist} - {item.Title}' (ID: {item.Id}) имеет некорректный URL: {url}. Пропускаем.");
#endif
                        continue;
                    }

                    // Создаем новый объект Track и заполняем его свойства
                    var newTrack = new Track
                    {
                        Artist = string.IsNullOrEmpty(item.Artist) ? "Неизвестный исполнитель" : item.Artist,
                        Title = string.IsNullOrEmpty(item.Title) ? "Без названия" : item.Title,
                        Duration = FormatDuration(item.Duration),
                        Url = url,
                        Aid = item.Id.HasValue ? item.Id.Value : 0,
                        Owner_id = item.OwnerId.HasValue ? item.OwnerId.Value : 0,
                    };
                    list.Add(newTrack);
#if DEBUG
                    Logger.LogDebug($"Добавлен трек: {newTrack.Artist} - {newTrack.Title}, URL: {url.Substring(0, Math.Min(50, url.Length))}...");
#endif
                }
            }
            catch (VkNet.Exception.UserAuthorizationFailException authEx)
            {
#if DEBUG
                Logger.Exception(authEx, "Ошибка авторизации при получении списка треков. Пользователю может потребоваться повторная аутентификация.");
#endif
                // Можно добавить код для повторной аутентификации или уведомления пользователя
                MessageBox.Show("Необходимо повторно авторизоваться в ВКонтакте", "Ошибка авторизации", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (VkNet.Exception.VkApiException apiEx)
            {
#if DEBUG
                Logger.Exception(apiEx, "Ошибка API ВКонтакте при получении списка треков.");
#endif
                MessageBox.Show($"Ошибка API ВКонтакте: {apiEx.Message}", "Ошибка API", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex) // Обработка более общих исключений для других потенциальных проблем
            {
#if DEBUG
                Logger.Exception(ex, "Ошибка при получении списка треков.");
#endif
                MessageBox.Show($"Произошла ошибка при получении списка треков: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
#if DEBUG
            Logger.Info($"Завершено получение треков. Всего получено треков: {list.Count}");
#endif
            return list;
        }
        
        /// <summary>
        /// Форматирует продолжительность трека в формат MM:SS
        /// </summary>
        /// <param name="durationInSeconds">Продолжительность в секундах</param>
        /// <returns>Отформатированная строка продолжительности</returns>
        private static string FormatDuration(int? durationInSeconds)
        {
            if (!durationInSeconds.HasValue || durationInSeconds.Value < 0)
                return "0:00";
                
            int duration = durationInSeconds.Value;
            int minutes = duration / 60;
            int seconds = duration % 60;
            
            return $"{minutes}:{seconds:D2}";
        }

        /// <summary>
        /// Асинхронно получает список аудиозаписей для авторизованного пользователя.
        /// </summary>
        /// <returns>Список объектов Track. Возвращает пустой список при ошибке или если треки не найдены. Возвращает null, если API не инициализирован.</returns>
        public static async Task<List<Track>> GetListOfTracksAsync()
        {
#if DEBUG
            Logger.Info("Попытка асинхронного получения списка треков");
#endif
            var list = new List<Track>();
            if (api == null)
            {
#if DEBUG
                Logger.Error("VK API не инициализирован. Невозможно получить треки асинхронно.");
#endif
                return null; // Возвращаем null, чтобы указать на ошибку инициализации API
            }

            if (!api.IsAuthorized)
            {
#if DEBUG
                Logger.Error("Пользователь не авторизован. Невозможно получить треки асинхронно.");
#endif
                return list; // Возвращаем пустой список, если пользователь не авторизован
            }

            try
            {
                // Получаем аудиозаписи с помощью VK API асинхронно
                // Увеличиваем количество получаемых треков до 50 для лучшего пользовательского опыта
                var audioGetParams = new AudioGetParams { Count = 50 };
                
#if DEBUG
                Logger.LogDebug($"Отправка асинхронного запроса на получение аудиозаписей: Count={audioGetParams.Count}");
#endif
                var audioList = await Task.Run(() => api.Audio.Get(audioGetParams));

                if (audioList == null || audioList.Count == 0)
                {
#if DEBUG
                    Logger.Warning("Список аудиозаписей не получен или пуст.");
#endif
                    return list; // Возвращаем пустой список, если треки не найдены
                }

#if DEBUG
                Logger.Info($"Асинхронно получено {audioList.Count} аудиозаписей от API.");
#endif
                foreach (var item in audioList)
                {
                    if (item == null)
                    {
#if DEBUG
                        Logger.Warning("Обнаружен null элемент в списке аудиозаписей.");
#endif
                        continue;
                    }
                    
                    // Проверяем наличие URL и его валидность
                    if (item.Url == null || string.IsNullOrEmpty(item.Url.AbsoluteUri))
                    {
#if DEBUG
                        Logger.Warning($"Аудиозапись '{item.Artist} - {item.Title}' (ID: {item.Id}) имеет недействительный URL. Пропускаем.");
#endif
                        continue;
                    }

                    // Проверяем, что URL начинается с http или https
                    string url = item.Url.AbsoluteUri;
                    if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                    {
#if DEBUG
                        Logger.Warning($"Аудиозапись '{item.Artist} - {item.Title}' (ID: {item.Id}) имеет некорректный URL: {url}. Пропускаем.");
#endif
                        continue;
                    }

                    // Создаем новый объект Track и заполняем его свойства
                    var newTrack = new Track
                    {
                        Artist = string.IsNullOrEmpty(item.Artist) ? "Неизвестный исполнитель" : item.Artist,
                        Title = string.IsNullOrEmpty(item.Title) ? "Без названия" : item.Title,
                        Duration = FormatDuration(item.Duration),
                        Url = url,
                        Aid = item.Id.HasValue ? item.Id.Value : 0,
                        Owner_id = item.OwnerId.HasValue ? item.OwnerId.Value : 0,
                    };
                    list.Add(newTrack);
#if DEBUG
                    Logger.LogDebug($"Добавлен трек: {newTrack.Artist} - {newTrack.Title}, URL: {url.Substring(0, Math.Min(50, url.Length))}...");
#endif
                }
            }
            catch (VkNet.Exception.UserAuthorizationFailException authEx)
            {
#if DEBUG
                Logger.Exception(authEx, "Ошибка авторизации при асинхронном получении списка треков. Пользователю может потребоваться повторная аутентификация.");
#endif
                // Можно добавить код для повторной аутентификации или уведомления пользователя
                MessageBox.Show("Необходимо повторно авторизоваться в ВКонтакте", "Ошибка авторизации", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (VkNet.Exception.VkApiException apiEx)
            {
#if DEBUG
                Logger.Exception(apiEx, "Ошибка API ВКонтакте при асинхронном получении списка треков.");
#endif
                MessageBox.Show($"Ошибка API ВКонтакте: {apiEx.Message}", "Ошибка API", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex) // Обработка более общих исключений для других потенциальных проблем
            {
#if DEBUG
                Logger.Exception(ex, "Ошибка при асинхронном получении списка треков.");
#endif
                MessageBox.Show($"Произошла ошибка при получении списка треков: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                // For now, returning the list which might be partially filled or empty.
            }
#if DEBUG
            Logger.Info($"Asynchronously finished getting tracks. Total tracks fetched: {list.Count}");
#endif
            return list;
        }

        /// <summary>
        /// Asynchronously retrieves a list of audio tracks for a specific user, or the current user if no ID is provided.
        /// </summary>
        /// <param name="ownerId">Optional ID of the user whose audio tracks to fetch. If null, fetches current user's audio.</param>
        /// <param name="count">Number of tracks to retrieve.</param>
        /// <param name="offset">Offset needed to return a specific subset of audio files.</param>
        /// <returns>A list of Track objects. Returns an empty list on failure or if no tracks are found. Returns null if API is not initialized.</returns>
        public static async Task<List<Track>> GetUserAudioAsync(long? ownerId = null, uint count = 10, uint offset = 0)
        {
#if DEBUG
            Logger.Info($"Attempting to asynchronously get user audio. OwnerID: {(ownerId.HasValue ? ownerId.Value.ToString() : "current user")}, Count: {count}, Offset: {offset}");
#endif
            var list = new List<Track>();
            if (api == null)
            {
#if DEBUG
                Logger.Error("VK API is not initialized. Cannot get user audio asynchronously.");
#endif
                return null; 
            }

            try
            {
                var audioGetParams = new AudioGetParams
                {
                    OwnerId = ownerId,
                    Count = count,
                    Offset = offset
                };

                var audioList = await Task.Run(() => api.Audio.Get(audioGetParams));

                if (audioList == null || audioList.Count == 0)
                {
#if DEBUG
                    Logger.Warning("User audio list not received or is empty.");
#endif
                    return list; 
                }

#if DEBUG
                Logger.Info($"Asynchronously received {audioList.Count} audio items for user {(ownerId.HasValue ? ownerId.Value.ToString() : "current user")}.");
#endif
                foreach (var item in audioList)
                {
                    if (item == null)
                    {
#if DEBUG
                        Logger.Warning("Encountered a null audio item in the user audio list.");
#endif
                        continue;
                    }
                    if (item.Url == null)
                    {
#if DEBUG
                        Logger.Warning($"Audio item '{item.Artist} - {item.Title}' (ID: {item.Id}) from user {(ownerId.HasValue ? ownerId.Value.ToString() : "current user")} has a null URL. Skipping.");
#endif
                        continue;
                    }

                    var newTrack = new Track
                    {
                        Artist = item.Artist ?? "Unknown Artist",
                        Title = item.Title ?? "Unknown Title",
                        Duration = (item.Duration / 60).ToString() + ":" + (item.Duration % 60 < 10 ? "0" + (item.Duration % 60).ToString() : (item.Duration % 60).ToString()),
                        Url = item.Url.AbsoluteUri,
                        Aid = item.Id.HasValue ? item.Id.Value : 0,
                        Owner_id = item.OwnerId.HasValue ? item.OwnerId.Value : (ownerId ?? 0),
                    };
                    list.Add(newTrack);
#if DEBUG
                    Logger.LogDebug($"Asynchronously added track from user audio: {newTrack.Artist} - {newTrack.Title}");
#endif
                }
            }
            catch (VkNet.Exception.UserAuthorizationFailException authEx)
            {
#if DEBUG
                Logger.Exception(authEx, "Authorization error while getting user audio asynchronously. User might need to re-authenticate.");
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                Logger.Exception(ex, "Error while getting user audio asynchronously.");
#endif
            }
#if DEBUG
            Logger.Info($"Asynchronously finished getting user audio. Total tracks fetched: {list.Count}");
#endif
            return list;
        }

        /// <summary>
        /// Asynchronously retrieves a list of audio tracks from a specific album.
        /// </summary>
        /// <param name="ownerId">ID of the user or community that owns the album.</param>
        /// <param name="albumId">ID of the album.</param>
        /// <param name="count">Number of tracks to retrieve.</param>
        /// <param name="offset">Offset needed to return a specific subset of audio files.</param>
        /// <returns>A list of Track objects. Returns an empty list on failure or if no tracks are found. Returns null if API is not initialized.</returns>
        public static async Task<List<Track>> GetAlbumTracksAsync(long ownerId, long albumId, uint count = 10, uint offset = 0)
        {
#if DEBUG
            Logger.Info($"Attempting to asynchronously get album tracks. OwnerID: {ownerId}, AlbumID: {albumId}, Count: {count}, Offset: {offset}");
#endif
            var list = new List<Track>();
            if (api == null)
            {
#if DEBUG
                Logger.Error("VK API is not initialized. Cannot get album tracks asynchronously.");
#endif
                return null; 
            }

            try
            {
                var audioGetParams = new AudioGetParams
                {
                    OwnerId = ownerId,
                    PlaylistId = albumId,
                    Count = count,
                    Offset = offset
                };

                // The method api.Audio.Get should work for albums by specifying AlbumId and OwnerId.
                // If there's a more specific method like api.Audio.GetFromAlbum, it should be preferred.
                // For now, assuming api.Audio.Get handles this.
                var audioList = await Task.Run(() => api.Audio.Get(audioGetParams));

                if (audioList == null || audioList.Count == 0)
                {
#if DEBUG
                    Logger.Warning("Album tracks list not received or is empty.");
#endif
                    return list; 
                }

#if DEBUG
                Logger.Info($"Asynchronously received {audioList.Count} audio items from album {albumId} (Owner: {ownerId}).");
#endif
                foreach (var item in audioList)
                {
                    if (item == null)
                    {
#if DEBUG
                        Logger.Warning("Encountered a null audio item in the album tracks list.");
#endif
                        continue;
                    }
                    if (item.Url == null)
                    {
#if DEBUG
                        Logger.Warning($"Audio item '{item.Artist} - {item.Title}' (ID: {item.Id}) from album {albumId} has a null URL. Skipping.");
#endif
                        continue;
                    }

                    var newTrack = new Track
                    {
                        Artist = item.Artist ?? "Unknown Artist",
                        Title = item.Title ?? "Unknown Title",
                        Duration = (item.Duration / 60).ToString() + ":" + (item.Duration % 60 < 10 ? "0" + (item.Duration % 60).ToString() : (item.Duration % 60).ToString()),
                        Url = item.Url.AbsoluteUri,
                        Aid = item.Id.HasValue ? item.Id.Value : 0,
                        Owner_id = item.OwnerId.HasValue ? item.OwnerId.Value : ownerId,
                    };
                    list.Add(newTrack);
#if DEBUG
                    Logger.LogDebug($"Asynchronously added track from album: {newTrack.Artist} - {newTrack.Title}");
#endif
                }
            }
            catch (VkNet.Exception.UserAuthorizationFailException authEx)
            {
#if DEBUG
                Logger.Exception(authEx, "Authorization error while getting album tracks asynchronously. User might need to re-authenticate.");
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                Logger.Exception(ex, "Error while getting album tracks asynchronously.");
#endif
            }
#if DEBUG
            Logger.Info($"Asynchronously finished getting album tracks. Total tracks fetched: {list.Count}");
#endif
            return list;
        }
    }
}
