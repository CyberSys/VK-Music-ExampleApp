using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using VK_Music.Utils;

namespace VK_Music
{
    public partial class MainForm : Form
    {
        private INAudioDemoPlugin currentPlugin;
        public MainForm()
        {
            // use reflection to find all the demos
            var plugins = ReflectionHelper.CreateAllInstancesOf<INAudioDemoPlugin>().OrderBy(d => d.Name);

            InitializeComponent();
            listBoxPlugins.DisplayMember = "Name";
            foreach (var plugin in plugins)
            {
                listBoxPlugins.Items.Add(plugin);
            }

            var arch = Environment.Is64BitProcess ? "x64" : "x86";
            var framework = ((TargetFrameworkAttribute)(Assembly.GetEntryAssembly().GetCustomAttributes(typeof(TargetFrameworkAttribute), true).ToArray()[0])).FrameworkName;
            this.Text = $"{this.Text} ({framework}) ({arch})";
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            dataGridView1.DataSource = VK.GetListOfTracks();

        }

        private void OnLoadPluginClick(object sender, EventArgs e)
        {
            var plugin = (INAudioDemoPlugin)listBoxPlugins.SelectedItem;
            if (plugin == currentPlugin) return;
            currentPlugin = plugin;
            DisposeCurrentPlugin();
            var control = plugin.CreatePanel();
            control.Dock = DockStyle.Fill;
            panel1.Controls.Add(control);
        }


        private void DisposeCurrentPlugin()
        {
            if (panel1.Controls.Count <= 0) return;
            panel1.Controls[0].Dispose();
            panel1.Controls.Clear();
            GC.Collect();
        }
        private void dataGridView1_CellContentDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (currentPlugin != null)
            {
                // MP3StreamingPanel. extBoxStreamingUrl.Text = dataGridView1    // DataSource[currentPlugin].StreamingUrl;
            }
            else
                return;
        }

        private void listBoxPlugins_DoubleClick(object sender, EventArgs e)
        {
            OnLoadPluginClick(sender, e);
        }
    }
}
