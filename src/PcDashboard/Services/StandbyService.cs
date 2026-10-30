using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using PcDashboard.Core;
using PcDashboard.Interop;

namespace PcDashboard.Services;

/// <summary>
/// Bekleme ekranı (WinDash'in kendi ekran koruyucusu):
///  - Belirli süre fare / klavye kullanılmazsa ana ekranda tam WinDash paneli açılır (diğer ekranlar kararır).
///  - "WinDash ile kilitle" (tepsi menüsü / kısayol) aynı ekranı hemen açar.
///  - Fare / klavye kullanılınca kapanır ve ayara göre Windows'u kilitler → Windows'un parola / Oturum aç ekranı gelir.
///  - Kilit açılınca / oturum açılınca <see cref="Unlocked"/> olayı (uygulama masaüstüne gömülü moda geçer).
/// Tam ekran oyun, sunum, "rahatsız etme" ya da video oynatılırken (ekranın açık kalması istenirken) kendiliğinden açılmaz.
/// Windows'un kilit ekranının içine hiçbir uygulama çizemez; bu yüzden görünüm kilitlemeden önce gösterilir.
/// </summary>
public sealed class StandbyService : IDisposable
{
    /// <summary>Açıldıktan sonra bu süre içindeki girişler sayılmaz (kısayolun tuşları bırakılırken vb.).</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(1.5);

    private readonly Func<StandbySettings> _settings;
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _inputTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly List<StandbyWindow> _windows = new();
    private readonly Dispatcher _dispatcher;
    private DateTime _shownAt;
    private uint _baselineInput;
    private bool _lockOnExit;
    private bool _sessionLocked;
    private bool _dismissing;

    /// <summary>Windows kilidi açıldı veya oturum açıldı.</summary>
    public event EventHandler? Unlocked;

    public bool IsActive => _windows.Count > 0;

    public StandbyService(Func<StandbySettings> settings)
    {
        _settings = settings;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _idleTimer.Tick += (_, _) => CheckIdle();
        _inputTimer.Tick += (_, _) => CheckInput();
        SystemEvents.SessionSwitch += OnSessionSwitch;
        _idleTimer.Start();
    }

    // ---------- Açma ----------

    /// <summary>Bekleme ekranını hemen açar. <paramref name="lockOnExit"/>: kapanırken Windows'u kilitle.</summary>
    public void Show(bool lockOnExit)
    {
        if (IsActive || _sessionLocked) return;

        var monitors = MonitorService.GetMonitors();
        if (monitors.Count == 0) return;

        Log.Info(lockOnExit ? "Bekleme ekranı açılıyor (çıkışta kilitlenecek)" : "Bekleme ekranı açılıyor");
        _lockOnExit = lockOnExit;
        _dismissing = false;

        var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        foreach (var monitor in monitors)
        {
            try
            {
                var window = new StandbyWindow(monitor, showDashboard: monitor.Handle == primary.Handle);
                _windows.Add(window);
                window.Show();
            }
            catch (Exception ex)
            {
                Log.Error($"Bekleme ekranı açılamadı: {monitor}", ex);
            }
        }

        _windows.FirstOrDefault()?.Activate();
        _shownAt = DateTime.UtcNow;
        _baselineInput = LastInputTick();
        _inputTimer.Start();
    }

    /// <summary>"WinDash ile kilitle": bekleme ekranını aç, dokunulunca Windows'u kilitle.</summary>
    public void LockWithStandby() => Show(lockOnExit: true);

    private void CheckIdle()
    {
        var s = _settings();
        if (s.IdleMinutes <= 0 || IsActive || _sessionLocked) return;

        var idle = unchecked((uint)Environment.TickCount - LastInputTick());
        if (idle < s.IdleMinutes * 60_000L) return;
        if (!CanStartAutomatically()) return;

        Show(lockOnExit: s.LockOnResume);
    }

    /// <summary>Tam ekran oyun / sunum / rahatsız etme modu veya video oynatılırken (ekran açık kalsın isteği) açılmaz.</summary>
    private static bool CanStartAutomatically()
    {
        try
        {
            if (NativeMethods.SHQueryUserNotificationState(out var state) == 0 &&
                state is NativeMethods.QUNS_BUSY or NativeMethods.QUNS_RUNNING_D3D_FULL_SCREEN or NativeMethods.QUNS_PRESENTATION_MODE)
            {
                return false;
            }

            if (NativeMethods.CallNtPowerInformation(NativeMethods.SystemExecutionState, IntPtr.Zero, 0,
                    out var execution, sizeof(uint)) == 0 &&
                (execution & NativeMethods.ES_DISPLAY_REQUIRED) != 0)
            {
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Ekran durumu okunamadı: {ex.Message}");
        }

        return true;
    }

    // ---------- Kapatma ----------

    private void CheckInput()
    {
        if (!IsActive || _dismissing) return;

        var last = LastInputTick();
        if (DateTime.UtcNow - _shownAt < Grace)
        {
            _baselineInput = last;
            return;
        }

        if (last != _baselineInput) Dismiss(lockNow: _lockOnExit);
    }

    private void Dismiss(bool lockNow)
    {
        if (_dismissing) return;
        _dismissing = true;
        _inputTimer.Stop();

        if (lockNow)
        {
            // Önce kilitle: Windows'un kilit ekranı gelirken arkadaki masaüstü görünmesin
            if (!NativeMethods.LockWorkStation())
                Log.Warn($"Windows kilitlenemedi (hata {Marshal.GetLastWin32Error()})");

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                CloseWindows();
            };
            timer.Start();
        }
        else
        {
            CloseWindows();
        }
    }

    private void CloseWindows()
    {
        foreach (var window in _windows.ToList())
        {
            try
            {
                window.Close();
            }
            catch (Exception ex)
            {
                Log.Error("Bekleme ekranı kapatılamadı", ex);
            }
        }

        _windows.Clear();
        _dismissing = false;
    }

    // ---------- Oturum ----------

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e) =>
        _dispatcher.InvokeAsync(() =>
        {
            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLock:
                    _sessionLocked = true;
                    // Win+L ile kilitlendi: açık bekleme ekranı varsa kaldır
                    if (IsActive)
                    {
                        _inputTimer.Stop();
                        CloseWindows();
                    }

                    break;

                case SessionSwitchReason.SessionUnlock:
                case SessionSwitchReason.SessionLogon:
                    _sessionLocked = false;
                    Log.Info("Windows kilidi açıldı / oturum açıldı");
                    Unlocked?.Invoke(this, EventArgs.Empty);
                    break;
            }
        });

    private static uint LastInputTick()
    {
        var info = new NativeMethods.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.LASTINPUTINFO>() };
        return NativeMethods.GetLastInputInfo(ref info) ? info.dwTime : (uint)Environment.TickCount;
    }

    public void Dispose()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _idleTimer.Stop();
        _inputTimer.Stop();
        CloseWindows();
    }
}
