namespace Pix.Launcher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstance = new SingleInstanceCoordinator();
        if (!singleInstance.IsPrimaryInstance)
        {
            singleInstance.SignalExistingInstance();
            return;
        }

        ApplicationConfiguration.Initialize();
        using var form = new LauncherForm();
        form.Shown += (_, _) => singleInstance.StartListening(form.ShowControlPanel);
        Application.Run(form);
    }
}
