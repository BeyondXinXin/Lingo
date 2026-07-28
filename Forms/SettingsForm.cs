using System.ComponentModel;
using Lingo.Infrastructure;
using Lingo.Models;
using Lingo.Services;

namespace Lingo.Forms;

// AIxyz 风格设置窗口：顶部标签页切换，底部固定按钮栏
internal sealed class SettingsForm : Form
{
    private static readonly (string Code, string Display)[] Languages =
    [
        ("zh", "中文"),
        ("en", "英语"),
        ("jp", "日语"),
        ("kor", "韩语"),
        ("fra", "法语"),
        ("de", "德语"),
        ("ru", "俄语"),
        ("spa", "西班牙语"),
    ];

    private readonly AppSettings _original;

    private readonly TextBox _hotkeyBox;
    private readonly CheckBox _startupCheck;
    private readonly ComboBox _targetCombo;

    private readonly CheckBox _baiduEnabledCheck;
    private readonly TextBox _baiduAppIdBox;
    private readonly TextBox _baiduKeyBox;
    private readonly ComboBox _baiduFromCombo;
    private readonly ComboBox _baiduToCombo;

    private readonly CheckBox _customEnabledCheck;
    private readonly ComboBox _apiSelector;
    private readonly DarkButton _apiAddButton;
    private readonly DarkButton _apiRemoveButton;
    private readonly TextBox _customNameBox;
    private readonly TextBox _endpointBox;
    private readonly TextBox _apiKeyBox;
    private readonly TextBox _modelBox;
    private readonly TextBox _promptBox;
    private readonly NumericUpDown _timeoutNumeric;

    // 模型翻译配置的工作副本，字段区只编辑当前选中项
    private readonly List<CustomApiSettings> _apis;
    private int _currentApiIndex = -1;

    private readonly List<(TabLabel Tab, Control Page)> _tabs = [];

    public SettingsForm(AppSettings settings)
    {
        _original = settings;

        Text = "Lingo 设置";
        Icon = AppIcon.Get();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(540, 730);
        BackColor = Theme.MainBg;
        ForeColor = Theme.Text;

        _hotkeyBox = CreateTextBox(settings.Hotkey);
        _hotkeyBox.ReadOnly = true;
        _hotkeyBox.KeyDown += OnHotkeyBoxKeyDown;
        _startupCheck = CreateCheckBox("开机自动启动", settings.LaunchAtStartup);
        _targetCombo = CreateLanguageCombo(includeFollowDefault: false, includeAuto: false);
        SelectLanguage(_targetCombo, settings.DefaultTargetLanguage, fallback: "zh");

        _baiduEnabledCheck = CreateCheckBox("启用百度翻译", settings.Baidu.Enabled);
        _baiduAppIdBox = CreateTextBox(settings.Baidu.AppId);
        _baiduKeyBox = CreateTextBox(settings.Baidu.SecretKey);
        _baiduKeyBox.UseSystemPasswordChar = true;
        _baiduFromCombo = CreateLanguageCombo(includeFollowDefault: false, includeAuto: true);
        SelectLanguage(_baiduFromCombo, settings.Baidu.SourceLanguage, fallback: "auto");
        _baiduToCombo = CreateLanguageCombo(includeFollowDefault: true, includeAuto: false);
        SelectLanguage(_baiduToCombo, settings.Baidu.TargetLanguage, fallback: string.Empty);

        _apis = [.. settings.CustomApis.Select(Clone)];
        _apiSelector = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.TweakBg,
            ForeColor = Theme.Text,
            Width = 168,
        };
        _apiSelector.SelectedIndexChanged += OnApiSelectionChanged;
        _apiAddButton = new DarkButton { Text = "添加", Size = new Size(54, 27), Margin = new Padding(8, 0, 0, 0) };
        _apiRemoveButton = new DarkButton { Text = "删除", Size = new Size(54, 27), Margin = new Padding(6, 0, 0, 0) };
        _apiAddButton.Click += OnApiAddClicked;
        _apiRemoveButton.Click += OnApiRemoveClicked;

        _customEnabledCheck = CreateCheckBox("启用该模型翻译（OpenAI 兼容）", false);
        _customNameBox = CreateTextBox(string.Empty);
        _endpointBox = CreateTextBox(string.Empty);
        _apiKeyBox = CreateTextBox(string.Empty);
        _apiKeyBox.UseSystemPasswordChar = true;
        _modelBox = CreateTextBox(string.Empty);
        _promptBox = CreateTextBox(string.Empty);
        _promptBox.Multiline = true;
        _promptBox.ScrollBars = ScrollBars.Vertical;
        _timeoutNumeric = new NumericUpDown
        {
            Minimum = 5,
            Maximum = 300,
            Value = 30,
            BackColor = Theme.StressBg,
            ForeColor = Theme.Text,
        };

        BuildLayout();
        RefreshApiSelector(_apis.Count > 0 ? 0 : -1);
    }

    public AppSettings? Result { get; private set; }

    private void BuildLayout()
    {
        // 顶部标签栏
        FlowLayoutPanel tabBar = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            BackColor = Theme.MainBg,
            Padding = new Padding(10, 8, 10, 0),
        };

        // 底部按钮栏
        FlowLayoutPanel buttonPanel = new()
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            AutoSize = true,
            BackColor = Theme.MainBg,
            Padding = new Padding(16, 6, 16, 12),
        };
        DarkButton saveButton = new() { Text = "保存", Size = new Size(84, 32) };
        DarkButton cancelButton = new() { Text = "取消", Size = new Size(84, 32), DialogResult = DialogResult.Cancel };
        cancelButton.Margin = new Padding(0, 0, 10, 0);
        saveButton.Click += OnSaveClicked;
        buttonPanel.Controls.Add(saveButton);
        buttonPanel.Controls.Add(cancelButton);

        // 内容卡片：三个页面共用一张卡片，切换标签只换可见页
        CardPanel card = new()
        {
            Dock = DockStyle.Fill,
            CornerRadius = 10,
            Padding = new Padding(16, 14, 16, 14),
        };
        Panel cardHost = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.MainBg,
            Padding = new Padding(14, 8, 14, 2),
        };
        cardHost.Controls.Add(card);

        TableLayoutPanel generalPage = CreatePage();
        AddRow(generalPage, "全局快捷键", Rounded(_hotkeyBox));
        AddRow(generalPage, string.Empty, CreateHint("点击输入框后按下组合键，例如 Ctrl+Alt+T"));
        AddRow(generalPage, "目标语言", _targetCombo);
        AddRow(generalPage, string.Empty, _startupCheck);

        TableLayoutPanel baiduPage = CreatePage();
        AddRow(baiduPage, string.Empty, _baiduEnabledCheck);
        AddRow(baiduPage, "App ID", Rounded(_baiduAppIdBox));
        AddRow(baiduPage, "Secret Key", Rounded(_baiduKeyBox));
        AddRow(baiduPage, "源语言", _baiduFromCombo);
        AddRow(baiduPage, "目标语言", _baiduToCombo);

        TableLayoutPanel customPage = CreatePage();
        FlowLayoutPanel apiBar = new()
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = Theme.StressBg,
        };
        apiBar.Controls.Add(_apiSelector);
        apiBar.Controls.Add(_apiAddButton);
        apiBar.Controls.Add(_apiRemoveButton);
        AddRow(customPage, "模型配置", apiBar);
        AddRow(customPage, string.Empty, _customEnabledCheck);
        AddRow(customPage, "显示名称", Rounded(_customNameBox));
        AddRow(customPage, "Endpoint", Rounded(_endpointBox));
        AddRow(customPage, "API 密钥", Rounded(_apiKeyBox));
        AddRow(customPage, "模型名称", Rounded(_modelBox));
        AddRow(customPage, "Prompt", Rounded(_promptBox, height: 176));
        AddRow(customPage, string.Empty, CreateHint("{text} 为原文占位符；留空则恢复默认 Prompt"));
        AddRow(customPage, "超时（秒）", Rounded(_timeoutNumeric, width: 90));

        card.Controls.Add(generalPage);
        card.Controls.Add(baiduPage);
        card.Controls.Add(customPage);

        AddTab(tabBar, "常规", generalPage);
        AddTab(tabBar, "百度翻译", baiduPage);
        AddTab(tabBar, "模型翻译", customPage);

        Controls.Add(cardHost);
        Controls.Add(tabBar);
        Controls.Add(buttonPanel);
        cardHost.BringToFront();

        SelectTab(0);
        AcceptButton = saveButton;
        CancelButton = cancelButton;
    }

    private void AddTab(FlowLayoutPanel tabBar, string title, Control page)
    {
        TabLabel tab = new(title);
        int index = _tabs.Count;
        tab.Click += (_, _) => SelectTab(index);
        _tabs.Add((tab, page));
        tabBar.Controls.Add(tab);
    }

    private void SelectTab(int index)
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            _tabs[i].Tab.Selected = i == index;
            _tabs[i].Page.Visible = i == index;
        }
    }

    private void OnSaveClicked(object? sender, EventArgs e)
    {
        string hotkey = _hotkeyBox.Text.Trim();
        if (!HotkeyService.TryParse(hotkey, out _, out _))
        {
            SelectTab(0);
            MessageBox.Show(this, "快捷键无效：必须包含 Ctrl/Alt/Shift/Win 修饰键和一个普通按键。",
                "Lingo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        CommitApiFields();
        Result = new AppSettings
        {
            Hotkey = hotkey,
            LaunchAtStartup = _startupCheck.Checked,
            DefaultTargetLanguage = SelectedLanguage(_targetCombo),
            Baidu = new BaiduSettings
            {
                Enabled = _baiduEnabledCheck.Checked,
                AppId = _baiduAppIdBox.Text.Trim(),
                SecretKey = _baiduKeyBox.Text.Trim(),
                SourceLanguage = SelectedLanguage(_baiduFromCombo),
                TargetLanguage = SelectedLanguage(_baiduToCombo),
            },
            CustomApis = [.. _apis.Select(Clone)],
            TranslateWindow = _original.TranslateWindow,
        };

        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnHotkeyBoxKeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;
        string formatted = HotkeyService.Format(e.KeyData);
        if (formatted.Length > 0)
        {
            _hotkeyBox.Text = formatted;
        }
    }

    private void OnApiSelectionChanged(object? sender, EventArgs e)
    {
        if (_apiSelector.SelectedIndex == _currentApiIndex)
        {
            return;
        }

        CommitApiFields();
        _currentApiIndex = _apiSelector.SelectedIndex;
        LoadApiFields();
    }

    private void OnApiAddClicked(object? sender, EventArgs e)
    {
        CommitApiFields();
        _apis.Add(new CustomApiSettings { Enabled = true });
        RefreshApiSelector(_apis.Count - 1);
    }

    private void OnApiRemoveClicked(object? sender, EventArgs e)
    {
        if (_currentApiIndex < 0 || _currentApiIndex >= _apis.Count)
        {
            return;
        }

        int removed = _currentApiIndex;
        _apis.RemoveAt(removed);
        _currentApiIndex = -1; // 已删除的项不再回写字段
        RefreshApiSelector(Math.Min(removed, _apis.Count - 1));
    }

    private void RefreshApiSelector(int selectIndex)
    {
        _currentApiIndex = -1;
        _apiSelector.Items.Clear();
        for (int i = 0; i < _apis.Count; i++)
        {
            _apiSelector.Items.Add(ApiDisplayName(_apis[i], i));
        }

        if (selectIndex >= 0 && selectIndex < _apis.Count)
        {
            _apiSelector.SelectedIndex = selectIndex;
        }
        else
        {
            LoadApiFields();
        }
    }

    // 把字段区内容写回当前选中的配置项
    private void CommitApiFields()
    {
        if (_currentApiIndex < 0 || _currentApiIndex >= _apis.Count)
        {
            return;
        }

        CustomApiSettings api = _apis[_currentApiIndex];
        api.Enabled = _customEnabledCheck.Checked;
        api.Name = _customNameBox.Text.Trim();
        api.Endpoint = _endpointBox.Text.Trim();
        api.ApiKey = _apiKeyBox.Text.Trim();
        api.Model = _modelBox.Text.Trim();
        string prompt = _promptBox.Text.Trim();
        api.Prompt = prompt.Length == 0 ? CustomApiSettings.DefaultPrompt : prompt;
        api.TimeoutSeconds = (int)_timeoutNumeric.Value;
        _apiSelector.Items[_currentApiIndex] = ApiDisplayName(api, _currentApiIndex);
    }

    private void LoadApiFields()
    {
        bool hasSelection = _currentApiIndex >= 0 && _currentApiIndex < _apis.Count;
        CustomApiSettings api = hasSelection ? _apis[_currentApiIndex] : new CustomApiSettings();
        _customEnabledCheck.Checked = hasSelection && api.Enabled;
        _customNameBox.Text = api.Name;
        _endpointBox.Text = api.Endpoint;
        _apiKeyBox.Text = api.ApiKey;
        _modelBox.Text = api.Model;
        _promptBox.Text = api.Prompt;
        _timeoutNumeric.Value = Math.Clamp(api.TimeoutSeconds, 5, 300);

        foreach (Control control in new Control[]
                 {
                     _customEnabledCheck, _customNameBox, _endpointBox,
                     _apiKeyBox, _modelBox, _promptBox, _timeoutNumeric,
                 })
        {
            control.Enabled = hasSelection;
        }

        _apiRemoveButton.Enabled = hasSelection;
    }

    private static string ApiDisplayName(CustomApiSettings api, int index) =>
        !string.IsNullOrWhiteSpace(api.Name) ? api.Name
        : !string.IsNullOrWhiteSpace(api.Model) ? api.Model
        : $"模型 {index + 1}";

    private static CustomApiSettings Clone(CustomApiSettings api) => new()
    {
        Enabled = api.Enabled,
        Name = api.Name,
        Endpoint = api.Endpoint,
        ApiKey = api.ApiKey,
        Model = api.Model,
        Prompt = api.Prompt,
        TimeoutSeconds = api.TimeoutSeconds,
    };

    private static TextBox CreateTextBox(string text) => new()
    {
        Text = text,
        BackColor = Theme.StressBg,
        ForeColor = Theme.Text,
    };

    private static CheckBox CreateCheckBox(string text, bool isChecked) => new()
    {
        Text = text,
        AutoSize = true,
        Checked = isChecked,
        ForeColor = Theme.Text,
    };

    private static RoundedInput Rounded(Control inner, int height = 0, int width = 0)
    {
        RoundedInput input = new(inner);
        if (height > 0)
        {
            input.Height = height;
        }

        if (width > 0)
        {
            input.Width = width;
            input.Anchor = AnchorStyles.Left;
        }

        return input;
    }

    private static TableLayoutPanel CreatePage()
    {
        TableLayoutPanel page = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            BackColor = Theme.StressBg,
            Visible = false,
        };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        return page;
    }

    private static void AddRow(TableLayoutPanel table, string labelText, Control control)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Label label = new()
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = Theme.Text,
            Margin = new Padding(0, 11, 6, 0),
        };
        if (control.Anchor != AnchorStyles.Left)
        {
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        }

        control.Margin = new Padding(0, 4, 0, 4);

        table.Controls.Add(label, 0, row);
        table.Controls.Add(control, 1, row);
    }

    private static Label CreateHint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.TextMuted,
        Margin = new Padding(2, 0, 0, 6),
    };

    private static ComboBox CreateLanguageCombo(bool includeFollowDefault, bool includeAuto)
    {
        ComboBox combo = new()
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.TweakBg,
            ForeColor = Theme.Text,
        };
        if (includeFollowDefault)
        {
            combo.Items.Add(new LanguageItem(string.Empty, "跟随默认"));
        }

        if (includeAuto)
        {
            combo.Items.Add(new LanguageItem("auto", "自动检测"));
        }

        foreach ((string code, string display) in Languages)
        {
            combo.Items.Add(new LanguageItem(code, display));
        }

        return combo;
    }

    private static void SelectLanguage(ComboBox combo, string code, string fallback)
    {
        foreach (object item in combo.Items)
        {
            if (item is LanguageItem language && language.Code == code)
            {
                combo.SelectedItem = item;
                return;
            }
        }

        foreach (object item in combo.Items)
        {
            if (item is LanguageItem language && language.Code == fallback)
            {
                combo.SelectedItem = item;
                return;
            }
        }

        combo.SelectedIndex = 0;
    }

    private static string SelectedLanguage(ComboBox combo) =>
        combo.SelectedItem is LanguageItem language ? language.Code : string.Empty;

    private sealed record LanguageItem(string Code, string Display)
    {
        public override string ToString() => Display;
    }

    // 标签页页签：选中时文字高亮并绘制下划线
    private sealed class TabLabel : Label
    {
        private bool _selected;

        public TabLabel(string title)
        {
            Text = title;
            AutoSize = true;
            Padding = new Padding(4, 6, 4, 8);
            Margin = new Padding(0, 0, 14, 0);
            ForeColor = Theme.TextMuted;
            Cursor = Cursors.Hand;
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Selected
        {
            get => _selected;
            set
            {
                _selected = value;
                ForeColor = value ? Color.White : Theme.TextMuted;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            if (!_selected)
            {
                ForeColor = Theme.Text;
            }

            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (!_selected)
            {
                ForeColor = Theme.TextMuted;
            }

            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_selected)
            {
                using Pen pen = new(Theme.BorderFocus, 2F);
                e.Graphics.DrawLine(pen, 4, Height - 2, Width - 4, Height - 2);
            }
        }
    }
}
