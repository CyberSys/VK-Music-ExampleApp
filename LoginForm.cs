using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

namespace VK_Music
{
    public partial class LoginForm : Form
    {
        private readonly List<Control> _controls;
        private bool _isAuthenticating;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly BackgroundWorker _authWorker;

        public LoginForm()
        {
            InitializeComponent();

#if DEBUG
            // Инициализируем логгер при запуске формы
            Logger.Initialize();
            Logger.Info("Форма авторизации инициализирована");
#endif

            _controls = new List<Control> { authButton, codeeloGradientPanel1, codeeloTextBox1, codeeloTextBox2, codeeloTextBox3, label1, pictureBox1, pictureBox2 };
            _controls.ForEach(control =>
            {
                if (control != null)
                {
                    control.MouseEnter += Control_MouseEnter;
                    control.MouseLeave += Control_MouseLeave;
                }
            });

            // Скрываем поле для ввода кода 2FA при запуске
            codeeloTextBox3.Visible = false;
            
            // Инициализируем источник токенов отмены и фоновый обработчик
            _cancellationTokenSource = new CancellationTokenSource();
            _authWorker = new BackgroundWorker();
            _authWorker.DoWork += AuthWorker_DoWork;
            _authWorker.RunWorkerCompleted += AuthWorker_RunWorkerCompleted;
            
            // Включаем обработку клавиш на уровне формы
            this.KeyPreview = true;
            this.KeyDown += LoginForm_KeyDown;
            
            // Настраиваем обработчик для поля ввода кода 2FA
            codeeloTextBox3.KeyPress += codeeloTextBox3_KeyPress;

#if DEBUG
            Logger.LogDebug("Настройка формы авторизации завершена");
#endif
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
            base.OnFormClosing(e);
        }

        private void AuthWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
#if DEBUG
                Logger.Info("Начало процесса аутентификации в фоновом потоке");
#endif
                var args = (Tuple<string, string>)e.Argument;
                
                // Используем асинхронный метод, но запускаем его синхронно в фоновом потоке
                // Это безопасно, так как BackgroundWorker выполняется в отдельном потоке
                Task.Run(() => VK.SignInAsync(this, args.Item1, args.Item2)).GetAwaiter().GetResult();
                
                e.Result = VK.IsAuth;
#if DEBUG
                Logger.Info($"Результат аутентификации: {VK.IsAuth}");
#endif
            }
            catch (OperationCanceledException ex)
            {
#if DEBUG
                Logger.Info($"Аутентификация отменена: {ex.Message}");
#endif
                _isCancelled = true;
                e.Result = ex;
            }
            catch (Exception ex)
            {
#if DEBUG
                Logger.Exception(ex, "Ошибка в процессе аутентификации");
#endif
                e.Result = ex;
            }
        }

        private void AuthWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
#if DEBUG
            Logger.Info("Завершение процесса аутентификации");
#endif
            _isAuthenticating = false;
            authButton.Enabled = true;
            codeeloTextBox1.Enabled = true;
            codeeloTextBox2.Enabled = true;
            
            // Сбрасываем состояние поля двухфакторной аутентификации
            codeeloTextBox3.Visible = false;
            codeeloTextBox3.Text = string.Empty;
            NeededTwoAuth = false;
            cancelAuthButton.Visible = false;

            if (e.Cancelled || _isCancelled)
            {
#if DEBUG
                Logger.Info("Процесс аутентификации был отменен пользователем");
#endif
                return;
            }

            if (e.Result is Exception ex)
            {
                // Проверяем, не является ли исключение результатом отмены операции
                if (ex is OperationCanceledException)
                {
#if DEBUG
                    Logger.Info($"Аутентификация отменена: {ex.Message}");
#endif
                    return;
                }
                
#if DEBUG
                Logger.Error($"Ошибка аутентификации: {ex.Message}");
#endif
                MessageBox.Show($"Произошла ошибка: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            bool isAuth = (bool)e.Result;
            if (isAuth)
            {
#if DEBUG
                Logger.Info("Аутентификация успешна, открытие главной формы");
#endif
                Hide();
                using (var mainForm = new MainForm())
                {
                    mainForm.ShowDialog();
                }
                Close();
            }
            else
            {
#if DEBUG
                Logger.Warning("Аутентификация не удалась, неверные учетные данные");
#endif
                MessageBox.Show("Ошибка аутентификации. Пожалуйста, проверьте ваши учетные данные.", "Ошибка аутентификации", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public bool NeededTwoAuth { get; private set; }
        private string _twoFactorCode = string.Empty;
        private TaskCompletionSource<string> _twoFactorTaskSource = null;
        private System.Windows.Forms.Timer _twoFactorTimer = null;
        private bool _isCancelled = false;

        public async Task<string> GetTwoAuthAsync()
        {
#if DEBUG
            Logger.Info("Запрос кода двухфакторной аутентификации");
#endif
            if (InvokeRequired)
            {
                return await (Task<string>)Invoke(new Func<Task<string>>(() => GetTwoAuthInternalAsync()));
            }
            return await GetTwoAuthInternalAsync();
        }
        
        // Для обратной совместимости
        public string GetTwoAuth()
        {
#if DEBUG
            Logger.Info("Запрос кода двухфакторной аутентификации (синхронный метод)");
#endif
            // Используем Task.Run для запуска асинхронного метода в синхронном контексте
            // Это не идеальное решение, но обеспечит обратную совместимость
            try
            {
                return Task.Run(() => GetTwoAuthAsync()).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
#if DEBUG
                Logger.Exception(ex, "Ошибка при получении кода 2FA");
#endif
                return string.Empty;
            }
        }

        private async Task<string> GetTwoAuthInternalAsync()
        {
#if DEBUG
            Logger.Info("Подготовка интерфейса для ввода кода 2FA");
#endif
            // Сбрасываем состояние отмены
            _isCancelled = false;
            
            // Инициализируем TaskCompletionSource для асинхронного ожидания
            _twoFactorTaskSource = new TaskCompletionSource<string>();
            
            // Показываем и активируем поле ввода кода
            await this.InvokeAsync(new Action(() => {
                NeededTwoAuth = true;
                codeeloTextBox3.Visible = true;
                codeeloTextBox3.Enabled = true;
                codeeloTextBox3.Text = string.Empty;
                codeeloTextBox3.Focus();
                
                // Добавляем кнопку отмены, если её ещё нет
                if (!Controls.Contains(cancelAuthButton))
                {
                    cancelAuthButton.Visible = true;
                    cancelAuthButton.Enabled = true;
                }
                
                // Регистрируем обработчик клавиши Escape
                this.KeyPreview = true;
                
                // Показываем сообщение пользователю
#if DEBUG
                Logger.Info("Отображение диалога для ввода кода 2FA");
#endif
                MessageBox.Show("Требуется двухфакторная аутентификация. Пожалуйста, введите код и нажмите Enter или Escape для отмены.", 
                    "Двухфакторная аутентификация", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }));
            
            // Создаем таймер для проверки таймаута
            _twoFactorTimer = new System.Windows.Forms.Timer { Interval = 120000 }; // 2 минуты
            _twoFactorTimer.Tick += (s, e) => {
                _twoFactorTimer.Stop();
                if (!_twoFactorTaskSource.Task.IsCompleted)
                {
#if DEBUG
                    Logger.Warning("Время ожидания кода 2FA истекло");
#endif
                    this.Invoke(new Action(() => {
                        MessageBox.Show("Время ожидания кода истекло.", 
                            "Двухфакторная аутентификация", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        
                        // Сбрасываем состояние UI
                        codeeloTextBox3.Visible = false;
                        codeeloTextBox3.Enabled = false;
                        cancelAuthButton.Visible = false;
                        NeededTwoAuth = false;
                    }));
                    
                    // Устанавливаем пустой результат, чтобы разблокировать процесс аутентификации
                    _twoFactorTaskSource.TrySetResult(string.Empty);
                }
            };
            _twoFactorTimer.Start();
            
#if DEBUG
            Logger.Info("Ожидание ввода кода 2FA или отмены операции");
#endif
            
            // Ожидаем результат асинхронно
            string code = await _twoFactorTaskSource.Task;
            
            // Очищаем ресурсы
            _twoFactorTimer.Stop();
            _twoFactorTimer.Dispose();
            _twoFactorTimer = null;
            _twoFactorTaskSource = null;
            
#if DEBUG
            if (_isCancelled)
                Logger.Info("Операция ввода кода 2FA отменена пользователем");
            else
                Logger.Info("Возврат кода 2FA в процесс аутентификации");
#endif
            return code;
        }
        
        // Метод-расширение для асинхронного вызова в UI потоке
        private Task InvokeAsync(Action action)
        {
            var tcs = new TaskCompletionSource<bool>();
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => {
                    try
                    {
                        action();
                        tcs.SetResult(true);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                }));
            }
            else
            {
                try
                {
                    action();
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }
            return tcs.Task;
        }

        private void Control_MouseLeave(object sender, EventArgs e) => Opacity = 0.9;

        private void Control_MouseEnter(object sender, EventArgs e) => Opacity = 1;

        private void label1_Click(object sender, EventArgs e)
        {
            if (_controls != null)
            {
                _controls.ForEach(control =>
                {
                    if (control != null)
                    {
                        control.MouseEnter -= Control_MouseEnter;
                        control.MouseLeave -= Control_MouseLeave;
                    }
                });
            }
            Application.Exit();
        }

        private async void authButton_Click(object sender, EventArgs e)
        {
            if (_isAuthenticating) return;

            try
            {
#if DEBUG
                Logger.Info("Нажата кнопка авторизации");
#endif
                _isAuthenticating = true;
                _isCancelled = false; // Сбрасываем флаг отмены
                authButton.Enabled = false;
                codeeloTextBox1.Enabled = false;
                codeeloTextBox2.Enabled = false;
                
                // Показываем кнопку отмены во время процесса авторизации
                cancelAuthButton.Location = new System.Drawing.Point(authButton.Location.X, authButton.Location.Y + authButton.Height + 10);
                cancelAuthButton.Visible = true;
                cancelAuthButton.Enabled = true;

                if (string.IsNullOrWhiteSpace(codeeloTextBox1.Text) || string.IsNullOrWhiteSpace(codeeloTextBox2.Text))
                {
#if DEBUG
                    Logger.Warning("Ошибка валидации: логин или пароль не заполнены");
#endif
                    MessageBox.Show("Пожалуйста, введите логин и пароль", "Ошибка валидации", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _isAuthenticating = false;
                    authButton.Enabled = true;
                    codeeloTextBox1.Enabled = true;
                    codeeloTextBox2.Enabled = true;
                    cancelAuthButton.Visible = false;
                    return;
                }

#if DEBUG
                Logger.Info($"Запуск процесса аутентификации для пользователя: {codeeloTextBox1.Text}");
#endif
                _authWorker.RunWorkerAsync(new Tuple<string, string>(codeeloTextBox1.Text, codeeloTextBox2.Text));
            }
            catch (Exception ex)
            {
#if DEBUG
                Logger.Exception(ex, "Ошибка при обработке нажатия кнопки авторизации");
#endif
                MessageBox.Show($"Произошла ошибка: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _isAuthenticating = false;
                authButton.Enabled = true;
                codeeloTextBox1.Enabled = true;
                codeeloTextBox2.Enabled = true;
                cancelAuthButton.Visible = false;
            }
        }

        // Кнопка отмены аутентификации
        private Button cancelAuthButton = new Button
        {
            Text = "Отмена",
            Location = new System.Drawing.Point(200, 250),
            Size = new System.Drawing.Size(100, 30),
            Visible = false,
            Enabled = false,
            FlatStyle = FlatStyle.Flat,
            BackColor = System.Drawing.Color.FromArgb(240, 240, 240),
            ForeColor = System.Drawing.Color.Black,
            Cursor = Cursors.Hand
        };

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            
            // Добавляем кнопку отмены на форму
            if (!Controls.Contains(cancelAuthButton))
            {
                Controls.Add(cancelAuthButton);
                cancelAuthButton.Click += CancelAuthButton_Click;
            }
            
            // Включаем обработку клавиш на уровне формы
            this.KeyPreview = true;
            this.KeyDown += LoginForm_KeyDown;
        }
        
        private void LoginForm_KeyDown(object sender, KeyEventArgs e)
        {
            // Обработка нажатия клавиши Escape
            if (e.KeyCode == Keys.Escape)
            {
#if DEBUG
                Logger.Info("Нажата клавиша Escape для отмены операции");
#endif
                // Если активен режим ввода кода 2FA, отменяем его
                if (NeededTwoAuth && _twoFactorTaskSource != null && !_twoFactorTaskSource.Task.IsCompleted)
                {
                    CancelAuthButton_Click(sender, e);
                    e.Handled = true;
                }
                // Если идет процесс аутентификации, отменяем его
                else if (_isAuthenticating)
                {
                    _isCancelled = true;
                    _cancellationTokenSource.Cancel();
                    _isAuthenticating = false;
                    authButton.Enabled = true;
                    codeeloTextBox1.Enabled = true;
                    codeeloTextBox2.Enabled = true;
                    
                    MessageBox.Show("Операция аутентификации отменена", "Отмена", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    e.Handled = true;
                }
            }
        }

        private void CancelAuthButton_Click(object sender, EventArgs e)
        {
#if DEBUG
            Logger.Info("Нажата кнопка отмены аутентификации");
#endif
            _isCancelled = true;
            
            // Сбрасываем состояние UI
            this.InvokeAsync(new Action(() => {
                codeeloTextBox3.Visible = false;
                codeeloTextBox3.Enabled = false;
                cancelAuthButton.Visible = false;
                NeededTwoAuth = false;
                
                // Завершаем задачу ожидания кода с пустым результатом
                if (_twoFactorTaskSource != null && !_twoFactorTaskSource.Task.IsCompleted)
                {
                    _twoFactorTaskSource.TrySetResult(string.Empty);
                }
                
                // Если процесс аутентификации все еще активен, отменяем его
                if (_isAuthenticating)
                {
                    _cancellationTokenSource.Cancel();
                }
                
                MessageBox.Show("Операция аутентификации отменена", "Отмена", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }));
        }

        private void codeeloTextBox3_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Enter)
            {
#if DEBUG
                Logger.Info("Введен код 2FA и нажата клавиша Enter");
#endif
                e.Handled = true;
                string code = codeeloTextBox3.Text;
                
                // Проверяем, что код не пустой
                if (string.IsNullOrWhiteSpace(code))
                {
#if DEBUG
                    Logger.Warning("Введен пустой код 2FA");
#endif
                    MessageBox.Show("Пожалуйста, введите код двухфакторной аутентификации", 
                        "Двухфакторная аутентификация", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                
                codeeloTextBox3.Enabled = false;
                cancelAuthButton.Visible = false;
                
                // Показываем сообщение без блокировки UI
                this.InvokeAsync(new Action(() => {
#if DEBUG
                    Logger.Info($"Код 2FA принят: {code.Length} символов");
#endif
                    MessageBox.Show("Код двухфакторной аутентификации отправлен", "Двухфакторная аутентификация", 
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }));
                
                // Завершаем задачу ожидания кода с полученным результатом
                if (_twoFactorTaskSource != null && !_twoFactorTaskSource.Task.IsCompleted)
                {
                    _twoFactorTaskSource.TrySetResult(code);
                }
            }
        }
    }
}