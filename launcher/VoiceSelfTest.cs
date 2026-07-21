using System.Runtime.InteropServices;
using System.Text;

namespace Pix.Launcher;

/// <summary>
/// 语音功能的命令行自测入口：
/// --voice-selftest &lt;wav路径&gt;  不走热键和麦克风，直接转写 wav 打印结果；
/// --inject-selftest &lt;文本&gt;    弹一个带文本框的窗口，把文本注入进去供人工/脚本断言。
/// </summary>
internal static class VoiceSelfTest
{
    private const uint AttachParentProcess = unchecked((uint)-1);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    /// <summary>WinExe 默认没有控制台，附加到父控制台并重建标准输出。</summary>
    private static void AttachParentConsole()
    {
        if (!AttachConsole(AttachParentProcess)) return;
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        try { Console.OutputEncoding = Encoding.UTF8; } catch { /* 编码设置失败不影响功能 */ }
    }

    /// <summary>转写指定 wav（16kHz 语音），结果打印到控制台，退出码 0/1 表成败。</summary>
    public static int RunVoiceSelfTest(string wavPath)
    {
        AttachParentConsole();
        try
        {
            if (!File.Exists(wavPath))
            {
                Console.Error.WriteLine($"文件不存在: {wavPath}");
                return 1;
            }
            Console.WriteLine($"正在加载模型并转写: {wavPath}");
            var text = VoiceInputService.TranscribeWaveFile(wavPath).Trim();
            Console.WriteLine($"识别结果: {text}");
            return text.Length > 0 ? 0 : 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"识别失败: {error.Message}");
            return 1;
        }
        finally
        {
            FreeConsole();
        }
    }

    /// <summary>
    /// 弹一个置前台的窗口，把文本注入文本框；窗口标题显示注入结果（OK/FAIL），
    /// 停留几秒后自动关闭，退出码 0/1 表成败。
    /// </summary>
    public static int RunInjectSelfTest(string text)
    {
        AttachParentConsole();
        ApplicationConfiguration.Initialize();

        var exitCode = 1;
        using var form = new Form
        {
            Text = "语音注入自测",
            Width = 560,
            Height = 180,
            StartPosition = FormStartPosition.CenterScreen,
            TopMost = true,
            Font = new Font("Microsoft YaHei UI", 10F),
        };
        var box = new TextBox { Dock = DockStyle.Fill, Multiline = true };
        form.Controls.Add(box);

        form.Shown += async (_, _) =>
        {
            form.Activate();
            box.Focus();
            await Task.Delay(400); // 等前台焦点稳定
            VoiceInputService.InjectText(text, form.Handle);
            await Task.Delay(1000); // 等逐字注入完成

            var ok = box.Text.Contains(text, StringComparison.Ordinal);
            form.Text = ok ? $"注入自测 OK: {box.Text}" : $"注入自测 FAIL: '{box.Text}'";
            Console.WriteLine(form.Text);
            exitCode = ok ? 0 : 1;

            await Task.Delay(3000); // 留时间人工查看
            form.Close();
        };

        Application.Run(form);
        FreeConsole();
        return exitCode;
    }
}
