using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Windows.Forms;
using NAudio.Wave;
using System.Threading.Tasks;

namespace VK_Music
{
    public partial class Mp3StreamingPanel : UserControl
    {
        /// <summary>
        /// Состояния проигрывателя
        /// </summary>
        enum StreamingPlaybackState
        {
            Stopped,   // Остановлен
            Playing,    // Воспроизведение
            Buffering,  // Буферизация
            Paused      // Пауза
        }

        private BufferedWaveProvider bufferedWaveProvider;
        private IWavePlayer waveOut;
        private volatile StreamingPlaybackState playbackState;
        private volatile bool fullyDownloaded;
        private static readonly HttpClient httpClient = new HttpClient();
        private VolumeWaveProvider16 volumeProvider;
        private CancellationTokenSource streamingCancellationTokenSource;
        private Task streamingTask;
        
        // Список треков для воспроизведения
        private List<Track> trackList;
        // Индекс текущего трека
        private int currentTrackIndex = -1;

        /// <summary>
        /// Конструктор панели проигрывателя MP3
        /// </summary>
        public Mp3StreamingPanel()
        {
            InitializeComponent();
            volumeSlider1.VolumeChanged += OnVolumeSliderChanged;
            Disposed += MP3StreamingPanel_Disposing;
            streamingCancellationTokenSource = new CancellationTokenSource();
            trackList = new List<Track>();
            
#if DEBUG
            Logger.Initialize();
            Logger.Info("MP3StreamingPanel инициализирован");
#endif
        }
        
        /// <summary>
        /// Установить список треков для воспроизведения
        /// </summary>
        /// <param name="tracks">Список треков</param>
        public void SetTrackList(List<Track> tracks)
        {
            if (tracks == null || tracks.Count == 0)
            {
#if DEBUG
                Logger.Warning("Попытка установить пустой список треков");
#endif
                return;
            }
            
            trackList = tracks;
            currentTrackIndex = 0;
            
#if DEBUG
            Logger.Info($"Установлен список треков: {tracks.Count} треков");
#endif
            
            // Обновляем информацию о текущем треке в интерфейсе
            UpdateCurrentTrackInfo();
            
            // Установить URL первого трека в текстовое поле
            if (tracks.Count > 0 && currentTrackIndex >= 0)
            {
                textBoxStreamingUrl.Text = tracks[currentTrackIndex].Url;
            }
        }

        /// <summary>
        /// Обработчик изменения громкости
        /// </summary>
        void OnVolumeSliderChanged(object sender, EventArgs e)
        {
            if (volumeProvider != null)
            {
                volumeProvider.Volume = volumeSlider1.Volume;
#if DEBUG
                Logger.LogDebug($"Громкость изменена: {volumeSlider1.Volume}");
#endif
            }
        }

        delegate void ShowErrorDelegate(string message);

        /// <summary>
        /// Отображение сообщения об ошибке
        /// </summary>
        private void ShowError(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new ShowErrorDelegate(ShowError), message);
            }
            else
            {
#if DEBUG
                Logger.Error($"Ошибка проигрывателя: {message}");
#endif
                MessageBox.Show(message, "Ошибка проигрывателя", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Потоковое воспроизведение MP3 из URL
        /// </summary>
        private void StreamMp3(object state)
        {
            var url = (string)state;
            var token = streamingCancellationTokenSource.Token;
            
#if DEBUG
            Logger.Info($"Начало потокового воспроизведения: {url}");
#endif

            try
            {
                // Проверяем URL на корректность
                if (string.IsNullOrEmpty(url))
                {
                    throw new ArgumentException("URL трека не указан");
                }
                
                // Проверяем, что URL начинается с http или https
                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                {
#if DEBUG
                    Logger.Warning($"Некорректный URL трека: {url}");
#endif
                    throw new ArgumentException("Некорректный URL трека. URL должен начинаться с http:// или https://");
                }
                
                // Добавляем User-Agent для предотвращения блокировки запросов
                if (!httpClient.DefaultRequestHeaders.Contains("User-Agent"))
                {
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                }
                
#if DEBUG
                Logger.LogDebug("Отправка HTTP запроса для получения аудио");
#endif
                using (var response = httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).Result)
                {
                    response.EnsureSuccessStatusCode();
#if DEBUG
                    Logger.LogDebug($"Получен ответ от сервера: {response.StatusCode}");
#endif
                    using (var stream = response.Content.ReadAsStreamAsync().Result)
                    {
                        var buffer = new byte[16384 * 4];
                        IMp3FrameDecompressor decompressor = null;

                        using (var readFullyStream = new ReadFullyStream(stream))
                        {
                            while (!token.IsCancellationRequested)
                            {
                                if (IsBufferNearlyFull)
                                {
                                    Debug.WriteLine("Buffer getting full, taking a break");
                                    Thread.Sleep(500);
                                    continue;
                                }

                                Mp3Frame frame;
                                try
                                {
                                    frame = Mp3Frame.LoadFromStream(readFullyStream);
                                }
                                catch (EndOfStreamException)
                                {
                                    fullyDownloaded = true;
                                    break;
                                }
                                catch (WebException)
                                {
                                    if (token.IsCancellationRequested)
                                        break;
                                    throw;
                                }

                                if (frame == null) break;

                                if (decompressor == null)
                                {
                                    decompressor = CreateFrameDecompressor(frame);
                                    bufferedWaveProvider = new BufferedWaveProvider(decompressor.OutputFormat);
                                    bufferedWaveProvider.BufferDuration = TimeSpan.FromSeconds(20);
                                }

                                int decompressed = decompressor.DecompressFrame(frame, buffer, 0);
                                bufferedWaveProvider.AddSamples(buffer, 0, decompressed);
                            }
                        }
                    }
                }
            }
            catch (HttpRequestException httpEx)
            {
                if (!token.IsCancellationRequested)
                {
#if DEBUG
                    Logger.Exception(httpEx, "Ошибка сетевого запроса при потоковом воспроизведении");
#endif
                    ShowError($"Ошибка сетевого подключения: {httpEx.Message}");
                    
                    // Если произошла ошибка сети, попробуем переключиться на следующий трек через 3 секунды
                    if (trackList.Count > 0 && currentTrackIndex >= 0)
                    {
                        BeginInvoke(new Action(() => 
                        {
                            Thread.Sleep(3000); // Даем время для отображения сообщения об ошибке
                            buttonNext_Click(null, EventArgs.Empty);
                        }));
                    }
                }
            }
            catch (WebException webEx)
            {
                if (!token.IsCancellationRequested)
                {
#if DEBUG
                    Logger.Exception(webEx, "Ошибка веб-запроса при потоковом воспроизведении");
#endif
                    ShowError($"Ошибка доступа к аудиофайлу: {webEx.Message}");
                }
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
#if DEBUG
                    Logger.Exception(ex, "Ошибка при потоковом воспроизведении");
#endif
                    ShowError($"Ошибка воспроизведения: {ex.Message}");
                }
            }
        }

        private static IMp3FrameDecompressor CreateFrameDecompressor(Mp3Frame frame)
        {
            WaveFormat waveFormat = new Mp3WaveFormat(frame.SampleRate, frame.ChannelMode == ChannelMode.Mono ? 1 : 2,
                frame.FrameLength, frame.BitRate);
            return new AcmMp3FrameDecompressor(waveFormat);
        }

        private bool IsBufferNearlyFull
        {
            get
            {
                return bufferedWaveProvider != null &&
                       bufferedWaveProvider.BufferLength - bufferedWaveProvider.BufferedBytes
                       < bufferedWaveProvider.WaveFormat.AverageBytesPerSecond / 4;
            }
        }

        /// <summary>
        /// Обработчик нажатия кнопки воспроизведения
        /// </summary>
        private void buttonPlay_Click(object sender, EventArgs e)
        {
            if (playbackState == StreamingPlaybackState.Stopped)
            {
                // Проверяем наличие URL трека
                if (string.IsNullOrEmpty(textBoxStreamingUrl.Text))
                {
                    if (trackList.Count > 0 && currentTrackIndex >= 0 && currentTrackIndex < trackList.Count)
                    {
                        textBoxStreamingUrl.Text = trackList[currentTrackIndex].Url;
                    }
                    else
                    {
                        ShowError("URL трека не указан");
                        return;
                    }
                }
                
                playbackState = StreamingPlaybackState.Buffering;
                bufferedWaveProvider = null;
                
                // Создаем новый токен отмены, если предыдущий был отменен
                if (streamingCancellationTokenSource.IsCancellationRequested)
                {
                    streamingCancellationTokenSource.Dispose();
                    streamingCancellationTokenSource = new CancellationTokenSource();
                }
                
                // Обновляем информацию о текущем треке перед началом воспроизведения
                if (trackList.Count > 0 && currentTrackIndex >= 0 && currentTrackIndex < trackList.Count)
                {
                    UpdateCurrentTrackInfo();
                }
                
                streamingTask = Task.Run(() => StreamMp3(textBoxStreamingUrl.Text));
                timer1.Enabled = true;
                
#if DEBUG
                Logger.Info($"Начало воспроизведения трека: {(currentTrackIndex >= 0 ? trackList[currentTrackIndex].Title : textBoxStreamingUrl.Text)}");
#endif
            }
            else if (playbackState == StreamingPlaybackState.Paused)
            {
                playbackState = StreamingPlaybackState.Buffering;
#if DEBUG
                Logger.Info("Возобновление воспроизведения после паузы");
#endif
            }
        }

        /// <summary>
        /// Остановка воспроизведения
        /// </summary>
        private void StopPlayback()
        {
#if DEBUG
            Logger.Info("Остановка воспроизведения");
#endif
            
            if (streamingTask != null)
            {
                streamingCancellationTokenSource.Cancel();
                try
                {
                    streamingTask.Wait(TimeSpan.FromSeconds(2));
                }
                catch (AggregateException)
                {
                    // Задача была отменена, это ожидаемое поведение
#if DEBUG
                    Logger.LogDebug("Задача потокового воспроизведения отменена");
#endif
                }
            }

            if (waveOut != null)
            {
                waveOut.Stop();
                waveOut.Dispose();
                waveOut = null;
            }

            if (bufferedWaveProvider != null)
            {
                bufferedWaveProvider.ClearBuffer();
                bufferedWaveProvider = null;
            }

            playbackState = StreamingPlaybackState.Stopped;
            fullyDownloaded = false;
        }

        private void ShowBufferState(double totalSeconds)
        {
            labelBuffered.Text = String.Format("{0:0.0}s", totalSeconds);
            progressBarBuffer.Value = (int)(totalSeconds * 1000);
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            if (playbackState != StreamingPlaybackState.Stopped)
            {
                if (waveOut == null && bufferedWaveProvider != null)
                {
                    Debug.WriteLine("Creating WaveOut Device");
                    waveOut = CreateWaveOut();
                    waveOut.PlaybackStopped += OnPlaybackStopped;
                    volumeProvider = new VolumeWaveProvider16(bufferedWaveProvider);
                    volumeProvider.Volume = volumeSlider1.Volume;
                    waveOut.Init(volumeProvider);
                    progressBarBuffer.Maximum = (int)bufferedWaveProvider.BufferDuration.TotalMilliseconds;
                }
                else if (bufferedWaveProvider != null)
                {
                    var bufferedSeconds = bufferedWaveProvider.BufferedDuration.TotalSeconds;
                    ShowBufferState(bufferedSeconds);
                    // make it stutter less if we buffer up a decent amount before playing
                    if (bufferedSeconds < 0.5 && playbackState == StreamingPlaybackState.Playing && !fullyDownloaded)
                    {
                        Pause();
                    }
                    else if (bufferedSeconds > 4 && playbackState == StreamingPlaybackState.Buffering)
                    {
                        Play();
                    }
                    else if (fullyDownloaded && bufferedSeconds == 0)
                    {
                        Debug.WriteLine("Reached end of stream");
#if DEBUG
                        Logger.Info("Достигнут конец трека, автоматическое переключение на следующий");
#endif
                        // Автоматически переключаемся на следующий трек
                        if (trackList.Count > 0 && currentTrackIndex >= 0)
                        {
                            StopPlayback();
                            // Переключаемся на следующий трек
                            currentTrackIndex++;
                            if (currentTrackIndex >= trackList.Count)
                            {
                                currentTrackIndex = 0; // Переход к первому треку при достижении конца списка
                            }
                            
                            // Устанавливаем URL нового трека
                            textBoxStreamingUrl.Text = trackList[currentTrackIndex].Url;
                            
#if DEBUG
                            Logger.Info($"Автоматическое переключение на трек: {trackList[currentTrackIndex].Title}");
#endif
                            
                            // Начинаем воспроизведение нового трека
                            BeginInvoke(new Action(() => buttonPlay_Click(null, EventArgs.Empty)));
                        }
                        else
                        {
                            StopPlayback();
                        }
                    }
                }

            }
        }

        /// <summary>
        /// Начать воспроизведение
        /// </summary>
        private void Play()
        {
            waveOut.Play();
            Debug.WriteLine(String.Format("Started playing, waveOut.PlaybackState={0}", waveOut.PlaybackState));
#if DEBUG
            Logger.Info($"Воспроизведение начато, состояние: {waveOut.PlaybackState}");
#endif
            playbackState = StreamingPlaybackState.Playing;
        }

        /// <summary>
        /// Приостановить воспроизведение для буферизации
        /// </summary>
        private void Pause()
        {
            playbackState = StreamingPlaybackState.Buffering;
            waveOut.Pause();
            Debug.WriteLine(String.Format("Paused to buffer, waveOut.PlaybackState={0}", waveOut.PlaybackState));
#if DEBUG
            Logger.LogDebug($"Пауза для буферизации, состояние: {waveOut.PlaybackState}");
#endif
        }

        private IWavePlayer CreateWaveOut()
        {
            return new WaveOut();
        }

        /// <summary>
        /// Освобождение ресурсов при уничтожении панели
        /// </summary>
        private void MP3StreamingPanel_Disposing(object sender, EventArgs e)
        {
#if DEBUG
            Logger.Info("Освобождение ресурсов MP3StreamingPanel");
#endif
            StopPlayback();
            streamingCancellationTokenSource?.Dispose();
        }

        /// <summary>
        /// Обработчик нажатия кнопки паузы
        /// </summary>
        private void buttonPause_Click(object sender, EventArgs e)
        {
            if (playbackState == StreamingPlaybackState.Playing || playbackState == StreamingPlaybackState.Buffering)
            {
                waveOut?.Pause();
                Debug.WriteLine(String.Format("User requested Pause, waveOut.PlaybackState={0}", waveOut?.PlaybackState));
#if DEBUG
                Logger.Info("Пользователь нажал паузу");
#endif
                playbackState = StreamingPlaybackState.Paused;
            }
        }

        /// <summary>
        /// Обработчик нажатия кнопки остановки
        /// </summary>
        private void buttonStop_Click(object sender, EventArgs e)
        {
            StopPlayback();
#if DEBUG
            Logger.Info("Пользователь остановил воспроизведение");
#endif
        }

        /// <summary>
        /// Обработчик события остановки воспроизведения
        /// </summary>
        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            Debug.WriteLine("Playback Stopped");
#if DEBUG
            Logger.Info("Воспроизведение остановлено");
#endif
            if (e.Exception != null)
            {
#if DEBUG
                Logger.Exception(e.Exception, "Ошибка при воспроизведении");
#endif
                MessageBox.Show(String.Format("Ошибка воспроизведения: {0}", e.Exception.Message), "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Переключение на предыдущий трек
        /// </summary>
        private void buttonBack_MouseClick(object sender, MouseEventArgs e)
        {
            if (trackList.Count == 0 || currentTrackIndex < 0)
            {
#if DEBUG
                Logger.Warning("Невозможно переключить на предыдущий трек: список треков пуст или не инициализирован");
#endif
                return;
            }
            
            // Переключаемся на предыдущий трек
            currentTrackIndex--;
            if (currentTrackIndex < 0)
            {
                currentTrackIndex = trackList.Count - 1; // Переход к последнему треку при достижении начала списка
            }
            
            // Останавливаем текущее воспроизведение
            StopPlayback();
            
            // Устанавливаем URL нового трека
            textBoxStreamingUrl.Text = trackList[currentTrackIndex].Url;
            
            // Обновляем информацию о текущем треке в интерфейсе
            UpdateCurrentTrackInfo();
            
#if DEBUG
            Logger.Info($"Переключение на предыдущий трек: {trackList[currentTrackIndex].Title}");
#endif
            
            // Начинаем воспроизведение нового трека
            buttonPlay_Click(sender, e);
        }

        /// <summary>
        /// Переключение на следующий трек
        /// </summary>
        private void buttonNext_Click(object sender, EventArgs e)
        {
            if (trackList.Count == 0 || currentTrackIndex < 0)
            {
#if DEBUG
                Logger.Warning("Невозможно переключить на следующий трек: список треков пуст или не инициализирован");
#endif
                return;
            }
            
            // Переключаемся на следующий трек
            currentTrackIndex++;
            if (currentTrackIndex >= trackList.Count)
            {
                currentTrackIndex = 0; // Переход к первому треку при достижении конца списка
            }
            
            // Останавливаем текущее воспроизведение
            StopPlayback();
            
            // Устанавливаем URL нового трека
            textBoxStreamingUrl.Text = trackList[currentTrackIndex].Url;
            
            // Обновляем информацию о текущем треке в интерфейсе
            UpdateCurrentTrackInfo();
            
#if DEBUG
            Logger.Info($"Переключение на следующий трек: {trackList[currentTrackIndex].Title}");
#endif
            
            // Начинаем воспроизведение нового трека
            buttonPlay_Click(sender, e);
        }
        
        /// <summary>
        /// Обновляет информацию о текущем треке в интерфейсе
        /// </summary>
        private void UpdateCurrentTrackInfo()
        {
            if (trackList.Count > 0 && currentTrackIndex >= 0 && currentTrackIndex < trackList.Count)
            {
                var currentTrack = trackList[currentTrackIndex];
                // Обновляем заголовок формы или другой элемент интерфейса с информацией о треке
                // Если в форме нет специального элемента для отображения информации о треке,
                // можно использовать текстовое поле URL или добавить Label в дизайнере формы
                string trackInfo = $"{currentTrack.Artist} - {currentTrack.Title} ({currentTrack.Duration})";
                
                // Если есть родительская форма, можно обновить её заголовок
                if (FindForm() != null)
                {
                    FindForm().Text = trackInfo;
                }
                
#if DEBUG
                Logger.LogDebug($"Обновлена информация о текущем треке: {trackInfo}");
#endif
            }
        }
    }
    
    public class Mp3StreamingPanelPlugin : INAudioDemoPlugin
    {
        public string Name
        {
            get { return "MP3 Streaming"; }
        }

        public Control CreatePanel()
        {
            return new Mp3StreamingPanel();
        }
    }
}
