using System;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Windows.Forms;
using NAudio.Wave;
using NAudio.FileFormats.Mp3;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Linq;
using System.Text.RegularExpressions;
using MediaToolkit;
using MediaToolkit.Model;
using MediaToolkit.Options;
using NAudio.Wave.SampleProviders;

namespace VK_Music
{
    /// <summary>
    /// Тип аудио кодека для HLS сегментов
    /// </summary>
    public enum AudioCodec
    {
        Mp3,
        Aac
    }

    /// <summary>
    /// HLS плеер для воспроизведения зашифрованных сегментов
    /// </summary>
    public class HlsPlayer
    {
        private readonly HttpClient httpClient;
        private readonly CancellationTokenSource cts;
        private BufferedWaveProvider bufferedWaveProvider;
        private IWavePlayer waveOut;
        private VolumeWaveProvider16 volumeProvider;
        private volatile Mp3StreamingPanel.StreamingPlaybackState playbackState;
        private volatile bool fullyDownloaded;
        private Task streamingTask;
        private List<HlsSegment> segments;
        private int currentSegmentIndex;
        private string keyUrl;
        private byte[] decryptionKey;
        private AudioCodec codec;
        private readonly Func<IWavePlayer> createWaveOut;

        public event EventHandler<StoppedEventArgs> PlaybackStopped;

        public HlsPlayer(Func<IWavePlayer> createWaveOut)
        {
            this.createWaveOut = createWaveOut;
            httpClient = new HttpClient();
            cts = new CancellationTokenSource();
            segments = new List<HlsSegment>();
            currentSegmentIndex = 0;
            playbackState = Mp3StreamingPanel.StreamingPlaybackState.Stopped;
            keyUrl = string.Empty;
            decryptionKey = Array.Empty<byte>();
            streamingTask = Task.CompletedTask;
            bufferedWaveProvider = null!; // Инициализируется позже
            waveOut = null!; // Инициализируется позже
            volumeProvider = null!; // Инициализируется позже

            // Добавляем User-Agent для обхода блокировок
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            httpClient.DefaultRequestHeaders.Add("Referer", "https://vk.com/");
        }

        /// <summary>
        /// Загружает и парсит HLS плейлист
        /// </summary>
        public async Task<bool> LoadPlaylistAsync(string playlistUrl, CancellationToken token)
        {
#if DEBUG
            Logger.Info($"Загрузка HLS плейлиста: {playlistUrl}");
#endif

            try
            {
                using (var response = await httpClient.GetAsync(playlistUrl, HttpCompletionOption.ResponseHeadersRead, token))
                {
                    response.EnsureSuccessStatusCode();

                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string content = await reader.ReadToEndAsync();
                        return ParsePlaylist(content, playlistUrl);
                    }
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                Logger.Exception(ex, $"Ошибка загрузки HLS плейлиста: {playlistUrl}");
#endif
                return false;
            }
        }

        /// <summary>
        /// Парсит содержимое HLS плейлиста
        /// </summary>
        private bool ParsePlaylist(string content, string baseUrl)
        {
            segments.Clear();
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            var baseUri = new Uri(baseUrl);
            string basePath = $"{baseUri.Scheme}://{baseUri.Host}{baseUri.AbsolutePath.Substring(0, baseUri.AbsolutePath.LastIndexOf('/') + 1)}";

            byte[] explicitIv = null;
            int sequenceNumber = 0;
            string codecString = null;

            foreach (var line in lines)
            {
                if (line.StartsWith("#EXT-X-KEY"))
                {
                    // Парсим ключ шифрования и IV
                    var keyMatch = Regex.Match(line, @"URI=""([^""]+)""");
                    if (keyMatch.Success)
                    {
                        keyUrl = keyMatch.Groups[1].Value;
                        if (!keyUrl.StartsWith("http"))
                        {
                            keyUrl = basePath + keyUrl;
                        }
#if DEBUG
                        Logger.LogDebug($"Найден ключ шифрования: {keyUrl}");
#endif
                    }

                    // Парсим IV если указан
                    var ivMatch = Regex.Match(line, @"IV=0x([0-9a-fA-F]+)");
                    if (ivMatch.Success)
                    {
                        explicitIv = StringToByteArray(ivMatch.Groups[1].Value);
#if DEBUG
                        Logger.LogDebug($"Найден явный IV: {BitConverter.ToString(explicitIv).Replace("-", "")}");
#endif
                    }
                }
                else if (line.StartsWith("#EXT-X-MEDIA-SEQUENCE"))
                {
                    // Парсим начальный номер последовательности
                    var seqMatch = Regex.Match(line, @"#EXT-X-MEDIA-SEQUENCE:(\d+)");
                    if (seqMatch.Success)
                    {
                        sequenceNumber = int.Parse(seqMatch.Groups[1].Value);
#if DEBUG
                        Logger.LogDebug($"Начальный номер последовательности: {sequenceNumber}");
#endif
                    }
                }
                else if (line.StartsWith("#EXT-X-STREAM-INF") || line.Contains("CODECS="))
                {
                    // Парсим информацию о кодеке
                    var codecsMatch = Regex.Match(line, @"CODECS=""([^""]+)""");
                    if (codecsMatch.Success)
                    {
                        codecString = codecsMatch.Groups[1].Value;
#if DEBUG
                        Logger.LogDebug($"Найдена информация о кодеке: {codecString}");
#endif
                    }
                }
                else if (!line.StartsWith("#") && !string.IsNullOrWhiteSpace(line))
                {
                    // Это сегмент
                    string segmentUrl = line.Trim();
                    if (!segmentUrl.StartsWith("http"))
                    {
                        segmentUrl = basePath + segmentUrl;
                    }

                    var segment = new HlsSegment
                    {
                        Url = segmentUrl,
                        SequenceNumber = sequenceNumber++,
                        Iv = explicitIv
                    };

                    segments.Add(segment);
#if DEBUG
                    Logger.LogDebug($"Добавлен HLS сегмент: {segmentUrl}, Sequence: {segment.SequenceNumber}");
#endif
                }
            }

            // Определяем тип кодека на основе информации из плейлиста
            if (!string.IsNullOrEmpty(codecString))
            {
                if (codecString.Contains("mp4a.40.2") || codecString.Contains("aac"))
                {
                    codec = AudioCodec.Aac;
#if DEBUG
                    Logger.Info($"Определен кодек AAC на основе CODECS: {codecString}");
#endif
                }
                else if (codecString.Contains("mp3"))
                {
                    codec = AudioCodec.Mp3;
#if DEBUG
                    Logger.Info($"Определен кодек MP3 на основе CODECS: {codecString}");
#endif
                }
                else
                {
                    // По умолчанию пытаемся определить по расширению сегментов
                    if (segments.Count > 0 && segments[0].Url.Contains(".ts"))
                    {
                        codec = AudioCodec.Aac; // .ts сегменты обычно содержат AAC
#if DEBUG
                        Logger.Info("Определен кодек AAC по расширению .ts (по умолчанию для HLS)");
#endif
                    }
                    else
                    {
                        codec = AudioCodec.Mp3; // fallback
#if DEBUG
                        Logger.Info("Определен кодек MP3 по умолчанию");
#endif
                    }
                }
            }
            else
            {
                // Если CODECS не указан, определяем по расширению
                if (segments.Count > 0 && segments[0].Url.Contains(".ts"))
                {
                    codec = AudioCodec.Aac;
#if DEBUG
                    Logger.Info("Определен кодек AAC по расширению .ts (CODECS не указан)");
#endif
                }
                else
                {
                    codec = AudioCodec.Mp3;
#if DEBUG
                    Logger.Info("Определен кодек MP3 по умолчанию (CODECS не указан)");
#endif
                }
            }

            return segments.Count > 0;
        }

        /// <summary>
        /// Начинает потоковое воспроизведение HLS
        /// </summary>
        public async Task PlayAsync()
        {
            if (segments.Count == 0)
            {
                throw new InvalidOperationException("Плейлист не загружен");
            }

            playbackState = Mp3StreamingPanel.StreamingPlaybackState.Buffering;

            // Загружаем ключ шифрования если есть
            if (!string.IsNullOrEmpty(keyUrl))
            {
                await LoadDecryptionKeyAsync(cts.Token);
            }

            streamingTask = StreamSegmentsAsync();
        }

        /// <summary>
        /// Загружает ключ для дешифрования
        /// </summary>
        private async Task LoadDecryptionKeyAsync(CancellationToken token)
        {
            try
            {
                using (var response = await httpClient.GetAsync(keyUrl, token))
                {
                    response.EnsureSuccessStatusCode();
                    decryptionKey = await response.Content.ReadAsByteArrayAsync();
#if DEBUG
                    Logger.LogDebug("Ключ дешифрования загружен успешно");
#endif
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                Logger.Exception(ex, "Ошибка загрузки ключа дешифрования");
#endif
                throw;
            }
        }

        /// <summary>
        /// Потоковое воспроизведение сегментов
        /// </summary>
        private async Task StreamSegmentsAsync()
        {
            var token = cts.Token;

            try
            {
                while (currentSegmentIndex < segments.Count && !token.IsCancellationRequested)
                {
                    var segment = segments[currentSegmentIndex];

                    if (IsBufferNearlyFull)
                    {
                        await Task.Delay(500, token);
                        continue;
                    }

                    await StreamSegmentAsync(segment, token);
                    currentSegmentIndex++;

                    // Если это последний сегмент
                    if (currentSegmentIndex >= segments.Count)
                    {
                        fullyDownloaded = true;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                Logger.Exception(ex, "Ошибка при потоковом воспроизведении HLS сегментов");
#endif
                PlaybackStopped?.Invoke(this, new StoppedEventArgs(ex));
            }
        }

        /// <summary>
        /// Воспроизводит отдельный сегмент
        /// </summary>
        private async Task StreamSegmentAsync(HlsSegment segment, CancellationToken token)
        {
            using (var response = await httpClient.GetAsync(segment.Url, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();

                using (var stream = await response.Content.ReadAsStreamAsync())
                {
                    Stream audioStream = stream;

                    // Дешифруем если есть ключ
                    if (decryptionKey != null)
                    {
                        audioStream = await DecryptSegmentAsync(stream, segment, token);
                    }

                    // Декодируем в зависимости от типа кодека
                    if (codec == AudioCodec.Aac)
                    {
                        await DecodeAacSegmentAsync(audioStream, token);
                    }
                    else
                    {
                        await DecodeMp3SegmentAsync(audioStream, token);
                    }
                }
            }
        }

        /// <summary>
        /// Декодирует AAC сегмент в PCM
        /// </summary>
        private async Task DecodeAacSegmentAsync(Stream audioStream, CancellationToken token)
        {
#if DEBUG
            Logger.LogDebug($"Начало декодирования AAC сегмента. Размер потока: {audioStream.Length}, позиция: {audioStream.Position}");
#endif

            try
            {
                // Создаем временный файл для TS сегмента
                string tempTsFile = Path.Combine(Path.GetTempPath(), $"aac_segment_{Guid.NewGuid()}.ts");
                string tempWavFile = Path.Combine(Path.GetTempPath(), $"aac_decoded_{Guid.NewGuid()}.wav");

                try
                {
                    // Сохраняем TS поток во временный файл
                    using (var fileStream = File.Create(tempTsFile))
                    {
                        await audioStream.CopyToAsync(fileStream, token);
                    }

#if DEBUG
                    Logger.LogDebug($"TS сегмент сохранен во временный файл: {tempTsFile}, размер: {new FileInfo(tempTsFile).Length} байт");
#endif

                    // Сначала пытаемся извлечь AAC из TS контейнера
                    try
                    {
                        await ExtractAacFromTsAsync(tempTsFile, tempWavFile, token);
                    }
                    catch (Exception extractEx)
                    {
#if DEBUG
                        Logger.Exception(extractEx, $"Извлечение AAC не удалось, пробуем прямую конвертацию TS: {extractEx.Message}");
#endif

                        // Fallback: пытаемся конвертировать TS напрямую через MediaToolkit
                        if (IsMediaToolkitAvailable())
                        {
                            var inputFile = new MediaFile { Filename = tempTsFile };
                            var outputFile = new MediaFile { Filename = tempWavFile };

                            using (var engine = new Engine())
                            {
                                var options = new ConversionOptions
                                {
                                    AudioSampleRate = AudioSampleRate.Hz44100
                                };

                                engine.Convert(inputFile, outputFile, options);
                            }
                        }
                        else
                        {
                            // MediaToolkit недоступен, используем NAudio fallback
                            await DecodeAacWithNaudioAsync(tempTsFile, tempWavFile, token);
                        }
                    }

                    // Проверяем, что WAV файл был создан
                    if (!File.Exists(tempWavFile) || new FileInfo(tempWavFile).Length == 0)
                    {
#if DEBUG
                        Logger.Warning("WAV файл не создан или пустой, переключаемся на fallback");
#endif
                        await DecodeAacSegmentFallbackAsync(audioStream, token);
                        return;
                    }

#if DEBUG
                    Logger.LogDebug($"AAC сегмент декодирован в WAV: {tempWavFile}, размер: {new FileInfo(tempWavFile).Length} байт");
#endif

                    // Читаем декодированный WAV файл и добавляем в буфер
                    using (var wavStream = File.OpenRead(tempWavFile))
                    using (var wavReader = new WaveFileReader(wavStream))
                    {
                        // Инициализируем буфер если необходимо
                        if (bufferedWaveProvider == null)
                        {
                            bufferedWaveProvider = new BufferedWaveProvider(wavReader.WaveFormat);
                            bufferedWaveProvider.BufferDuration = TimeSpan.FromSeconds(20);
                        }

                        // Читаем WAV данные и добавляем в буфер
                        var buffer = new byte[4096];
                        int bytesRead;
                        while ((bytesRead = wavReader.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            bufferedWaveProvider.AddSamples(buffer, 0, bytesRead);
                            CheckBufferAndPlay();
                        }
                    }

#if DEBUG
                    Logger.Info($"AAC сегмент успешно декодирован и добавлен в буфер воспроизведения. Формат WAV: {bufferedWaveProvider.WaveFormat}");
#endif
                }
                finally
                {
                    // Очищаем временные файлы
                    try
                    {
                        if (File.Exists(tempTsFile)) File.Delete(tempTsFile);
                        if (File.Exists(tempWavFile)) File.Delete(tempWavFile);
                    }
                    catch (Exception cleanupEx)
                    {
#if DEBUG
                        Logger.LogDebug($"Ошибка при очистке временных файлов: {cleanupEx.Message}");
#endif
                    }
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                Logger.Exception(ex, $"Ошибка при декодировании AAC сегмента: {ex.Message}. Тип исключения: {ex.GetType().Name}. Детали: {ex.StackTrace}. Переключаемся на fallback");
#endif

                // Fallback на прямое воспроизведение
                try
                {
                    await DecodeAacSegmentFallbackAsync(audioStream, token);
                }
                catch (Exception fallbackEx)
                {
#if DEBUG
                    Logger.Exception(fallbackEx, $"Ошибка fallback декодирования AAC сегмента: {fallbackEx.Message}. Тип: {fallbackEx.GetType().Name}");
#endif
                    throw new Exception($"Все методы декодирования AAC сегмента не удались. Основная ошибка: {ex.Message}. Fallback ошибка: {fallbackEx.Message}", ex);
                }
            }
        }

        /// <summary>
        /// Проверяет доступность MediaToolkit
        /// </summary>
        private bool IsMediaToolkitAvailable()
        {
            try
            {
                // Проверяем наличие FFmpeg в системе
                var engine = new Engine();
                return engine != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Извлечение AAC потока из TS контейнера с приоритетом методов
        /// </summary>
        private async Task ExtractAacFromTsAsync(string tsFile, string outputFile, CancellationToken token)
        {
#if DEBUG
            Logger.LogDebug($"Извлечение AAC потока из TS файла: {tsFile}");
#endif

            try
            {
                // Метод 1: Прямое извлечение AAC через FFmpeg с правильными параметрами
                string extractedAacFile = Path.Combine(Path.GetTempPath(), $"extracted_aac_{Guid.NewGuid()}.aac");

                try
                {
                    string ffmpegArgs = $"-i \"{tsFile}\" -c:a copy -bsf:a aac_adtstoasc -f adts \"{extractedAacFile}\"";
                    var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = ffmpegArgs,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    });

                    if (process != null)
                    {
                        await process.WaitForExitAsync(token);
                        if (process.ExitCode == 0 && File.Exists(extractedAacFile) && new FileInfo(extractedAacFile).Length > 0)
                        {
#if DEBUG
                            Logger.Info("AAC успешно извлечен из TS через FFmpeg (copy mode)");
#endif
                        }
                        else
                        {
                            string error = await process.StandardError.ReadToEndAsync();
#if DEBUG
                            Logger.Warning($"FFmpeg copy mode завершился с ошибкой: {error}, пробуем перекодирование");
#endif
                            throw new Exception($"FFmpeg copy failed: {error}");
                        }
                    }
                }
                catch (Exception ffmpegCopyEx)
                {
#if DEBUG
                    Logger.Exception(ffmpegCopyEx, "FFmpeg copy mode не удался, пробуем перекодирование");
#endif

                    // Метод 2: Перекодирование AAC через FFmpeg
                    try
                    {
                        string ffmpegArgs = $"-i \"{tsFile}\" -c:a aac -ar 44100 -f adts \"{extractedAacFile}\"";
                        var process = Process.Start(new ProcessStartInfo
                        {
                            FileName = "ffmpeg",
                            Arguments = ffmpegArgs,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        });

                        if (process != null)
                        {
                            await process.WaitForExitAsync(token);
                            if (process.ExitCode != 0 || !File.Exists(extractedAacFile) || new FileInfo(extractedAacFile).Length == 0)
                            {
                                string error = await process.StandardError.ReadToEndAsync();
                                throw new Exception($"FFmpeg recode failed: {error}");
                            }
#if DEBUG
                            Logger.Info("AAC успешно перекодирован из TS через FFmpeg");
#endif
                        }
                    }
                    catch (Exception ffmpegRecodeEx)
                    {
#if DEBUG
                        Logger.Exception(ffmpegRecodeEx, "FFmpeg перекодирование не удалось, пробуем MediaToolkit");
#endif

                        // Метод 3: Использование MediaToolkit для извлечения
                        using (var engine = new Engine())
                        {
                            var inputFile = new MediaFile { Filename = tsFile };
                            var outputAacFile = new MediaFile { Filename = extractedAacFile };

                            var options = new ConversionOptions
                            {
                                AudioSampleRate = AudioSampleRate.Hz44100
                            };

                            engine.Convert(inputFile, outputAacFile, options);
                        }

                        if (!File.Exists(extractedAacFile) || new FileInfo(extractedAacFile).Length == 0)
                        {
                            throw new Exception("MediaToolkit не создал AAC файл");
                        }

#if DEBUG
                        Logger.Info("AAC успешно извлечен через MediaToolkit");
#endif
                    }
                }

                // Конвертируем AAC в WAV
                try
                {
                    string ffmpegWavArgs = $"-i \"{extractedAacFile}\" -f wav -acodec pcm_s16le -ar 44100 \"{outputFile}\"";
                    var wavProcess = Process.Start(new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = ffmpegWavArgs,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    });

                    if (wavProcess != null)
                    {
                        await wavProcess.WaitForExitAsync(token);
                        if (wavProcess.ExitCode == 0 && File.Exists(outputFile) && new FileInfo(outputFile).Length > 0)
                        {
#if DEBUG
                            Logger.Info("AAC успешно конвертирован в WAV");
#endif
                        }
                        else
                        {
                            string error = await wavProcess.StandardError.ReadToEndAsync();
                            throw new Exception($"WAV conversion failed: {error}");
                        }
                    }
                }
                catch (Exception wavEx)
                {
#if DEBUG
                    Logger.Exception(wavEx, "Конвертация AAC в WAV не удалась, пробуем MediaToolkit");
#endif

                    // Fallback: MediaToolkit для конвертации AAC в WAV
                    using (var engine = new Engine())
                    {
                        var inputAacFile = new MediaFile { Filename = extractedAacFile };
                        var outputWavFile = new MediaFile { Filename = outputFile };

                        var options = new ConversionOptions
                        {
                            AudioSampleRate = AudioSampleRate.Hz44100
                        };

                        engine.Convert(inputAacFile, outputWavFile, options);
                    }
                }

                // Очищаем временный AAC файл
                try
                {
                    if (File.Exists(extractedAacFile)) File.Delete(extractedAacFile);
                }
                catch (Exception cleanupEx)
                {
#if DEBUG
                    Logger.LogDebug($"Ошибка при очистке временного AAC файла: {cleanupEx.Message}");
#endif
                }

#if DEBUG
                Logger.Info("AAC поток успешно извлечен из TS контейнера и конвертирован в WAV");
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                Logger.Exception(ex, $"Все методы извлечения AAC из TS не удались: {ex.Message}. Тип: {ex.GetType().Name}. Переключаемся на прямое декодирование TS через NAudio");
#endif
                // Fallback на прямое декодирование TS через NAudio
                await DecodeAacWithNaudioAsync(tsFile, outputFile, token);
            }
        }
        /// <summary>
        /// Альтернативное декодирование AAC/TS с использованием NAudio
        /// </summary>
        private async Task DecodeAacWithNaudioAsync(string inputFile, string outputFile, CancellationToken token)
        {
#if DEBUG
            Logger.LogDebug($"Альтернативное декодирование AAC/TS с NAudio: {inputFile}");
#endif

            try
            {
                // Пытаемся декодировать TS/AAC через MediaFoundationReader
                using (var reader = new MediaFoundationReader(inputFile))
                using (var writer = new WaveFileWriter(outputFile, reader.WaveFormat))
                {
                    reader.CopyTo(writer);
                }

#if DEBUG
                Logger.Info("AAC/TS файл успешно декодирован с помощью NAudio MediaFoundation");
#endif
            }
            catch (Exception naudEx)
            {
#if DEBUG
                Logger.Exception(naudEx, $"NAudio MediaFoundation декодирование не удалось: {naudEx.Message}");
#endif

                // Fallback: пытаемся декодировать как raw AAC через FFmpeg напрямую
                try
                {
                    string ffmpegArgs = $"-i \"{inputFile}\" -f wav -acodec pcm_s16le -ar 44100 \"{outputFile}\"";
                    var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = ffmpegArgs,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    });

                    if (process != null)
                    {
                        await process.WaitForExitAsync(token);
                        if (process.ExitCode == 0)
                        {
#if DEBUG
                            Logger.Info("AAC/TS успешно декодирован через FFmpeg напрямую");
#endif
                            return;
                        }
                        else
                        {
                            string error = await process.StandardError.ReadToEndAsync();
#if DEBUG
                            Logger.Warning($"FFmpeg декодирование завершилось с ошибкой: {error}");
#endif
                        }
                    }
                }
                catch (Exception ffmpegEx)
                {
#if DEBUG
                    Logger.Exception(ffmpegEx, $"FFmpeg fallback декодирование не удалось: {ffmpegEx.Message}");
#endif
                }

                // Последний fallback - копируем как raw данные с предположением формата
                try
                {
                    using (var input = File.OpenRead(inputFile))
                    using (var output = File.Create(outputFile))
                    {
                        // Предполагаем стандартный WAV формат для AAC (44.1kHz, stereo, 16-bit)
                        var waveFormat = new WaveFormat(44100, 16, 2);
                        using (var writer = new WaveFileWriter(output, waveFormat))
                        {
                            await input.CopyToAsync(writer, token);
                        }
                    }
#if DEBUG
                    Logger.Warning("AAC/TS декодирован как raw данные (может не воспроизводиться корректно)");
#endif
                }
                catch (Exception rawEx)
                {
#if DEBUG
                    Logger.Exception(rawEx, $"Ошибка при raw декодировании AAC/TS: {rawEx.Message}. Тип: {rawEx.GetType().Name}");
#endif
                    throw new Exception($"Все методы декодирования AAC/TS не удались. Raw ошибка: {rawEx.Message}", rawEx);
                }
            }
        }

        /// <summary>
        /// Fallback декодирование AAC сегмента (прямое воспроизведение без декодирования)
        /// </summary>
        private async Task DecodeAacSegmentFallbackAsync(Stream audioStream, CancellationToken token)
        {
#if DEBUG
            Logger.LogDebug("Использование fallback режима для AAC сегмента");
#endif

            try
            {
                // Улучшенный fallback режим с попыткой декодирования AAC через NAudio
                try
                {
                    // Сначала пытаемся декодировать через MediaFoundation (если AAC поддерживается)
                    string tempAacFile = Path.Combine(Path.GetTempPath(), $"fallback_aac_{Guid.NewGuid()}.aac");

                    using (var fileStream = File.Create(tempAacFile))
                    {
                        await audioStream.CopyToAsync(fileStream, token);
                    }

                    // Пытаемся декодировать AAC через MediaFoundationReader
                    using (var reader = new MediaFoundationReader(tempAacFile))
                    {
                        // Инициализируем буфер с правильным форматом
                        if (bufferedWaveProvider == null)
                        {
                            bufferedWaveProvider = new BufferedWaveProvider(reader.WaveFormat);
                            bufferedWaveProvider.BufferDuration = TimeSpan.FromSeconds(20);
                        }

                        // Читаем и добавляем декодированные данные в буфер
                        var buffer = new byte[4096];
                        int bytesRead;
                        while ((bytesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            bufferedWaveProvider.AddSamples(buffer, 0, bytesRead);
                            CheckBufferAndPlay();
                        }
                    }

                    // Очищаем временный файл
                    try
                    {
                        if (File.Exists(tempAacFile)) File.Delete(tempAacFile);
                    }
                    catch (Exception cleanupEx)
                    {
#if DEBUG
                        Logger.LogDebug($"Ошибка при очистке временного AAC файла: {cleanupEx.Message}");
#endif
                    }

#if DEBUG
                    Logger.Info("AAC сегмент успешно декодирован в fallback режиме через MediaFoundation");
#endif
                }
                catch (Exception mfEx)
                {
#if DEBUG
                    Logger.Warning($"MediaFoundation декодирование не удалось ({mfEx.Message}), пробуем FFmpeg fallback");
#endif

                    // FFmpeg fallback для AAC
                    try
                    {
                        string tempInputFile = Path.Combine(Path.GetTempPath(), $"fallback_input_{Guid.NewGuid()}.ts");
                        string tempOutputFile = Path.Combine(Path.GetTempPath(), $"fallback_output_{Guid.NewGuid()}.wav");

                        // Сохраняем поток во временный файл
                        audioStream.Position = 0; // Сбрасываем позицию потока
                        using (var fileStream = File.Create(tempInputFile))
                        {
                            await audioStream.CopyToAsync(fileStream, token);
                        }

                        // Пытаемся декодировать через FFmpeg
                        string ffmpegArgs = $"-i \"{tempInputFile}\" -f wav -acodec pcm_s16le -ar 44100 \"{tempOutputFile}\"";
                        var process = Process.Start(new ProcessStartInfo
                        {
                            FileName = "ffmpeg",
                            Arguments = ffmpegArgs,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        });

                        if (process != null)
                        {
                            await process.WaitForExitAsync(token);
                            if (process.ExitCode == 0 && File.Exists(tempOutputFile))
                            {
                                // Читаем декодированный WAV файл
                                using (var wavStream = File.OpenRead(tempOutputFile))
                                using (var wavReader = new WaveFileReader(wavStream))
                                {
                                    if (bufferedWaveProvider == null)
                                    {
                                        bufferedWaveProvider = new BufferedWaveProvider(wavReader.WaveFormat);
                                        bufferedWaveProvider.BufferDuration = TimeSpan.FromSeconds(20);
                                    }

                                    var buffer = new byte[4096];
                                    int bytesRead;
                                    while ((bytesRead = wavReader.Read(buffer, 0, buffer.Length)) > 0)
                                    {
                                        bufferedWaveProvider.AddSamples(buffer, 0, bytesRead);
                                        CheckBufferAndPlay();
                                    }
                                }

#if DEBUG
                                Logger.Info("AAC сегмент успешно декодирован через FFmpeg fallback");
#endif
                            }
                            else
                            {
                                string error = await process.StandardError.ReadToEndAsync();
#if DEBUG
                                Logger.Warning($"FFmpeg fallback завершился с ошибкой: {error}");
#endif
                                throw new Exception($"FFmpeg error: {error}");
                            }
                        }

                        // Очищаем временные файлы
                        try
                        {
                            if (File.Exists(tempInputFile)) File.Delete(tempInputFile);
                            if (File.Exists(tempOutputFile)) File.Delete(tempOutputFile);
                        }
                        catch (Exception cleanupEx)
                        {
#if DEBUG
                            Logger.LogDebug($"Ошибка при очистке временных файлов FFmpeg fallback: {cleanupEx.Message}");
#endif
                        }
                    }
                    catch (Exception ffmpegEx)
                    {
#if DEBUG
                        Logger.Exception(ffmpegEx, $"FFmpeg fallback не удался: {ffmpegEx.Message}, переключаемся на raw режим");
#endif

                        // Raw fallback - копируем данные напрямую (последний вариант)
                        if (bufferedWaveProvider == null)
                        {
                            // Предполагаем стандартный формат AAC (44.1kHz, stereo, 16-bit)
                            var waveFormat = new WaveFormat(44100, 16, 2);
                            bufferedWaveProvider = new BufferedWaveProvider(waveFormat);
                            bufferedWaveProvider.BufferDuration = TimeSpan.FromSeconds(20);
                        }

                        // Копируем данные напрямую в буфер (без декодирования)
                        audioStream.Position = 0; // Сбрасываем позицию потока
                        var buffer = new byte[4096];
                        int bytesRead;
                        while ((bytesRead = await audioStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                        {
                            bufferedWaveProvider.AddSamples(buffer, 0, bytesRead);
                            CheckBufferAndPlay();
                        }

#if DEBUG
                        Logger.Warning("AAC сегмент добавлен в буфер в raw fallback режиме (может не воспроизводиться)");
#endif
                    }
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                Logger.Exception(ex, $"Критическая ошибка при fallback декодировании AAC сегмента: {ex.Message}. Тип: {ex.GetType().Name}. StackTrace: {ex.StackTrace}");
#endif
                throw new Exception($"Критическая ошибка декодирования AAC сегмента. Все методы декодирования исчерпаны: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Декодирует MP3 сегмент в PCM
        /// </summary>
        private async Task DecodeMp3SegmentAsync(Stream audioStream, CancellationToken token)
        {
#if DEBUG
            Logger.LogDebug("Начало декодирования MP3 сегмента");
#endif

            using (var readFullyStream = new ReadFullyStream(audioStream))
            {
                IMp3FrameDecompressor decompressor = null;

                while (!token.IsCancellationRequested)
                {
                    Mp3Frame frame;
                    try
                    {
                        frame = Mp3Frame.LoadFromStream(readFullyStream);
                    }
                    catch (EndOfStreamException)
                    {
                        break;
                    }

                    if (frame == null) break;

                    if (decompressor == null)
                    {
                        decompressor = Mp3StreamingPanel.CreateFrameDecompressor(frame);
                        bufferedWaveProvider = new BufferedWaveProvider(decompressor.OutputFormat);
                        bufferedWaveProvider.BufferDuration = TimeSpan.FromSeconds(20);
                    }

                    int decompressedBufferSize = decompressor.OutputFormat.AverageBytesPerSecond / 10;
                    var decompressedBuffer = new byte[decompressedBufferSize];
                    int decompressed = decompressor.DecompressFrame(frame, decompressedBuffer, 0);
                    bufferedWaveProvider.AddSamples(decompressedBuffer, 0, decompressed);
                    CheckBufferAndPlay();
                }
            }
        }

        /// <summary>
        /// Дешифрует сегмент AES-128 в режиме CTR
        /// </summary>
        private async Task<Stream> DecryptSegmentAsync(Stream encryptedStream, HlsSegment segment, CancellationToken token)
        {
            try
            {
#if DEBUG
                Logger.LogDebug($"Начало дешифровки HLS сегмента {segment.SequenceNumber}");
#endif

                byte[] iv;

                // Используем явный IV из сегмента или генерируем на основе номера последовательности
                if (segment.Iv != null)
                {
                    iv = segment.Iv;
#if DEBUG
                    Logger.LogDebug($"Используется явный IV из сегмента: {BitConverter.ToString(iv).Replace("-", "")}");
#endif
                }
                else
                {
                    // Генерируем IV на основе номера сегмента (big-endian, дополненный нулями)
                    iv = new byte[16];
                    byte[] seqBytes = BitConverter.GetBytes((uint)segment.SequenceNumber);
                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(seqBytes);
                    }
                    Array.Copy(seqBytes, 0, iv, iv.Length - seqBytes.Length, seqBytes.Length);

#if DEBUG
                    Logger.LogDebug($"Сгенерирован IV для сегмента {segment.SequenceNumber}: {BitConverter.ToString(iv).Replace("-", "")}");
#endif
                }

                // Реализуем CTR режим вручную (ECB + XOR)
                var memoryStream = new MemoryStream();
                var buffer = new byte[4096];
                int bytesRead;
                long blockIndex = 0;

                using (var aes = Aes.Create())
                {
                    aes.Key = decryptionKey;
                    aes.Mode = CipherMode.ECB;
                    aes.Padding = PaddingMode.None;

                    var encryptor = aes.CreateEncryptor();

                    while ((bytesRead = await encryptedStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                    {
                        // Обрабатываем данные блоками по 16 байт
                        for (int i = 0; i < bytesRead; i += 16)
                        {
                            int blockSize = Math.Min(16, bytesRead - i);

                            // Создаем счетчик для текущего блока
                            byte[] counter = new byte[16];
                            Array.Copy(iv, counter, 16);

                            // Инкрементируем счетчик (big-endian)
                            long currentBlockIndex = blockIndex + (i / 16);
                            byte[] blockIndexBytes = BitConverter.GetBytes(currentBlockIndex);
                            if (BitConverter.IsLittleEndian)
                            {
                                Array.Reverse(blockIndexBytes);
                            }
                            for (int j = 0; j < 8; j++)
                            {
                                counter[15 - j] ^= blockIndexBytes[7 - j];
                            }

                            // Шифруем счетчик в ECB режиме
                            byte[] encryptedCounter = new byte[16];
                            encryptor.TransformBlock(counter, 0, 16, encryptedCounter, 0);

                            // XOR с данными
                            for (int j = 0; j < blockSize; j++)
                            {
                                buffer[i + j] ^= encryptedCounter[j];
                            }
                        }

                        // Записываем дешифрованные данные
                        await memoryStream.WriteAsync(buffer, 0, bytesRead, token);
                        blockIndex += bytesRead / 16;
                        if (bytesRead % 16 != 0) blockIndex++;
                    }
                }

                memoryStream.Position = 0;

#if DEBUG
                Logger.LogDebug($"Дешифровка CTR завершена успешно, размер: {memoryStream.Length} байт");
#endif

                return memoryStream;
            }
            catch (CryptographicException cryptoEx)
            {
#if DEBUG
                Logger.Exception(cryptoEx, $"Ошибка криптографической операции при дешифровке HLS сегмента {segment.SequenceNumber}");
#endif
                throw new InvalidOperationException($"Ошибка дешифровки: {cryptoEx.Message}", cryptoEx);
            }
            catch (Exception ex)
            {
#if DEBUG
                Logger.Exception(ex, $"Неожиданная ошибка при дешифровке HLS сегмента {segment.SequenceNumber}");
#endif
                throw;
            }
        }

        /// <summary>
        /// Преобразует hex строку в массив байт
        /// </summary>
        private static byte[] StringToByteArray(string hex)
        {
            int length = hex.Length;
            byte[] bytes = new byte[length / 2];
            for (int i = 0; i < length; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }
            return bytes;
        }

        /// <summary>
        /// Проверяет, заполнен ли буфер
        /// </summary>
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
        /// Останавливает воспроизведение
        /// </summary>
        public void Stop()
        {
            cts.Cancel();

            if (streamingTask != null)
            {
                try
                {
                    streamingTask.Wait(TimeSpan.FromSeconds(2));
                }
                catch (AggregateException) { }
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

            playbackState = Mp3StreamingPanel.StreamingPlaybackState.Stopped;
            fullyDownloaded = false;
            currentSegmentIndex = 0;
        }

        /// <summary>
        /// Проверяет буфер и запускает воспроизведение
        /// </summary>
        private void CheckBufferAndPlay()
        {
            if (waveOut == null && bufferedWaveProvider != null && bufferedWaveProvider.BufferedDuration.TotalSeconds > 1)
            {
                try
                {
                    waveOut = createWaveOut();
                    waveOut.PlaybackStopped += (s, e) => PlaybackStopped?.Invoke(this, e);
                    volumeProvider = new VolumeWaveProvider16(bufferedWaveProvider);
                    volumeProvider.Volume = 1.0f;
                    waveOut.Init(volumeProvider);
                    waveOut.Play();
                    playbackState = Mp3StreamingPanel.StreamingPlaybackState.Playing;
#if DEBUG
                    Logger.Info("HLS воспроизведение начато");
#endif
                }
                catch (Exception ex)
                {
#if DEBUG
                    Logger.Exception(ex, "Ошибка инициализации WaveOut в HLS плеере");
#endif
                }
            }
        }

        /// <summary>
        /// Освобождает ресурсы
        /// </summary>
        public void Dispose()
        {
            Stop();
            cts.Dispose();
            httpClient.Dispose();
        }
    }

    /// <summary>
    /// HLS сегмент
    /// </summary>
    public class HlsSegment
    {
        public string Url { get; set; } = string.Empty;
        public double Duration { get; set; }
        public int SequenceNumber { get; set; }
        public byte[]? Iv { get; set; }
    }

    public partial class Mp3StreamingPanel : UserControl
    {
        /// <summary>
        /// Состояния проигрывателя
        /// </summary>
        public enum StreamingPlaybackState
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

        // HLS плеер
        private HlsPlayer hlsPlayer;
        private bool isHlsMode = false;

        // Список треков для воспроизведения
        private List<Track> trackList;
        // Индекс текущего трека
        private int currentTrackIndex = -1;

        // Поля для прогресса загрузки
        private int downloadingCurrentFilePercent;
        private int downloadingTotalFiles;
        private int downloadingCompletedFiles;
        private string downloadingCurrentFileName = string.Empty;
        private bool isDownloading;
        private PictureBox customProgressBar;

        private string _statusText = string.Empty;
        /// <summary>
        /// Текст статуса для отображения в прогресс-баре
        /// </summary>
        public string StatusText
        {
            get { return _statusText; }
            set
            {
                _statusText = value;
                if (customProgressBar != null)
                {
                    if (customProgressBar.InvokeRequired)
                        customProgressBar.BeginInvoke(new Action(customProgressBar.Invalidate));
                    else
                        customProgressBar.Invalidate();
                }
            }
        }

        /// <summary>
        /// Событие при смене трека
        /// </summary>
        public event EventHandler<Track> TrackChanged;

        /// <summary>
        /// Конструктор панели проигрывателя MP3
        /// </summary>
        public Mp3StreamingPanel()
        {
            InitializeComponent();
            volumeSlider1.VolumeChanged += OnVolumeSliderChanged!;
            Disposed += MP3StreamingPanel_Disposing!;
            streamingCancellationTokenSource = new CancellationTokenSource();
            trackList = new List<Track>();
            
            // Инициализация полей, чтобы избежать предупреждений CS8618
            bufferedWaveProvider = null!;
            waveOut = null!;
            volumeProvider = null!;
            streamingTask = Task.CompletedTask;
            hlsPlayer = null!;

#if DEBUG
            Logger.Initialize();
            Logger.Info("MP3StreamingPanel инициализирован");
#endif
            InitializeCustomProgressBar();
        }

        // Метод для обновления прогресса
        public void SetDownloadProgress(int currentFilePercent, int totalFiles, int completedFiles, string currentFileName)
        {
            downloadingCurrentFilePercent = currentFilePercent;
            downloadingTotalFiles = totalFiles;
            downloadingCompletedFiles = completedFiles;
            downloadingCurrentFileName = currentFileName;
            isDownloading = completedFiles < totalFiles;
            
            if (customProgressBar != null)
            {
                if (customProgressBar.InvokeRequired)
                {
                    customProgressBar.BeginInvoke(new Action(customProgressBar.Invalidate));
                }
                else
                {
                    customProgressBar.Invalidate();
                }
            }
        }

        // Инициализация кастомного прогресс бара в конструкторе
        private void InitializeCustomProgressBar()
        {
            if (progressBarBuffer != null)
            {
                progressBarBuffer.Visible = false;
                
                customProgressBar = new PictureBox();
                customProgressBar.Location = progressBarBuffer.Location;
                customProgressBar.Size = progressBarBuffer.Size;
                customProgressBar.Anchor = progressBarBuffer.Anchor;
                customProgressBar.BackColor = Color.White;
                customProgressBar.Paint += CustomProgressBar_Paint;
                
                this.Controls.Add(customProgressBar);
                customProgressBar.BringToFront();
            }
        }

        private void CustomProgressBar_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            var rect = customProgressBar.ClientRectangle;
            
            // Очистка фона
            g.Clear(Color.White);
            
            // 1. Отрисовка буферизации (зеленая полоса)
            if (progressBarBuffer != null)
            {
                float bufferPercent = 0;
                if (progressBarBuffer.Maximum > 0)
                    bufferPercent = (float)progressBarBuffer.Value / progressBarBuffer.Maximum;
                    
                if (bufferPercent > 1) bufferPercent = 1;
                int bufferWidth = (int)(rect.Width * bufferPercent);
                if (bufferWidth > 0)
                {
                    using (var brush = new SolidBrush(Color.LightGreen))
                    {
                        g.FillRectangle(brush, 0, 0, bufferWidth, rect.Height);
                    }
                }
            }
            
            // 2. Отрисовка общего прогресса загрузки (синяя полоса)
            if (isDownloading && downloadingTotalFiles > 0)
            {
                float totalProgressPercent = (float)downloadingCompletedFiles / downloadingTotalFiles;
                int totalWidth = (int)(rect.Width * totalProgressPercent);
                
                // Рисуем синюю полосу поверх зеленой
                using (var brush = new SolidBrush(Color.FromArgb(128, Color.Blue))) // Полупрозрачный синий
                {
                    g.FillRectangle(brush, 0, 0, totalWidth, rect.Height);
                }
                
                // 3. Отрисовка прогресса текущего файла (желтая полоса)
                float currentFileGlobalPercent = (float)downloadingCurrentFilePercent / 100 / downloadingTotalFiles;
                int currentFileWidth = (int)(rect.Width * currentFileGlobalPercent);
                int startX = totalWidth;
                
                using (var brush = new SolidBrush(Color.FromArgb(128, Color.Yellow)))
                {
                    g.FillRectangle(brush, startX, 0, currentFileWidth, rect.Height);
                }
                
                // 4. Текст
                string text = $"Загрузка: {downloadingCurrentFileName} ({downloadingCurrentFilePercent}%)";
                    
                using (var font = new Font("Arial", 8))
                using (var brush = new SolidBrush(Color.Black))
                {
                    var textSize = g.MeasureString(text, font);
                    float textX = (rect.Width - textSize.Width) / 2;
                    float textY = (rect.Height - textSize.Height) / 2;
                    g.DrawString(text, font, brush, textX, textY);
                }
            }
            else if (!string.IsNullOrEmpty(StatusText))
            {
                // Отрисовка StatusText по центру
                using (var font = new Font("Arial", 9, FontStyle.Bold))
                using (var brush = new SolidBrush(Color.Black))
                {
                    var textSize = g.MeasureString(StatusText, font);
                    float textX = (rect.Width - textSize.Width) / 2;
                    float textY = (rect.Height - textSize.Height) / 2;
                    g.DrawString(StatusText, font, brush, textX, textY);
                }
            }
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
            
            // Проверяем и декодируем URL каждого трека перед сохранением списка
            foreach (var track in tracks)
            {
                if (!string.IsNullOrEmpty(track.Url))
                {
                    // Дополнительное декодирование URL для корректной обработки ссылок из ВКонтакте
                    track.Url = WebUtility.UrlDecode(track.Url);
#if DEBUG
                    Logger.LogDebug($"Декодирован URL трека '{track.Artist} - {track.Title}': {track.Url}");
#endif
                }
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
            if (isHlsMode && hlsPlayer != null)
            {
                // HLS плеер не поддерживает изменение громкости в данной реализации
                // Можно добавить поддержку через системный микшер или другие средства
#if DEBUG
                Logger.LogDebug("Изменение громкости в HLS режиме не поддерживается");
#endif
                return;
            }

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
            if (isDisposed || !this.IsHandleCreated) return;

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
        private async Task StreamMp3Async(string url)
        {
            if (isDisposed) return;

            // Дополнительное декодирование URL для корректной обработки ссылок из ВКонтакте
            if (!string.IsNullOrEmpty(url))
            {
                url = WebUtility.UrlDecode(url);
#if DEBUG
                Logger.LogDebug($"URL после декодирования: {url}");
#endif
            }

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

                // Проверяем, является ли URL HLS плейлистом (.m3u8)
                if (url.Contains(".m3u8") || url.Contains("index.m3u8"))
                {
#if DEBUG
                    Logger.Info($"Обнаружен HLS плейлист: {url}");
#endif
                    // Проверяем наличие FFmpeg
                    if (IsFfmpegAvailable())
                    {
#if DEBUG
                        Logger.Info("FFmpeg найден, используем потоковое воспроизведение через FFmpeg");
#endif
                        isHlsMode = true;
                        await StreamHlsWithFfmpegAsync(url, token);
                        return;
                    }
                    else
                    {
#if DEBUG
                        Logger.Warning("FFmpeg не найден. Попытка использовать встроенный HLS плеер.");
#endif
                        // Используем HLS плеер для зашифрованных сегментов
                        isHlsMode = true;
                        hlsPlayer = new HlsPlayer(() => CreateWaveOut());
                        hlsPlayer.PlaybackStopped += OnHlsPlaybackStopped;

                        bool loaded = await hlsPlayer.LoadPlaylistAsync(url, token);
                        if (loaded)
                        {
                            await hlsPlayer.PlayAsync();
                            return;
                        }
                        else
                        {
#if DEBUG
                            Logger.Warning("Не удалось загрузить HLS плейлист, переключаемся на fallback");
#endif
                            // Fallback на старый метод парсинга
                            string audioUrl = await ParseHlsPlaylistAsync(url, token);
                            if (!string.IsNullOrEmpty(audioUrl))
                            {
                                url = audioUrl;
                            }
                        }
                    }
                }

                string actualUrl = url;

                // Добавляем User-Agent и Referer для предотвращения блокировки запросов
                if (!httpClient.DefaultRequestHeaders.Contains("User-Agent"))
                {
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                }

                // Добавляем Referer, чтобы обойти ограничения некоторых серверов
                if (httpClient.DefaultRequestHeaders.Contains("Referer"))
                {
                    httpClient.DefaultRequestHeaders.Remove("Referer");
                }
                httpClient.DefaultRequestHeaders.Add("Referer", "https://vk.com/");

#if DEBUG
                 Logger.LogDebug($"Отправка асинхронного HTTP запроса для получения аудио: {actualUrl}");
#endif
                 using (var response = await httpClient.GetAsync(actualUrl, HttpCompletionOption.ResponseHeadersRead, token))
                 {
                     response.EnsureSuccessStatusCode();
#if DEBUG
                    Logger.LogDebug($"Получен ответ от сервера: {response.StatusCode}");
#endif
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    {
                        // Адаптивный размер буфера на основе скорости сети
                        var httpBufferSize = CalculateBufferSize(response.Content.Headers.ContentLength ?? 16384 * 4);
                        var buffer = new byte[httpBufferSize];
                        IMp3FrameDecompressor decompressor = null;

                        using (var readFullyStream = new ReadFullyStream(stream))
                        {
                            while (!token.IsCancellationRequested)
                            {
                                if (isDisposed) break;

                                if (IsBufferNearlyFull)
                                {
                                    Debug.WriteLine("Buffer getting full, taking a break");
                                    await Task.Delay(500, token);
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

                                int bufferSize = decompressor.OutputFormat.AverageBytesPerSecond / 10;
                                var decompressedBuffer = new byte[bufferSize];
                                int decompressed = decompressor.DecompressFrame(frame, decompressedBuffer, 0);
                                try
                                {
                                    bufferedWaveProvider.AddSamples(decompressedBuffer, 0, decompressed);
                                }
                                catch (Exception ex)
                                {
#if DEBUG
                                    Logger.Exception(ex, "Ошибка при добавлении сэмплов в буфер");
#endif
                                }
                            }
                        }
                    }
                }
            }
            catch (HttpRequestException httpEx) when (httpEx.StatusCode == HttpStatusCode.NotFound)
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
                        // Используем Task.Run вместо BeginInvoke с async void, чтобы избежать падения приложения при исключении
                        Task.Run(async () =>
                        {
                            try
                            {
#if DEBUG
                                Logger.Info($"Попытка повторного воспроизведения трека после сетевой ошибки: {url}");
#endif
                                // Повторная попытка с экспоненциальной задержкой
                                await RetryWithBackoffAsync(() => StreamMp3Async(url), 3, token);
                            }
                            catch (Exception ex)
                            {
#if DEBUG
                                Logger.Exception(ex, "Ошибка при повторной попытке воспроизведения");
#endif
                            }
                        }, token);
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
            catch (OperationCanceledException)
            {
#if DEBUG
                Logger.Info("Потоковое воспроизведение отменено.");
#endif
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

        public static IMp3FrameDecompressor CreateFrameDecompressor(Mp3Frame frame)
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
#if DEBUG
            Logger.Info($"Нажата кнопка воспроизведения. Текущее состояние: {playbackState}, Текущий трек: {currentTrackIndex + 1}/{trackList.Count}");
#endif

            if (playbackState == StreamingPlaybackState.Stopped)
            {
                // Проверяем наличие URL трека
                if (string.IsNullOrEmpty(textBoxStreamingUrl.Text))
                {
                    if (trackList.Count > 0 && currentTrackIndex >= 0 && currentTrackIndex < trackList.Count)
                    {
                        textBoxStreamingUrl.Text = trackList[currentTrackIndex].Url;
#if DEBUG
                        Logger.LogDebug($"URL взят из списка треков: {trackList[currentTrackIndex].Url}");
#endif
                    }
                    else
                    {
                        ShowError("URL трека не указан");
                        return;
                    }
                }

                playbackState = Mp3StreamingPanel.StreamingPlaybackState.Buffering;
                bufferedWaveProvider = null;

                // Создаем новый токен отмены, если предыдущий был отменен
                if (streamingCancellationTokenSource.IsCancellationRequested)
                {
                    streamingCancellationTokenSource.Dispose();
                    streamingCancellationTokenSource = new CancellationTokenSource();
#if DEBUG
                    Logger.LogDebug("Создан новый токен отмены для потокового воспроизведения");
#endif
                }

                // Обновляем информацию о текущем треке перед началом воспроизведения
                if (trackList.Count > 0 && currentTrackIndex >= 0 && currentTrackIndex < trackList.Count)
                {
                    UpdateCurrentTrackInfo();
                }

                streamingTask = StreamMp3Async(textBoxStreamingUrl.Text);
                timer1.Enabled = true;

#if DEBUG
                Logger.Info($"Начало воспроизведения трека: '{(currentTrackIndex >= 0 && currentTrackIndex < trackList.Count ? trackList[currentTrackIndex].Title : textBoxStreamingUrl.Text)}'");
#endif
            }
            else if (playbackState == StreamingPlaybackState.Paused)
            {
                playbackState = Mp3StreamingPanel.StreamingPlaybackState.Buffering;
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
            Logger.Info($"Остановка воспроизведения. Текущее состояние: {playbackState}, Текущий трек: {currentTrackIndex + 1}/{trackList.Count}");
#endif

            // Останавливаем HLS плеер если активен
            if (isHlsMode && hlsPlayer != null)
            {
                hlsPlayer.Stop();
                hlsPlayer.Dispose();
                hlsPlayer = null;
                isHlsMode = false;
#if DEBUG
                Logger.LogDebug("HLS плеер остановлен");
#endif
            }

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
#if DEBUG
                Logger.LogDebug("WaveOut устройство остановлено и освобождено");
#endif
            }

            if (bufferedWaveProvider != null)
            {
                bufferedWaveProvider.ClearBuffer();
                bufferedWaveProvider = null;
#if DEBUG
                Logger.LogDebug("Буфер очищен");
#endif
            }

            playbackState = Mp3StreamingPanel.StreamingPlaybackState.Stopped;
            fullyDownloaded = false;
#if DEBUG
            Logger.LogDebug("Воспроизведение полностью остановлено");
#endif
        }

        private void ShowBufferState(double totalSeconds)
        {
            labelBuffered.Text = String.Format("{0:0.0}s", totalSeconds);
            progressBarBuffer.Value = (int)(totalSeconds * 1000);
            if (customProgressBar != null) customProgressBar.Invalidate();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            if (isDisposed || !this.IsHandleCreated) return;

#if DEBUG
            // Logger.LogDebug($"Timer tick - PlaybackState: {playbackState}, FullyDownloaded: {fullyDownloaded}, CurrentTrackIndex: {currentTrackIndex}, IsHlsMode: {isHlsMode}");
#endif

            // Пропускаем обработку таймера для HLS режима, если используется встроенный HlsPlayer
            if (isHlsMode && hlsPlayer != null)
            {
                return;
            }

            if (playbackState != StreamingPlaybackState.Stopped)
            {
                if (waveOut == null && bufferedWaveProvider != null)
                {
#if DEBUG
                    Logger.Info("Создание WaveOut устройства");
#endif
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
                    var bufferPercentage = (bufferedWaveProvider.BufferedBytes * 100.0) / bufferedWaveProvider.BufferLength;
#if DEBUG
                    Logger.LogDebug($"Буферизация: {bufferedSeconds:F2}s ({bufferPercentage:F1}%), Состояние: {playbackState}");
#endif
                    ShowBufferState(bufferedSeconds);

                    // make it stutter less if we buffer up a decent amount before playing
                    if (bufferedSeconds < 0.5 && playbackState == StreamingPlaybackState.Playing && !fullyDownloaded)
                    {
#if DEBUG
                        Logger.Warning($"Буфер слишком мал ({bufferedSeconds:F2}s), приостанавливаем воспроизведение");
#endif
                        Pause();
                    }
                    else if (bufferedSeconds > 4 && playbackState == StreamingPlaybackState.Buffering)
                    {
#if DEBUG
                        Logger.Info($"Буфер достаточен ({bufferedSeconds:F2}s), возобновляем воспроизведение");
#endif
                        Play();
                    }
                    else if (fullyDownloaded && bufferedSeconds == 0)
                    {
                        Debug.WriteLine("Reached end of stream");
#if DEBUG
                        Logger.Info($"Достигнут конец трека '{(currentTrackIndex >= 0 && currentTrackIndex < trackList.Count ? trackList[currentTrackIndex].Title : "неизвестный")}', автоматическое переключение на следующий");
#endif
                        // Автоматически переключаемся на следующий трек
                        if (trackList.Count > 0 && currentTrackIndex >= 0)
                        {
                            var nextIndex = currentTrackIndex + 1;
                            if (nextIndex >= trackList.Count)
                            {
                                nextIndex = 0; // Переход к первому треку при достижении конца списка
#if DEBUG
                                Logger.Info("Достигнут конец списка, переход к первому треку");
#endif
                            }

#if DEBUG
                            Logger.Info($"Переключение с трека {currentTrackIndex + 1} на {nextIndex + 1}/{trackList.Count}");
#endif
                            StopPlayback();
                            currentTrackIndex = nextIndex;

                            // Устанавливаем URL нового трека
                            textBoxStreamingUrl.Text = trackList[currentTrackIndex].Url;

#if DEBUG
                            Logger.Info($"Автоматическое переключение на трек: '{trackList[currentTrackIndex].Title}'");
#endif

                            // Начинаем воспроизведение нового трека
                            BeginInvoke(new Action(() => buttonPlay_Click(null, EventArgs.Empty)));
                        }
                        else
                        {
#if DEBUG
                            Logger.Warning("Список треков пуст или не инициализирован");
#endif
                            StopPlayback();
                        }
                    }
                }
                else
                {
#if DEBUG
                    Logger.Warning("bufferedWaveProvider равен null");
#endif
                }
            }
            else
            {
#if DEBUG
                Logger.LogDebug("Воспроизведение остановлено, таймер не активен");
#endif
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
            playbackState = Mp3StreamingPanel.StreamingPlaybackState.Buffering;
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
        /// Обработчик остановки HLS воспроизведения
        /// </summary>
        private void OnHlsPlaybackStopped(object sender, StoppedEventArgs e)
        {
#if DEBUG
            Logger.Info("HLS воспроизведение остановлено");
#endif
            if (e.Exception != null)
            {
#if DEBUG
                Logger.Exception(e.Exception, "Ошибка HLS воспроизведения");
#endif
                ShowError($"Ошибка HLS воспроизведения: {e.Exception.Message}");
            }

            // Останавливаем HLS плеер
            if (hlsPlayer != null)
            {
                hlsPlayer.Stop();
                hlsPlayer.Dispose();
                hlsPlayer = null;
            }

            isHlsMode = false;
            playbackState = Mp3StreamingPanel.StreamingPlaybackState.Stopped;
        }

        /// <summary>
        /// Освобождение ресурсов при уничтожении панели
        /// </summary>
        private bool isDisposed = false;

        private void MP3StreamingPanel_Disposing(object sender, EventArgs e)
        {
            isDisposed = true;
#if DEBUG
            Logger.Info("Освобождение ресурсов MP3StreamingPanel");
#endif
            StopPlayback();
            streamingCancellationTokenSource?.Dispose();

            // Освобождаем HLS плеер
            if (hlsPlayer != null)
            {
                hlsPlayer.Dispose();
                hlsPlayer = null;
            }
        }

        /// <summary>
        /// Обработчик нажатия кнопки паузы
        /// </summary>
        private void buttonPause_Click(object sender, EventArgs e)
        {
            if (isHlsMode && hlsPlayer != null)
            {
                // HLS плеер не поддерживает паузу, останавливаем
                StopPlayback();
#if DEBUG
                Logger.Info("HLS плеер не поддерживает паузу, остановлено воспроизведение");
#endif
                return;
            }

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
                Logger.Warning($"Невозможно переключить на предыдущий трек: список треков пуст ({trackList.Count}) или индекс не инициализирован ({currentTrackIndex})");
#endif
                return;
            }

            var oldIndex = currentTrackIndex;
            // Переключаемся на предыдущий трек
            currentTrackIndex--;
            if (currentTrackIndex < 0)
            {
                currentTrackIndex = trackList.Count - 1; // Переход к последнему треку при достижении начала списка
#if DEBUG
                Logger.Info("Достигнуто начало списка треков, переход к последнему треку");
#endif
            }
            
            // Убедимся, что индекс в допустимых пределах
            if (currentTrackIndex < 0 || currentTrackIndex >= trackList.Count)
            {
                currentTrackIndex = 0;
            }

#if DEBUG
            Logger.Info($"Переключение на предыдущий трек: {oldIndex + 1} -> {currentTrackIndex + 1}/{trackList.Count}");
#endif

            // Останавливаем текущее воспроизведение
            StopPlayback();

            // Устанавливаем URL нового трека
            textBoxStreamingUrl.Text = trackList[currentTrackIndex].Url;

            // Обновляем информацию о текущем треке в интерфейсе
            UpdateCurrentTrackInfo();

#if DEBUG
            Logger.Info($"Новый трек установлен: '{trackList[currentTrackIndex].Title}' - {trackList[currentTrackIndex].Url}");
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
                Logger.Warning($"Невозможно переключить на следующий трек: список треков пуст ({trackList.Count}) или индекс не инициализирован ({currentTrackIndex})");
#endif
                return;
            }

            var oldIndex = currentTrackIndex;
            // Переключаемся на следующий трек
            currentTrackIndex++;
            if (currentTrackIndex >= trackList.Count)
            {
                currentTrackIndex = 0; // Переход к первому треку при достижении конца списка
#if DEBUG
                Logger.Info("Достигнут конец списка треков, переход к первому треку");
#endif
            }

            // Убедимся, что индекс в допустимых пределах
            if (currentTrackIndex < 0 || currentTrackIndex >= trackList.Count)
            {
                currentTrackIndex = 0;
            }

#if DEBUG
            Logger.Info($"Переключение на следующий трек: {oldIndex + 1} -> {currentTrackIndex + 1}/{trackList.Count}");
#endif

            // Останавливаем текущее воспроизведение
            StopPlayback();

            // Устанавливаем URL нового трека
            textBoxStreamingUrl.Text = trackList[currentTrackIndex].Url;

            // Обновляем информацию о текущем треке в интерфейсе
            UpdateCurrentTrackInfo();

#if DEBUG
            Logger.Info($"Новый трек установлен: '{trackList[currentTrackIndex].Title}' - {trackList[currentTrackIndex].Url}");
#endif

            // Начинаем воспроизведение нового трека
            buttonPlay_Click(sender, e);
        }

        /// <summary>
        /// Выбрать трек для воспроизведения (без запуска)
        /// </summary>
        public void SelectTrack(Track track)
        {
            if (track == null) return;

            // Пытаемся найти трек в текущем списке
            int index = trackList.FindIndex(t => t.Url == track.Url && t.Title == track.Title);
            
            // Если трек уже выбран, не обновляем ничего, чтобы разорвать цикл
            if (index == currentTrackIndex && index >= 0)
            {
                return;
            }

            if (index >= 0)
            {
                currentTrackIndex = index;
            }
            else
            {
                // Если трека нет в списке, добавляем его или просто устанавливаем URL
                // В данном контексте лучше просто установить URL
                currentTrackIndex = -1;
            }

            textBoxStreamingUrl.Text = track.Url;
            
            // Если трек не из списка, UpdateCurrentTrackInfo может не сработать корректно,
            // если он полагается только на currentTrackIndex
            if (currentTrackIndex >= 0)
            {
                UpdateCurrentTrackInfo();
            }
        }

        /// <summary>
        /// Выбрать и воспроизвести трек
        /// </summary>
        public void PlayTrack(Track track)
        {
            SelectTrack(track);
            // Запускаем воспроизведение
            buttonPlay_Click(this, EventArgs.Empty);
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
                // Уведомляем подписчиков о смене трека
                TrackChanged?.Invoke(this, currentTrack);
            }
        }
        
        /// <summary>
        /// Рассчитывает оптимальный размер буфера на основе скорости сети
        /// </summary>
        private int CalculateBufferSize(long contentLength)
        {
            // Базовый размер буфера - 64KB
            int baseBufferSize = 65536;
            
            // Если размер контента известен, используем 10% от размера контента, но не более 1MB
            if (contentLength > 0)
            {
                return (int)Math.Min(contentLength / 10, 1048576);
            }
            
            return baseBufferSize;
        }
        
        /// <summary>
        /// Повторная попытка с экспоненциальной задержкой
        /// </summary>
        private async Task RetryWithBackoffAsync(Func<Task> operation, int maxRetries, CancellationToken token)
        {
            int retryCount = 0;
            while (retryCount < maxRetries && !token.IsCancellationRequested)
            {
                try
                {
                    await operation();
                    return;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    if (retryCount >= maxRetries)
                    {
                        throw;
                    }
                    
                    int delay = (int)Math.Pow(2, retryCount) * 1000;
#if DEBUG
                    Logger.LogDebug($"Повторная попытка {retryCount}/{maxRetries} через {delay}ms");
#endif
                    await Task.Delay(delay, token);
                }
            }
        }
        
        /// <summary>
        /// Кэширование аудиофрагментов на диск
        /// </summary>
        private async Task CacheAudioAsync(string url, Stream audioStream)
        {
            string cacheDir = Path.Combine(Path.GetTempPath(), "VKMusicCache");
            Directory.CreateDirectory(cacheDir);
            
            string cacheFile = Path.Combine(cacheDir, GetUrlHash(url) + ".mp3");
            
            using (var fileStream = File.Create(cacheFile))
            {
                await audioStream.CopyToAsync(fileStream);
            }
            
#if DEBUG
            Logger.LogDebug($"Аудиофрагмент сохранен в кэш: {cacheFile}");
#endif
        }
        
        /// <summary>
        /// Парсинг HLS плейлиста для извлечения лучшего доступного аудио сегмента (fallback метод)
        /// </summary>
        private async Task<string> ParseHlsPlaylistAsync(string playlistUrl, CancellationToken token)
        {
#if DEBUG
            Logger.Info($"Начало парсинга HLS плейлиста: {playlistUrl}");
#endif

            try
            {
                using (var response = await httpClient.GetAsync(playlistUrl, HttpCompletionOption.ResponseHeadersRead, token))
                {
                    response.EnsureSuccessStatusCode();

                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string playlistContent = await reader.ReadToEndAsync();

#if DEBUG
                        Logger.LogDebug($"Содержимое HLS плейлиста:\n{playlistContent}");
#endif

                        // Парсим содержимое построчно
                        var lines = playlistContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        var segments = new List<(string Url, int Priority)>();

                        // Определяем базовый URL для относительных ссылок
                        var baseUri = new Uri(playlistUrl);
                        string baseUrl = $"{baseUri.Scheme}://{baseUri.Host}{baseUri.AbsolutePath.Substring(0, baseUri.AbsolutePath.LastIndexOf('/') + 1)}";

                        foreach (var line in lines)
                        {
                            // Пропускаем комментарии и пустые строки
                            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                                continue;

                            string segmentUrl = line.Trim();

                            // Обрабатываем относительные URL
                            if (!segmentUrl.StartsWith("http://") && !segmentUrl.StartsWith("https://"))
                            {
                                segmentUrl = baseUrl + segmentUrl;
#if DEBUG
                                Logger.LogDebug($"Относительный URL преобразован в абсолютный: {segmentUrl}");
#endif
                            }

                            // Определяем приоритет формата
                            int priority = 0;
                            if (segmentUrl.Contains(".mp3"))
                                priority = 3; // MP3 - наивысший приоритет
                            else if (segmentUrl.Contains(".aac"))
                                priority = 2; // AAC - средний приоритет
                            else if (segmentUrl.Contains(".ts"))
                                priority = 1; // TS - низкий приоритет

                            if (priority > 0)
                            {
                                segments.Add((segmentUrl, priority));
#if DEBUG
                                Logger.LogDebug($"Найден аудио сегмент: {segmentUrl} (приоритет: {priority})");
#endif
                            }
                        }

                        // Выбираем сегмент с наивысшим приоритетом
                        if (segments.Count > 0)
                        {
                            var bestSegment = segments.OrderByDescending(s => s.Priority).First();
#if DEBUG
                            Logger.Info($"Выбран лучший аудио сегмент: {bestSegment.Url} (приоритет: {bestSegment.Priority})");
#endif
                            return bestSegment.Url;
                        }

                        // Fallback: если не найдены сегменты с известными расширениями, ищем первый URL в плейлисте
                        foreach (var line in lines)
                        {
                            if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("#") &&
                                (line.StartsWith("http://") || line.StartsWith("https://") || !line.Contains("://")))
                            {
                                string fallbackUrl = line.Trim();
                                if (!fallbackUrl.StartsWith("http://") && !fallbackUrl.StartsWith("https://"))
                                {
                                    fallbackUrl = baseUrl + fallbackUrl;
                                }
#if DEBUG
                                Logger.Info($"Fallback: выбран первый URL из HLS плейлиста: {fallbackUrl}");
#endif
                                return fallbackUrl;
                            }
                        }

                        // Дополнительный fallback: возвращаем оригинальный HLS URL для прямого воспроизведения
                        // Некоторые плееры могут обрабатывать HLS как обычный поток
#if DEBUG
                        Logger.Info($"Дополнительный fallback: возвращаем оригинальный HLS URL для прямого воспроизведения: {playlistUrl}");
#endif
                        return playlistUrl;

#if DEBUG
                        Logger.Warning("В HLS плейлисте не найдены подходящие аудио сегменты или URL");
#endif
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                Logger.Exception(ex, $"Ошибка при парсинге HLS плейлиста: {playlistUrl}");
#endif
                return null;
            }
        }

        /// <summary>
        /// Генерация хэша для URL
        /// </summary>
        private string GetUrlHash(string url)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(url));
                return BitConverter.ToString(hashBytes).Replace("-", "");
            }
        }
        /// <summary>
        /// Проверяет наличие FFmpeg в системе
        /// </summary>
        private bool IsFfmpegAvailable()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = "-version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Потоковое воспроизведение HLS через FFmpeg pipe
        /// </summary>
        private async Task StreamHlsWithFfmpegAsync(string url, CancellationToken token)
        {
            try
            {
#if DEBUG
                Logger.Info($"Запуск FFmpeg для потока: {url}");
#endif
                // Добавляем User-Agent и Referer для обхода защиты VK
                // -user_agent "Mozilla/5.0..." -headers "Referer: https://vk.com/"
                string userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36";
                string headers = "Referer: https://vk.com/";
                
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-user_agent \"{userAgent}\" -headers \"{headers}\" -i \"{url}\" -f s16le -acodec pcm_s16le -ar 44100 -ac 2 -",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                Logger.Info($"Запуск FFmpeg с аргументами: {processStartInfo.Arguments}");
                using (var process = Process.Start(processStartInfo))
                {
                    if (process == null) throw new Exception("Не удалось запустить FFmpeg (Process.Start вернул null)");

                    // Асинхронное чтение ошибок FFmpeg для диагностики
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            string errorLine;
                            while ((errorLine = await process.StandardError.ReadLineAsync()) != null)
                            {
#if DEBUG
                                // Логируем все сообщения FFmpeg для отладки
                                Logger.LogDebug($"FFmpeg stderr: {errorLine}");
#endif
                            }
                        }
                        catch (Exception ex)
                        {
#if DEBUG
                            Logger.Exception(ex, "Ошибка при чтении stderr FFmpeg");
#endif
                        }
                    });

                    // Читаем стандартный вывод FFmpeg (PCM данные)
                    using (var stream = process.StandardOutput.BaseStream)
                    {
                        var buffer = new byte[16384]; // 16KB буфер
                        int totalBytesRead = 0;
                        
                        // Инициализируем провайдер, если еще не создан
                        if (bufferedWaveProvider == null)
                        {
                            bufferedWaveProvider = new BufferedWaveProvider(new WaveFormat(44100, 16, 2));
                            bufferedWaveProvider.BufferDuration = TimeSpan.FromSeconds(20);
                        }

                        while (!token.IsCancellationRequested)
                        {
                            if (IsBufferNearlyFull)
                            {
                                await Task.Delay(500, token);
                                continue;
                            }

                            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                            if (bytesRead > 0)
                            {
                                // Logger.LogDebug($"FFmpeg read {bytesRead} bytes");
                            }
                            if (bytesRead == 0)
                            {
#if DEBUG
                                Logger.Info($"FFmpeg stdout закрыт. Всего прочитано байт: {totalBytesRead}");
#endif
                                fullyDownloaded = true;
                                break;
                            }
                            
                            totalBytesRead += bytesRead;
                            try
                            {
                                bufferedWaveProvider.AddSamples(buffer, 0, bytesRead);
                            }
                            catch (Exception ex)
                            {
#if DEBUG
                                Logger.Exception(ex, "Ошибка при добавлении сэмплов в буфер (HLS)");
#endif
                            }
                        }
                    }
                    
                    if (!process.HasExited)
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch (Exception killEx)
                        {
#if DEBUG
                            Logger.Warning($"Не удалось принудительно завершить процесс FFmpeg: {killEx.Message}");
#endif
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
#if DEBUG
                Logger.Info("Воспроизведение HLS отменено пользователем");
#endif
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
#if DEBUG
                    Logger.Exception(ex, "Ошибка при воспроизведении HLS через FFmpeg");
#endif
                    ShowError($"Ошибка воспроизведения HLS: {ex.Message}\nУбедитесь, что FFmpeg установлен и доступен в PATH.");
                }
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
