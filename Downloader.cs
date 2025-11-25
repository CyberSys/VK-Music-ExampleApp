using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace VK_Music
{
    public class Downloader
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public event Action<string> OnLog;
        public event Action<int, int, int, string> OnProgress; // currentFilePercent, totalFiles, completedFiles, currentFileName

        private void Log(string message)
        {
            OnLog?.Invoke(message);
        }

        private void ReportProgress(int currentFilePercent, int totalFiles, int completedFiles, string currentFileName)
        {
            OnProgress?.Invoke(currentFilePercent, totalFiles, completedFiles, currentFileName);
        }

        public async Task DownloadTrackAsync(Track track, string destinationPath, int totalFiles = 1, int completedFiles = 0)
        {
            if (string.IsNullOrEmpty(track.Url))
            {
                string msg = $"URL not found for track: {track.Artist} - {track.Title}";
                Logger.Warning(msg);
                Log(msg);
                return;
            }

            Logger.Info($"Starting download: {track.Artist} - {track.Title}");

            try
            {
                var directory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (track.Url.Contains(".m3u8"))
                {
                    await DownloadM3u8Async(track.Url, destinationPath, totalFiles, completedFiles, $"{track.Artist} - {track.Title}");
                }
                else
                {
                    await DownloadMp3Async(track.Url, destinationPath, totalFiles, completedFiles, $"{track.Artist} - {track.Title}");
                }
                
                string successMsg = $"Downloaded: {track.Artist} - {track.Title}";
                Logger.Info(successMsg);
                Log(successMsg);
            }
            catch (Exception ex)
            {
                string errorMsg = $"Error downloading {track.Artist} - {track.Title}: {ex.Message}";
                Logger.Exception(ex, errorMsg);
                Log(errorMsg);
            }
        }

        private async Task DownloadMp3Async(string url, string destinationPath, int totalFiles, int completedFiles, string trackName)
        {
            using (var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var canReportProgress = totalBytes != -1;

                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    if (canReportProgress)
                    {
                        var buffer = new byte[8192];
                        var totalRead = 0L;
                        int bytesRead;
                        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;
                            int percent = (int)((totalRead * 100) / totalBytes);
                            ReportProgress(percent, totalFiles, completedFiles, trackName);
                        }
                    }
                    else
                    {
                        await stream.CopyToAsync(fileStream);
                        ReportProgress(50, totalFiles, completedFiles, trackName); // Indeterminate
                    }
                }
            }
        }

        private Task DownloadM3u8Async(string url, string destinationPath, int totalFiles, int completedFiles, string trackName)
        {
            var tcs = new TaskCompletionSource<bool>();

            // ffmpeg -i "url" "output.mp3" automatically handles codecs
            string arguments = $"-i \"{url}\" -y \"{destinationPath}\""; 

            var processStartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = new Process { StartInfo = processStartInfo };

            process.EnableRaisingEvents = true;
            process.Exited += (sender, args) =>
            {
                if (process.ExitCode == 0)
                {
                    tcs.SetResult(true);
                }
                else
                {
                    tcs.SetException(new Exception($"FFmpeg exited with code {process.ExitCode}"));
                }
                process.Dispose();
            };

            try
            {
                ReportProgress(0, totalFiles, completedFiles, trackName);
                process.Start();
                // FFmpeg progress parsing is complex, so we just report 50% while running
                ReportProgress(50, totalFiles, completedFiles, trackName);
            }
            catch (Exception ex)
            {
                string errorMsg = $"Failed to start ffmpeg. Make sure it is installed and in PATH. {ex.Message}";
                Logger.Exception(ex, errorMsg);
                tcs.SetException(new Exception(errorMsg));
            }

            return tcs.Task;
        }

        public async Task DownloadTracksAsync(IEnumerable<Track> tracks, string baseFolder)
        {
            var trackList = new List<Track>(tracks);
            int totalFiles = trackList.Count;
            int completedFiles = 0;

            foreach (var track in trackList)
            {
                string fileName = $"{track.Artist} - {track.Title}.mp3";
                fileName = SanitizeFileName(fileName);
                
                string destinationPath = Path.Combine(baseFolder, fileName);
                await DownloadTrackAsync(track, destinationPath, totalFiles, completedFiles);
                completedFiles++;
                ReportProgress(100, totalFiles, completedFiles, $"{track.Artist} - {track.Title}");
            }
            // Final report
            ReportProgress(100, totalFiles, totalFiles, "Все загрузки завершены");
        }

        private string SanitizeFileName(string fileName)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c, '_');
            }
            return fileName;
        }
    }
}