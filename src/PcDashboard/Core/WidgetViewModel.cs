using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PcDashboard.Core;

/// <summary>
/// Tüm widget ViewModel'lerinin temeli. Zamanlayıcıları yönetir, hataları yakalar
/// ve Dispose edildiğinde her şeyi durdurur.
/// </summary>
public abstract partial class WidgetViewModel : ObservableObject, IDisposable
{
    private readonly List<DispatcherTimer> _timers = new();
    private bool _disposed;

    /// <summary>Widget ekrana eklendikten sonra bir kez çağrılır.</summary>
    public virtual Task StartAsync() => Task.CompletedTask;

    /// <summary>Belirli aralıklarla UI iş parçacığında senkron bir işlem çalıştırır.</summary>
    protected void Every(TimeSpan interval, Action action, bool runNow = true)
    {
        EveryAsync(interval, () =>
        {
            action();
            return Task.CompletedTask;
        }, runNow);
    }

    /// <summary>
    /// Belirli aralıklarla asenkron bir işlem çalıştırır. Önceki çalışma bitmeden yenisi başlamaz.
    /// </summary>
    protected void EveryAsync(TimeSpan interval, Func<Task> action, bool runNow = true)
    {
        var running = false;

        async Task RunOnce()
        {
            if (running || _disposed) return;
            running = true;
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                Log.Error($"{GetType().Name} güncellenemedi", ex);
            }
            finally
            {
                running = false;
            }
        }

        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = interval };
        timer.Tick += async (_, _) => await RunOnce();
        timer.Start();
        _timers.Add(timer);

        if (runNow) _ = RunOnce();
    }

    protected static Task OnUiAsync(Action action) =>
        Application.Current.Dispatcher.InvokeAsync(action).Task;

    protected static Brush ResourceBrush(string key) =>
        Application.Current.TryFindResource(key) as Brush ?? Brushes.White;

    protected static Geometry ResourceGeometry(string key) =>
        Application.Current.TryFindResource(key) as Geometry ?? Geometry.Empty;

    protected virtual void OnDisposing()
    {
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var timer in _timers) timer.Stop();
        _timers.Clear();

        try
        {
            OnDisposing();
        }
        catch (Exception ex)
        {
            Log.Error($"{GetType().Name} kapatılamadı", ex);
        }

        GC.SuppressFinalize(this);
    }
}
