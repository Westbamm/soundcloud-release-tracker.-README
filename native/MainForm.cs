using System.Diagnostics;

namespace SoundCloudReleaseTracker;

public sealed class MainForm : Form
{
    private readonly AppConfig _cfg;
    private readonly TrackStore _store = new();
    private readonly MciPlayer _player = new();

    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoGenerateColumns = false };
    private readonly ToolStripStatusLabel _status = new("Готово");
    private readonly TextBox _clientId = new() { Width = 620, ReadOnly = true };
    private readonly TextBox _clientSecret = new() { Width = 620, UseSystemPasswordChar = true, ReadOnly = true };
    private readonly Label _loginStatus = new() { AutoSize = true, Text = "SoundCloud API: не подключено" };
    private readonly TextBox _genres = new() { Width = 620 };
    private readonly NumericUpDown _poll = new() { Minimum = 1, Maximum = 1440, Value = 15, Width = 100 };
    private readonly NumericUpDown _lookback = new() { Minimum = 1, Maximum = 720, Value = 24, Width = 100 };
    private readonly TextBox _downloadDir = new() { Width = 620 };
    private readonly CheckBox _autoDownload = new() { Text = "Автоскачивание только официально downloadable треков", AutoSize = true };
    private readonly TextBox _favorites = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly TextBox _blacklist = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly TextBox _bpmRules = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly CheckBox _onlyFavorites = new() { Text = "★ Любимые", AutoSize = true };
    private readonly CheckBox _onlyDownload = new() { Text = "Только download", AutoSize = true };
    private readonly System.Windows.Forms.Timer _timer = new();
    private bool _busy;

    public MainForm()
    {
        Text = "SoundCloud Release Tracker v5.4";
        Width = 1280;
        Height = 820;
        MinimumSize = new Size(1000, 650);

        _cfg = AppConfig.Load();
        AppConfig.TryMigrateLegacyPlaintextSecret();

        BuildUi();
        LoadSettings();
        RefreshGrid();

        _timer.Tick += async (_, _) => await CheckNowAsync();
        FormClosed += (_, _) => _player.Dispose();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "SoundCloud Release Tracker v5.2",
            AutoSize = true,
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            Padding = new Padding(10, 10, 10, 6)
        };
        root.Controls.Add(title, 0, 0);

        var tracksTab = new TabPage("Новые треки");
        var artistsTab = new TabPage("Артисты и BPM");
        var settingsTab = new TabPage("Настройки");
        _tabs.TabPages.AddRange(new[] { tracksTab, artistsTab, settingsTab });
        root.Controls.Add(_tabs, 0, 1);

        BuildTracksTab(tracksTab);
        BuildArtistsTab(artistsTab);
        BuildSettingsTab(settingsTab);

        var status = new StatusStrip();
        status.Items.Add(_status);
        root.Controls.Add(status, 0, 2);
        Controls.Add(root);
    }

    private void BuildTracksTab(TabPage tab)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(6) };
        Button Btn(string text, EventHandler handler)
        {
            var b = new Button { Text = text, AutoSize = true };
            b.Click += handler;
            return b;
        }

        bar.Controls.Add(Btn("Проверить сейчас", async (_, _) => await CheckNowAsync()));
        bar.Controls.Add(Btn("▶ Мониторинг", (_, _) => ToggleMonitoring()));
        bar.Controls.Add(Btn("▶ Preview", async (_, _) => await PreviewAsync()));
        bar.Controls.Add(Btn("■ Stop", (_, _) => { _player.Stop(); SetStatus("Preview остановлен"); }));
        bar.Controls.Add(Btn("Скачать", async (_, _) => await DownloadSelectedAsync()));
        bar.Controls.Add(Btn("Открыть SoundCloud", (_, _) => OpenSelected()));
        _onlyFavorites.CheckedChanged += (_, _) => RefreshGrid();
        _onlyDownload.CheckedChanged += (_, _) => RefreshGrid();
        bar.Controls.Add(_onlyFavorites);
        bar.Controls.Add(_onlyDownload);

        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Дата", DataPropertyName = "CreatedAt", Width = 150 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Артист", DataPropertyName = "Artist", Width = 190 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Название", DataPropertyName = "Title", Width = 360 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Жанр", DataPropertyName = "Genre", Width = 150 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "BPM", DataPropertyName = "Bpm", Width = 70 });
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Download", DataPropertyName = "Downloadable", Width = 80 });
        _grid.CellDoubleClick += async (_, _) => await PreviewAsync();

        panel.Controls.Add(bar, 0, 0);
        panel.Controls.Add(_grid, 0, 1);
        tab.Controls.Add(panel);
    }

    private void BuildArtistsTab(TabPage tab)
    {
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Padding = new Padding(8) };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.4f));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        split.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        split.Controls.Add(Box("Любимые артисты", _favorites), 0, 0);
        split.Controls.Add(Box("Blacklist", _blacklist), 1, 0);
        split.Controls.Add(Box("BPM-правила: Жанр=MIN-MAX", _bpmRules), 2, 0);

        var save = new Button { Text = "Сохранить списки и BPM", AutoSize = true, Margin = new Padding(8) };
        save.Click += (_, _) => SaveSettings();
        split.Controls.Add(save, 0, 1);
        split.SetColumnSpan(save, 3);
        tab.Controls.Add(split);
    }

    private static GroupBox Box(string title, Control child)
    {
        var box = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(8) };
        box.Controls.Add(child);
        return box;
    }

    private void BuildSettingsTab(TabPage tab)
    {
        var form = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(12) };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddRow(form, 0, "Client ID (автоматически)", _clientId);
        AddRow(form, 1, "Client Secret (защищён)", _clientSecret);
        AddRow(form, 2, "Жанры через запятую", _genres);
        AddRow(form, 3, "Проверять каждые, мин", _poll);
        AddRow(form, 4, "Искать за последние, ч", _lookback);
        AddRow(form, 5, "Папка загрузки", _downloadDir);

        var browse = new Button { Text = "Выбрать…" };
        browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { SelectedPath = _downloadDir.Text };
            if (dlg.ShowDialog(this) == DialogResult.OK) _downloadDir.Text = dlg.SelectedPath;
        };
        form.Controls.Add(browse, 2, 5);

        form.Controls.Add(_autoDownload, 1, 6);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var connect = new Button
        {
            Text = "Подключить SoundCloud автоматически",
            AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        connect.Click += async (_, _) => await AutoConnectSoundCloudAsync();
        var test = new Button { Text = "Проверить API", AutoSize = true };
        test.Click += async (_, _) => await TestApiAsync();
        var save = new Button { Text = "Сохранить настройки", AutoSize = true };
        save.Click += (_, _) => SaveSettings();
        var del = new Button { Text = "Удалить Client Secret", AutoSize = true };
        del.Click += (_, _) => DeleteSecret();
        buttons.Controls.AddRange(new Control[] { connect, test, save, del });
        form.Controls.Add(buttons, 1, 7);

        _loginStatus.ForeColor = Color.DimGray;
        form.Controls.Add(_loginStatus, 1, 8);

        var note = new Label
        {
            Text = "Рекомендуется кнопка «Подключить SoundCloud автоматически». Пароль вводится только на официальном сайте SoundCloud.\nClient Secret хранится в Windows Credential Manager и не записывается в config.json.",
            AutoSize = true,
            ForeColor = Color.DarkGreen,
            Padding = new Padding(0, 8, 0, 0)
        };
        form.Controls.Add(note, 1, 9);

        tab.Controls.Add(form);
    }

    private static void AddRow(TableLayoutPanel form, int row, string label, Control control)
    {
        form.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 12, 3) }, 0, row);
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        form.Controls.Add(control, 1, row);
    }

    private void LoadSettings()
    {
        _clientId.Text = _cfg.ClientId;
        try { _clientSecret.Text = CredentialStore.ReadSecret(); } catch { _clientSecret.Text = ""; }
        _genres.Text = string.Join(", ", _cfg.Genres);
        _poll.Value = Math.Clamp(_cfg.PollMinutes, 1, 1440);
        _lookback.Value = Math.Clamp(_cfg.LookbackHours, 1, 720);
        _downloadDir.Text = _cfg.DownloadDir;
        _autoDownload.Checked = _cfg.AutoDownload;
        _favorites.Text = string.Join(Environment.NewLine, _cfg.FavoriteArtists);
        _blacklist.Text = string.Join(Environment.NewLine, _cfg.BlacklistedArtists);
        _bpmRules.Text = string.Join(Environment.NewLine, _cfg.GenreBpmRules.Select(x => $"{x.Key}={x.Value.From}-{x.Value.To}"));
    }

    private void SaveSettings()
    {
        try
        {
            _cfg.ClientId = _clientId.Text.Trim();
            _cfg.Genres = Csv(_genres.Text);
            _cfg.PollMinutes = (int)_poll.Value;
            _cfg.LookbackHours = (int)_lookback.Value;
            _cfg.DownloadDir = _downloadDir.Text.Trim();
            _cfg.AutoDownload = _autoDownload.Checked;
            _cfg.FavoriteArtists = Lines(_favorites.Text);
            _cfg.BlacklistedArtists = Lines(_blacklist.Text);
            _cfg.GenreBpmRules = ParseRules(_bpmRules.Text);
            if (!string.IsNullOrWhiteSpace(_clientSecret.Text)) CredentialStore.WriteSecret(_clientSecret.Text);
            _cfg.Save();
            SetStatus("Настройки сохранены • Secret защищён Windows");
            RefreshGrid();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async Task AutoConnectSoundCloudAsync()
    {
        try
        {
            SetStatus("Подключаю SoundCloud…");
            var result = await SoundCloudAutoSetup.RunAsync(
                status => BeginInvoke(() => SetStatus(status)),
                CancellationToken.None);

            CredentialStore.WriteSecret(result.ClientSecret);
            _clientId.Text = result.ClientId;
            _clientSecret.Text = result.ClientSecret;
            _cfg.ClientId = result.ClientId;
            _cfg.Save();

            SetStatus("SoundCloud подключён • проверяю API…");
            var client = new SoundCloudApiClient(result.ClientId, result.ClientSecret);
            await client.TestAsync(CancellationToken.None);

            _loginStatus.Text = "SoundCloud API: подключено";
            _loginStatus.ForeColor = Color.DarkGreen;
            SetStatus("SoundCloud подключён");
            MessageBox.Show(
                this,
                "Готово. SoundCloud подключён автоматически.\n\n" +
                "Client Secret сохранён в Windows Credential Manager. " +
                "Пароль SoundCloud приложение не получает.",
                "SoundCloud",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Подключение отменено");
        }
        catch (Exception ex)
        {
            _loginStatus.Text = "SoundCloud API: не подключено";
            _loginStatus.ForeColor = Color.DarkRed;
            ShowError(ex.Message);
        }
    }

    private async Task TestApiAsync()
    {
        try
        {
            var client = ClientFromUi();
            SetStatus("Проверяю API…");
            await client.TestAsync(CancellationToken.None);
            SetStatus("API подключён");
            MessageBox.Show(this, "API подключён успешно.", "SoundCloud", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async Task CheckNowAsync()
    {
        if (_busy) return;
        try
        {
            SaveSettings();
            var secret = CredentialStore.ReadSecret();
            if (string.IsNullOrWhiteSpace(_cfg.ClientId) || string.IsNullOrWhiteSpace(secret))
            {
                MessageBox.Show(this, "Введите Client ID и Client Secret во вкладке «Настройки».", "SoundCloud API",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                var tracks = await client.SearchTracksAsync(genre, from, rule?.From, rule?.To, 100, CancellationToken.None);
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
                                var path = await client.DownloadTrackAsync(track, _cfg.DownloadDir, CancellationToken.None);
                                _store.MarkDownloaded(track.Urn, path);
                                downloaded++;
                            }
                            catch { }
                        }
                    }
                }
            }

            RefreshGrid();
            SetStatus($"Новых: {added}" + (downloaded > 0 ? $" • скачано: {downloaded}" : ""));
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; }
    }

    private async Task PreviewAsync()
    {
        var track = SelectedTrack();
        if (track is null) return;
        try
        {
            SetStatus("Готовлю preview…");
            var client = ClientFromUi();
            var path = await client.DownloadPreviewAsync(track, CancellationToken.None);
            _player.Play(path);
            SetStatus("Preview играет");
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async Task DownloadSelectedAsync()
    {
        var track = SelectedTrack();
        if (track is null) return;
        try
        {
            var client = ClientFromUi();
            SetStatus("Скачиваю…");
            var path = await client.DownloadTrackAsync(track, _downloadDir.Text.Trim(), CancellationToken.None);
            _store.MarkDownloaded(track.Urn, path);
            SetStatus("Трек скачан");
            MessageBox.Show(this, $"Сохранено:\n{path}", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void OpenSelected()
    {
        var track = SelectedTrack();
        if (track is null || string.IsNullOrWhiteSpace(track.PermalinkUrl)) return;
        Process.Start(new ProcessStartInfo(track.PermalinkUrl) { UseShellExecute = true });
    }

    private void ToggleMonitoring()
    {
        _timer.Enabled = !_timer.Enabled;
        _timer.Interval = (int)_poll.Value * 60 * 1000;
        SetStatus(_timer.Enabled ? "Мониторинг включён" : "Мониторинг выключен");
        if (_timer.Enabled) _ = CheckNowAsync();
    }

    private SoundCloudApiClient ClientFromUi()
    {
        var id = _clientId.Text.Trim();
        var secret = _clientSecret.Text.Trim();
        if (string.IsNullOrWhiteSpace(secret))
        {
            try { secret = CredentialStore.ReadSecret(); } catch { }
        }
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException(
                "Нажмите «Подключить SoundCloud автоматически» в настройках.");

        return new SoundCloudApiClient(id, secret);
    }

    private void DeleteSecret()
    {
        if (MessageBox.Show(this, "Удалить Client Secret из Windows Credential Manager?", "Подтверждение",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            CredentialStore.DeleteSecret();
            _clientSecret.Clear();
            SetStatus("Client Secret удалён");
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void RefreshGrid()
    {
        var rows = _store.All()
            .Where(t => !Contains(_cfg.BlacklistedArtists, t.Artist))
            .Where(t => !_onlyFavorites.Checked || Contains(_cfg.FavoriteArtists, t.Artist))
            .Where(t => !_onlyDownload.Checked || t.Downloadable)
            .ToList();
        _grid.DataSource = rows;
    }

    private TrackEntry? SelectedTrack() =>
        _grid.CurrentRow?.DataBoundItem as TrackEntry;

    private void SetStatus(string text) => _status.Text = text;

    private void ShowError(string message)
    {
        SetStatus("Ошибка: " + (message.Length > 100 ? message[..100] + "…" : message));
        MessageBox.Show(this, message, "SoundCloud", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private static bool Contains(IEnumerable<string> list, string value) =>
        list.Any(x => string.Equals(x.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase));

    private static List<string> Csv(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static List<string> Lines(string value) =>
        value.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static Dictionary<string, BpmRule> ParseRules(string value)
    {
        var result = new Dictionary<string, BpmRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in Lines(value))
        {
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var genre = line[..eq].Trim();
            var parts = line[(eq + 1)..].Split('-', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2) continue;
            int? from = int.TryParse(parts[0], out var a) ? a : null;
            int? to = int.TryParse(parts[1], out var b) ? b : null;
            if (from.HasValue && to.HasValue && from > to) (from, to) = (to, from);
            result[genre] = new BpmRule { From = from, To = to };
        }
        return result;
    }
}
