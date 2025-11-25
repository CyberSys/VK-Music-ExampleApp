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
using System.IO;
using VK_Music.Utils;
namespace VK_Music
{
    public partial class MainForm : Form
    {
        private INAudioDemoPlugin currentPlugin;

        // UI Controls
        private Panel controlsPanel;
        private ComboBox modeComboBox;
        private TextBox searchTextBox;
        private Button searchButton;
        private ComboBox targetComboBox;
        private CheckBox showAlbumsCheckBox;

        // Data caches
        private List<VkNet.Model.User> friendsList;
        private List<VkNet.Model.Group> groupsList;

        public MainForm()
        {
#if DEBUG
            Logger.Info("Инициализация главной формы приложения");
#endif

            // use reflection to find all the demos
            var plugins = ReflectionHelper.CreateAllInstancesOf<INAudioDemoPlugin>().OrderBy(d => d.Name);

#if DEBUG
            Logger.Info($"Найдено плагинов: {plugins.Count()}");
            foreach (var plugin in plugins)
            {
                Logger.LogDebug($"Найден плагин: {plugin.Name}");
            }
#endif

            InitializeComponent();
            listBoxPlugins.DisplayMember = "Name";
            foreach (var plugin in plugins)
            {
                listBoxPlugins.Items.Add(plugin);
#if DEBUG
                Logger.LogDebug($"Плагин '{plugin.Name}' добавлен в список");
#endif
            }

            var arch = Environment.Is64BitProcess ? "x64" : "x86";
            var framework = ((TargetFrameworkAttribute)(Assembly.GetEntryAssembly().GetCustomAttributes(typeof(TargetFrameworkAttribute), true).ToArray()[0])).FrameworkName;
            this.Text = $"{this.Text} ({framework}) ({arch})";

#if DEBUG
            Logger.Info("Главная форма инициализирована");
#endif
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
#if DEBUG
            Logger.Info("Загрузка главной формы приложения");
#endif
            InitializeCustomControls();
            dataGridView1.DataSource = VK.GetListOfTracks();

            var contextMenu = new ContextMenuStrip();
            var downloadItem = new ToolStripMenuItem("Скачать");
            downloadItem.Click += DownloadItem_Click;
            contextMenu.Items.Add(downloadItem);
            
            var downloadAllItem = new ToolStripMenuItem("Скачать все");
            downloadAllItem.Click += DownloadAllItem_Click;
            contextMenu.Items.Add(downloadAllItem);
            
            dataGridView1.ContextMenuStrip = contextMenu;

            // Подписка на события для взаимодействия с плеером
            dataGridView1.SelectionChanged += DataGridView1_SelectionChanged;
            dataGridView1.CellDoubleClick += DataGridView1_CellDoubleClick;
            this.FormClosing += MainForm_FormClosing;
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
#if DEBUG
            Logger.Info("MainForm_FormClosing: Начало закрытия формы");
#endif
            DisposeCurrentPlugin();
        }

        private void DownloadItem_Click(object sender, EventArgs e)
        {
            DownloadTracks(false);
        }

        private void DownloadAllItem_Click(object sender, EventArgs e)
        {
            DownloadTracks(true);
        }

        private async void DownloadTracks(bool downloadAll)
        {
            var tracksToDownload = new List<Track>();
            var allTracks = dataGridView1.DataSource as List<Track>;

            if (allTracks == null || allTracks.Count == 0)
            {
                MessageBox.Show("Нет треков для скачивания.");
                return;
            }

            if (downloadAll)
            {
                tracksToDownload.AddRange(allTracks);
            }
            else
            {
                var selectedRows = dataGridView1.SelectedRows.Cast<DataGridViewRow>().ToList();
                if (selectedRows.Any())
                {
                    foreach (var row in selectedRows)
                    {
                        if (row.DataBoundItem is Track track)
                        {
                            tracksToDownload.Add(track);
                        }
                    }
                }
                else if (dataGridView1.SelectedCells.Count > 0)
                {
                    var selectedIndices = dataGridView1.SelectedCells.Cast<DataGridViewCell>()
                                          .Select(c => c.RowIndex)
                                          .Distinct();
                    foreach (var index in selectedIndices)
                    {
                        if (index >= 0 && index < allTracks.Count)
                        {
                            tracksToDownload.Add(allTracks[index]);
                        }
                    }
                }
            }

            if (tracksToDownload.Count == 0)
            {
                MessageBox.Show("Выберите треки для скачивания.");
                return;
            }

            // Определяем имя папки по умолчанию
            string defaultFolderName = "Unknown Album";
            if (targetComboBox.Visible && targetComboBox.SelectedItem != null)
            {
                if (targetComboBox.SelectedItem is VkNet.Model.AudioPlaylist playlist)
                {
                    defaultFolderName = ReplaceInvalidChars(playlist.Title);
                }
                else if (modeComboBox.SelectedItem.ToString() == "Друзья" || modeComboBox.SelectedItem.ToString() == "Группы")
                {
                     // Пытаемся получить имя из выбранного элемента
                     try {
                        dynamic item = targetComboBox.SelectedItem;
                        // Проверяем наличие свойства Name или Title
                        var props = item.GetType().GetProperties();
                        if (item.GetType().GetProperty("Name") != null) defaultFolderName = ReplaceInvalidChars(item.Name);
                        else if (item.GetType().GetProperty("Title") != null) defaultFolderName = ReplaceInvalidChars(item.Title);
                     } catch {}
                }
            }
            else if (modeComboBox.SelectedItem.ToString() == "Моя музыка")
            {
                defaultFolderName = "My Music";
            }

            using (var fbd = new FolderBrowserDialog())
            {
                // В FolderBrowserDialog нельзя задать имя новой папки программно,
                // но мы можем создать её сами внутри выбранной, если пользователь согласится.
                // Или просто скачиваем в выбранную.
                // По заданию: "в папку с названием текущего альбома (или "Unknown Album")"
                
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    string targetPath = Path.Combine(fbd.SelectedPath, defaultFolderName);
                    if (!Directory.Exists(targetPath))
                    {
                        Directory.CreateDirectory(targetPath);
                    }

                    var downloader = new Downloader();
                    var originalTitle = this.Text;
                    
                    SetStatus("Скачивание...");

                    downloader.OnLog += (msg) =>
                    {
                        if (this.IsHandleCreated)
                        {
                            this.Invoke((MethodInvoker)delegate
                            {
                                this.Text = $"Скачивание... {msg}";
                            });
                        }
                    };

                    downloader.OnProgress += (currentFilePercent, totalFiles, completedFiles, currentFileName) =>
                    {
                        if (this.IsHandleCreated)
                        {
                            this.Invoke((MethodInvoker)delegate
                            {
                                if (panel1.Controls.Count > 0 && panel1.Controls[0] is Mp3StreamingPanel streamingPanel)
                                {
                                    streamingPanel.SetDownloadProgress(currentFilePercent, totalFiles, completedFiles, currentFileName);
                                }
                            });
                        }
                    };

                    try
                    {
                        await downloader.DownloadTracksAsync(tracksToDownload, targetPath);
                        MessageBox.Show($"Скачивание завершено! Файлы сохранены в: {targetPath}", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка при скачивании: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    finally
                    {
                        SetStatus("");
                        if (this.IsHandleCreated)
                        {
                            this.Invoke((MethodInvoker)delegate
                            {
                                this.Text = originalTitle;
                            });
                        }
                    }
                }
            }
        }

        private string ReplaceInvalidChars(string filename)
        {
            return string.Join("_", filename.Split(Path.GetInvalidFileNameChars()));
        }

        private void SetStatus(string status)
        {
            if (panel1.Controls.Count > 0 && panel1.Controls[0] is Mp3StreamingPanel streamingPanel)
            {
                streamingPanel.StatusText = status;
            }
        }
        
        private void UpdateStatusText()
        {
             if (dataGridView1.DataSource is List<Track> tracks)
             {
                 SetStatus($"Найдено: {tracks.Count}");
             }
             else
             {
                 SetStatus("");
             }
        }

        private void OnLoadPluginClick(object sender, EventArgs e)
        {
            var plugin = (INAudioDemoPlugin)listBoxPlugins.SelectedItem;
            if (plugin == null)
            {
#if DEBUG
                Logger.Warning("Попытка загрузить плагин, но ни один плагин не выбран");
#endif
                return;
            }

#if DEBUG
            Logger.Info($"Загрузка плагина: {plugin.Name}");
#endif

            if (plugin == currentPlugin)
            {
#if DEBUG
                Logger.LogDebug($"Плагин '{plugin.Name}' уже загружен, пропуск");
#endif
                return;
            }

            currentPlugin = plugin;
            DisposeCurrentPlugin();

            try
            {
                var control = plugin.CreatePanel();
                if (control == null)
                {
#if DEBUG
                    Logger.Error($"Плагин '{plugin.Name}' вернул null вместо панели управления");
#endif
                    return;
                }

                control.Dock = DockStyle.Fill;
                panel1.Controls.Add(control);

#if DEBUG
                Logger.Info($"Плагин '{plugin.Name}' успешно загружен и добавлен в интерфейс");
#endif

                // Если загружен плагин MP3StreamingPanel, передаем ему список треков
                if (plugin.Name == "MP3 Streaming" && dataGridView1.DataSource is List<Track> tracks && tracks.Count > 0)
                {
#if DEBUG
                    Logger.Info($"Передача списка треков ({tracks.Count}) в MP3StreamingPanel");
#endif
                    var streamingPanel = control as Mp3StreamingPanel;
                    if (streamingPanel != null)
                    {
                        streamingPanel.SetTrackList(tracks);
                        // Подписываемся на событие смены трека
                        streamingPanel.TrackChanged += StreamingPanel_TrackChanged;
                    }
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                Logger.Exception(ex, $"Ошибка при загрузке плагина '{plugin.Name}'");
#endif
                MessageBox.Show($"Ошибка загрузки плагина {plugin.Name}: {ex.Message}", "Ошибка плагина", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }


        private void DisposeCurrentPlugin()
        {
            if (panel1.Controls.Count <= 0)
            {
#if DEBUG
                Logger.LogDebug("Нет активных плагинов для освобождения");
#endif
                return;
            }

#if DEBUG
            Logger.Info($"Освобождение ресурсов плагина: {currentPlugin?.Name ?? "неизвестный"}");
#endif

            try
            {
                var control = panel1.Controls[0];
                if (control is Mp3StreamingPanel streamingPanel)
                {
                    // Отписываемся от события
                    streamingPanel.TrackChanged -= StreamingPanel_TrackChanged;
                }
                control.Dispose();
                panel1.Controls.Clear();
                GC.Collect();

#if DEBUG
                Logger.LogDebug("Ресурсы плагина успешно освобождены");
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                Logger.Exception(ex, "Ошибка при освобождении ресурсов плагина");
#endif
            }
        }
        private void DataGridView1_SelectionChanged(object sender, EventArgs e)
        {
            if (dataGridView1.SelectedRows.Count > 0)
            {
                var track = dataGridView1.SelectedRows[0].DataBoundItem as Track;
                if (track != null && currentPlugin != null && currentPlugin.Name == "MP3 Streaming")
                {
                    if (panel1.Controls.Count > 0 && panel1.Controls[0] is Mp3StreamingPanel streamingPanel)
                    {
                        streamingPanel.SelectTrack(track);
                    }
                }
            }
        }

        private void DataGridView1_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.RowIndex < dataGridView1.Rows.Count)
            {
                var track = dataGridView1.Rows[e.RowIndex].DataBoundItem as Track;
                if (track != null && currentPlugin != null && currentPlugin.Name == "MP3 Streaming")
                {
                    if (panel1.Controls.Count > 0 && panel1.Controls[0] is Mp3StreamingPanel streamingPanel)
                    {
                        streamingPanel.PlayTrack(track);
                    }
                }
            }
        }

        private void listBoxPlugins_DoubleClick(object sender, EventArgs e)
        {
            OnLoadPluginClick(sender, e);
        }
        private void InitializeCustomControls()
        {
            // Сдвигаем существующие элементы вниз, чтобы освободить место
            int offset = 40;
            dataGridView1.Top += offset;
            dataGridView1.Height -= offset;
            panel1.Top += offset;
            panel1.Height -= offset;

            controlsPanel = new Panel();
            controlsPanel.Location = new Point(dataGridView1.Left, dataGridView1.Top - offset);
            controlsPanel.Size = new Size(dataGridView1.Width + panel1.Width, 35);
            controlsPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            this.Controls.Add(controlsPanel);

            modeComboBox = new ComboBox();
            modeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            modeComboBox.Items.AddRange(new object[] { "Моя музыка", "Поиск", "Друзья", "Группы", "Альбомы" });
            modeComboBox.SelectedIndex = 0;
            modeComboBox.Location = new Point(0, 5);
            modeComboBox.Width = 120;
            modeComboBox.SelectedIndexChanged += ModeComboBox_SelectedIndexChanged;
            controlsPanel.Controls.Add(modeComboBox);

            searchTextBox = new TextBox();
            searchTextBox.Location = new Point(modeComboBox.Right + 10, 5);
            searchTextBox.Width = 200;
            searchTextBox.Visible = false;
            controlsPanel.Controls.Add(searchTextBox);

            searchButton = new Button();
            searchButton.Text = "Найти";
            searchButton.Location = new Point(searchTextBox.Right + 5, 4);
            searchButton.Click += SearchButton_Click;
            searchButton.Visible = false;
            controlsPanel.Controls.Add(searchButton);

            targetComboBox = new ComboBox();
            targetComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            targetComboBox.Location = new Point(modeComboBox.Right + 10, 5);
            targetComboBox.Width = 200;
            targetComboBox.Visible = false;
            targetComboBox.SelectedIndexChanged += TargetComboBox_SelectedIndexChanged;
            controlsPanel.Controls.Add(targetComboBox);

            showAlbumsCheckBox = new CheckBox();
            showAlbumsCheckBox.Text = "Показать альбомы";
            showAlbumsCheckBox.Location = new Point(targetComboBox.Right + 10, 5);
            showAlbumsCheckBox.Width = 150;
            showAlbumsCheckBox.Visible = false;
            showAlbumsCheckBox.CheckedChanged += ShowAlbumsCheckBox_CheckedChanged;
            controlsPanel.Controls.Add(showAlbumsCheckBox);
        }

        private void ShowAlbumsCheckBox_CheckedChanged(object sender, EventArgs e)
        {
#if DEBUG
            Logger.Info($"ShowAlbumsCheckBox_CheckedChanged: {showAlbumsCheckBox.Checked}");
#endif
            HandleShowAlbumsCheckedChanged();
        }

        private async void ModeComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            string mode = modeComboBox.SelectedItem.ToString();
            
            // Сброс видимости
            searchTextBox.Visible = false;
            searchButton.Visible = false;
            targetComboBox.Visible = false;
            showAlbumsCheckBox.Visible = false;
            showAlbumsCheckBox.Checked = false; // Сбрасываем чекбокс при смене режима
            targetComboBox.DataSource = null;
            targetComboBox.Items.Clear();

            SetStatus("Загрузка...");

            try
            {
                switch (mode)
                {
                    case "Моя музыка":
                        showAlbumsCheckBox.Visible = true; // Разрешаем показывать альбомы для своей музыки
                        if (showAlbumsCheckBox.Checked)
                        {
                            // Если чекбокс уже был включен (хотя мы его сбросили выше, но на будущее)
                            // Логика обработки чекбокса будет в TargetComboBox_SelectedIndexChanged или здесь,
                            // но для "Моя музыка" нет targetComboBox.
                            // Поэтому обработаем здесь:
                            var albums = await VK.GetAlbumsAsync();
                            // Для отображения альбомов нам нужен список, но DataGridView ожидает треки.
                            // По заданию: "Если чекбокс включен, загружай альбомы... Если выключен — показывай список друзей/групп"
                            // Но для "Моя музыка" это означает переключение между треками и альбомами.
                            // Однако, DataGridView отображает треки. Если мы загрузим альбомы, нам нужно их где-то показать.
                            // Вероятно, подразумевается, что при выборе альбома в выпадающем списке (которого нет для "Моя музыка")
                            // или, возможно, список альбомов должен появиться в targetComboBox?
                            // В задании сказано: "UI: Добавь CheckBox... рядом с выбором друга/группы."
                            // "Навигация: Реализуй логику переключения: если чекбокс включен, загружай альбомы выбранного друга/группы... Если выключен — показывай список друзей/групп."
                            // Это относится к режимам "Друзья" и "Группы".
                            
                            // Для "Моя музыка" чекбокс, возможно, должен переключать на список альбомов текущего пользователя в targetComboBox?
                            // Давайте сделаем так: для "Моя музыка" targetComboBox скрыт, но если включить "Показать альбомы",
                            // то targetComboBox появится со списком альбомов.
                            
                            targetComboBox.Visible = true;
                            targetComboBox.DataSource = albums;
                            targetComboBox.DisplayMember = "Title";
                            targetComboBox.ValueMember = "Id";
                        }
                        else
                        {
                            targetComboBox.Visible = false; // Скрываем, если не альбомы
                            dataGridView1.DataSource = await VK.GetListOfTracksAsync();
                            UpdatePlayerTrackList();
                        }
                        break;

                    case "Поиск":
                        searchTextBox.Visible = true;
                        searchButton.Visible = true;
                        break;

                    case "Друзья":
                        targetComboBox.Visible = true;
                        showAlbumsCheckBox.Visible = true;
                        if (friendsList == null)
                        {
                            friendsList = await VK.GetFriendsAsync();
                        }
                        
                        // Здесь мы всегда показываем список друзей в targetComboBox.
                        // Логика "показать альбомы" будет применяться при выборе друга.
                        // Или, если чекбокс меняет содержимое targetComboBox?
                        // "Если выключен — показывай список друзей/групп." -> в targetComboBox.
                        // Значит, если включен -> показывай список альбомов? Но чьих? Выбранного друга?
                        // Это сложный UX. Обычно сначала выбирают друга, а потом смотрят его альбомы.
                        // Вероятно, чекбокс должен фильтровать, что мы видим ПОСЛЕ выбора друга.
                        // НО, фраза "Если выключен — показывай список друзей/групп" намекает, что targetComboBox содержит друзей.
                        // А "если чекбокс включен, загружай альбомы выбранного друга/группы" - это действие.
                        // Скорее всего, имеется в виду:
                        // 1. Выбираем друга в targetComboBox.
                        // 2. Если чекбокс выключен -> грузим треки друга.
                        // 3. Если чекбокс включен -> грузим список альбомов друга... КУДА?
                        // Если в DataGridView - то там треки.
                        // Если в targetComboBox - то мы потеряем возможность выбрать другого друга.
                        
                        // Интерпретация архитектора: "добавь вложенную навигацию".
                        // Возможно, при включении чекбокса, targetComboBox должен показывать альбомы УЖЕ ВЫБРАННОГО друга?
                        // Но тогда как вернуться к списку друзей? Выключить чекбокс.
                        // Давайте попробуем так:
                        // При переключении на "Друзья", targetComboBox заполняется друзьями.
                        // При выборе друга грузятся его треки.
                        // Если нажать "Показать альбомы", то:
                        //   - Запоминаем выбранного друга.
                        //   - Загружаем его альбомы.
                        //   - targetComboBox заполняется альбомами этого друга.
                        //   - При выборе альбома грузятся треки альбома.
                        //   - Если снять чекбокс -> targetComboBox снова заполняется друзьями, восстанавливается выбор.
                        
                        targetComboBox.DisplayMember = "FirstName";
                        var friendItems = friendsList.Select(f => new { Name = $"{f.FirstName} {f.LastName}", Id = f.Id }).ToList();
                        targetComboBox.DataSource = friendItems;
                        targetComboBox.DisplayMember = "Name";
                        targetComboBox.ValueMember = "Id";
                        break;

                    case "Группы":
                        targetComboBox.Visible = true;
                        showAlbumsCheckBox.Visible = true;
                        if (groupsList == null)
                        {
                            groupsList = await VK.GetGroupsAsync();
                        }
                        targetComboBox.DataSource = groupsList;
                        targetComboBox.DisplayMember = "Name";
                        targetComboBox.ValueMember = "Id";
                        break;

                    case "Альбомы":
                        // Этот режим, возможно, становится избыточным при наличии чекбокса, но оставим для совместимости
                        targetComboBox.Visible = true;
                        var allAlbums = await VK.GetAlbumsAsync();
                        targetComboBox.DataSource = allAlbums;
                        targetComboBox.DisplayMember = "Title";
                        targetComboBox.ValueMember = "Id";
                        break;
                }
            }
            finally
            {
                SetStatus("");
                UpdateStatusText();
            }
        }

        private async void SearchButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(searchTextBox.Text)) return;
            
            searchButton.Enabled = false;
            try
            {
                dataGridView1.DataSource = await VK.SearchAudioAsync(searchTextBox.Text);
                UpdatePlayerTrackList();
            }
            finally
            {
                searchButton.Enabled = true;
            }
        }

        // Храним выбранного друга/группу, чтобы при возврате из режима альбомов восстановить контекст
        private long _selectedOwnerId = 0;

        private async void TargetComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (targetComboBox.SelectedItem == null) return;

            SetStatus("Загрузка...");
            try
            {
                long id = 0;
                string mode = modeComboBox.SelectedItem.ToString();
#if DEBUG
                Logger.Info($"TargetComboBox selection changed. Mode: {mode}, SelectedItemType: {targetComboBox.SelectedItem.GetType().Name}");
#endif

                if (mode == "Друзья")
                {
                    if (showAlbumsCheckBox.Checked)
                    {
                        // Режим просмотра альбомов друга
                        // В этом случае в targetComboBox уже должны быть альбомы
                        if (targetComboBox.SelectedItem is VkNet.Model.AudioPlaylist playlist)
                        {
                            long ownerId = playlist.OwnerId ?? _selectedOwnerId;
                            dataGridView1.DataSource = await VK.GetAlbumTracksAsync(ownerId, playlist.Id ?? 0);
                            UpdatePlayerTrackList();
                        }
                        else
                        {
                            // Если мы только что переключили чекбокс, и в комбобоксе еще друзья,
                            // то мы должны загрузить альбомы выбранного друга и обновить комбобокс.
                            // Но это событие вызывается при смене индекса.
                            // Логика переключения контента комбобокса должна быть в CheckBox_CheckedChanged.
                            // Однако, если мы здесь, значит пользователь выбрал что-то в списке.
                            
                            // Если мы здесь и чекбокс включен, но в списке не плейлисты - значит это первый выбор друга,
                            // после которого нужно показать его альбомы?
                            // Нет, давайте придерживаться логики:
                            // 1. Выбрали друга -> загрузили его треки.
                            // 2. Нажали "Показать альбомы" -> загрузили альбомы ЭТОГО друга в комбобокс.
                            
                            // Значит, если мы здесь, и чекбокс включен, и в списке Друг - это странная ситуация,
                            // которая может возникнуть только если мы программно не обновили список.
                            
                            // Давайте упростим. Если чекбокс включен, мы ожидаем, что пользователь выбирает АЛЬБОМ.
                            // Если выключен - ДРУГА.
                            
                            // Но есть проблема: как выбрать друга, если чекбокс включен? Никак.
                            // Нужно выключить чекбокс, выбрать друга, включить чекбокс.
                            
                            // Обработка ситуации, когда мы переключаем чекбокс:
                            // В ShowAlbumsCheckBox_CheckedChanged мы вызываем этот метод.
                            // Если чекбокс стал Checked:
                            //   Берем текущего выбранного друга из комбобокса (пока он там есть).
                            //   Загружаем его альбомы.
                            //   Заменяем DataSource комбобокса на альбомы.
                            
                            // Если чекбокс стал Unchecked:
                            //   Возвращаем список друзей в комбобокс.
                            //   Пытаемся восстановить выбор (если сохранили ID).
                        }
                    }
                    else
                    {
                        // Режим выбора друга
                        dynamic item = targetComboBox.SelectedItem;
                        id = item.Id;
                        _selectedOwnerId = id; // Запоминаем ID
#if DEBUG
                        Logger.Info($"Selected friend ID: {id}");
#endif
                        dataGridView1.DataSource = await VK.GetAudioAsync(id);
                        UpdatePlayerTrackList();
                    }
                }
                else if (mode == "Группы")
                {
                    if (showAlbumsCheckBox.Checked)
                    {
                         if (targetComboBox.SelectedItem is VkNet.Model.AudioPlaylist playlist)
                        {
                            long ownerId = playlist.OwnerId ?? _selectedOwnerId;
                            dataGridView1.DataSource = await VK.GetAlbumTracksAsync(ownerId, playlist.Id ?? 0);
                            UpdatePlayerTrackList();
                        }
                    }
                    else
                    {
                        if (targetComboBox.SelectedItem is VkNet.Model.Group group)
                        {
                            id = -group.Id;
                            _selectedOwnerId = id;
#if DEBUG
                            Logger.Info($"Selected group ID: {group.Id} (Audio Owner ID: {id})");
#endif
                            dataGridView1.DataSource = await VK.GetAudioAsync(id);
                            UpdatePlayerTrackList();
                        }
                        else
                        {
                            try
                            {
                                dynamic item = targetComboBox.SelectedItem;
                                long groupId = item.Id;
                                id = -groupId;
                                _selectedOwnerId = id;
                                dataGridView1.DataSource = await VK.GetAudioAsync(id);
                                UpdatePlayerTrackList();
                            }
                            catch(Exception ex) { }
                        }
                    }
                }
                else if (mode == "Альбомы" || (mode == "Моя музыка" && showAlbumsCheckBox.Checked))
                {
                    if (targetComboBox.SelectedItem is VkNet.Model.AudioPlaylist playlist)
                    {
                        long ownerId = playlist.OwnerId ?? 0;
                        dataGridView1.DataSource = await VK.GetAlbumTracksAsync(ownerId, playlist.Id ?? 0);
                        UpdatePlayerTrackList();
                    }
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                Logger.Exception(ex, "Error in TargetComboBox_SelectedIndexChanged");
#endif
                MessageBox.Show($"Ошибка при выборе: {ex.Message}");
            }
            finally
            {
                SetStatus("");
                UpdateStatusText();
            }
            
            // Логика переключения списка в комбобоксе при изменении чекбокса
            // (вызывается из ShowAlbumsCheckBox_CheckedChanged)
            if (sender == showAlbumsCheckBox)
            {
                 // Это хак, так как мы вызываем этот метод из обработчика чекбокса
                 // Но нам нужно разделить логику загрузки данных и логику смены источника данных комбобокса
            }
        }
        
        // Переопределяем метод для корректной обработки смены состояния чекбокса
        private async void HandleShowAlbumsCheckedChanged()
        {
            string mode = modeComboBox.SelectedItem.ToString();
            SetStatus("Загрузка альбомов...");
            
            try
            {
#if DEBUG
                Logger.Info($"HandleShowAlbumsCheckedChanged: Mode={mode}, Checked={showAlbumsCheckBox.Checked}, SelectedOwnerId={_selectedOwnerId}");
#endif
                if (showAlbumsCheckBox.Checked)
                {
                    // Переход в режим альбомов
                    if (mode == "Друзья" || mode == "Группы")
                    {
                        if (_selectedOwnerId != 0)
                        {
#if DEBUG
                            Logger.Info($"Загрузка альбомов для владельца {_selectedOwnerId}");
#endif
                            var albums = await VK.GetAlbumsByOwnerAsync(_selectedOwnerId);
#if DEBUG
                            Logger.Info($"Загружено альбомов: {albums?.Count ?? 0}");
#endif
                            targetComboBox.DataSource = null; // Сначала сбрасываем, чтобы избежать лишних событий
                            targetComboBox.DataSource = albums;
                            targetComboBox.DisplayMember = "Title";
                            targetComboBox.ValueMember = "Id";
                        }
                        else
                        {
#if DEBUG
                            Logger.Warning("Попытка загрузить альбомы, но владелец не выбран (_selectedOwnerId == 0)");
#endif
                        }
                    }
                    else if (mode == "Моя музыка")
                    {
                         var myAlbums = await VK.GetAlbumsAsync();
                         targetComboBox.Visible = true;
                         targetComboBox.DataSource = myAlbums;
                         targetComboBox.DisplayMember = "Title";
                         targetComboBox.ValueMember = "Id";
                    }
                }
                else
                {
                    // Возврат в обычный режим
                    if (mode == "Друзья")
                    {
                        if (friendsList != null)
                        {
                            var friendItems = friendsList.Select(f => new { Name = $"{f.FirstName} {f.LastName}", Id = f.Id }).ToList();
                            targetComboBox.DataSource = friendItems;
                            targetComboBox.DisplayMember = "Name";
                            targetComboBox.ValueMember = "Id";
                            // Попытка восстановить выбор не гарантирована, так как ID могут быть разными объектами
                            // Но ValueMember = Id должен помочь
                            targetComboBox.SelectedValue = _selectedOwnerId;
                        }
                    }
                    else if (mode == "Группы")
                    {
                        if (groupsList != null)
                        {
                            targetComboBox.DataSource = groupsList;
                            targetComboBox.DisplayMember = "Name";
                            targetComboBox.ValueMember = "Id";
                            targetComboBox.SelectedValue = Math.Abs(_selectedOwnerId); // ID группы положительный в списке
                        }
                    }
                    else if (mode == "Моя музыка")
                    {
                        targetComboBox.Visible = false;
                        dataGridView1.DataSource = await VK.GetListOfTracksAsync();
                        UpdatePlayerTrackList();
                    }
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                Logger.Exception(ex, "Ошибка в HandleShowAlbumsCheckedChanged");
#endif
                MessageBox.Show($"Ошибка при загрузке альбомов: {ex.Message}");
            }
            finally
            {
                SetStatus("");
                UpdateStatusText();
            }
        }

        private void UpdatePlayerTrackList()
        {
            if (currentPlugin != null && currentPlugin.Name == "MP3 Streaming" && panel1.Controls.Count > 0)
            {
                if (panel1.Controls[0] is Mp3StreamingPanel streamingPanel && dataGridView1.DataSource is List<Track> tracks)
                {
#if DEBUG
                    Logger.Info($"Обновление списка треков в плеере. Количество: {tracks.Count}");
#endif
                    streamingPanel.SetTrackList(tracks);
                }
            }
        }

        private void StreamingPanel_TrackChanged(object sender, Track track)
        {
            if (track == null) return;

            // Обновляем выделение в dataGridView1
            if (dataGridView1.InvokeRequired)
            {
                dataGridView1.Invoke(new Action(() => SelectTrackInGrid(track)));
            }
            else
            {
                SelectTrackInGrid(track);
            }
        }

        private void SelectTrackInGrid(Track track)
        {
            if (dataGridView1.DataSource is List<Track> tracks)
            {
                // Ищем трек в списке
                int index = tracks.FindIndex(t => t.Url == track.Url && t.Title == track.Title);
                if (index >= 0 && index < dataGridView1.Rows.Count)
                {
                    // Снимаем текущее выделение
                    dataGridView1.ClearSelection();
                    // Выделяем новую строку
                    dataGridView1.Rows[index].Selected = true;
                    // Прокручиваем к ней
                    dataGridView1.FirstDisplayedScrollingRowIndex = index;
                }
            }
        }
    }
}
