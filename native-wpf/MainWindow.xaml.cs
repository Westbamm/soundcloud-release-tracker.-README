using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace SoundCloudReleaseTracker;

public partial class MainWindow : Window
{
    private readonly AppConfig _cfg;
    private readonly TrackStore _store = new();
    private readonly MciPlayer _player = new();
    private readonly System.Windows.Threading.DispatcherTimer _timer = new();
    private bool _busy;
    private string _lastDownloadedPath = "";

    public MainWindow()
    {
        InitializeComponent();

        _cfg = AppConfig.Load();
        AppConfig.TryMigrateLegacyPlaintextSecret();

        LoadSettings();
        RefreshGrid();
        UpdateConnectionState();
        ShowTracksPage();

        _timer.Tick += async (_, _) => await CheckNowAsync();
        Closed += (_, _) => _player.Dispose();
    }

    private void ShowTracksPage()
    {
        TracksPage.Visibility = Visibility.Visible;
        ArtistsPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;
        SetNavState(NavTracks);
    }

    private void ShowArtistsPage()
    {
        TracksPage.Visibility = Visibility.Collapsed;
        ArtistsPage.Visibility = Visibility.Visible;
        SettingsPage.Visibility = Visibility.Collapsed;
        SetNavState(NavArtists);
    }

    private void ShowSettingsPage()
    {
        TracksPage.Visibility = Visibility.Collapsed;
        ArtistsPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Visible;
        SetNavState(NavSettings);
    }

    private void SetNavState(Button active)
    {
        foreach (var b in new[] { NavTracks, NavArtists, NavSettings })
        {
            b.Background = ReferenceEquals(b, active)
                ? (System.Windows.Media.Brush)FindResource("CardAlt")
                : System.Windows.Media.Brushes.Transparent;
            b.Foreground = ReferenceEquals(b, active)
                ? (System.Windows.Media.Brush)FindResource("Text")
                : (System.Windows.Media.Brush)FindResource("Muted");
        }
    }

    private void NavTracks_Click(object sender, RoutedEventArgs e) => ShowTracksPage();
    private void NavArtists_Click(object sender, RoutedEventArgs e) => ShowArtistsPage();
    private void NavSettings_Click(object sender, RoutedEventArgs e) => ShowSettingsPage();

    private async void CheckNow_Click(object sender, RoutedEventArgs e) => await CheckNowAsync();
    private void Monitor_Click(object sender, RoutedEventArgs e) => ToggleMonitoring();
    private async void Preview_Click(object sender, RoutedEventArgs e) => await PreviewAsync();
    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _player.Stop();
        SetStatus("Preview остановлен");
    }
    private async void Download_Click(object sender, RoutedEventArgs e) => await DownloadSelectedAsync();
    private void OpenSoundCloud_Click(object sender, RoutedEventArgs e) => OpenSelected();

    private async void TracksGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        await PreviewAsync();

    private void TracksGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var track = TracksGrid.SelectedItem as TrackEntry;
        DownloadButton.IsEnabled = track?.Downloadable == true;
    }

    private void FilterChanged(object sender, RoutedEventArgs e) => RefreshGrid();
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var hasText = !string.IsNullOrWhiteSpace(SearchBox.Text);
        SearchHint.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
        ClearSearchButton.IsEnabled = hasText;
        RefreshGrid();
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }

    private void ClearTracks_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                this,
                "Очистить весь локальный список найденных треков?\n\n" +
                "Скачанные музыкальные файлы, настройки, любимые артисты и blacklist удалены не будут.",
                "Очистить список",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _player.Stop();
        _store.Clear();
        SearchBox.Clear();
        OnlyFavoritesCheck.IsChecked = false;
        OnlyDownloadCheck.IsChecked = false;
        RefreshGrid();
        SetStatus("Список найденных треков очищен");
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetStatus("Подключаю SoundCloud…");

            var result = await SoundCloudAutoSetup.RunAsync(
                status => Dispatcher.Invoke(() => SetStatus(status)),
                CancellationToken.None);

            CredentialStore.WriteSecret(result.ClientSecret);
            _cfg.ClientId = result.ClientId;
            _cfg.Save();
            ClientIdBox.Text = result.ClientId;

            SetStatus("Проверяю API…");
            var client = new SoundCloudApiClient(result.ClientId, result.ClientSecret);
            await client.TestAsync(CancellationToken.None);

            UpdateConnectionState(true);
            SetStatus("SoundCloud подключён");

            MessageBox.Show(
                this,
                "SoundCloud подключён успешно.",
                "Release Radar",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Подключение отменено");
        }
        catch (Exception ex)
        {
            UpdateConnectionState(false);
            ShowError(ex.Message);
        }
    }

    private async void TestApi_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var client = ClientFromSettings();
            SetStatus("Проверяю API…");
            await client.TestAsync(CancellationToken.None);
            UpdateConnectionState(true);
            SetStatus("API подключён");

            MessageBox.Show(
                this,
                "API подключён успешно.",
                "Release Radar",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            UpdateConnectionState(false);
            ShowError(ex.Message);
        }
    }

    private void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                this,
                "Отключить SoundCloud и удалить сохранённый Client Secret?",
                "Подтверждение",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            CredentialStore.DeleteSecret();
            _cfg.ClientId = "";
            _cfg.Save();
            ClientIdBox.Text = "";
            UpdateConnectionState(false);
            SetStatus("SoundCloud отключён");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку для загрузок",
            InitialDirectory = DownloadDirBox.Text
        };

        if (dialog.ShowDialog(this) == true)
            DownloadDirBox.Text = dialog.FolderName;
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e) => SaveSettings();

    private async Task CheckNowAsync()
    {
        if (_busy) return;

        try
        {
            SaveSettings();

            var secret = CredentialStore.ReadSecret();
            if (string.IsNullOrWhiteSpace(_cfg.ClientId) || string.IsNullOrWhiteSpace(secret))
            {
                MessageBox.Show(
                    this,
                    "Откройте «Настройки» и подключите SoundCloud.",
                    "Release Radar",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            _busy = true;
            var client = new SoundCloudApiClient(_cfg.ClientId, secret);
            var from = DateTime.UtcNow.AddHours(-_cfg.LookbackHours);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var added = 0;
            var downloaded = 0;

            foreach (var genre in _cfg.Genres)
            {
                SetStatus($"Ищу: {genre}…");
                _cfg.GenreBpmRules.TryGetValue(genre, out var rule);

                var tracks = await client.SearchTracksAsync(
                    genre,
                    from,
                    rule?.From,
                    rule?.To,
                    100,
                    CancellationToken.None);

                foreach (var track in tracks)
                {
                    if (!seen.Add(track.Urn)) continue;
                    if (Contains(_cfg.BlacklistedArtists, track.Artist)) continue;

                    if (_store.Upsert(track))
                    {
                        added++;

                        if (_cfg.AutoDownload && track.Downloadable)
                        {
                            try
                            {
                                var path = await client.DownloadTrackAsync(
                                    track,
                                    _cfg.DownloadDir,
                                    CancellationToken.None);
                                _store.MarkDownloaded(track.Urn, path);
                                downloaded++;
                            }
                            catch
                            {
                            }
                        }
                    }
                }
            }

            RefreshGrid();
            SetStatus($"Новых: {added}" + (downloaded > 0 ? $" • скачано: {downloaded}" : ""));
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task PreviewAsync()
    {
        var track = TracksGrid.SelectedItem as TrackEntry;
        if (track is null) return;

        try
        {
            SetStatus("Готовлю preview…");
            var client = ClientFromSettings();
            var path = await client.DownloadPreviewAsync(track, CancellationToken.None);
            _player.Play(path);
            SetStatus($"Preview: {track.Artist} — {track.Title}");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async Task DownloadSelectedAsync()
    {
        var track = TracksGrid.SelectedItem as TrackEntry;
        if (track is null) return;

        try
        {
            SetStatus("Скачиваю…");
            var client = ClientFromSettings();
            var path = await client.DownloadTrackAsync(
                track,
                DownloadDirBox.Text.Trim(),
                CancellationToken.None);

            _store.MarkDownloaded(track.Urn, path);
            _lastDownloadedPath = path;
            DownloadToastText.Text = Path.GetFileName(path);
            DownloadToast.Visibility = Visibility.Visible;
            SetStatus($"Скачано: {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void OpenDownloadFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastDownloadedPath)) return;

        var directory = Path.GetDirectoryName(_lastDownloadedPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;

        Process.Start(new ProcessStartInfo(directory)
        {
            UseShellExecute = true
        });
    }

    private void CloseDownloadToast_Click(object sender, RoutedEventArgs e)
    {
        DownloadToast.Visibility = Visibility.Collapsed;
    }

    private void OpenSelected()
    {
        var track = TracksGrid.SelectedItem as TrackEntry;
        if (track is null || string.IsNullOrWhiteSpace(track.PermalinkUrl)) return;

        Process.Start(new ProcessStartInfo(track.PermalinkUrl)
        {
            UseShellExecute = true
        });
    }

    private void ToggleMonitoring()
    {
        if (_timer.IsEnabled)
        {
            _timer.Stop();
            MonitorButton.Content = "Мониторинг";
            SetStatus("Мониторинг выключен");
            return;
        }

        if (!int.TryParse(PollBox.Text, out var minutes))
            minutes = _cfg.PollMinutes;

        _timer.Interval = TimeSpan.FromMinutes(Math.Clamp(minutes, 1, 1440));
        _timer.Start();
        MonitorButton.Content = "Остановить";
        SetStatus("Мониторинг включён");
        _ = CheckNowAsync();
    }

    private void LoadSettings()
    {
        ClientIdBox.Text = _cfg.ClientId;
        GenresBox.Text = string.Join(", ", _cfg.Genres);
        PollBox.Text = _cfg.PollMinutes.ToString();
        LookbackBox.Text = _cfg.LookbackHours.ToString();
        DownloadDirBox.Text = _cfg.DownloadDir;
        AutoDownloadCheck.IsChecked = _cfg.AutoDownload;

        FavoritesBox.Text = string.Join(Environment.NewLine, _cfg.FavoriteArtists);
        BlacklistBox.Text = string.Join(Environment.NewLine, _cfg.BlacklistedArtists);
        BpmRulesBox.Text = string.Join(
            Environment.NewLine,
            _cfg.GenreBpmRules.Select(x => $"{x.Key}={x.Value.From}-{x.Value.To}"));
    }

    private void SaveSettings()
    {
        try
        {
            _cfg.ClientId = ClientIdBox.Text.Trim();
            _cfg.Genres = Csv(GenresBox.Text);
            _cfg.PollMinutes = ParseBoundedInt(PollBox.Text, 15, 1, 1440);
            _cfg.LookbackHours = ParseBoundedInt(LookbackBox.Text, 24, 1, 720);
            _cfg.DownloadDir = DownloadDirBox.Text.Trim();
            _cfg.AutoDownload = AutoDownloadCheck.IsChecked == true;
            _cfg.FavoriteArtists = Lines(FavoritesBox.Text);
            _cfg.BlacklistedArtists = Lines(BlacklistBox.Text);
            _cfg.GenreBpmRules = ParseRules(BpmRulesBox.Text);
            _cfg.Save();

            SetStatus("Настройки сохранены");
            RefreshGrid();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private SoundCloudApiClient ClientFromSettings()
    {
        var id = ClientIdBox.Text.Trim();
        var secret = CredentialStore.ReadSecret();

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Сначала подключите SoundCloud в настройках.");

        return new SoundCloudApiClient(id, secret);
    }

    private void RefreshGrid()
    {
        var query = SearchBox.Text?.Trim() ?? "";

        var rows = _store.All()
            .Where(t => !Contains(_cfg.BlacklistedArtists, t.Artist))
            .Where(t => OnlyFavoritesCheck.IsChecked != true || Contains(_cfg.FavoriteArtists, t.Artist))
            .Where(t => OnlyDownloadCheck.IsChecked != true || t.Downloadable)
            .Where(t => string.IsNullOrWhiteSpace(query) ||
                        t.Artist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        t.Genre.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        TracksGrid.ItemsSource = rows;
        DownloadButton.IsEnabled = (TracksGrid.SelectedItem as TrackEntry)?.Downloadable == true;
        TrackCountText.Text =
            $"{rows.Count} треков  •  {rows.Count(x => x.Downloadable)} доступны для официального скачивания";
    }

    private void UpdateConnectionState(bool? connected = null)
    {
        var ok = connected ?? (
            !string.IsNullOrWhiteSpace(_cfg.ClientId) &&
            SafeHasSecret());

        ConnectionBadge.Text = ok
            ? "●  SoundCloud подключён"
            : "●  SoundCloud не подключён";

        ConnectionBadge.Foreground = ok
            ? (System.Windows.Media.Brush)FindResource("Success")
            : (System.Windows.Media.Brush)FindResource("Danger");

        SettingsConnectionText.Text = ok
            ? "Подключено. Client Secret защищён Windows Credential Manager."
            : "Не подключено.";

        SettingsConnectionText.Foreground = ok
            ? (System.Windows.Media.Brush)FindResource("Success")
            : (System.Windows.Media.Brush)FindResource("Muted");
    }

    private static bool SafeHasSecret()
    {
        try
        {
            return !string.IsNullOrWhiteSpace(CredentialStore.ReadSecret());
        }
        catch
        {
            return false;
        }
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private void ShowError(string message)
    {
        SetStatus("Ошибка: " + (message.Length > 100 ? message[..100] + "…" : message));

        MessageBox.Show(
            this,
            message,
            "Release Radar",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private static int ParseBoundedInt(string value, int fallback, int min, int max) =>
        int.TryParse(value, out var n) ? Math.Clamp(n, min, max) : fallback;

    private static bool Contains(IEnumerable<string> list, string value) =>
        list.Any(x => string.Equals(
            x.Trim(),
            value.Trim(),
            StringComparison.OrdinalIgnoreCase));

    private static List<string> Csv(string value) =>
        value.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<string> Lines(string value) =>
        value.Split(
                new[] { "\r\n", "\n" },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static Dictionary<string, BpmRule> ParseRules(string value)
    {
        var result = new Dictionary<string, BpmRule>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in Lines(value))
        {
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var genre = line[..eq].Trim();
            var parts = line[(eq + 1)..]
                .Split('-', 2, StringSplitOptions.TrimEntries);

            if (parts.Length != 2) continue;

            int? from = int.TryParse(parts[0], out var a) ? a : null;
            int? to = int.TryParse(parts[1], out var b) ? b : null;

            if (from.HasValue && to.HasValue && from > to)
                (from, to) = (to, from);

            result[genre] = new BpmRule { From = from, To = to };
        }

        return result;
    }
}
