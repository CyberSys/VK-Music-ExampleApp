using System;
using System.Windows.Forms;

namespace VK_Music
{
    public interface INAudioDemoPlugin
    {
        string Name { get; }
        Control CreatePanel();
    }
}
