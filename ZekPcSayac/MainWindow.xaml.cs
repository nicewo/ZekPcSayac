using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace ZekPcSayac
{
    public partial class MainWindow : Window
    {
        // Prototype default only. This will become a setting from the web panel.
        private const int InitialDailyMinutes = 120;

        private readonly DispatcherTimer _timer;
        private readonly string _stateFile;
        private UsageState _state;

        public MainWindow()
        {
            InitializeComponent();

            string appDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ZekPcSayac");

            Directory.CreateDirectory(appDirectory);
            _stateFile = Path.Combine(appDirectory, "usage-state.json");
            _state = LoadState();

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += Timer_Tick;

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            PositionTimerWindow();
            RenderState();

            if (_state.IsPaused || _state.RemainingSeconds <= 0)
            {
                _state.IsPaused = true;
                SaveState();
                EnterPausedScreen();
            }
            else
            {
                _state.LastStartedUtc = DateTime.UtcNow;
                SaveState();
                _timer.Start();
            }
        }

        private void PositionTimerWindow()
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 12;
            Top = workArea.Top + 8;
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (_state.RemainingSeconds > 0)
                _state.RemainingSeconds--;

            RenderState();

            if (_state.RemainingSeconds <= 0)
            {
                _state.RemainingSeconds = 0;
                _state.IsPaused = true;
                SaveState();
                EnterPausedScreen();
                return;
            }

            // Persist frequently so a restart does not restore spent time.
            SaveState();
        }

        private void PauseResumeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_state.IsPaused)
                ResumeUsage();
            else
                PauseUsage();
        }

        private void PauseUsage()
        {
            _timer.Stop();
            _state.IsPaused = true;
            SaveState();
            EnterPausedScreen();
        }

        private void ResumeUsage()
        {
            if (_state.RemainingSeconds <= 0)
            {
                EnterPausedScreen();
                return;
            }

            _state.IsPaused = false;
            _state.LastStartedUtc = DateTime.UtcNow;
            SaveState();

            PausedPanel.Visibility = Visibility.Collapsed;
            TimerPanel.Visibility = Visibility.Visible;
            WindowState = WindowState.Normal;
            Width = 150;
            Height = 76;
            Background = System.Windows.Media.Brushes.Transparent;
            PositionTimerWindow();
            RenderState();
            _timer.Start();
        }

        private void EnterPausedScreen()
        {
            _timer.Stop();
            TimerPanel.Visibility = Visibility.Collapsed;
            PausedPanel.Visibility = Visibility.Visible;
            Background = System.Windows.Media.Brushes.Black;
            WindowState = WindowState.Normal;
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
            WindowState = WindowState.Maximized;
            Topmost = true;
            RenderState();
        }

        private void ShutdownButton_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            _state.IsPaused = true;
            SaveState();

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "shutdown.exe",
                Arguments = "/s /t 0",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            System.Diagnostics.Process.Start(startInfo);
        }

        private void RenderState()
        {
            string time = TimeSpan.FromSeconds(Math.Max(0, _state.RemainingSeconds))
                .ToString(@"hh\:mm\:ss");

            RemainingText.Text = time;
            PausedRemainingText.Text = time;
            PauseResumeButton.Content = _state.IsPaused ? "Devam Et" : "Durdur";
        }

        private UsageState LoadState()
        {
            try
            {
                if (File.Exists(_stateFile))
                {
                    string json = File.ReadAllText(_stateFile);
                    var saved = JsonSerializer.Deserialize<UsageState>(json);
                    if (saved is not null && saved.RemainingSeconds >= 0)
                        return saved;
                }
            }
            catch
            {
                // A corrupt local state must not crash startup.
            }

            return new UsageState
            {
                RemainingSeconds = InitialDailyMinutes * 60,
                IsPaused = false,
                LastStartedUtc = DateTime.UtcNow,
                DateKey = DateTime.Now.ToString("yyyy-MM-dd")
            };
        }

        private void SaveState()
        {
            try
            {
                string json = JsonSerializer.Serialize(_state);
                string tempFile = _stateFile + ".tmp";
                File.WriteAllText(tempFile, json);
                File.Move(tempFile, _stateFile, true);
            }
            catch
            {
                // The timer continues if a local write temporarily fails.
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _timer.Stop();
            SaveState();
        }

        private sealed class UsageState
        {
            public int RemainingSeconds { get; set; }
            public bool IsPaused { get; set; }
            public DateTime LastStartedUtc { get; set; }
            public string DateKey { get; set; } = "";
        }
    }
}
