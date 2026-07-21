namespace Pix.Launcher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // 自测分支：不启动单实例守卫和主窗口
        if (args is ["--voice-selftest", var wavPath, ..])
            return VoiceSelfTest.RunVoiceSelfTest(wavPath);
        if (args is ["--inject-selftest", var injectText, ..])
            return VoiceSelfTest.RunInjectSelfTest(injectText);

        using var singleInstance = new SingleInstanceCoordinator();
        if (!singleInstance.IsPrimaryInstance)
        {
            singleInstance.SignalExistingInstance();
            return 0;
        }

        ApplicationConfiguration.Initialize();
        using var form = new LauncherForm();
        form.Shown += (_, _) => singleInstance.StartListening(form.ShowControlPanel);
        Application.Run(form);
        return 0;
    }
}
