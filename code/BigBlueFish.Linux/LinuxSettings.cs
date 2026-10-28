using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
#if !COMPANION_SAFE
using BigBlueFish.Control;
#endif

namespace BigBlueFish.Windows;

internal sealed partial class LinuxPetWindow
{
    bool consentOpen;
    Window? consentWindow;
    Action? refreshLinuxSettings;
    static string AutostartFile => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/LaunchAgents/com.bigbluefish.desktoppet.plist")
        : Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "autostart/bigbluefish.desktop");
    bool StartupEnabled => File.Exists(AutostartFile);
    void SetStartup(bool on)
    {
        if (!on) { if (File.Exists(AutostartFile)) File.Delete(AutostartFile); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(AutostartFile)!);
        string executable = Path.Combine(AppContext.BaseDirectory, "BigBlueFish.Linux");
        if (OperatingSystem.IsMacOS())
        {
            string escapedExe = System.Security.SecurityElement.Escape(executable) ?? executable;
            string escapedDir = System.Security.SecurityElement.Escape(AppContext.BaseDirectory) ?? AppContext.BaseDirectory;
            File.WriteAllText(AutostartFile,
                $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                    <key>Label</key>
                    <string>com.bigbluefish.desktoppet</string>
                    <key>ProgramArguments</key>
                    <array>
                        <string>{escapedExe}</string>
                    </array>
                    <key>WorkingDirectory</key>
                    <string>{escapedDir}</string>
                    <key>RunAtLoad</key>
                    <true/>
                </dict>
                </plist>
                """);
            return;
        }
        File.WriteAllText(AutostartFile, "[Desktop Entry]\nType=Application\nName=蓝色大肥鱼\nExec=\"" + executable.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"\nTerminal=false\n");
    }
    void ApplySettings(PixelPoint? requestedPosition = null)
    {
        speechVersion++; brain.Stop(); brain.UseGpu = Preferences.GpuEnabled; brain.StopOnBattery = Preferences.StopModelOnBattery; brain.WeatherRegionCode = Preferences.WeatherRegionCode;
        Rescale(); var appliedPosition = Clamp(requestedPosition); nextSpeech = DateTime.UtcNow.Add(Preferences.NextSpeechDelay(random)); nextEvent = DateTime.UtcNow.AddSeconds(Preferences.AnimationIntervalSeconds); nextWarmup = DateTime.MinValue;
        if (!Preferences.HideOnFullscreen && fullscreenHidden) { fullscreenHidden = false; if (!hidden) Show(); }
        if (bubbleWindow.IsVisible) ResetDwell();
        Preferences.Left = appliedPosition.X; Preferences.Top = appliedPosition.Y; Preferences.Save();
    }
    static Button ActionButton(string label, Action action)
    {
        var button = new Button {
            Content = label, Padding = new Thickness(16, 9), CornerRadius = new CornerRadius(9),
            Margin = new Thickness(0, 4, 10, 4),
            Background = new SolidColorBrush(Color.FromRgb(237, 243, 252)),
            Foreground = new SolidColorBrush(Color.FromRgb(49, 64, 94)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 229, 242)),
            BorderThickness = new Thickness(1)
        };
        button.Click += (_, _) => action(); return button;
    }
    static TextBlock Paragraph(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12), Foreground = Brushes.SlateGray };
    static Window Dialog(string title, Avalonia.Controls.Control content, double width = 630, double height = 460, bool pad = true) => new() {
        Title = title, Width = width, Height = height, MinWidth = 480, MinHeight = 360,
        FontFamily = new FontFamily("PingFang SC, Noto Sans CJK SC"), FontSize = 14,
        WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = new SolidColorBrush(Color.FromRgb(246, 248, 252)),
        Content = pad ? new Border { Padding = new Thickness(28), Child = content } : content };
    async Task<bool> Confirm(string title, string message, string accept = "确定", CancellationToken token = default, string reject = "取消")
    {
        if (consentOpen) throw new InvalidOperationException("请先处理已打开的提示窗口。");
        consentOpen = true; bool accepted = false;
        try {
            var panel = new StackPanel { Spacing = 15 };
            panel.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = FontWeight.SemiBold }); panel.Children.Add(Paragraph(message));
            var dialog = Dialog(title, panel); var buttons = new WrapPanel();
            buttons.Children.Add(ActionButton(accept, () => { accepted = true; dialog.Close(); })); buttons.Children.Add(ActionButton(reject, dialog.Close)); panel.Children.Add(buttons);
            using var registration = token.Register(() => Dispatcher.UIThread.Post(() => { accepted = false; dialog.Close(); }));
            var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.Closed += (_, _) => finished.TrySetResult(accepted && !token.IsCancellationRequested);
            consentWindow = dialog;
            if (settings?.IsVisible == true) dialog.Show(settings); else dialog.Show();
            return await finished.Task;
        } finally { consentWindow = null; consentOpen = false; }
    }
    void OpenSettings()
    {
        if (settings != null) { if (settings.WindowState == WindowState.Minimized) settings.WindowState = WindowState.Normal; settings.Activate(); return; }
        moveRule = null;
        string version = typeof(LinuxPetWindow).Assembly.GetName().Version!.ToString(3);
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("205,*"), Background = new SolidColorBrush(Color.FromRgb(246, 248, 252)) };
        var navigation = new StackPanel { Spacing = 8, Margin = new Thickness(18, 24, 18, 18) };
        navigation.Children.Add(new TextBlock { Text = "蓝色大肥鱼", FontSize = 22, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.FromRgb(53, 76, 129)), Margin = new Thickness(5, 0, 0, 4) });
        navigation.Children.Add(new TextBlock { Text = $"{version}{(McpAvailable ? "" : " · 无MCP版")} · 陪伴设置", FontSize = 13, Foreground = Brushes.SlateGray, Margin = new Thickness(5, 0, 0, 20) });
        var sidebarFrame = new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(226, 233, 243)), BorderThickness = new Thickness(0, 0, 1, 0), Child = navigation };
        var pageContainer = new StackPanel { Spacing = 14, Margin = new Thickness(26, 24, 26, 24) };
        var pageTitle = new TextBlock { FontSize = 25, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.FromRgb(42, 56, 83)), Margin = new Thickness(0, 0, 0, 4) };
        var body = new StackPanel { Spacing = 8 };
        var card = new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(226, 233, 243)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(22), Child = body };
        pageContainer.Children.Add(pageTitle);
        pageContainer.Children.Add(card);
        var scroll = new ScrollViewer { Content = pageContainer };
        Grid.SetColumn(scroll, 1);
        root.Children.Add(sidebarFrame);
        root.Children.Add(scroll);
        Action? tickStatus = null;
        var statusClock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        statusClock.Tick += (_, _) => tickStatus?.Invoke();
        statusClock.Start();
        settings = Dialog("大肥鱼设置", root, 920, 720, pad: false); settings.MinWidth = 780; settings.MinHeight = 560;
        settings.Closed += (_, _) => { statusClock.Stop(); tickStatus = null; settings = null; refreshLinuxSettings = null; Save(); nextEvent = DateTime.UtcNow.AddSeconds(Preferences.AnimationIntervalSeconds); };
        Action selectedPage = General;
        var navButtons = new List<(Action Page, Button Btn)>();
        void UpdateNavHighlight()
        {
            foreach (var item in navButtons)
            {
                bool active = item.Page == selectedPage;
                item.Btn.Background = active ? new SolidColorBrush(Color.FromRgb(220, 234, 255)) : Brushes.Transparent;
                item.Btn.FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal;
            }
        }
        void Navigate(Action page) { selectedPage = page; UpdateNavHighlight(); scroll.Offset = new Vector(0, 0); page(); }
        void AddNav(string label, Action page)
        {
            var btn = new Button
            {
                Content = label,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(14, 10),
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromRgb(49, 64, 94)),
                Background = Brushes.Transparent
            };
            btn.Click += (_, _) => Navigate(page);
            navButtons.Add((page, btn));
            navigation.Children.Add(btn);
        }
        void Title(string text) { tickStatus = null; pageTitle.Text = text; body.Children.Clear(); }
        void Toggle(string text, bool value, Action<bool> set) { var box = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 5) }; box.IsCheckedChanged += (_, _) => { set(box.IsChecked == true); Save(); }; body.Children.Add(box); }
        void Slider(string text, double value, double low, double high, Action<double> set, double tick = 1, string suffix = "") {
            var label = new TextBlock { Text = text + "：" + value.ToString("0.##") + suffix, Margin = new Thickness(0, 12, 0, 2) };
            var slider = new Avalonia.Controls.Slider { Minimum = low, Maximum = high, Value = value, TickFrequency = tick, IsSnapToTickEnabled = true };
            slider.ValueChanged += (_, _) => { label.Text = text + "：" + slider.Value.ToString("0.##") + suffix; set(slider.Value); Save(); }; body.Children.Add(label); body.Children.Add(slider);
        }
        void Select(string label, string[] choices, int selected, Action<int> set) {
            body.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 4) }); var combo = new ComboBox { ItemsSource = choices, SelectedIndex = selected, HorizontalAlignment = HorizontalAlignment.Stretch };
            combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) { set(combo.SelectedIndex); Save(); } }; body.Children.Add(combo);
        }
        void General() {
            Title("陪伴与外观");
            Slider("缩放", Preferences.Scale * 100, 50, 200, value => { Preferences.Scale = value / 100; Rescale(); Clamp(); }, 5, "%");
            Slider("文字气泡大小", Preferences.BubbleScale * 100, 50, 200, value => { Preferences.BubbleScale = value / 100; MoveBubble(); }, 5, "%");
            Toggle("开机自动启动", StartupEnabled, SetStartup);
            Toggle("全屏时自动隐藏桌宠", Preferences.HideOnFullscreen, value => {
                Preferences.HideOnFullscreen = value;
                if (!value && fullscreenHidden) { fullscreenHidden = false; if (!hidden) { Show(); Clamp(); } }
            });
            Select("气泡驻留时间", CompanionPreferences.BubbleDurationLabels, Preferences.BubbleDurationIndex, value => { Preferences.BubbleDurationIndex = value; if (bubbleWindow.IsVisible) ResetDwell(); });
            var actions = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            actions.Children.Add(ActionButton("立即吐字测试", async () => await Speak(false)));
            actions.Children.Add(ActionButton("立即播报天气", async () => await Speak(true)));
            actions.Children.Add(ActionButton("回到右下角", SnapToBottomRight));
            Button? hideBtn = null;
            hideBtn = ActionButton(hidden ? "显示桌宠" : "隐藏桌宠", () => {
                if (hidden) RestorePet();
                else { hidden = true; Hide(); bubbleWindow.Hide(); speechVersion++; brain.CancelGeneration(); }
                if (hideBtn != null) hideBtn.Content = hidden ? "显示桌宠" : "隐藏桌宠";
            });
            actions.Children.Add(hideBtn);
            actions.Children.Add(ActionButton("关闭桌宠", () => { settings?.Close(); Close(); }));
            body.Children.Add(actions);
        }
        void Model() {
            Title("模型与天气");
            var modelStatus = Paragraph("");
            void RefreshModelStatus() {
                string power = CompanionNative.OnAC ? "已插电" : Preferences.StopModelOnBattery ? "电池供电 · 已按设置暂停模型" : "电池供电 · 允许模型运行";
                modelStatus.Text = $"{power}\n推理设备：{brain.ExecutionDevice}\n{(brain.ModelAvailable ? brain.Status : "尚未安装模型")}";
            }
            RefreshModelStatus(); tickStatus = RefreshModelStatus; body.Children.Add(modelStatus);
            Toggle("启用本地大模型", Preferences.ModelEnabled, value => {
                Preferences.ModelEnabled = value;
                if (!value) { speechVersion++; brain.Stop(); }
                else { nextWarmup = DateTime.MinValue; nextSpeech = DateTime.UtcNow.AddSeconds(20); }
                RefreshModelStatus();
            });
            Toggle("使用显卡加速（关闭则使用 CPU）", Preferences.GpuEnabled, value => {
                Preferences.GpuEnabled = value;
                speechVersion++; brain.Stop(); brain.UseGpu = value; nextWarmup = DateTime.MinValue;
                RefreshModelStatus();
            });
            Toggle("拔电时暂停并卸载模型", Preferences.StopModelOnBattery, value => {
                Preferences.StopModelOnBattery = value;
                brain.StopOnBattery = value;
                if (!brain.CanRunOnCurrentPower) { speechVersion++; brain.Stop(); loading.IsVisible = false; if (modelReply) bubbleWindow.Hide(); }
                nextWarmup = DateTime.MinValue;
                RefreshModelStatus();
            });
            Toggle("自动获取天气和科技新闻", Preferences.PublicInfo, value => Preferences.PublicInfo = value);
            Toggle("感知当前应用（仅在本机处理）", Preferences.ForegroundEnabled, value => Preferences.ForegroundEnabled = value);
            body.Children.Add(ActionButton(brain.ModelAvailable ? "管理本地模型" : "安装模型", async () => { await InstallModel(); RefreshModelStatus(); }));
            Select("说话间隔", CompanionPreferences.SpeechRanges.Select(r => $"说话间隔：{r.Min}–{r.Max} 分钟").ToArray(), Preferences.SpeechRangeIndex, value => { Preferences.SpeechMinMinutes = CompanionPreferences.SpeechRanges[value].Min; nextSpeech = DateTime.UtcNow.Add(Preferences.NextSpeechDelay(random)); });
            body.Children.Add(new TextBlock { Text = "天气地点", Margin = new Thickness(0, 12, 0, 4) });
            var regionsGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
            ComboBox RegionColumn(string caption, int col)
            {
                var colPanel = new StackPanel { Margin = new Thickness(col == 0 ? 0 : 8, 0, 0, 0) };
                colPanel.Children.Add(new TextBlock { Text = caption, FontSize = 12, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 0, 0, 4) });
                var cb = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
                colPanel.Children.Add(cb);
                Grid.SetColumn(colPanel, col);
                regionsGrid.Children.Add(colPanel);
                return cb;
            }
            var province = RegionColumn("省 / 自治区 / 直辖市", 0);
            var city = RegionColumn("市 / 州", 1);
            var district = RegionColumn("县 / 区", 2);
            body.Children.Add(regionsGrid);
            var selected = CompanionRegions.District(Preferences.WeatherRegionCode); var provinces = CompanionRegions.All.Where(r => r.Level == 0).ToArray();
            CompanionRegion[] cities = [], districts = []; bool changing = true;
            void PopulateCity(CompanionRegion choice, string? keep = null) { cities = CompanionRegions.Children(choice.Code); city.ItemsSource = cities.Select(r => r.Name).ToArray(); city.SelectedIndex = Math.Max(0, Array.FindIndex(cities, r => r.Code == keep)); }
            void PopulateDistrict(CompanionRegion choice, string? keep = null) { districts = CompanionRegions.Children(choice.Code); district.ItemsSource = districts.Select(r => r.Name).ToArray(); district.SelectedIndex = Math.Max(0, Array.FindIndex(districts, r => r.Code == keep)); }
            province.ItemsSource = provinces.Select(r => r.Name).ToArray(); province.SelectedIndex = Array.FindIndex(provinces, r => r.Code == CompanionRegions.Parent(CompanionRegions.Parent(selected)).Code);
            PopulateCity(provinces[province.SelectedIndex], CompanionRegions.Parent(selected).Code); PopulateDistrict(cities[city.SelectedIndex], selected.Code); changing = false;
            void RegionChanged() {
                if (district.SelectedIndex >= 0 && district.SelectedIndex < districts.Length) {
                    string newCode = districts[district.SelectedIndex].Code;
                    if (Preferences.WeatherRegionCode != newCode) { speechVersion++; brain.CancelGeneration(); }
                    Preferences.WeatherRegionCode = newCode;
                    brain.WeatherRegionCode = newCode;
                    Save();
                }
            }
            province.SelectionChanged += (_, _) => { if (changing || province.SelectedIndex < 0) return; changing = true; PopulateCity(provinces[province.SelectedIndex]); PopulateDistrict(cities[city.SelectedIndex]); changing = false; RegionChanged(); };
            city.SelectionChanged += (_, _) => { if (changing || city.SelectedIndex < 0) return; changing = true; PopulateDistrict(cities[city.SelectedIndex]); changing = false; RegionChanged(); };
            district.SelectionChanged += (_, _) => { if (!changing) RegionChanged(); };
            body.Children.Add(Paragraph("天气来自 Open-Meteo，按所选区县中心附近估算。"));
        }
        void Animation() {
            Title("动作与作息");
            Slider("动作触发间隔", Preferences.AnimationIntervalSeconds, 30, 300, value => { Preferences.AnimationIntervalSeconds = (int)value; nextEvent = DateTime.UtcNow.AddSeconds(value); }, 10, " 秒");
            var hours = Enumerable.Range(0, 24).Select(h => $"{h:00}:00").ToArray();
            var mealsGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), Margin = new Thickness(0, 10, 0, 8) };
            void MealColumn(string labelText, int col, int currentHour, Action<int> setHour)
            {
                var colPanel = new StackPanel { Margin = new Thickness(col == 0 ? 0 : 8, 0, 0, 0) };
                colPanel.Children.Add(new TextBlock { Text = labelText, Margin = new Thickness(0, 0, 0, 4) });
                var cb = new ComboBox { ItemsSource = hours, SelectedIndex = currentHour, HorizontalAlignment = HorizontalAlignment.Stretch };
                cb.SelectionChanged += (_, _) => { if (cb.SelectedIndex >= 0) { setHour(cb.SelectedIndex); Save(); } };
                colPanel.Children.Add(cb);
                Grid.SetColumn(colPanel, col);
                mealsGrid.Children.Add(colPanel);
            }
            MealColumn("早餐时间", 0, Preferences.BreakfastHour, v => Preferences.BreakfastHour = v);
            MealColumn("午餐时间", 1, Preferences.LunchHour, v => Preferences.LunchHour = v);
            MealColumn("晚餐时间", 2, Preferences.DinnerHour, v => Preferences.DinnerHour = v);
            body.Children.Add(mealsGrid);

            body.Children.Add(new TextBlock { Text = $"动作图鉴与点播（共 {policy.Rules.Count} 个动作）", FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 16, 0, 4) });
            var currentLabel = Paragraph($"当前动作：{currentAction}（{phase}）");
            void RefreshActionStatus() => currentLabel.Text = $"当前动作：{currentAction}（{phase}）";
            tickStatus = RefreshActionStatus;
            body.Children.Add(currentLabel);

            var quickRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            quickRow.Children.Add(ActionButton("🎲 随机播放动作", () => { PlayRandomAction(); RefreshActionStatus(); }));
            quickRow.Children.Add(ActionButton("恢复待机动作", () => { Idle(); RefreshActionStatus(); }));
            body.Children.Add(quickRow);

            var searchBox = new TextBox { PlaceholderText = "搜索动作名称（例如：猫耳、火锅、舞、写代码）…", Margin = new Thickness(0, 0, 0, 8) };
            body.Children.Add(searchBox);

            var groups = ActionCategories();
            string activeCategory = "全部";
            var categoryBar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            var actionsWrap = new WrapPanel();

            void RenderActions()
            {
                actionsWrap.Children.Clear();
                string keyword = (searchBox.Text ?? "").Trim();
                IEnumerable<CompanionAnimationRule> source = activeCategory == "全部"
                    ? policy.Rules
                    : groups.FirstOrDefault(g => g.Category == activeCategory).Rules ?? [];
                if (keyword.Length > 0)
                    source = source.Where(r => r.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase));
                foreach (var rule in source)
                {
                    var captured = rule;
                    bool isCurrent = captured.Id == currentAction;
                    var btn = new Button
                    {
                        Content = captured.Id,
                        Padding = new Thickness(12, 6),
                        CornerRadius = new CornerRadius(7),
                        Margin = new Thickness(0, 3, 8, 3),
                        FontSize = 13,
                        Background = isCurrent ? new SolidColorBrush(Color.FromRgb(220, 234, 255)) : new SolidColorBrush(Color.FromRgb(247, 249, 253)),
                        Foreground = new SolidColorBrush(Color.FromRgb(49, 64, 94)),
                        BorderBrush = new SolidColorBrush(isCurrent ? Color.FromRgb(89, 124, 200) : Color.FromRgb(220, 229, 242)),
                        BorderThickness = new Thickness(1)
                    };
                    btn.Click += (_, _) => { PlayManualAction(captured); RefreshActionStatus(); RenderActions(); };
                    actionsWrap.Children.Add(btn);
                }
            }

            void BuildCategoryBar()
            {
                categoryBar.Children.Clear();
                var allTabs = new List<(string Name, int Count)> { ("全部", policy.Rules.Count) };
                allTabs.AddRange(groups.Select(g => (g.Category, g.Rules.Length)));
                foreach (var tab in allTabs)
                {
                    string cat = tab.Name;
                    bool isSelected = activeCategory == cat;
                    var tabBtn = new Button
                    {
                        Content = $"{cat} ({tab.Count})",
                        Padding = new Thickness(12, 5),
                        CornerRadius = new CornerRadius(14),
                        Margin = new Thickness(0, 2, 6, 4),
                        FontSize = 12,
                        Background = isSelected ? new SolidColorBrush(Color.FromRgb(89, 124, 200)) : Brushes.White,
                        Foreground = isSelected ? Brushes.White : new SolidColorBrush(Color.FromRgb(49, 64, 94)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(212, 218, 235)),
                        BorderThickness = new Thickness(1)
                    };
                    tabBtn.Click += (_, _) => { activeCategory = cat; BuildCategoryBar(); RenderActions(); };
                    categoryBar.Children.Add(tabBtn);
                }
            }

            searchBox.TextChanged += (_, _) => RenderActions();
            BuildCategoryBar();
            RenderActions();
            body.Children.Add(categoryBar);
            body.Children.Add(actionsWrap);
        }
        void About() {
            Title("关于与更新");
            body.Children.Add(Paragraph("蓝色大肥鱼 " + version + (McpAvailable ? "" : " · 无MCP版")));
            Toggle("启动时检查更新", Preferences.CheckUpdatesOnStartup, value => Preferences.CheckUpdatesOnStartup = value);
            var updateLabel = Paragraph(updateStatus);
            void RefreshUpdateStatus() => updateLabel.Text = updateStatus;
            tickStatus = RefreshUpdateStatus;
            body.Children.Add(ActionButton("检查更新", async () => { RefreshUpdateStatus(); await CheckUpdates(true); RefreshUpdateStatus(); }));
            body.Children.Add(updateLabel);
        }
        AddNav("陪伴与外观", General);
        AddNav("模型与天气", Model);
        AddNav("动作与作息", Animation);
#if !COMPANION_SAFE
        void Agent() {
            Title("Agent 与 MCP");
            var enabled = new CheckBox { Content = "开启 MCP 接口", IsChecked = Preferences.McpEnabled }; body.Children.Add(enabled); body.Children.Add(Paragraph("开启后行为可被其他 Agent 控制"));
            var extra = new StackPanel { IsVisible = Preferences.McpEnabled, Spacing = 10 };
            var connStatus = Paragraph("");
            void RefreshMcpStatus() => connStatus.Text = $"已连接 {mcpServer?.Connections ?? 0} 个 MCP 客户端";
            RefreshMcpStatus(); tickStatus = RefreshMcpStatus; extra.Children.Add(connStatus);
            var puppet = new CheckBox { Content = "提线木偶模式", IsChecked = Preferences.PuppetMode }; extra.Children.Add(puppet); extra.Children.Add(Paragraph("关闭大肥鱼自身的吐字和动作切换逻辑，完全由外部 Agent 接管"));
            var chat = new CheckBox { Content = "双击与鱼鱼对话", IsChecked = Preferences.McpChatEnabled };
            chat.Click += (_, _) => { Preferences.McpChatEnabled = chat.IsChecked == true; if (!Preferences.McpChatEnabled) mcpInput?.Close(); Save(); };
            extra.Children.Add(chat); extra.Children.Add(Paragraph("仅在 MCP 客户端已连接时生效，输入内容通过 MCP 发送给已连接的 Agent。"));
            var wake = new CheckBox { Content = "允许鱼鱼通过 MCP 触发 Agent", IsChecked = Preferences.McpWakeEnabled };
            wake.Click += (_,_) => { Preferences.McpWakeEnabled = wake.IsChecked == true; mcpServer?.SetWakeEnabled(Preferences.McpWakeEnabled); Save(); };
            extra.Children.Add(wake); extra.Children.Add(Paragraph("通过客户端 sampling 请求响应，由客户端决定是否接受。"));
            var wakeFlags = Paragraph(""); extra.Children.Add(wakeFlags);
            var wakeClock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            void WakeStatus() {
                string Flag(bool on) => on ? "唤起" : "不唤起";
                wakeFlags.Text = $"文字：{Flag(Preferences.McpWakeOnText)}；点击：{Flag(Preferences.McpWakeOnClick)}；拖动：{Flag(Preferences.McpWakeOnDrag)}。三项由 Agent 调整。支持触发的连接：{mcpServer?.SamplingConnections ?? 0}；{mcpServer?.WakeStatus ?? "尚未请求"}";
            }
            WakeStatus(); wakeClock.Tick += (_,_) => WakeStatus(); wakeClock.Start();
            extra.DetachedFromVisualTree += (_,_) => wakeClock.Stop();
            Button? config = null;
            config = ActionButton("复制 MCP 连接配置", async () => {
                if (settings?.Clipboard != null) {
                    await settings.Clipboard.SetTextAsync(System.Text.Json.JsonSerializer.Serialize(new { mcpServers = new Dictionary<string, object> { ["big-blue-fish"] = new { command = Path.Combine(AppContext.BaseDirectory, "BigBlueFish.Mcp"), args = Array.Empty<string>() } } }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    if (config != null) config.Content = "连接配置已复制";
                }
            }); extra.Children.Add(config); body.Children.Add(extra);
            enabled.Click += async (_, _) => {
                bool on = enabled.IsChecked == true;
                if (on && !await Confirm("开启 MCP 接口", ControlTools.PrivacyWarning, "了解并开启")) { enabled.IsChecked = false; return; }
                if (on && !File.Exists(Path.Combine(AppContext.BaseDirectory, "BigBlueFish.Mcp"))) { await Confirm("MCP 组件未就绪", "请使用包含 MCP 组件的完整标准版安装包。"); enabled.IsChecked = false; return; }
                if (!on) SetPuppet(false); Preferences.McpEnabled = on; if (on) InitializeMcp(); else StopMcp(); extra.IsVisible = on; puppet.IsChecked = Preferences.PuppetMode; RefreshMcpStatus(); Save();
            };
            puppet.Click += async (_, _) => { bool on = puppet.IsChecked == true; if (on && !await Confirm("开启提线木偶模式", ControlTools.PuppetWarning, "了解并开启")) { puppet.IsChecked = false; return; } SetPuppet(on); Save(); };
        }
        AddNav("Agent 与 MCP", Agent);
#endif
        AddNav("关于与更新", About);
        refreshLinuxSettings = () => { var offset = scroll.Offset; UpdateNavHighlight(); selectedPage(); scroll.Offset = offset; };
        UpdateNavHighlight();
        General(); settings.Show(); settings.Activate();
    }
}


