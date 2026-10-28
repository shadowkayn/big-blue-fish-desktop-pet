using System.Text.Json;
using Avalonia;
using Avalonia.Media.Imaging;

namespace BigBlueFish.Windows;

internal sealed partial class LinuxPetWindow
{
    DateTime nextTestStatus;
    async Task TestCommands()
    {
        if (!App.Args.Contains("--test-mode")) return;
        if (DateTime.UtcNow >= nextTestStatus) {
            nextTestStatus = DateTime.UtcNow.AddSeconds(1);
            File.WriteAllText("test-status.json", JsonSerializer.Serialize(new { version = typeof(LinuxPetWindow).Assembly.GetName().Version!.ToString(3), currentAction, phase, frames = player.Frames, player.LastReadMs, player.LastUiMs, player.LastCopyMs, player.LastShapeMs, text = bubbleText.Text, bubbleVisible = bubbleWindow.IsVisible, Puppet, McpAvailable, visible = IsVisible, hidden, fullscreenHidden, modelPid = brain.RunnerPid, modelBusy = brain.Busy, modelStatus = brain.Status, generating, settingsOpen = settings != null, Position.X, Position.Y }));
        }
        if (!File.Exists("test-command.txt")) return;
        string command = File.ReadAllText("test-command.txt").Trim(); File.Delete("test-command.txt");
        try {
            switch (command)
            {
                case "settings": OpenSettings(); break;
                case "close-settings": settings?.Close(); break;
                case "speech-test": await Speak(false); break;
                case "weather": await Speak(true); break;
                case "bubble": ShowText("主人，小女仆今天也元气满满地来报到啦～", false, fromModel: true); break;
                case "bubble-next": ShowText("主人，下一句已经送到啦，小尾巴也跟着摇起来～", false, fromModel: true); break;
                case "battery": CompanionNative.TestPowerOverride = false; break;
                case "ac": CompanionNative.TestPowerOverride = true; nextWarmup = DateTime.MinValue; break;
                case "real-power": CompanionNative.TestPowerOverride = null; break;
                case "model-off": Preferences.ModelEnabled = false; brain.Stop(); break;
                case "model-on": Preferences.ModelEnabled = true; nextWarmup = DateTime.MinValue; break;
                case "generate": nextSpeech = DateTime.UtcNow; break;
                case "hide": hidden = true; Hide(); bubbleWindow.Hide(); break;
                case "show": case "restore": RestorePet(); break;
                case "close": Close(); break;
                case "exit": CloseImmediately(); break;
                case "ocr":
                    var ocr = await CompanionOcr.ReadAsync(CompanionNative.ResolveForeground(CompanionNative.GetForegroundWindow(), lastForeground), CancellationToken.None);
                    File.WriteAllText("ocr-status.json", JsonSerializer.Serialize(ocr)); break;
                case "render": RenderControl(this, "pet.png"); break;
                case "render-settings": if (settings != null) RenderControl(settings, "settings.png"); break;
#if !COMPANION_SAFE
                case "render-input": if (mcpInput != null) RenderControl(mcpInput, "input.png"); break;
#endif
                default:
                    if (command.StartsWith("bubble ")) ShowText(command[7..], true);
                    else if (command.StartsWith("event:")) Event(policy.Find(command[6..]));
                    else if (command.StartsWith("action:"))
                    {
                        string name = command[7..];
                        if (name == "drag") { moveRule = null; Play(policy.Find("被鼠标拖拽悬空反馈"), "Drag"); }
                        else
                        {
                            string id = name switch { "idle" => "待机呼吸休闲", "wave" => "点击回应-元气挥手", "angry" => "点击回应-傲娇生气", "sleep" => "原地小憩沉眠", _ => name };
                            var rule = policy.Rules.FirstOrDefault(r => r.Id == id);
                            if (rule != null) PlayManualAction(rule);
                        }
                    }
                    break;
            }
        } catch (Exception error) { File.WriteAllText("test-error.txt", error.ToString()); }
    }
    static void RenderControl(Avalonia.Controls.Control control, string path)
    {
        using var image = new RenderTargetBitmap(new PixelSize((int)control.Bounds.Width, (int)control.Bounds.Height)); image.Render(control); image.Save(path, PngBitmapEncoderOptions.Default);
    }
}


