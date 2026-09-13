using System.Diagnostics;

namespace SoundCloudReleaseTracker;

public sealed class MainForm : Form
{
    private readonly AppConfig _cfg;
    private readonly TrackStore _store = new();
    private readonly MciPlayer _player = new();

    private static readonly Color Bg = Color.FromArgb(18, 20, 24);
    private static readonly Color NavBg = Color.FromArgb(23, 26, 32);
    private static readonly Color Surface = Color.FromArgb(28, 32, 39);
    private static readonly Color SurfaceAlt = Color.FromArgb(34, 39, 48);
    private static readonly Color Border = Color.FromArgb(52, 59, 70);
    private static readonly Color TextPrimary = Color.FromArgb(244, 246, 249);
    private static readonly Color TextSecondary = Color.FromArgb(169, 178, 190);
    private static readonly Color Accent = Color.FromArgb(255, 85, 0);
    private static readonly Color AccentHover = Color.FromArgb(255, 110, 35);
    private static readonly Color Success = Color.FromArgb(57, 201, 133);
    private static readonly Color Danger = Color.FromArgb(235, 91, 91);

    private readonly Panel _tracksPage = new() { Dock = DockStyle.Fill, BackColor = Bg };
    private readonly Panel _artistsPage = new() { Dock = DockStyle.Fill, BackColor = Bg, Visible = false };
    private readonly Panel _settingsPage = new() { Dock = DockStyle.Fill, BackColor = Bg, Visible = false };

    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AutoGenerateColumns = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        RowHeadersVisible = false,
        BorderStyle = BorderStyle.None,
        BackgroundColor = Surface,
        GridColor = Border
    };

    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Text = "Готово",
        ForeColor = TextSecondary,
        Font = new Font("Segoe UI", 9)
    };

    private readonly Label _connectionBadge = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleRight,
        Text = "SoundCloud не подключён",
        ForeColor = Danger,
        Font = new Font("Segoe UI Semibold", 9.5f)
    };

    private readonly Label _trackCount = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = TextSecondary,
        Font = new Font("Segoe UI", 9)
    };

    private readonly TextBox _clientId = new() { ReadOnly = true };
    private readonly TextBox _genres = new();
    private readonly NumericUpDown _poll = new() { Minimum = 1, Maximum = 1440, Value = 15 };
    private readonly NumericUpDown _lookback = new() { Minimum = 1, Maximum = 720, Value = 24 };
    private readonly TextBox _downloadDir = new();
    private readonly CheckBox _autoDownload = new()
    {
        Text = "Автоматически скачивать только официально downloadable треки",
        AutoSize = true
    };

    private readonly TextBox _favorites = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly TextBox _blacklist = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly TextBox _bpmRules = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };

    private readonly CheckBox _onlyFavorites = new() { Text = "Только любимые", AutoSize = true };
    private readonly CheckBox _onlyDownload = new() { Text = "Только download", AutoSize = true };
    private readonly TextBox _searchBox = new() { PlaceholderText = "Поиск по артисту, названию или жанру…" };

    private readonly Button _monitorButton = new();
    private Button? _navTracks;
    private Button? _navArtists;
    private Button? _navSettings;
    private Label? _settingsConnectionStatus;

    private readonly System.Windows.Forms.Timer _timer = new();
    private bool _busy;

    public MainForm()
    {
        Text = "Release Radar v6.1";
        Width = 1380;
        Height = 860;
        MinimumSize = new Size(1080, 680);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Bg;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 10);

        _cfg = AppConfig.Load();
        AppConfig.TryMigrateLegacyPlaintextSecret();

        BuildUi();
        LoadSettings();
        RefreshGrid();
        UpdateConnectionBadge();

        _timer.Tick += async (_, _) => await CheckNowAsync();
        FormClosed += (_, _) => _player.Dispose();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            BackColor = Bg,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildBody(), 0, 1);
        root.Controls.Add(BuildFooter(), 0, 2);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = Bg,
            Padding = new Padding(24, 12, 24, 8)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, BackColor = Bg };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 38));

        left.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Release Radar",
            TextAlign = ContentAlignment.BottomLeft,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI Semibold", 21, FontStyle.Bold)
        }, 0, 0);

        left.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "SoundCloud release tracker  •  v6.1",
            TextAlign = ContentAlignment.TopLeft,
            ForeColor = TextSecondary,
            Font = new Font("Segoe UI", 9)
        }, 0, 1);

        header.Controls.Add(left, 0, 0);
        header.Controls.Add(_connectionBadge, 1, 0);
        return header;
    }

    private Control BuildBody()
    {
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = Bg,
            Padding = new Padding(18, 0, 18, 12)
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        body.Controls.Add(BuildSidebar(), 0, 0);

        var host = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Bg,
            Padding = new Padding(18, 0, 0, 0)
        };

        BuildTracksPage();
        BuildArtistsPage();
        BuildSettingsPage();

        host.Controls.Add(_settingsPage);
        host.Controls.Add(_artistsPage);
        host.Controls.Add(_tracksPage);

        body.Controls.Add(host, 1, 0);
        return body;
    }

    private Control BuildSidebar()
    {
        var sidebar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 5,
            BackColor = NavBg,
            Padding = new Padding(10, 14, 10, 14)
        };
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));

        _navTracks = NavButton("Новые треки", true, (_, _) => ShowPage(_tracksPage, _navTracks!));
        _navArtists = NavButton("Артисты и BPM", false, (_, _) => ShowPage(_artistsPage, _navArtists!));
        _navSettings = NavButton("Настройки", false, (_, _) => ShowPage(_settingsPage, _navSettings!));

        sidebar.Controls.Add(_navTracks, 0, 0);
        sidebar.Controls.Add(_navArtists, 0, 1);
        sidebar.Controls.Add(_navSettings, 0, 2);

        sidebar.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Мониторинг новых релизов\nпо жанрам и BPM",
            TextAlign = ContentAlignment.BottomLeft,
            ForeColor = TextSecondary,
            Font = new Font("Segoe UI", 8.5f),
            Padding = new Padding(8, 0, 0, 6)
        }, 0, 4);

        return sidebar;
    }

    private Button NavButton(string text, bool active, EventHandler handler)
    {
        var b = new Button
        {
            Dock = DockStyle.Fill,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 0, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = active ? SurfaceAlt : NavBg,
            ForeColor = active ? TextPrimary : TextSecondary,
            Font = new Font("Segoe UI Semibold", 10),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 0, 4)
        };
        b.FlatAppearance.BorderSize = 0;
        b.Click += handler;
        return b;
    }

    private void ShowPage(Panel page, Button active)
    {
        _tracksPage.Visible = false;
        _artistsPage.Visible = false;
        _settingsPage.Visible = false;
        page.Visible = true;
        page.BringToFront();

        foreach (var b in new[] { _navTracks, _navArtists, _navSettings })
        {
            if (b is null) continue;
            var selected = ReferenceEquals(b, active);
            b.BackColor = selected ? SurfaceAlt : NavBg;
            b.ForeColor = selected ? TextPrimary : TextSecondary;
        }
    }

    private void BuildTracksPage()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 1,
            BackColor = Bg
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(BuildPageTitle("Новые треки", _trackCount), 0, 0);
        layout.Controls.Add(BuildTrackActions(), 0, 1);
        layout.Controls.Add(BuildTrackFilters(), 0, 2);

        var gridCard = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Padding = new Padding(1),
            Margin = new Padding(0, 8, 0, 0)
        };
        ConfigureGrid();
        gridCard.Controls.Add(_grid);
        layout.Controls.Add(gridCard, 0, 3);

        _tracksPage.Controls.Add(layout);
    }

    private Control BuildPageTitle(string title, Label subtitle)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            BackColor = Bg
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 40));

        panel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = title,
            TextAlign = ContentAlignment.BottomLeft,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI Semibold", 17, FontStyle.Bold)
        }, 0, 0);

        panel.Controls.Add(subtitle, 0, 1);
        return panel;
    }

    private Control BuildTrackActions()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            BackColor = Bg,
            Margin = Padding.Empty
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        table.Controls.Add(ActionButton("Проверить сейчас", true, async (_, _) => await CheckNowAsync()), 0, 0);

        _monitorButton.Text = "Мониторинг";
        StyleButton(_monitorButton, false);
        _monitorButton.Click += (_, _) => ToggleMonitoring();
        table.Controls.Add(_monitorButton, 1, 0);

        table.Controls.Add(ActionButton("Preview", false, async (_, _) => await PreviewAsync()), 2, 0);
        table.Controls.Add(ActionButton("Stop", false, (_, _) =>
        {
            _player.Stop();
            SetStatus("Preview остановлен");
        }), 3, 0);
        table.Controls.Add(ActionButton("Скачать", false, async (_, _) => await DownloadSelectedAsync()), 4, 0);
        table.Controls.Add(ActionButton("Открыть в SoundCloud", false, (_, _) => OpenSelected()), 5, 0);

        return table;
    }

    private Control BuildTrackFilters()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            BackColor = Bg
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 270));

        StyleCheck(_onlyFavorites);
        StyleCheck(_onlyDownload);
        StyleTextBox(_searchBox);

        _onlyFavorites.CheckedChanged += (_, _) => RefreshGrid();
        _onlyDownload.CheckedChanged += (_, _) => RefreshGrid();
        _searchBox.TextChanged += (_, _) => RefreshGrid();

        table.Controls.Add(_onlyFavorites, 0, 0);
        table.Controls.Add(_onlyDownload, 1, 0);
        table.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Bg }, 2, 0);

        _searchBox.Dock = DockStyle.Fill;
        _searchBox.Margin = new Padding(0, 8, 0, 8);
        table.Controls.Add(_searchBox, 3, 0);

        return table;
    }

    private void ConfigureGrid()
    {
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.ColumnHeadersHeight = 42;
        _grid.RowTemplate.Height = 38;

        _grid.DefaultCellStyle.BackColor = Surface;
        _grid.DefaultCellStyle.ForeColor = TextPrimary;
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(92, 49, 28);
        _grid.DefaultCellStyle.SelectionForeColor = Color.White;
        _grid.DefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
        _grid.DefaultCellStyle.Font = new Font("Segoe UI", 9.5f);

        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(31, 35, 43);

        _grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceAlt;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = TextSecondary;
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9);
        _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 8, 0);

        _grid.Columns.Clear();
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ДАТА", DataPropertyName = "CreatedAt", Width = 150 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "АРТИСТ", DataPropertyName = "Artist", Width = 200 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "НАЗВАНИЕ",
            DataPropertyName = "Title",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 320
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ЖАНР", DataPropertyName = "Genre", Width = 140 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "BPM", DataPropertyName = "Bpm", Width = 70 });
        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "DOWNLOAD",
            DataPropertyName = "Downloadable",
            Width = 95,
            FlatStyle = FlatStyle.Flat
        });

        _grid.CellDoubleClick += async (_, _) => await PreviewAsync();
    }

    private void BuildArtistsPage()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            BackColor = Bg
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));

        var subtitle = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Любимые артисты, blacklist и BPM-правила",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextSecondary
        };
        layout.Controls.Add(BuildPageTitle("Артисты и BPM", subtitle), 0, 0);

        var editors = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            BackColor = Bg,
            Padding = new Padding(0, 8, 0, 0)
        };
        editors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        editors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        editors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334f));

        StyleEditor(_favorites);
        StyleEditor(_blacklist);
        StyleEditor(_bpmRules);

        editors.Controls.Add(EditorCard("Любимые артисты", "Один артист на строку", _favorites), 0, 0);
        editors.Controls.Add(EditorCard("Blacklist", "Не показывать этих артистов", _blacklist), 1, 0);
        editors.Controls.Add(EditorCard("BPM-правила", "Пример: House=122-132", _bpmRules), 2, 0);

        layout.Controls.Add(editors, 0, 1);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Bg };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        bottom.Controls.Add(ActionButton("Сохранить изменения", true, (_, _) => SaveSettings()), 1, 0);
        layout.Controls.Add(bottom, 0, 2);

        _artistsPage.Controls.Add(layout);
    }

    private Control EditorCard(string title, string hint, TextBox editor)
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            BackColor = Surface,
            Padding = new Padding(16),
            Margin = new Padding(0, 0, 12, 0)
        };
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        card.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = title,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI Semibold", 11)
        }, 0, 0);

        card.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = hint,
            TextAlign = ContentAlignment.TopLeft,
            ForeColor = TextSecondary,
            Font = new Font("Segoe UI", 8.5f)
        }, 0, 1);

        editor.Margin = new Padding(0, 8, 0, 0);
        card.Controls.Add(editor, 0, 2);
        return card;
    }

    private void BuildSettingsPage()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 1,
            BackColor = Bg
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 330));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var subtitle = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Подключение SoundCloud и параметры мониторинга",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextSecondary
        };
        layout.Controls.Add(BuildPageTitle("Настройки", subtitle), 0, 0);
        layout.Controls.Add(BuildConnectionCard(), 0, 1);
        layout.Controls.Add(BuildMonitoringCard(), 0, 2);

        _settingsPage.Controls.Add(layout);
    }

    private Control BuildConnectionCard()
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 3,
            BackColor = Surface,
            Padding = new Padding(18),
            Margin = new Padding(0, 8, 0, 10)
        };
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));

        card.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Подключение SoundCloud",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI Semibold", 11)
        }, 0, 0);
        card.SetColumnSpan(card.GetControlFromPosition(0, 0)!, 3);

        _settingsConnectionStatus = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextSecondary,
            Font = new Font("Segoe UI", 9)
        };
        card.Controls.Add(_settingsConnectionStatus, 0, 1);
        card.SetColumnSpan(_settingsConnectionStatus, 3);

        StyleTextBox(_clientId);
        _clientId.Dock = DockStyle.Fill;
        _clientId.Margin = new Padding(0, 4, 12, 4);
        _clientId.PlaceholderText = "Client ID появится автоматически";
        card.Controls.Add(_clientId, 0, 2);

        card.Controls.Add(ActionButton("Подключить", true, async (_, _) => await AutoConnectSoundCloudAsync()), 1, 2);
        card.Controls.Add(ActionButton("Проверить API", false, async (_, _) => await TestApiAsync()), 2, 2);

        card.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Пароль вводится только на официальном сайте. Client Secret хранится в Windows Credential Manager.",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextSecondary,
            Font = new Font("Segoe UI", 8.5f)
        }, 0, 3);

        var disconnect = ActionButton("Отключить", false, (_, _) => DeleteSecret());
        disconnect.Dock = DockStyle.Right;
        card.Controls.Add(disconnect, 2, 3);

        return card;
    }

    private Control BuildMonitoringCard()
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 7,
            ColumnCount = 3,
            BackColor = Surface,
            Padding = new Padding(18),
            Margin = new Padding(0, 0, 0, 10)
        };

        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));

        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        card.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Мониторинг",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI Semibold", 11)
        }, 0, 0);
        card.SetColumnSpan(card.GetControlFromPosition(0, 0)!, 3);

        AddSettingRow(card, 1, "Жанры", _genres, "");
        AddSettingRow(card, 2, "Проверять каждые", _poll, "мин");
        AddSettingRow(card, 3, "Искать за последние", _lookback, "часов");
        AddSettingRow(card, 4, "Папка загрузки", _downloadDir, "");

        var browse = ActionButton("Выбрать…", false, (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { SelectedPath = _downloadDir.Text };
            if (dlg.ShowDialog(this) == DialogResult.OK) _downloadDir.Text = dlg.SelectedPath;
        });
        card.Controls.Add(browse, 2, 4);

        StyleCheck(_autoDownload);
        _autoDownload.Dock = DockStyle.Fill;
        _autoDownload.Margin = new Padding(0);
        card.Controls.Add(_autoDownload, 1, 5);
        card.SetColumnSpan(_autoDownload, 2);

        var save = ActionButton("Сохранить настройки", true, (_, _) => SaveSettings());
        save.Dock = DockStyle.Right;
        card.Controls.Add(save, 2, 6);

        return card;
    }

    private void AddSettingRow(TableLayoutPanel table, int row, string label, Control control, string suffix)
    {
        table.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = label,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextSecondary
        }, 0, row);

        if (control is TextBox tb) StyleTextBox(tb);
        if (control is NumericUpDown nud) StyleNumeric(nud);

        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 6, 10, 6);
        table.Controls.Add(control, 1, row);

        if (!string.IsNullOrWhiteSpace(suffix))
        {
            table.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = suffix,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = TextSecondary
            }, 2, row);
        }
    }

    private Control BuildFooter()
    {
        var footer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = NavBg,
            Padding = new Padding(22, 0, 22, 0)
        };
        footer.Controls.Add(_status);
        return footer;
    }

    private Button ActionButton(string text, bool primary, EventHandler handler)
    {
        var b = new Button { Text = text, Dock = DockStyle.Fill };
        StyleButton(b, primary);
        b.Click += handler;
        return b;
    }

    private void StyleButton(Button b, bool primary)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = primary ? 0 : 1;
        b.FlatAppearance.BorderColor = Border;
        b.BackColor = primary ? Accent : SurfaceAlt;
        b.ForeColor = TextPrimary;
        b.Font = new Font("Segoe UI Semibold", 9.5f);
        b.Cursor = Cursors.Hand;
        b.Margin = new Padding(0, 5, 8, 5);

        b.MouseEnter += (_, _) => b.BackColor = primary ? AccentHover : Color.FromArgb(41, 47, 57);
        b.MouseLeave += (_, _) => b.BackColor = primary ? Accent : SurfaceAlt;
    }

    private void StyleTextBox(TextBox box)
    {
        box.BackColor = SurfaceAlt;
        box.ForeColor = TextPrimary;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = new Font("Segoe UI", 9.5f);
    }

    private void StyleNumeric(NumericUpDown box)
    {
        box.BackColor = SurfaceAlt;
        box.ForeColor = TextPrimary;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = new Font("Segoe UI", 9.5f);
    }

    private void StyleEditor(TextBox box)
    {
        box.BackColor = SurfaceAlt;
        box.ForeColor = TextPrimary;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = new Font("Segoe UI", 10);
    }

    private void StyleCheck(CheckBox box)
    {
        box.ForeColor = TextPrimary;
        box.BackColor = Color.Transparent;
        box.Font = new Font("Segoe UI", 9);
        box.Margin = Padding.Empty;
    }

    private void LoadSettings()
    {
        _clientId.Text = _cfg.ClientId;
        _genres.Text = string.Join(", ", _cfg.Genres);
        _poll.Value = Math.Clamp(_cfg.PollMinutes, 1, 1440);
        _lookback.Value = Math.Clamp(_cfg.LookbackHours, 1, 720);
        _downloadDir.Text = _cfg.DownloadDir;
        _autoDownload.Checked = _cfg.AutoDownload;
        _favorites.Text = string.Join(Environment.NewLine, _cfg.FavoriteArtists);
        _blacklist.Text = string.Join(Environment.NewLine, _cfg.BlacklistedArtists);
        _bpmRules.Text = string.Join(Environment.NewLine,
            _cfg.GenreBpmRules.Select(x => $"{x.Key}={x.Value.From}-{x.Value.To}"));
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
            _cfg.Save();

            SetStatus("Настройки сохранены");
            RefreshGrid();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
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
            _cfg.ClientId = result.ClientId;
            _cfg.Save();

            var client = new SoundCloudApiClient(result.ClientId, result.ClientSecret);
            SetStatus("Проверяю API…");
            await client.TestAsync(CancellationToken.None);

            UpdateConnectionBadge(true);
            SetStatus("SoundCloud подключён");
            MessageBox.Show(this, "SoundCloud подключён успешно.", "Готово",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Подключение отменено");
        }
        catch (Exception ex)
        {
            UpdateConnectionBadge(false);
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
            UpdateConnectionBadge(true);
            SetStatus("API подключён");
            MessageBox.Show(this, "API подключён успешно.", "SoundCloud",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            UpdateConnectionBadge(false);
            ShowError(ex.Message);
        }
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
                MessageBox.Show(this,
                    "Откройте «Настройки» и нажмите «Подключить».",
                    "SoundCloud API",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
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
                    genre, from, rule?.From, rule?.To, 100, CancellationToken.None);

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
                                    track, _cfg.DownloadDir, CancellationToken.None);
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
        var track = SelectedTrack();
        if (track is null) return;

        try
        {
            SetStatus("Готовлю preview…");
            var client = ClientFromUi();
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
        var track = SelectedTrack();
        if (track is null) return;

        try
        {
            SetStatus("Скачиваю…");
            var client = ClientFromUi();
            var path = await client.DownloadTrackAsync(
                track, _downloadDir.Text.Trim(), CancellationToken.None);
            _store.MarkDownloaded(track.Urn, path);

            SetStatus("Трек скачан");
            MessageBox.Show(this, $"Сохранено:\n{path}", "Готово",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void OpenSelected()
    {
        var track = SelectedTrack();
        if (track is null || string.IsNullOrWhiteSpace(track.PermalinkUrl)) return;

        Process.Start(new ProcessStartInfo(track.PermalinkUrl)
        {
            UseShellExecute = true
        });
    }

    private void ToggleMonitoring()
    {
        _timer.Enabled = !_timer.Enabled;
        _timer.Interval = (int)_poll.Value * 60 * 1000;

        _monitorButton.Text = _timer.Enabled ? "Остановить" : "Мониторинг";
        SetStatus(_timer.Enabled ? "Мониторинг включён" : "Мониторинг выключен");

        if (_timer.Enabled)
            _ = CheckNowAsync();
    }

    private SoundCloudApiClient ClientFromUi()
    {
        var id = _clientId.Text.Trim();
        var secret = "";

        try
        {
            secret = CredentialStore.ReadSecret();
        }
        catch
        {
        }

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Сначала подключите SoundCloud в настройках.");

        return new SoundCloudApiClient(id, secret);
    }

    private void DeleteSecret()
    {
        if (MessageBox.Show(this,
                "Отключить SoundCloud и удалить сохранённый Client Secret?",
                "Подтверждение",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        try
        {
            CredentialStore.DeleteSecret();
            _cfg.ClientId = "";
            _cfg.Save();
            _clientId.Clear();
            UpdateConnectionBadge(false);
            SetStatus("SoundCloud отключён");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void UpdateConnectionBadge(bool? connected = null)
    {
        var isConnected = connected ?? (
            !string.IsNullOrWhiteSpace(_cfg.ClientId) &&
            SafeHasSecret());

        _connectionBadge.Text = isConnected
            ? "●  SoundCloud подключён"
            : "●  SoundCloud не подключён";
        _connectionBadge.ForeColor = isConnected ? Success : Danger;

        if (_settingsConnectionStatus is not null)
        {
            _settingsConnectionStatus.Text = isConnected
                ? "Подключено. Client Secret хранится в Windows Credential Manager."
                : "Не подключено.";
            _settingsConnectionStatus.ForeColor = isConnected ? Success : TextSecondary;
        }
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

    private void RefreshGrid()
    {
        var query = _searchBox.Text.Trim();

        var rows = _store.All()
            .Where(t => !Contains(_cfg.BlacklistedArtists, t.Artist))
            .Where(t => !_onlyFavorites.Checked || Contains(_cfg.FavoriteArtists, t.Artist))
            .Where(t => !_onlyDownload.Checked || t.Downloadable)
            .Where(t => string.IsNullOrWhiteSpace(query) ||
                        t.Artist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        t.Genre.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        _grid.DataSource = rows;

        var downloadable = rows.Count(x => x.Downloadable);
        _trackCount.Text = $"{rows.Count} треков  •  {downloadable} доступны для официального скачивания";
    }

    private TrackEntry? SelectedTrack() =>
        _grid.CurrentRow?.DataBoundItem as TrackEntry;

    private void SetStatus(string text) =>
        _status.Text = text;

    private void ShowError(string message)
    {
        SetStatus("Ошибка: " + (message.Length > 100 ? message[..100] + "…" : message));
        MessageBox.Show(this, message, "Release Radar",
            MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private static bool Contains(IEnumerable<string> list, string value) =>
        list.Any(x => string.Equals(
            x.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase));

    private static List<string> Csv(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<string> Lines(string value) =>
        value.Split(new[] { "\r\n", "\n" },
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
            var parts = line[(eq + 1)..].Split('-', 2, StringSplitOptions.TrimEntries);
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
