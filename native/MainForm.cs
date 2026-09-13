using System.Diagnostics;

namespace SoundCloudReleaseTracker;

public sealed class MainForm : Form
{
    private readonly AppConfig _cfg;
    private readonly TrackStore _store = new();
    private readonly MciPlayer _player = new();

    private static readonly Color Bg = Color.FromArgb(15, 17, 21);
    private static readonly Color Surface = Color.FromArgb(23, 26, 33);
    private static readonly Color Surface2 = Color.FromArgb(29, 33, 42);
    private static readonly Color Border = Color.FromArgb(48, 54, 65);
    private static readonly Color TextPrimary = Color.FromArgb(244, 246, 248);
    private static readonly Color TextMuted = Color.FromArgb(153, 163, 177);
    private static readonly Color Accent = Color.FromArgb(255, 85, 0);
    private static readonly Color AccentHover = Color.FromArgb(255, 108, 31);
    private static readonly Color Success = Color.FromArgb(58, 203, 132);
    private static readonly Color Danger = Color.FromArgb(238, 91, 91);

    private readonly Panel _contentHost = new() { Dock = DockStyle.Fill };
    private readonly Panel _tracksPage = new() { Dock = DockStyle.Fill };
    private readonly Panel _artistsPage = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly Panel _settingsPage = new() { Dock = DockStyle.Fill, Visible = false };

    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoGenerateColumns = false,
        MultiSelect = false,
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
        AutoSize = true,
        Text = "Готово",
        ForeColor = TextMuted,
        Font = new Font("Segoe UI", 9)
    };

    private readonly Label _trackCount = new()
    {
        AutoSize = true,
        ForeColor = TextMuted,
        Font = new Font("Segoe UI", 9)
    };

    private readonly Label _connectionBadge = new()
    {
        AutoSize = true,
        Text = "●  SoundCloud не подключён",
        ForeColor = Danger,
        Font = new Font("Segoe UI Semibold", 9)
    };

    private readonly TextBox _clientId = new()
    {
        ReadOnly = true,
        BorderStyle = BorderStyle.FixedSingle
    };

    private readonly TextBox _genres = new();
    private readonly NumericUpDown _poll = new() { Minimum = 1, Maximum = 1440, Value = 15 };
    private readonly NumericUpDown _lookback = new() { Minimum = 1, Maximum = 720, Value = 24 };
    private readonly TextBox _downloadDir = new();
    private readonly CheckBox _autoDownload = new()
    {
        Text = "Автоматически скачивать только официально downloadable треки",
        AutoSize = true
    };

    private readonly TextBox _favorites = new()
    {
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None
    };

    private readonly TextBox _blacklist = new()
    {
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None
    };

    private readonly TextBox _bpmRules = new()
    {
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None
    };

    private readonly CheckBox _onlyFavorites = new() { Text = "★ Любимые", AutoSize = true };
    private readonly CheckBox _onlyDownload = new() { Text = "Только download", AutoSize = true };
    private readonly TextBox _searchBox = new()
    {
        Width = 240,
        PlaceholderText = "Поиск по артисту или названию…"
    };

    private readonly Button _monitorButton = new();
    private Button? _navTracks;
    private Button? _navArtists;
    private Button? _navSettings;

    private readonly System.Windows.Forms.Timer _timer = new();
    private bool _busy;

    public MainForm()
    {
        Text = "Release Radar v6.0";
        Width = 1440;
        Height = 900;
        MinimumSize = new Size(1100, 700);
        BackColor = Bg;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterScreen;

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
        SuspendLayout();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            BackColor = Bg,
            Padding = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildBody(), 0, 1);
        root.Controls.Add(BuildFooter(), 0, 2);

        Controls.Add(root);
        ResumeLayout(true);
    }

    private Control BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Bg,
            Padding = new Padding(28, 18, 28, 12)
        };

        var title = new Label
        {
            Text = "Release Radar",
            AutoSize = true,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI Semibold", 24, FontStyle.Bold),
            Location = new Point(28, 16)
        };

        var subtitle = new Label
        {
            Text = "SoundCloud release tracker  •  v6.0",
            AutoSize = true,
            ForeColor = TextMuted,
            Font = new Font("Segoe UI", 9.5f),
            Location = new Point(31, 58)
        };

        _connectionBadge.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _connectionBadge.Location = new Point(Width - 280, 34);
        header.Resize += (_, _) =>
            _connectionBadge.Location = new Point(Math.Max(700, header.ClientSize.Width - _connectionBadge.Width - 30), 34);

        header.Controls.Add(title);
        header.Controls.Add(subtitle);
        header.Controls.Add(_connectionBadge);
        return header;
    }

    private Control BuildBody()
    {
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = Bg,
            Padding = new Padding(18, 0, 18, 14)
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        body.Controls.Add(BuildSidebar(), 0, 0);

        _contentHost.BackColor = Bg;
        _contentHost.Padding = new Padding(14, 0, 0, 0);

        BuildTracksPage();
        BuildArtistsPage();
        BuildSettingsPage();

        _contentHost.Controls.Add(_settingsPage);
        _contentHost.Controls.Add(_artistsPage);
        _contentHost.Controls.Add(_tracksPage);

        body.Controls.Add(_contentHost, 1, 0);
        return body;
    }

    private Control BuildSidebar()
    {
        var sidebar = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Padding = new Padding(10, 16, 10, 16)
        };

        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            BackColor = Surface
        };

        _navTracks = NavButton("  Новые треки", true, (_, _) => ShowPage(_tracksPage, _navTracks!));
        _navArtists = NavButton("  Артисты и BPM", false, (_, _) => ShowPage(_artistsPage, _navArtists!));
        _navSettings = NavButton("  Настройки", false, (_, _) => ShowPage(_settingsPage, _navSettings!));

        stack.Controls.Add(_navTracks);
        stack.Controls.Add(_navArtists);
        stack.Controls.Add(_navSettings);

        var hint = new Label
        {
            Text = "Автоматический мониторинг\nновых релизов по жанрам",
            AutoSize = true,
            ForeColor = TextMuted,
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(18, 250)
        };

        sidebar.Controls.Add(stack);
        sidebar.Controls.Add(hint);
        return sidebar;
    }

    private Button NavButton(string text, bool active, EventHandler click)
    {
        var b = new Button
        {
            Text = text,
            Width = 175,
            Height = 46,
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 0 },
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI Semibold", 10),
            ForeColor = active ? TextPrimary : TextMuted,
            BackColor = active ? Surface2 : Surface,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 0, 6)
        };
        b.Click += click;
        return b;
    }

    private void ShowPage(Panel page, Button activeButton)
    {
        _tracksPage.Visible = false;
        _artistsPage.Visible = false;
        _settingsPage.Visible = false;
        page.Visible = true;
        page.BringToFront();

        foreach (var b in new[] { _navTracks, _navArtists, _navSettings }.Where(x => x is not null))
        {
            b!.BackColor = ReferenceEquals(b, activeButton) ? Surface2 : Surface;
            b.ForeColor = ReferenceEquals(b, activeButton) ? TextPrimary : TextMuted;
        }
    }

    private void BuildTracksPage()
    {
        _tracksPage.BackColor = Bg;
        _tracksPage.Padding = new Padding(0);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            BackColor = Bg
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var heading = new Panel { Dock = DockStyle.Fill, BackColor = Bg };
        var title = new Label
        {
            Text = "Новые треки",
            AutoSize = true,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI Semibold", 18, FontStyle.Bold),
            Location = new Point(2, 5)
        };
        _trackCount.Location = new Point(4, 42);
        heading.Controls.Add(title);
        heading.Controls.Add(_trackCount);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Bg,
            Padding = new Padding(0, 4, 0, 6)
        };

        toolbar.Controls.Add(ActionButton("↻  Проверить сейчас", true, async (_, _) => await CheckNowAsync()));

        _monitorButton.Text = "▶  Мониторинг";
        StyleActionButton(_monitorButton, false);
        _monitorButton.Click += (_, _) => ToggleMonitoring();
        toolbar.Controls.Add(_monitorButton);

        toolbar.Controls.Add(ActionButton("▶  Preview", false, async (_, _) => await PreviewAsync()));
        toolbar.Controls.Add(ActionButton("■  Stop", false, (_, _) =>
        {
            _player.Stop();
            SetStatus("Preview остановлен");
        }));
        toolbar.Controls.Add(ActionButton("↓  Скачать", false, async (_, _) => await DownloadSelectedAsync()));
        toolbar.Controls.Add(ActionButton("↗  Открыть", false, (_, _) => OpenSelected()));

        StyleCheck(_onlyFavorites);
        StyleCheck(_onlyDownload);
        _onlyFavorites.CheckedChanged += (_, _) => RefreshGrid();
        _onlyDownload.CheckedChanged += (_, _) => RefreshGrid();
        toolbar.Controls.Add(_onlyFavorites);
        toolbar.Controls.Add(_onlyDownload);

        StyleTextBox(_searchBox);
        _searchBox.TextChanged += (_, _) => RefreshGrid();
        toolbar.Controls.Add(_searchBox);

        ConfigureGrid();
        var gridCard = Card();
        gridCard.Padding = new Padding(1);
        gridCard.Controls.Add(_grid);

        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(toolbar, 0, 1);
        layout.Controls.Add(gridCard, 0, 2);
        _tracksPage.Controls.Add(layout);
    }

    private void ConfigureGrid()
    {
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.ColumnHeadersHeight = 44;
        _grid.RowTemplate.Height = 38;
        _grid.DefaultCellStyle.BackColor = Surface;
        _grid.DefaultCellStyle.ForeColor = TextPrimary;
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(84, 43, 27);
        _grid.DefaultCellStyle.SelectionForeColor = Color.White;
        _grid.DefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
        _grid.DefaultCellStyle.Font = new Font("Segoe UI", 9.5f);
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(26, 30, 38);
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Surface2;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = TextMuted;
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9);
        _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 8, 0);

        _grid.Columns.Clear();
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "ДАТА",
            DataPropertyName = "CreatedAt",
            Width = 155
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "АРТИСТ",
            DataPropertyName = "Artist",
            Width = 210
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "НАЗВАНИЕ",
            DataPropertyName = "Title",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 300
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "ЖАНР",
            DataPropertyName = "Genre",
            Width = 145
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "BPM",
            DataPropertyName = "Bpm",
            Width = 70
        });
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
        _artistsPage.BackColor = Bg;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            BackColor = Bg
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));

        root.Controls.Add(PageHeading("Артисты и BPM", "Управляй любимыми артистами, blacklist и BPM-правилами"), 0, 0);

        var cards = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            BackColor = Bg
        };
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));

        StyleEditor(_favorites);
        StyleEditor(_blacklist);
        StyleEditor(_bpmRules);

        cards.Controls.Add(EditorCard("★  Любимые артисты", "Один артист на строку", _favorites), 0, 0);
        cards.Controls.Add(EditorCard("⛔  Blacklist", "Не показывать этих артистов", _blacklist), 1, 0);
        cards.Controls.Add(EditorCard("♬  BPM-правила", "Например: House=122-132", _bpmRules), 2, 0);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = Bg,
            Padding = new Padding(0, 12, 0, 0)
        };
        footer.Controls.Add(ActionButton("Сохранить изменения", true, (_, _) => SaveSettings()));

        root.Controls.Add(cards, 0, 1);
        root.Controls.Add(footer, 0, 2);
        _artistsPage.Controls.Add(root);
    }

    private void BuildSettingsPage()
    {
        _settingsPage.BackColor = Bg;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            BackColor = Bg,
            AutoScroll = true
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(PageHeading("Настройки", "Подключение SoundCloud и параметры мониторинга"), 0, 0);

        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Bg,
            Padding = new Padding(0, 4, 4, 20)
        };

        stack.Controls.Add(BuildConnectionCard());
        stack.Controls.Add(BuildMonitoringCard());

        root.Controls.Add(stack, 0, 1);
        _settingsPage.Controls.Add(root);
    }

    private Control BuildConnectionCard()
    {
        var card = Card(920, 185);
        card.Margin = new Padding(0, 0, 0, 14);

        var title = CardTitle("Подключение SoundCloud", 22, 18);
        var desc = new Label
        {
            Text = "Подключение проходит через официальный сайт. Пароль приложение не получает.",
            AutoSize = true,
            ForeColor = TextMuted,
            Location = new Point(22, 49)
        };

        var status = new Label
        {
            Text = string.IsNullOrWhiteSpace(_cfg.ClientId) ? "●  Не подключено" : "●  Подключено",
            AutoSize = true,
            ForeColor = string.IsNullOrWhiteSpace(_cfg.ClientId) ? Danger : Success,
            Font = new Font("Segoe UI Semibold", 10),
            Location = new Point(22, 83)
        };
        _loginStatusProxy = status;

        _clientId.Width = 420;
        _clientId.Location = new Point(22, 115);
        StyleTextBox(_clientId);
        _clientId.PlaceholderText = "Client ID появится автоматически";

        var connect = ActionButton("Подключить SoundCloud", true, async (_, _) => await AutoConnectSoundCloudAsync());
        connect.Location = new Point(465, 111);
        connect.Width = 190;

        var test = ActionButton("Проверить API", false, async (_, _) => await TestApiAsync());
        test.Location = new Point(665, 111);
        test.Width = 135;

        var disconnect = ActionButton("Отключить", false, (_, _) => DeleteSecret());
        disconnect.Location = new Point(810, 111);
        disconnect.Width = 95;

        card.Controls.Add(title);
        card.Controls.Add(desc);
        card.Controls.Add(status);
        card.Controls.Add(_clientId);
        card.Controls.Add(connect);
        card.Controls.Add(test);
        card.Controls.Add(disconnect);

        return card;
    }

    private Label? _loginStatusProxy;

    private Control BuildMonitoringCard()
    {
        var card = Card(920, 355);

        var title = CardTitle("Мониторинг", 22, 18);
        var desc = new Label
        {
            Text = "Жанры, частота проверки и папка для разрешённых загрузок.",
            AutoSize = true,
            ForeColor = TextMuted,
            Location = new Point(22, 49)
        };

        card.Controls.Add(title);
        card.Controls.Add(desc);

        var form = new TableLayoutPanel
        {
            Location = new Point(22, 86),
            Size = new Size(875, 205),
            ColumnCount = 3,
            RowCount = 5,
            BackColor = Surface
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));

        StyleTextBox(_genres);
        StyleNumeric(_poll);
        StyleNumeric(_lookback);
        StyleTextBox(_downloadDir);
        StyleCheck(_autoDownload);

        AddSettingRow(form, 0, "Жанры", _genres, null);
        AddSettingRow(form, 1, "Проверять каждые", _poll, "мин");
        AddSettingRow(form, 2, "Искать за последние", _lookback, "часов");
        AddSettingRow(form, 3, "Папка загрузки", _downloadDir, null);

        var browse = ActionButton("Выбрать…", false, (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { SelectedPath = _downloadDir.Text };
            if (dlg.ShowDialog(this) == DialogResult.OK) _downloadDir.Text = dlg.SelectedPath;
        });
        browse.Dock = DockStyle.Fill;
        browse.Margin = new Padding(8, 4, 0, 4);
        form.Controls.Add(browse, 2, 3);

        form.Controls.Add(_autoDownload, 1, 4);
        form.SetColumnSpan(_autoDownload, 2);

        var save = ActionButton("Сохранить настройки", true, (_, _) => SaveSettings());
        save.Location = new Point(22, 300);
        save.Width = 180;

        card.Controls.Add(form);
        card.Controls.Add(save);
        return card;
    }

    private void AddSettingRow(TableLayoutPanel form, int row, string label, Control control, string? suffix)
    {
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        var l = new Label
        {
            Text = label,
            AutoSize = true,
            ForeColor = TextMuted,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 12, 0)
        };
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 4, 8, 4);

        form.Controls.Add(l, 0, row);
        form.Controls.Add(control, 1, row);

        if (!string.IsNullOrWhiteSpace(suffix))
        {
            form.Controls.Add(new Label
            {
                Text = suffix,
                AutoSize = true,
                ForeColor = TextMuted,
                Anchor = AnchorStyles.Left
            }, 2, row);
        }
    }

    private Control BuildFooter()
    {
        var footer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Padding = new Padding(22, 8, 22, 6)
        };
        _status.Location = new Point(22, 9);
        footer.Controls.Add(_status);
        return footer;
    }

    private Panel PageHeading(string titleText, string subtitleText)
    {
        var p = new Panel { Dock = DockStyle.Fill, BackColor = Bg };
        var title = new Label
        {
            Text = titleText,
            AutoSize = true,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI Semibold", 18, FontStyle.Bold),
            Location = new Point(2, 4)
        };
        var subtitle = new Label
        {
            Text = subtitleText,
            AutoSize = true,
            ForeColor = TextMuted,
            Font = new Font("Segoe UI", 9),
            Location = new Point(4, 42)
        };
        p.Controls.Add(title);
        p.Controls.Add(subtitle);
        return p;
    }

    private Panel Card(int width = 0, int height = 0)
    {
        var p = new Panel
        {
            BackColor = Surface,
            BorderStyle = BorderStyle.FixedSingle
        };
        if (width > 0) p.Width = width;
        if (height > 0) p.Height = height;
        return p;
    }

    private Label CardTitle(string text, int x, int y) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = TextPrimary,
        Font = new Font("Segoe UI Semibold", 12, FontStyle.Bold),
        Location = new Point(x, y)
    };

    private Control EditorCard(string title, string subtitle, TextBox editor)
    {
        var card = Card();
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(0, 0, 12, 0);
        card.Padding = new Padding(18);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            BackColor = Surface
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI Semibold", 11, FontStyle.Bold)
        }, 0, 0);
        root.Controls.Add(new Label
        {
            Text = subtitle,
            AutoSize = true,
            ForeColor = TextMuted,
            Font = new Font("Segoe UI", 8.5f)
        }, 0, 1);
        root.Controls.Add(editor, 0, 2);
        card.Controls.Add(root);
        return card;
    }

    private Button ActionButton(string text, bool primary, EventHandler click)
    {
        var b = new Button { Text = text, AutoSize = false };
        StyleActionButton(b, primary);
        b.Click += click;
        return b;
    }

    private void StyleActionButton(Button b, bool primary)
    {
        b.Height = 38;
        b.Width = Math.Max(112, TextRenderer.MeasureText(b.Text, new Font("Segoe UI Semibold", 9.5f)).Width + 30);
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = primary ? 0 : 1;
        b.FlatAppearance.BorderColor = Border;
        b.BackColor = primary ? Accent : Surface2;
        b.ForeColor = TextPrimary;
        b.Font = new Font("Segoe UI Semibold", 9.5f);
        b.Cursor = Cursors.Hand;
        b.Margin = new Padding(0, 0, 8, 0);

        b.MouseEnter += (_, _) => b.BackColor = primary ? AccentHover : Color.FromArgb(38, 43, 54);
        b.MouseLeave += (_, _) => b.BackColor = primary ? Accent : Surface2;
    }

    private void StyleTextBox(TextBox box)
    {
        box.BackColor = Surface2;
        box.ForeColor = TextPrimary;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = new Font("Segoe UI", 9.5f);
    }

    private void StyleEditor(TextBox box)
    {
        box.BackColor = Surface2;
        box.ForeColor = TextPrimary;
        box.Font = new Font("Segoe UI", 10);
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Padding = new Padding(8);
    }

    private void StyleNumeric(NumericUpDown box)
    {
        box.BackColor = Surface2;
        box.ForeColor = TextPrimary;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = new Font("Segoe UI", 9.5f);
    }

    private void StyleCheck(CheckBox box)
    {
        box.ForeColor = TextPrimary;
        box.BackColor = Color.Transparent;
        box.Font = new Font("Segoe UI", 9);
        box.Margin = new Padding(10, 10, 8, 0);
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

            SetStatus("SoundCloud подключён • проверяю API…");
            var client = new SoundCloudApiClient(result.ClientId, result.ClientSecret);
            await client.TestAsync(CancellationToken.None);

            UpdateConnectionBadge(true);
            SetStatus("SoundCloud подключён");

            MessageBox.Show(
                this,
                "SoundCloud подключён. Client Secret сохранён в Windows Credential Manager.",
                "Готово",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
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
            MessageBox.Show(this, "API подключён успешно.", "SoundCloud", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                MessageBox.Show(
                    this,
                    "Откройте «Настройки» и нажмите «Подключить SoundCloud».",
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
        var track = SelectedTrack();
        if (track is null) return;

        try
        {
            SetStatus("Готовлю preview…");
            var client = ClientFromUi();
            var path = await client.DownloadPreviewAsync(track, CancellationToken.None);
            _player.Play(path);
            SetStatus($"Preview • {track.Artist} — {track.Title}");
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
            var client = ClientFromUi();
            SetStatus("Скачиваю…");

            var path = await client.DownloadTrackAsync(
                track,
                _downloadDir.Text.Trim(),
                CancellationToken.None);

            _store.MarkDownloaded(track.Urn, path);
            SetStatus("Трек скачан");

            MessageBox.Show(
                this,
                $"Сохранено:\n{path}",
                "Готово",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
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

        _monitorButton.Text = _timer.Enabled ? "■  Остановить мониторинг" : "▶  Мониторинг";
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
            throw new InvalidOperationException(
                "Откройте «Настройки» и нажмите «Подключить SoundCloud».");

        return new SoundCloudApiClient(id, secret);
    }

    private void DeleteSecret()
    {
        if (MessageBox.Show(
                this,
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

        if (_loginStatusProxy is not null)
        {
            _loginStatusProxy.Text = isConnected ? "●  Подключено" : "●  Не подключено";
            _loginStatusProxy.ForeColor = isConnected ? Success : Danger;
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

    private void SetStatus(string text)
    {
        _status.Text = text;
    }

    private void ShowError(string message)
    {
        SetStatus("Ошибка: " + (message.Length > 110 ? message[..110] + "…" : message));
        MessageBox.Show(this, message, "Release Radar", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

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

            result[genre] = new BpmRule
            {
                From = from,
                To = to
            };
        }

        return result;
    }
}
