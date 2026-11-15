using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using ZekPcSayac.Core;

namespace ZekPcSayac
{
    public partial class MainWindow : Window
    {
        private const int InitialDailyMinutes = 120;
        private int _configuredDailyMinutes = InitialDailyMinutes;
        private int _configuredBonusMinutes;
        private readonly TrustedClock _clock = new();
        private readonly FirebaseSyncService _firebase;
        // Null ise çıkış şifresi tanımlı değil; uygulama kapatılamaz.
        private ExitPasswordHash? _exitPassword;
        private readonly ExitPasswordAttemptLimiter _exitLimiter = new(new SystemClock());
        private readonly DispatcherTimer _firebaseTimer;
        private bool _firebaseBusy;
        private bool _firebaseReady;
        private bool _firebaseErrorDialogShown;

        private readonly DispatcherTimer _lockScreenTimer;
        private readonly DispatcherTimer _timer;
        private readonly DispatcherTimer _dailyCheckTimer;
        // Duraklatılmışken de saat aralığı geçişlerini yakalar (1 sn).
        private readonly DispatcherTimer _windowTimer;
        private readonly string _stateFile;
        // Null ise imza anahtarı kullanılamıyor; durum kilitli başlar ve yazılmaz.
        private readonly SignedStateStore? _stateStore;
        private UsageState _state;
        private bool _audioStateCaptured;
        private bool _wasMutedBeforePause;
        private bool _allowClose;
        private bool _closePromptOpen;

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
            _stateStore = CreateStateStore(appDirectory, _stateFile);
            _state = LoadState();
            _firebase = new FirebaseSyncService(appDirectory, _clock);
            // Offline çıkış için son bilinen hash; Firebase okunana kadar bu kullanılır.
            _exitPassword = _firebase.LoadCachedExitPassword();

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

            _windowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _windowTimer.Tick += WindowTimer_Tick;
            _windowTimer.Start();

            _firebaseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _firebaseTimer.Tick += FirebaseTimer_Tick;

            // Lock screen hardening: re-assert topmost/activation every second while the
            // paused screen is visible, and re-fit it when display settings change.
            _lockScreenTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _lockScreenTimer.Tick += LockScreenTimer_Tick;
            Deactivated += MainWindow_Deactivated;
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RegisterStartup();
            PositionTimerWindow();

            if (!_firebase.HasCredentials)
                ShowFirebaseLoginDialog();

            if (_firebase.HasCredentials)
            {
                try
                {
                    FirebaseSnapshot snapshot = await _firebase.ConnectAndReadAsync();
                    _configuredDailyMinutes = snapshot.BaseSeconds / 60;
                    _configuredBonusMinutes = snapshot.BonusSeconds / 60;
                    _exitPassword = snapshot.ExitPassword;
                    ApplyWindowConfig(snapshot.AllowedWindowsEnabled, snapshot.AllowedWindows);
                    string today = DateKeys.For(_clock);

                    if (snapshot.StateDocumentMissing || !string.Equals(snapshot.DateKey, today, StringComparison.Ordinal))
                    {
                        _state.BaseSeconds = snapshot.BaseSeconds;
                        _state.BonusSeconds = snapshot.BonusSeconds;
                        _state.RemainingSeconds = snapshot.BaseSeconds + snapshot.BonusSeconds;
                        _state.DateKey = today;
                    }
                    else
                    {
                        _state.BaseSeconds = snapshot.BaseSeconds;
                        _state.BonusSeconds = snapshot.BonusSeconds;
                        _state.RemainingSeconds = Math.Max(0, snapshot.CloudRemainingSeconds ?? _state.RemainingSeconds);
                        _state.IsPaused = snapshot.CloudIsPaused ?? _state.IsPaused;
                        _state.DateKey = today;
                    }

                    _firebaseReady = true;
                    await _firebase.PushStateAsync(_state.RemainingSeconds, _state.IsPaused, _state.DateKey);
                    _firebaseTimer.Start();
                    LogFirebaseMessage("Firebase bağlantısı kuruldu. PC kullanım durumu web paneliyle eşitleniyor.");
                }
                catch (Exception ex)
                {
                    LogFirebaseError(ex);
                    if (ex.Message.StartsWith("Firebase girişi başarısız", StringComparison.Ordinal))
                        _firebase.ClearCredentials();
                    // Show the error dialog at most once per session; later failures only go to the log.
                    if (!_firebaseErrorDialogShown)
                    {
                        _firebaseErrorDialogShown = true;
                        MessageBox.Show("Firebase bağlantısı kurulamadı. Uygulama yerel sayaçla çalışmaya devam edecek.\n\n" + ex.Message,
                            "Firebase bağlantı hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }

            EnsureDailyState();
            RenderState();
            if (_state.IsPaused || _state.RemainingSeconds <= 0 || DecideNow().Reason == LockReason.OutsideWindow)
            {
                _state.IsPaused = true;
                if (_state.AudioWasMutedByApp) SetSystemMute(true);
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

        private bool ShowFirebaseLoginDialog()
        {
            var emailBox = new System.Windows.Controls.TextBox
            {
                Width = 260, FontSize = 14, Margin = new Thickness(0, 6, 0, 12)
            };
            var passwordBox = new System.Windows.Controls.PasswordBox
            {
                Width = 260, FontSize = 14, Margin = new Thickness(0, 6, 0, 14)
            };
            var error = new System.Windows.Controls.TextBlock
            {
                Text = "Firebase cihaz hesabının e-posta ve şifresini gir.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
            };
            var saveButton = new System.Windows.Controls.Button
            {
                Content = "Bağlan", Width = 90, Height = 30, IsDefault = true,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var cancelButton = new System.Windows.Controls.Button
            {
                Content = "Şimdilik geç", Width = 100, Height = 30, IsCancel = true
            };
            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(saveButton);
            buttons.Children.Add(cancelButton);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Firebase e-posta adresi" });
            panel.Children.Add(emailBox);
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Firebase şifresi" });
            panel.Children.Add(passwordBox);
            panel.Children.Add(error);
            panel.Children.Add(buttons);

            var dialog = new Window
            {
                Title = "ZekPcSayac - Firebase bağlantısı", Content = panel,
                Width = 340, Height = 300, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this,
                ShowInTaskbar = false, Topmost = true
            };

            bool accepted = false;
            saveButton.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(emailBox.Text) || string.IsNullOrEmpty(passwordBox.Password))
                {
                    error.Text = "E-posta ve şifre zorunludur.";
                    return;
                }
                try
                {
                    _firebase.SaveCredentials(emailBox.Text, passwordBox.Password);
                    accepted = true;
                    dialog.DialogResult = true;
                }
                catch (Exception ex)
                {
                    error.Text = "Giriş bilgileri güvenli kaydedilemedi: " + ex.Message;
                }
            };
            dialog.Loaded += (_, _) => emailBox.Focus();
            dialog.ShowDialog();
            return accepted;
        }

        private async void FirebaseTimer_Tick(object? sender, EventArgs e)
        {
            if (!_firebaseReady || _firebaseBusy) return;
            _firebaseBusy = true;
            try
            {
                var config = await _firebase.ReadConfigAsync();
                _configuredDailyMinutes = config.DailySeconds / 60;
                _configuredBonusMinutes = config.BonusSeconds / 60;
                _exitPassword = config.ExitPassword;
                ApplyWindowConfig(config.AllowedWindowsEnabled, config.AllowedWindows);

                // Yalnızca gün ileri gittiğinde yenile; saat geri alınırsa gün değişmez.
                string today = DateKeys.For(_clock);
                if (string.CompareOrdinal(today, _state.DateKey) > 0)
                {
                    EnsureDailyState();
                    RenderState();
                    if (_state.IsPaused || _state.RemainingSeconds <= 0)
                        EnterPausedScreen();
                    else
                        _timer.Start();
                }
                else if (config.BonusSeconds > _state.BonusSeconds)
                {
                    // A bonus granted today increases the live balance immediately.
                    int addedSeconds = config.BonusSeconds - _state.BonusSeconds;
                    _state.BonusSeconds = config.BonusSeconds;
                    _state.RemainingSeconds = Math.Min(172800, _state.RemainingSeconds + addedSeconds);
                    RenderState();
                    SaveState();
                }

                var command = await _firebase.GetRemainingCommandAsync();
                if (!string.IsNullOrWhiteSpace(command.CommandId)
                    && command.RemainingSeconds.HasValue
                    && !string.Equals(command.CommandId, _state.LastAppliedRemainingCommandId, StringComparison.Ordinal))
                {
                    _state.RemainingSeconds = Math.Clamp(command.RemainingSeconds.Value, 0, 86400);
                    _state.LastAppliedRemainingCommandId = command.CommandId;
                    if (_state.RemainingSeconds == 0)
                    {
                        _state.IsPaused = true;
                        CaptureAndMuteAudio();
                        TryPauseMedia();
                        EnterPausedScreen();
                    }
                    else
                    {
                        RenderState();
                        if (!_state.IsPaused)
                            _timer.Start();
                    }
                    SaveState();
                }

                await _firebase.PushStateAsync(_state.RemainingSeconds, _state.IsPaused, _state.DateKey);
            }
            catch (Exception ex)
            {
                LogFirebaseError(ex);
                // Keep retrying periodically; the next attempt will re-authenticate if needed.
            }
            finally { _firebaseBusy = false; }
        }

        private void LogFirebaseError(Exception ex)
        {
            AppendFirebaseLog(ex.ToString());
        }

        private void LogFirebaseMessage(string message)
        {
            AppendFirebaseLog(message);
        }

        private void AppendFirebaseLog(string text)
        {
            try
            {
                string logPath = Path.Combine(Path.GetDirectoryName(_stateFile)!, "firebase.log");
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}\r\n");
            }
            catch { }
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

        // Son başarılı okunan saat kısıtı; offline iken UsageState'teki bu kopya kullanılır.
        private void ApplyWindowConfig(bool enabled, System.Collections.Generic.IReadOnlyList<AllowedWindow> windows)
        {
            _state.AllowedWindowsEnabled = enabled;
            _state.AllowedWindows = enabled ? new System.Collections.Generic.List<AllowedWindow>(windows) : new();
            SaveState();
        }

        private LockDecision DecideNow()
        {
            DateTime localNow = DateKeys.TurkeyLocal(_clock.UtcNow);
            bool quotaExhausted = _state.IsPaused || _state.RemainingSeconds <= 0;
            return LockDecider.Decide(localNow, _state.AllowedWindowsEnabled, _state.AllowedWindows, quotaExhausted);
        }

        // Çalışırken saat dışına çıkıldıysa sayacı düşürmeden kilitler. Kilitlediyse true döner.
        private bool EnforceWindowLock()
        {
            if (_state.IsPaused || IsLockScreenActive) return false;
            if (DecideNow().Reason != LockReason.OutsideWindow) return false;

            CaptureAndMuteAudio();
            TryPauseMedia();
            _state.IsPaused = true;
            SaveState();
            EnterPausedScreen();
            return true;
        }

        private void WindowTimer_Tick(object? sender, EventArgs e)
        {
            if (EnforceWindowLock()) return;
            RenderState();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (EnforceWindowLock()) return;

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
            // Saat dışında "Devam Et" açmaz; kilit ekranı sebebi gösterir.
            if (_state.RemainingSeconds <= 0 || DecideNow().Reason == LockReason.OutsideWindow)
            {
                EnterPausedScreen();
                return;
            }

            RestoreAudio();
            _state.IsPaused = false;
            _state.LastStartedUtc = DateTime.UtcNow;
            SaveState();

            _lockScreenTimer.Stop();
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
            ApplyLockScreenBounds();
            Topmost = true;
            RenderState();
            _lockScreenTimer.Start();
        }

        // Fits the window exactly onto the virtual screen (all monitors). Normal state is used
        // instead of Maximized: a maximized borderless window only fills one monitor's work area,
        // so it could never cover the virtual screen.
        private void ApplyLockScreenBounds()
        {
            WindowState = WindowState.Normal;
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
        }

        private bool IsLockScreenActive => PausedPanel.Visibility == Visibility.Visible;

        // The close-password dialog is modal and runs on this thread, so the lock screen must not
        // steal activation from it while it is open.
        private bool CanEnforceLockScreen => IsLockScreenActive && !_closePromptOpen;

        private bool CoversVirtualScreen()
        {
            const double tolerance = 0.5;
            return Left <= SystemParameters.VirtualScreenLeft + tolerance
                && Top <= SystemParameters.VirtualScreenTop + tolerance
                && Left + Width >= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - tolerance
                && Top + Height >= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - tolerance;
        }

        private void LockScreenTimer_Tick(object? sender, EventArgs e)
        {
            if (!CanEnforceLockScreen) return;

            Topmost = true;
            Activate();
            if (!CoversVirtualScreen())
                ApplyLockScreenBounds();
        }

        private void MainWindow_Deactivated(object? sender, EventArgs e)
        {
            if (!CanEnforceLockScreen) return;

            // Re-activate after focus is lost; deferred so activation is not changed mid-deactivation.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (CanEnforceLockScreen)
                    Activate();
            }), DispatcherPriority.Background);
        }

        private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsLockScreenActive)
                    ApplyLockScreenBounds();
            }), DispatcherPriority.Background);
        }

        private void CaptureAndMuteAudio()
        {
            if (_state.AudioWasMutedByApp)
            {
                // Keep the original pre-pause state; repeated pause commands must not
                // mistake the app's own mute for the user's original mute setting.
                SetSystemMute(true);
                return;
            }

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
            TimeSpan remaining = TimeSpan.FromSeconds(Math.Max(0, _state.RemainingSeconds));
            string time = $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";

            RemainingText.Text = time;
            PausedRemainingText.Text = time;
            PauseResumeButton.Content = _state.IsPaused ? "Devam Et" : "Durdur";

            LockDecision decision = DecideNow();

            if (decision.Reason == LockReason.OutsideWindow)
            {
                PausedReasonText.Text = decision.NextWindowStart is DateTime next
                    ? $"Kullanım saati dışında. Açılış: {LockDecider.FormatNextStart(next)}"
                    : "Kullanım saati dışında.";
                PausedReasonText.Visibility = Visibility.Visible;
            }
            else
            {
                PausedReasonText.Visibility = Visibility.Collapsed;
            }

            // Kapanış uyarısı yalnızca sayaç çalışırken gösterilir.
            int level = _state.IsPaused ? 0 : LockDecider.WarningLevel(decision.TimeUntilWindowEnd);
            RemainingText.Foreground = level switch
            {
                2 => System.Windows.Media.Brushes.Red,
                1 => System.Windows.Media.Brushes.Orange,
                _ => System.Windows.Media.Brushes.White
            };

            if (level == 2 && decision.TimeUntilWindowEnd is TimeSpan left)
            {
                CloseWarningText.Text = $"Kapanışa {Math.Max(0, (int)Math.Ceiling(left.TotalSeconds))} sn";
                CloseWarningText.Visibility = Visibility.Visible;
            }
            else
            {
                CloseWarningText.Visibility = Visibility.Collapsed;
            }

            if (!IsLockScreenActive)
                Height = CloseWarningText.Visibility == Visibility.Visible ? 92 : 76;
        }

        private static SignedStateStore? CreateStateStore(string appDirectory, string stateFile)
        {
            byte[]? key = LoadOrCreateStateKey(appDirectory);
            return key is null ? null : new SignedStateStore(stateFile, key);
        }

        // Anahtar DPAPI (CurrentUser) ile şifreli saklanır. Mevcut anahtar okunamazsa
        // yeni anahtar üretilmez; aksi halde imzalı durum sessizce sıfırlanabilirdi.
        private static byte[]? LoadOrCreateStateKey(string appDirectory)
        {
            string keyPath = Path.Combine(appDirectory, "state-key.bin");
            try
            {
                if (File.Exists(keyPath))
                {
                    byte[] protectedKey = File.ReadAllBytes(keyPath);
                    byte[] key = ProtectedData.Unprotect(protectedKey, null, DataProtectionScope.CurrentUser);
                    return key.Length == 32 ? key : null;
                }

                byte[] newKey = RandomNumberGenerator.GetBytes(32);
                byte[] protectedNewKey = ProtectedData.Protect(newKey, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(keyPath, protectedNewKey);
                return newKey;
            }
            catch
            {
                return null;
            }
        }

        private UsageState LoadState()
        {
            if (_stateStore is null)
            {
                AppendFirebaseLog("state key unavailable; state locked");
                return TamperedState();
            }

            LoadResult result;
            try
            {
                result = _stateStore.Load();
            }
            catch
            {
                // Okunamayan durum, süre sıfırlanarak bypass edilemesin diye kilitli kabul edilir.
                result = new LoadResult(LoadStatus.Tampered);
            }

            switch (result.Status)
            {
                case LoadStatus.Ok:
                    // Kayıtlı güvenilir zaman, gün anahtarı hesaplanmadan önce geri yüklenir.
                    _clock.Restore(TimeSpan.FromTicks(result.State!.ServerOffsetTicks), result.State.LastTrustedUtc);
                    return result.State;
                case LoadStatus.Missing:
                    return DefaultState();
                default:
                    AppendFirebaseLog("state tampered");
                    return TamperedState();
            }
        }

        private UsageState DefaultState()
        {
            return new UsageState
            {
                BaseSeconds = InitialDailyMinutes * 60,
                BonusSeconds = 0,
                RemainingSeconds = InitialDailyMinutes * 60,
                IsPaused = false,
                LastStartedUtc = DateTime.UtcNow,
                DateKey = DateKeys.For(_clock)
            };
        }

        private UsageState TamperedState()
        {
            UsageState state = DefaultState();
            state.RemainingSeconds = 0;
            state.IsPaused = true;
            return state;
        }

        private void EnsureDailyState()
        {
            string today = DateKeys.For(_clock);
            // Yalnızca gün ileri gittiğinde yenile; saat geri alınırsa kullanım hakkı yenilenmez.
            if (string.CompareOrdinal(today, _state.DateKey) <= 0)
                return;

            // New local calendar day: renew the base allowance and discard yesterday's bonus.
            // Preserve pause state so a paused session never resumes by itself after midnight.
            _state.BaseSeconds = _configuredDailyMinutes * 60;
            _state.BonusSeconds = _configuredBonusMinutes * 60;
            _state.RemainingSeconds = _state.BaseSeconds + _state.BonusSeconds;
            _state.DateKey = today;
            _state.LastStartedUtc = DateTime.UtcNow;
            SaveState();
        }

        private void SaveState()
        {
            if (_stateStore is null)
                return;

            try
            {
                _state.ServerOffsetTicks = _clock.ServerOffset.Ticks;
                _state.LastTrustedUtc = _clock.LastTrustedUtc;
                _stateStore.Save(_state);
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

            // If the app muted system audio while pausing, restore the prior mute state
            // before exiting so Windows audio is not left muted after the app closes.
            RestoreAudio();

            _timer.Stop();
            _dailyCheckTimer.Stop();
            _windowTimer.Stop();
            _firebaseTimer.Stop();
            _lockScreenTimer.Stop();
            SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
            SaveState();
        }

        private void RequestCloseWithPassword()
        {
            ExitPasswordHash? expectedHash = _exitPassword;
            if (expectedHash is null)
            {
                MessageBox.Show("Çıkış şifresi henüz belirlenmemiş. Web panelden belirleyin.",
                    "ZekPcSayac - Çıkış", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

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
                TimeSpan? locked = _exitLimiter.RemainingLock();
                if (locked is not null)
                {
                    errorText.Text = LockedMessage(locked.Value);
                    passwordBox.Clear();
                    return;
                }

                if (ExitPasswordVerifier.Verify(passwordBox.Password, expectedHash))
                {
                    _exitLimiter.RecordSuccess();
                    dialog.DialogResult = true;
                    return;
                }

                _exitLimiter.RecordFailure();
                TimeSpan? lockedAfterFailure = _exitLimiter.RemainingLock();
                errorText.Text = lockedAfterFailure is null ? "Şifre yanlış." : LockedMessage(lockedAfterFailure.Value);
                passwordBox.Clear();
                passwordBox.Focus();
            };

            dialog.Loaded += (_, _) => passwordBox.Focus();
            bool? result = dialog.ShowDialog();
            if (result == true)
            {
                _allowClose = true;
                Close();
            }
        }

        private static string LockedMessage(TimeSpan remaining)
        {
            int seconds = (int)Math.Ceiling(remaining.TotalSeconds);
            return $"Çok fazla yanlış deneme. {seconds} sn sonra tekrar deneyin.";
        }

    }
}
