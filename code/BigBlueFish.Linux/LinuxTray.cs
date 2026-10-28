using Avalonia.Controls;
using Avalonia.Threading;

namespace BigBlueFish.Windows;

internal sealed partial class LinuxPetWindow
{
    TrayIcon? tray;
    void RestorePet() { hidden = false; Show(); player.Paused = false; Clamp(); }
    void InitializeTray()
    {
        var menu = new NativeMenu();
        NativeMenuItem Item(string title, Action action) { var item = new NativeMenuItem(title); item.Click += (_, _) => action(); menu.Items.Add(item); return item; }
        Item("显示桌宠", RestorePet);
        var actionsRoot = new NativeMenuItem($"播放动作（{policy.Rules.Count}）") { Menu = new NativeMenu() };
        var randomItem = new NativeMenuItem("🎲 随机播放动作"); randomItem.Click += (_, _) => PlayRandomAction();
        actionsRoot.Menu.Items.Add(randomItem);
        actionsRoot.Menu.Items.Add(new NativeMenuItemSeparator());
        foreach (var group in ActionCategories())
        {
            var groupMenu = new NativeMenu();
            foreach (var rule in group.Rules)
            {
                var r = rule;
                var subItem = new NativeMenuItem(r.Id);
                subItem.Click += (_, _) => PlayManualAction(r);
                groupMenu.Items.Add(subItem);
            }
            actionsRoot.Menu.Items.Add(new NativeMenuItem($"{group.Category}（{group.Rules.Length}）") { Menu = groupMenu });
        }
        menu.Items.Add(actionsRoot);
        Item("设置", OpenSettings);
        var weather = Item("天气", async () => await Speak(true)); var speech = Item("吐字测试", async () => await Speak(false));
        Item("回到右下角", SnapToBottomRight);
        Item("关闭桌宠", Close);
        tray = new TrayIcon { Icon = new WindowIcon(Path.Combine(AppContext.BaseDirectory, "BigBlueFish.ico")), ToolTipText = "蓝色大肥鱼", Menu = menu, IsVisible = true };
        tray.Clicked += (_, _) => RestorePet();
        TrayIcon.SetIcons(Avalonia.Application.Current!, new TrayIcons { tray });
        var status = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        status.Tick += (_, _) => { weather.IsEnabled = speech.IsEnabled = !Puppet; };
        status.Start(); Closed += (_, _) => { status.Stop(); tray.Dispose(); };
    }
}
