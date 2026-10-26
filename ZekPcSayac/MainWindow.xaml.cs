using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using NAudio.CoreAudioApi;

namespace ZekPcSayac
{
    public partial class MainWindow : Window
    {
        // Prototype default only. This will become a setting from the web panel.
        private const int InitialDailyMinutes = 120;

        private readonly DispatcherTimer _timer;
        private readonly DispatcherTimer _dailyCheckTimer;
        private readonly string _stateFile;
        private UsageState _state;
        private bool _audioStateCaptured;
        private bool _wasMutedBeforePause;
        private bool _allowClose;
        private bool _closePromptOpen;
        private const string ExitPassword = "682543";

        [DllImport("user32.dll", SetLastError = false)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const byte VkMediaPlayPause = 0xB3;
        private const uint KeyeventfKeyup = 0x0002;

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

            // This timer remains active even while usage is paused, so midnight
            // renewal still happens without consuming usage time.
            _dailyCheckTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _dailyCheckTimer.Tick += DailyCheckTimer_Tick;
            _dailyCheckTimer.Start();

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RegisterStartup();
            EnsureDailyState();
            PositionTimerWindow();
            RenderState();

            if (_state.IsPaused || _state.RemainingSeconds <= 0)
            {
                _state.IsPaused = true;
                if (_state.AudioWasMutedByApp)
                    SetSystemMute(true);
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

        private void DailyCheckTimer_Tick(object? sender, EventArgs e)
        {
            string previousDate = _state.DateKey;
            EnsureDailyState();

            if (!string.Equals(previousDate, _state.DateKey, StringComparison.Ordinal))
            {
                RenderState();

                // If the app was paused, keep the blackout active after daily renewal.
                // If it was running, the main countdown continues with the new day's allowance.
                if (_state.IsPaused || _state.RemainingSeconds <= 0)
                    EnterPausedScreen();
                else
                    SaveState();
            }
        }

        private static void RegisterStartup()
        {
            try
            {
                // Never register a temporary development/debug executable in Windows startup.
                if (System.Diagnostics.Debugger.IsAttached)
                    return;

                string? executablePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executablePath))
                    return;

                // Do not register builds launched from any directory under a folder named "bin".
                // This keeps Visual Studio / development builds from adding themselves to startup.
                if (IsUnderBinDirectory(executablePath))
                    return;

                // Do not register the dotnet host if launched from a development environment.
                if (string.Equals(Path.GetFileNameWithoutExtension(executablePath), "dotnet", StringComparison.OrdinalIgnoreCase))
                    return;

                using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                runKey?.SetValue("ZekPcSayac", $"\"{executablePath}\"");
            }
            catch
            {
                // Startup registration is best-effort; the app remains usable if policy blocks it.
            }
        }

        private static bool IsUnderBinDirectory(string executablePath)
        {
            DirectoryInfo? directory = new FileInfo(executablePath).Directory;
            while (directory is not null)
            {
                if (string.Equals(directory.Name, "bin", StringComparison.OrdinalIgnoreCase))
                    return true;

                directory = directory.Parent;
            }

            return false;
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            EnsureDailyState();

            if (_state.RemainingSeconds > 0)
                _state.RemainingSeconds--;

            RenderState();

            if (_state.RemainingSeconds <= 0)
            {
                _state.RemainingSeconds = 0;
                CaptureAndMuteAudio();
                TryPauseMedia();
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
            CaptureAndMuteAudio();
            TryPauseMedia();
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

            RestoreAudio();
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

        private void CaptureAndMuteAudio()
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _wasMutedBeforePause = device.AudioEndpointVolume.Mute;
                _audioStateCaptured = true;
                _state.AudioWasMutedBeforePause = _wasMutedBeforePause;
                _state.AudioWasMutedByApp = !_wasMutedBeforePause;
                device.AudioEndpointVolume.Mute = true;
            }
            catch
            {
                // Audio control is best-effort; the pause overlay still works.
            }
        }

        private void RestoreAudio()
        {
            if (!_state.AudioWasMutedByApp)
                return;

            SetSystemMute(_state.AudioWasMutedBeforePause);
            _state.AudioWasMutedByApp = false;
            _audioStateCaptured = false;
            SaveState();
        }

        private static void SetSystemMute(bool muted)
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                device.AudioEndpointVolume.Mute = muted;
            }
            catch
            {
                // Audio endpoint may be unavailable or permissions may block access.
            }
        }

        private static void TryPauseMedia()
        {
            try
            {
                // Sends the standard media Play/Pause key. Apps that support media keys
                // may pause playback; Windows cannot guarantee every app will respond.
                keybd_event(VkMediaPlayPause, 0, 0, UIntPtr.Zero);
                keybd_event(VkMediaPlayPause, 0, KeyeventfKeyup, UIntPtr.Zero);
            }
            catch
            {
                // Media control is best-effort.
            }
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
                BaseSeconds = InitialDailyMinutes * 60,
                BonusSeconds = 0,
                RemainingSeconds = InitialDailyMinutes * 60,
                IsPaused = false,
                LastStartedUtc = DateTime.UtcNow,
                DateKey = DateTime.Now.ToString("yyyy-MM-dd")
            };
        }

        private void EnsureDailyState()
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (string.Equals(_state.DateKey, today, StringComparison.Ordinal))
                return;

            // New local calendar day: renew the base allowance and discard yesterday's bonus.
            // Preserve pause state so a paused session never resumes by itself after midnight.
            _state.BaseSeconds = InitialDailyMinutes * 60;
            _state.BonusSeconds = 0;
            _state.RemainingSeconds = _state.BaseSeconds;
            _state.DateKey = today;
            _state.LastStartedUtc = DateTime.UtcNow;
            SaveState();
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
            if (!_allowClose)
            {
                e.Cancel = true;

                // Do not show a modal dialog synchronously inside the Closing event.
                // WPF is still processing the close request at this point.
                if (!_closePromptOpen)
                {
                    _closePromptOpen = true;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            RequestCloseWithPassword();
                        }
                        finally
                        {
                            _closePromptOpen = false;
                        }
                    }), DispatcherPriority.Background);
                }

                return;
            }

            _timer.Stop();
            _dailyCheckTimer.Stop();
            SaveState();
        }

        private void RequestCloseWithPassword()
        {
            var passwordBox = new System.Windows.Controls.PasswordBox
            {
                Width = 220,
                Margin = new Thickness(0, 10, 0, 12),
                FontSize = 18,
                MaxLength = 20
            };

            var message = new System.Windows.Controls.TextBlock
            {
                Text = "Uygulamadan çıkmak için şifreyi girin:",
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap
            };

            var errorText = new System.Windows.Controls.TextBlock
            {
                Text = "",
                Foreground = System.Windows.Media.Brushes.IndianRed,
                Margin = new Thickness(0, 0, 0, 8)
            };

            var exitButton = new System.Windows.Controls.Button
            {
                Content = "Çıkış",
                Width = 90,
                Height = 30,
                IsDefault = true,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var cancelButton = new System.Windows.Controls.Button
            {
                Content = "Vazgeç",
                Width = 90,
                Height = 30,
                IsCancel = true
            };

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(exitButton);
            buttons.Children.Add(cancelButton);

            var panel = new System.Windows.Controls.StackPanel
            {
                Margin = new Thickness(20)
            };
            panel.Children.Add(message);
            panel.Children.Add(passwordBox);
            panel.Children.Add(errorText);
            panel.Children.Add(buttons);

            var dialog = new Window
            {
                Title = "ZekPcSayac - Çıkış onayı",
                Content = panel,
                Width = 330,
                Height = 190,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ShowInTaskbar = false,
                Topmost = true
            };

            exitButton.Click += (_, _) =>
            {
                if (string.Equals(passwordBox.Password, ExitPassword, StringComparison.Ordinal))
                {
                    dialog.DialogResult = true;
                }
                else
                {
                    errorText.Text = "Şifre yanlış.";
                    passwordBox.Clear();
                    passwordBox.Focus();
                }
            };

            dialog.Loaded += (_, _) => passwordBox.Focus();
            bool? result = dialog.ShowDialog();
            if (result == true)
            {
                _allowClose = true;
                Close();
            }
        }

        private sealed class UsageState
        {
            public int BaseSeconds { get; set; }
            public int BonusSeconds { get; set; }
            public int RemainingSeconds { get; set; }
            public bool IsPaused { get; set; }
            public DateTime LastStartedUtc { get; set; }
            public string DateKey { get; set; } = "";
            public bool AudioWasMutedBeforePause { get; set; }
            public bool AudioWasMutedByApp { get; set; }
        }
    }
}
