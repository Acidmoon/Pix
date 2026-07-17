using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Pix.Launcher;

/// <summary>
/// Owns a Windows Job Object configured to terminate every assigned process
/// when the launcher closes its handle.
/// </summary>
internal sealed class WindowsJobObject : IDisposable
{
    private const uint CreateNoWindow = 0x08000000;
    private const uint CreateSuspended = 0x00000004;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const int ExtendedLimitInformationClass = 9;

    private IntPtr handle;

    public WindowsJobObject()
    {
        handle = CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero) throw new Win32Exception();

        var info = new JobObjectExtendedLimitInformation();
        info.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
        var size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, pointer, false);
            if (!SetInformationJobObject(handle, ExtendedLimitInformationClass, pointer, (uint)size))
            {
                throw new Win32Exception();
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    /// <summary>
    /// Creates the process suspended so it joins the Job Object before it can
    /// spawn descendants, then resumes its primary thread.
    /// </summary>
    public Process StartProcess(
        string executable,
        string arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null)
    {
        var startupInfo = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>() };
        var commandLine = new StringBuilder($"\"{executable}\" {arguments}");
        var environment = BuildEnvironmentBlock(environmentOverrides);
        var environmentHandle = GCHandle.Alloc(environment, GCHandleType.Pinned);

        try
        {
            if (!CreateProcess(
                executable,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                CreateNoWindow | CreateSuspended | CreateUnicodeEnvironment,
                environmentHandle.AddrOfPinnedObject(),
                workingDirectory,
                ref startupInfo,
                out var processInfo))
            {
                throw new Win32Exception();
            }

            try
            {
                if (!AssignProcessToJobObject(handle, processInfo.Process))
                {
                    TerminateProcess(processInfo.Process, 1);
                    throw new Win32Exception();
                }

                if (ResumeThread(processInfo.Thread) == uint.MaxValue)
                {
                    TerminateProcess(processInfo.Process, 1);
                    throw new Win32Exception();
                }

                return Process.GetProcessById((int)processInfo.ProcessId);
            }
            finally
            {
                CloseHandle(processInfo.Thread);
                CloseHandle(processInfo.Process);
            }
        }
        finally
        {
            environmentHandle.Free();
        }
    }

    public void Terminate(uint exitCode = 1)
    {
        if (handle != IntPtr.Zero) TerminateJobObject(handle, exitCode);
    }

    public void Dispose()
    {
        if (handle == IntPtr.Zero) return;
        CloseHandle(handle);
        handle = IntPtr.Zero;
    }

    private static char[] BuildEnvironmentBlock(IReadOnlyDictionary<string, string?>? overrides)
    {
        var values = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                values[key] = value;
            }
        }

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                if (value is null) values.Remove(key);
                else values[key] = value;
            }
        }

        return (string.Join('\0', values.Select(pair => $"{pair.Key}={pair.Value}")) + "\0\0").ToCharArray();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2;
        public IntPtr Reserved2Pointer;
        public IntPtr StdInput;
        public IntPtr StdOutput;
        public IntPtr StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr securityAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(IntPtr job, uint exitCode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(
        string applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr value);
}
