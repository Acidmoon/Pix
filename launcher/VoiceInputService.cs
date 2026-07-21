using System.Runtime.InteropServices;
using System.Text;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SherpaOnnx;

namespace Pix.Launcher;

/// <summary>
/// 系统级语音输入（push-to-talk）：按住右 Ctrl / 右 Alt 说话，松开后
/// 用 SenseVoice 离线识别并把文字注入前台窗口光标处。
/// 参考 Python 版 voice-ime 的实现 1:1 翻译（hotkeys.py / inject.py）。
/// </summary>
internal sealed class VoiceInputService : IDisposable
{
    /// <summary>服务状态，供 UI 显示。</summary>
    public enum Status { Off, Ready, Recording, Recognizing }

    private const int SampleRate = 16000;
    // 短于该时长的录音视为误触，不送识别
    private const double MinRecordSeconds = 0.3;
    // 长于该阈值的文本走剪贴板粘贴而不是逐字键入
    private const int PasteThreshold = 30;

    // VK_RCONTROL / VK_RMENU（右 Alt），按 vkCode 精确匹配，严格区分左右
    private const int VkRightControl = 0xA3;
    private const int VkRightAlt = 0xA5;

    private readonly object audioLock = new();
    private readonly List<float> audioBuffer = new();
    private WaveInEvent? waveIn;

    private Thread? hookThread;
    private readonly ManualResetEventSlim hookThreadReady = new();
    private uint hookThreadId;
    private IntPtr hookHandle;
    // 必须持有委托引用，防止被 GC 回收后钩子回调崩溃
    private readonly NativeMethods.LowLevelKeyboardProc hookProc;

    private int activeVk;
    private IntPtr targetWindow;

    public event Action<Status>? StatusChanged;
    public event Action<string>? ErrorOccurred;

    public bool IsRunning { get; private set; }
    public Status CurrentStatus { get; private set; } = Status.Off;

    public VoiceInputService()
    {
        hookProc = LowLevelKeyboardCallback;
    }

    // ---- 开关 ----

    public void Start()
    {
        if (IsRunning) return;

        waveIn = new WaveInEvent
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1),
            BufferMilliseconds = 50,
        };
        waveIn.DataAvailable += OnWaveDataAvailable;

        hookThreadReady.Reset();
        hookThread = new Thread(HookLoop) { IsBackground = true, Name = "VoiceHotkeyHook" };
        hookThread.Start();
        // 等钩子线程报出线程 id，否则 Stop 时无法投递 WM_QUIT
        hookThreadReady.Wait(TimeSpan.FromSeconds(5));

        IsRunning = true;
        SetStatus(Status.Ready);
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;

        if (waveIn is not null)
        {
            try { waveIn.StopRecording(); } catch { /* 忽略停止录音的异常 */ }
            waveIn.DataAvailable -= OnWaveDataAvailable;
            waveIn.Dispose();
            waveIn = null;
        }

        if (hookThread is not null)
        {
            // 给钩子线程发 WM_QUIT 让它退出消息泵（随之卸载钩子）
            NativeMethods.PostThreadMessageW(hookThreadId, NativeMethods.WmQuit, UIntPtr.Zero, IntPtr.Zero);
            hookThread.Join(TimeSpan.FromSeconds(3));
            hookThread = null;
        }

        lock (audioLock) audioBuffer.Clear();
        activeVk = 0;
        SetStatus(Status.Off);
    }

    public void Dispose() => Stop();

    private void SetStatus(Status status)
    {
        CurrentStatus = status;
        StatusChanged?.Invoke(status);
    }

    // ---- 全局热键（WH_KEYBOARD_LL，钩子线程内跑消息泵） ----

    private void HookLoop()
    {
        hookThreadId = NativeMethods.GetCurrentThreadId();
        hookThreadReady.Set();
        // 低级钩子的 hMod 传 NULL：回调在本进程内，不需要模块句柄
        hookHandle = NativeMethods.SetWindowsHookExW(NativeMethods.WhKeyboardLl, hookProc, IntPtr.Zero, 0);
        if (hookHandle == IntPtr.Zero)
        {
            ErrorOccurred?.Invoke($"安装全局键盘钩子失败（错误 {Marshal.GetLastWin32Error()}）");
        }

        // GetMessage 泵：低级钩子依赖线程消息循环投递，收到 WM_QUIT 返回 false 退出
        while (NativeMethods.GetMessageW(out var msg, IntPtr.Zero, 0, 0))
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessageW(ref msg);
        }

        if (hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(hookHandle);
            hookHandle = IntPtr.Zero;
        }
    }

    private IntPtr LowLevelKeyboardCallback(int nCode, UIntPtr wParam, IntPtr lParam)
    {
        if (nCode == 0)
        {
            var message = (uint)wParam;
            if (message is NativeMethods.WmKeyDown or NativeMethods.WmKeyUp
                or NativeMethods.WmSysKeyDown or NativeMethods.WmSysKeyUp)
            {
                // KBDLLHOOKSTRUCT 第一个字段就是 vkCode
                var vk = Marshal.ReadInt32(lParam);
                if (vk is VkRightControl or VkRightAlt)
                {
                    if (message is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown)
                        HandlePress(vk);
                    else
                        HandleRelease(vk);
                }
            }
        }
        return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private void HandlePress(int vk)
    {
        // 忽略长按自动重复；一次只允许一个会话
        if (Interlocked.CompareExchange(ref activeVk, vk, 0) != 0) return;

        // 记录按下时的前台窗口，注入前若前台变了再拉回来
        targetWindow = NativeMethods.GetForegroundWindow();
        lock (audioLock) audioBuffer.Clear();
        try
        {
            waveIn?.StartRecording();
            SetStatus(Status.Recording);
        }
        catch (Exception error)
        {
            Interlocked.Exchange(ref activeVk, 0);
            ErrorOccurred?.Invoke($"打开麦克风失败：{error.Message}");
        }
    }

    private void HandleRelease(int vk)
    {
        if (Interlocked.CompareExchange(ref activeVk, 0, vk) != vk) return;

        float[] samples;
        try { waveIn?.StopRecording(); } catch { /* 忽略 */ }
        lock (audioLock)
        {
            samples = audioBuffer.ToArray();
            audioBuffer.Clear();
        }

        if (samples.Length < (int)(SampleRate * MinRecordSeconds))
        {
            SetStatus(Status.Ready);
            return;
        }

        SetStatus(Status.Recognizing);
        var hwnd = targetWindow;
        // 识别放线程池，避免阻塞钩子回调（低级钩子有超时，卡太久会被系统跳过）
        ThreadPool.QueueUserWorkItem(_ => RecognizeAndInject(samples, hwnd));
    }

    private void RecognizeAndInject(float[] samples, IntPtr hwnd)
    {
        try
        {
            var text = Transcribe(samples).Trim();
            // 空结果不注入
            if (text.Length > 0) InjectText(text, hwnd);
        }
        catch (Exception error)
        {
            ErrorOccurred?.Invoke($"语音识别失败：{error.Message}");
        }
        finally
        {
            if (IsRunning) SetStatus(Status.Ready);
        }
    }

    private void OnWaveDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        var count = eventArgs.BytesRecorded / sizeof(float);
        var floats = new float[count];
        Buffer.BlockCopy(eventArgs.Buffer, 0, floats, 0, eventArgs.BytesRecorded);
        lock (audioLock) audioBuffer.AddRange(floats);
    }

    // ---- 识别（SenseVoice 离线模型，懒加载单例） ----

    private static readonly object RecognizerLock = new();
    private static OfflineRecognizer? recognizer;

    /// <summary>模型目录解析：环境变量 → 从程序目录向上找 → 固定兜底路径。</summary>
    private static string ResolveModelDir()
    {
        var env = Environment.GetEnvironmentVariable("PIX_VOICE_MODEL_DIR");
        if (!string.IsNullOrWhiteSpace(env) && IsModelDir(env)) return env;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "models", "sensevoice-small-int8");
            if (IsModelDir(candidate)) return candidate;
            dir = dir.Parent;
        }

        const string fallback = @"E:\Pix\models\sensevoice-small-int8";
        if (IsModelDir(fallback)) return fallback;
        throw new FileNotFoundException("未找到 SenseVoice 模型目录（需要 model.int8.onnx 和 tokens.txt）");
    }

    private static bool IsModelDir(string dir)
        => File.Exists(Path.Combine(dir, "model.int8.onnx")) && File.Exists(Path.Combine(dir, "tokens.txt"));

    private static OfflineRecognizer GetRecognizer()
    {
        lock (RecognizerLock)
        {
            if (recognizer is not null) return recognizer;

            var modelDir = ResolveModelDir();
            var config = new OfflineRecognizerConfig();
            config.FeatConfig.SampleRate = SampleRate;
            config.FeatConfig.FeatureDim = 80;
            config.ModelConfig.SenseVoice.Model = Path.Combine(modelDir, "model.int8.onnx");
            config.ModelConfig.SenseVoice.UseInverseTextNormalization = 1;
            config.ModelConfig.Tokens = Path.Combine(modelDir, "tokens.txt");
            config.ModelConfig.NumThreads = 4;
            config.ModelConfig.Provider = "cpu";
            config.ModelConfig.Debug = 0;
            recognizer = new OfflineRecognizer(config);
            return recognizer;
        }
    }

    /// <summary>识别一段 16kHz 单声道 float 音频。</summary>
    public static string Transcribe(float[] samples)
    {
        OfflineRecognizer offline;
        lock (RecognizerLock) offline = GetRecognizer();
        // Decode 非线程安全，串行化（实际同一时刻也只有一个会话）
        lock (RecognizerLock)
        {
            var stream = offline.CreateStream();
            stream.AcceptWaveform(SampleRate, samples);
            offline.Decode(stream);
            return stream.Result.Text;
        }
    }

    /// <summary>自测用：转写一个 wav 文件（任意采样率/声道，重采样到 16k 单声道）。</summary>
    public static string TranscribeWaveFile(string path)
    {
        ISampleProvider provider = new AudioFileReader(path);
        if (provider.WaveFormat.Channels == 2) provider = provider.ToMono();
        if (provider.WaveFormat.SampleRate != SampleRate)
            provider = new WdlResamplingSampleProvider(provider, SampleRate);

        var samples = new List<float>();
        var chunk = new float[SampleRate];
        int read;
        while ((read = provider.Read(chunk, 0, chunk.Length)) > 0)
            for (var i = 0; i < read; i++) samples.Add(chunk[i]);
        return Transcribe(samples.ToArray());
    }

    // ---- 文字注入 ----

    /// <summary>把文本注入前台窗口；targetWindow 非空且前台已变化时先恢复前台。</summary>
    public static void InjectText(string text, IntPtr targetWindow)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (targetWindow != IntPtr.Zero && NativeMethods.GetForegroundWindow() != targetWindow)
        {
            BringToForeground(targetWindow);
            Thread.Sleep(100); // 等焦点切换稳定
        }

        if (text.Length > PasteThreshold) PasteText(text);
        else TypeText(text);
    }

    /// <summary>SendInput + KEYEVENTF_UNICODE 逐字符输入，支持任意 Unicode（C# char 即 UTF-16 码元，代理对自然处理）。</summary>
    private static void TypeText(string text)
    {
        foreach (var codeUnit in text)
        {
            var inputs = new[]
            {
                NativeMethods.Input.UnicodeKey(codeUnit, keyUp: false),
                NativeMethods.Input.UnicodeKey(codeUnit, keyUp: true),
            };
            NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
            Thread.Sleep(5); // 字符间留小间隔，避免过快被目标应用丢弃
        }
    }

    /// <summary>剪贴板粘贴：备份 → 写入 → Ctrl+V → 恢复。</summary>
    private static void PasteText(string text)
    {
        var old = TryGetClipboardText();
        SetClipboardText(text);
        Thread.Sleep(50); // 等剪贴板就绪

        // 模拟 Ctrl+V
        var inputs = new[]
        {
            NativeMethods.Input.VirtualKey(NativeMethods.VkControl, keyUp: false),
            NativeMethods.Input.VirtualKey(0x56, keyUp: false), // 'V'
            NativeMethods.Input.VirtualKey(0x56, keyUp: true),
            NativeMethods.Input.VirtualKey(NativeMethods.VkControl, keyUp: true),
        };
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
        Thread.Sleep(150); // 等目标应用完成粘贴再恢复剪贴板

        if (old is not null) SetClipboardText(old);
    }

    /// <summary>
    /// Windows 有前台锁：后台进程默认无权抢占前台。先注入一次无害的
    /// Ctrl 按键事件，本进程即可获得 SetForegroundWindow 权限。
    /// </summary>
    private static void BringToForeground(IntPtr hwnd)
    {
        NativeMethods.keybd_event(NativeMethods.VkControl, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VkControl, 0, NativeMethods.KeyeventfKeyUp, UIntPtr.Zero);
        Thread.Sleep(50);
        NativeMethods.ShowWindow(hwnd, NativeMethods.SwRestore);
        NativeMethods.SetForegroundWindow(hwnd);
        NativeMethods.SetActiveWindow(hwnd);
    }

    // ---- 剪贴板（原生 API，避免 WinForms Clipboard 的 STA 线程限制） ----

    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x2002;

    private static string? TryGetClipboardText()
    {
        if (!OpenClipboardWithRetry()) return null;
        try
        {
            var handle = NativeMethods.GetClipboardData(CfUnicodeText);
            if (handle == IntPtr.Zero) return null;
            var pointer = NativeMethods.GlobalLock(handle);
            if (pointer == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUni(pointer); }
            finally { NativeMethods.GlobalUnlock(handle); }
        }
        finally { NativeMethods.CloseClipboard(); }
    }

    private static void SetClipboardText(string text)
    {
        if (!OpenClipboardWithRetry()) return;
        try
        {
            NativeMethods.EmptyClipboard();
            var bytes = (text.Length + 1) * 2;
            var handle = NativeMethods.GlobalAlloc(GmemMoveable, (UIntPtr)bytes);
            var pointer = NativeMethods.GlobalLock(handle);
            Marshal.Copy(text.ToCharArray(), 0, pointer, text.Length);
            Marshal.WriteInt16(pointer, text.Length * 2, 0);
            NativeMethods.GlobalUnlock(handle);
            NativeMethods.SetClipboardData(CfUnicodeText, handle);
        }
        finally { NativeMethods.CloseClipboard(); }
    }

    private static bool OpenClipboardWithRetry()
    {
        // 剪贴板可能被其他进程短暂占用，重试几次
        for (var i = 0; i < 10; i++)
        {
            if (NativeMethods.OpenClipboard(IntPtr.Zero)) return true;
            Thread.Sleep(20);
        }
        return false;
    }

    // ---- Win32 P/Invoke ----

    private static class NativeMethods
    {
        public const int WhKeyboardLl = 13;
        public const uint WmKeyDown = 0x0100;
        public const uint WmKeyUp = 0x0101;
        public const uint WmSysKeyDown = 0x0104;
        public const uint WmSysKeyUp = 0x0105;
        public const uint WmQuit = 0x0012;
        public const byte VkControl = 0x11;
        public const uint KeyeventfKeyUp = 0x0002;
        public const int SwRestore = 9;

        private const uint InputKeyboard = 1;
        private const uint KeyeventfUnicode = 0x0004;

        public delegate IntPtr LowLevelKeyboardProc(int nCode, UIntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct Msg
        {
            public IntPtr Hwnd;
            public uint Message;
            public UIntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public int PtX;
            public int PtY;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KeyboardInput
        {
            public ushort VK;
            public ushort Scan;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        // 占位：Win32 的 INPUT 共用体按最大的 MOUSEINPUT 定尺寸。
        // x64 下 sizeof(INPUT) 必须是 40，否则 SendInput 报参数错误（87）静默失败。
        [StructLayout(LayoutKind.Sequential)]
        public struct MouseInput
        {
            public int Dx;
            public int Dy;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MouseInput Mouse;
            [FieldOffset(0)] public KeyboardInput Keyboard;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Input
        {
            public uint Type;
            public InputUnion Union;

            public static Input UnicodeKey(char codeUnit, bool keyUp)
                => new()
                {
                    Type = InputKeyboard,
                    Union = new InputUnion
                    {
                        Keyboard = new KeyboardInput
                        {
                            VK = 0,
                            Scan = codeUnit,
                            Flags = KeyeventfUnicode | (keyUp ? KeyeventfKeyUp : 0),
                        },
                    },
                };

            public static Input VirtualKey(ushort vk, bool keyUp)
                => new()
                {
                    Type = InputKeyboard,
                    Union = new InputUnion
                    {
                        Keyboard = new KeyboardInput { VK = vk, Flags = keyUp ? KeyeventfKeyUp : 0 },
                    },
                };
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookExW(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, UIntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMessageW(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        public static extern bool TranslateMessage(ref Msg lpMsg);

        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessageW(ref Msg lpMsg);

        [DllImport("user32.dll")]
        public static extern bool PostThreadMessageW(uint idThread, uint msg, UIntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, [In] Input[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr SetActiveWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll")]
        public static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        public static extern bool EmptyClipboard();

        [DllImport("user32.dll")]
        public static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll")]
        public static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        public static extern bool GlobalUnlock(IntPtr hMem);
    }
}
