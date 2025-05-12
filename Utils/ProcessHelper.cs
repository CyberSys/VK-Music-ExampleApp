using System.Diagnostics;

namespace VK_Music.Utils
{
    static class ProcessHelper
    {
        public static void ShellExecute(string file)
        {
            var process = new Process();
            process.StartInfo = new ProcessStartInfo(file)
            {
                UseShellExecute = true
            };
            process.Start();
        }
    }
}
