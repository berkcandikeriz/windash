using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcDashboard.Core;
using PcDashboard.Services;
using Windows.Media.Control;
using MediaPlaybackAutoRepeatMode = Windows.Media.MediaPlaybackAutoRepeatMode;
using MediaSession = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using MediaSessionManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;

namespace PcDashboard.Widgets.NowPlaying;

/// <summary>
/// Windows'un medya oturumu API'si üzerinden Spotify'ı (ve istenirse diğer oynatıcıları) izler.
/// Spotify hesabı veya API anahtarı gerektirmez.
/// </summary>
public sealed partial class NowPlayingViewModel : WidgetViewModel
{
    private readonly MediaSettings _settings;
    private MediaSessionManager? _manager;
    private MediaSession? _session;
    private string _thumbnailKey = "";

    [ObservableProperty] private string _header = "ŞU AN ÇALIYOR";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private bool _hasSession;
    [ObservableProperty] private string _title = "Bir şey çalmıyor";
    [ObservableProperty] private string _artist = "Müzik veya video başlat";
    [ObservableProperty] private string _album = "";
    [ObservableProperty] private ImageSource? _thumbnail;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _positionText = "0:00";
    [ObservableProperty] private string _durationText = "0:00";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseIcon))]
    private bool _isPlaying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShuffleBrush))]
    private bool _isShuffle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RepeatBrush))]
    [NotifyPropertyChangedFor(nameof(RepeatTooltip))]
    private MediaPlaybackAutoRepeatMode _repeatMode;

    public NowPlayingViewModel(MediaSettings settings) => _settings = settings;

    public Geometry PlayPauseIcon => ResourceGeometry(IsPlaying ? "IconPause" : "IconPlay");
    public Brush ShuffleBrush => IsShuffle ? ResourceBrush("AccentBrush") : ResourceBrush("MutedBrush");
    public Brush RepeatBrush => RepeatMode == MediaPlaybackAutoRepeatMode.None ? ResourceBrush("MutedBrush") : ResourceBrush("AccentBrush");

    public string RepeatTooltip => RepeatMode switch
    {
        MediaPlaybackAutoRepeatMode.Track => "Tekrarla: şarkı",
        MediaPlaybackAutoRepeatMode.List => "Tekrarla: liste",
        _ => "Tekrarla: kapalı",
    };

    public override async Task StartAsync()
    {
        try
        {
            _manager = await MediaSessionManager.RequestAsync();
            _manager.SessionsChanged += OnSessionsChanged;
            _manager.CurrentSessionChanged += OnCurrentSessionChanged;
        }
        catch (Exception ex)
        {
            Log.Error("Medya oturumu yöneticisi açılamadı", ex);
            return;
        }

        await PickSessionAsync();
        Every(TimeSpan.FromSeconds(1), UpdateTimeline);
        Every(TimeSpan.FromSeconds(5), UpdateOutput);
    }

    // ---------- Oturum seçimi ----------

    private void OnSessionsChanged(MediaSessionManager sender, SessionsChangedEventArgs args) =>
        _ = OnUiAsync(() => _ = PickSessionAsync());

    private void OnCurrentSessionChanged(MediaSessionManager sender, CurrentSessionChangedEventArgs args) =>
        _ = OnUiAsync(() => _ = PickSessionAsync());

    private async Task PickSessionAsync()
    {
        if (_manager is null) return;

        MediaSession? chosen = null;
        try
        {
            var sessions = _manager.GetSessions();
            chosen = sessions.FirstOrDefault(s =>
                s.SourceAppUserModelId.Contains(_settings.PreferredApp, StringComparison.OrdinalIgnoreCase));

            if (chosen is null && _settings.FallbackToAnyPlayer)
                chosen = _manager.GetCurrentSession() ?? sessions.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Log.Error("Medya oturumları okunamadı", ex);
        }

        if (!ReferenceEquals(chosen, _session) &&
            (chosen is null || _session is null || chosen.SourceAppUserModelId != _session.SourceAppUserModelId))
        {
            Detach();
            _session = chosen;
            if (_session is not null)
            {
                _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
                _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
                _session.TimelinePropertiesChanged += OnTimelineChanged;
            }
        }

        HasSession = _session is not null;
        Header = _session is null ? "ŞU AN ÇALIYOR" : $"ŞU AN ÇALIYOR · {AppName(_session.SourceAppUserModelId)}";

        if (_session is null)
        {
            ClearMedia();
            return;
        }

        await UpdateMediaAsync();
        UpdatePlayback();
        UpdateTimeline();
    }

    private void Detach()
    {
        if (_session is null) return;
        _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        _session.TimelinePropertiesChanged -= OnTimelineChanged;
        _session = null;
    }

    private void OnMediaPropertiesChanged(MediaSession sender, MediaPropertiesChangedEventArgs args) =>
        _ = OnUiAsync(() => _ = UpdateMediaAsync());

    private void OnPlaybackInfoChanged(MediaSession sender, PlaybackInfoChangedEventArgs args) =>
        _ = OnUiAsync(UpdatePlayback);

    private void OnTimelineChanged(MediaSession sender, TimelinePropertiesChangedEventArgs args) =>
        _ = OnUiAsync(UpdateTimeline);

    // ---------- Güncellemeler ----------

    private async Task UpdateMediaAsync()
    {
        var session = _session;
        if (session is null) return;

        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (props is null) return;

            Title = string.IsNullOrWhiteSpace(props.Title) ? "Bilinmeyen şarkı" : props.Title;
            Artist = props.Artist ?? "";
            Album = props.AlbumTitle ?? "";

            var key = $"{props.Title}|{props.Artist}|{props.AlbumTitle}";
            if (key != _thumbnailKey)
            {
                _thumbnailKey = key;
                Thumbnail = props.Thumbnail is null ? null : await LoadThumbnailAsync(props.Thumbnail);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Şarkı bilgisi okunamadı", ex);
        }
    }

    private static async Task<ImageSource?> LoadThumbnailAsync(Windows.Storage.Streams.IRandomAccessStreamReference reference)
    {
        try
        {
            using var winStream = await reference.OpenReadAsync();
            await using var stream = winStream.AsStreamForRead();
            var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 480;
            image.StreamSource = ms;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            Log.Error("Albüm kapağı okunamadı", ex);
            return null;
        }
    }

    private void UpdatePlayback()
    {
        var session = _session;
        if (session is null) return;

        try
        {
            var info = session.GetPlaybackInfo();
            IsPlaying = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            IsShuffle = info.IsShuffleActive ?? false;
            RepeatMode = info.AutoRepeatMode ?? MediaPlaybackAutoRepeatMode.None;
        }
        catch (Exception ex)
        {
            Log.Error("Oynatma durumu okunamadı", ex);
        }
    }

    private void UpdateTimeline()
    {
        var session = _session;
        if (session is null) return;

        try
        {
            var timeline = session.GetTimelineProperties();
            var end = timeline.EndTime - timeline.StartTime;
            var position = timeline.Position - timeline.StartTime;

            // Spotify konumu her saniye bildirmez; son bildirimden bu yana geçen süreyi ekliyoruz
            if (IsPlaying && timeline.LastUpdatedTime > DateTimeOffset.MinValue)
            {
                var elapsed = DateTimeOffset.Now - timeline.LastUpdatedTime;
                if (elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromHours(1)) position += elapsed;
            }

            if (end <= TimeSpan.Zero)
            {
                Progress = 0;
                PositionText = Format(position);
                DurationText = "—";
                return;
            }

            if (position > end) position = end;
            if (position < TimeSpan.Zero) position = TimeSpan.Zero;

            Progress = position.TotalSeconds / end.TotalSeconds * 100;
            PositionText = Format(position);
            DurationText = Format(end);
        }
        catch (Exception ex)
        {
            Log.Error("Zaman çizelgesi okunamadı", ex);
        }
    }

    private void UpdateOutput()
    {
        var name = AudioHub.DefaultOutputName();
        Output = name is null ? "" : $"Çıkış: {AudioHub.Display(name).Name}";
    }

    private void ClearMedia()
    {
        Title = "Bir şey çalmıyor";
        Artist = "Müzik veya video başlat";
        Album = "";
        Thumbnail = null;
        _thumbnailKey = "";
        IsPlaying = false;
        Progress = 0;
        PositionText = "0:00";
        DurationText = "0:00";
    }

    // ---------- Komutlar ----------

    [RelayCommand]
    private async Task PlayPauseAsync()
    {
        if (_session is not null) await _session.TryTogglePlayPauseAsync();
    }

    [RelayCommand]
    private async Task PreviousAsync()
    {
        if (_session is not null) await _session.TrySkipPreviousAsync();
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        if (_session is not null) await _session.TrySkipNextAsync();
    }

    [RelayCommand]
    private async Task ShuffleAsync()
    {
        if (_session is not null) await _session.TryChangeShuffleActiveAsync(!IsShuffle);
    }

    [RelayCommand]
    private async Task RepeatAsync()
    {
        if (_session is null) return;
        var next = RepeatMode switch
        {
            MediaPlaybackAutoRepeatMode.None => MediaPlaybackAutoRepeatMode.List,
            MediaPlaybackAutoRepeatMode.List => MediaPlaybackAutoRepeatMode.Track,
            _ => MediaPlaybackAutoRepeatMode.None,
        };
        await _session.TryChangeAutoRepeatModeAsync(next);
    }

    // ---------- Yardımcılar ----------

    private static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    private static string AppName(string appId)
    {
        if (appId.Contains("spotify", StringComparison.OrdinalIgnoreCase)) return "SPOTIFY";
        var name = appId.Split('!', '\\').Last();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name.ToUpperInvariant();
    }

    protected override void OnDisposing()
    {
        Detach();
        if (_manager is not null)
        {
            _manager.SessionsChanged -= OnSessionsChanged;
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            _manager = null;
        }
    }
}
