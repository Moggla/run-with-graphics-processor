using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

// Out-of-process COM server for the Windows 11 File Explorer context menu entry
// "Run with NVIDIA GPU". Activated by explorer.exe through the package identity
// declared in AppxManifest.xml (com:ExeServer, Arguments="-Embedding"). Launches
// the selected file with SHIM_MCCOMPAT/SHIM_RENDERING_MODE set only in the child
// process environment - no registry entry, no permanent change, applies to this
// one launch only.

internal static class HResult
{
    public const int S_OK = 0;
    public const int E_NOTIMPL = unchecked((int)0x80004001);
    public const int E_FAIL = unchecked((int)0x80004005);
    public const int CLASS_E_NOAGGREGATION = unchecked((int)0x80040110);
}

[Flags]
internal enum EXPCMDFLAGS
{
    ECF_DEFAULT = 0x000,
}

internal enum EXPCMDSTATE
{
    ECS_ENABLED = 0x00,
    ECS_DISABLED = 0x01,
    ECS_HIDDEN = 0x02,
}

internal enum SIGDN : uint
{
    SIGDN_FILESYSPATH = 0x80058000,
}

internal enum SIATTRIBFLAGS : uint
{
    SIATTRIBFLAGS_AND = 0x1,
    SIATTRIBFLAGS_OR = 0x2,
}

[StructLayout(LayoutKind.Sequential)]
internal struct PROPERTYKEY
{
    public Guid fmtid;
    public int pid;
}

// Method order matches the .NET Framework's own interop code exactly
// (ndp/fx/src/WinForms/.../FileDialog_Vista_Interop.cs) - do not reorder,
// or calls will land on the wrong COM vtable slot.
[ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    void BindToHandler([In] IntPtr pbc, [In] ref Guid bhid, [In] ref Guid riid, out IntPtr ppv);
    void GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
    void GetDisplayName([In] SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
    void GetAttributes([In] uint sfgaoMask, out uint psfgaoAttribs);
    void Compare([In, MarshalAs(UnmanagedType.Interface)] IShellItem psi, [In] uint hint, out int piOrder);
}

[ComImport, Guid("b63ea76d-1f85-456f-a19c-48159efa858b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemArray
{
    void BindToHandler([In] IntPtr pbc, [In] ref Guid rbhid, [In] ref Guid riid, out IntPtr ppvOut);
    void GetPropertyStore([In] int flags, [In] ref Guid riid, out IntPtr ppv);
    void GetPropertyDescriptionList([In] ref PROPERTYKEY keyType, [In] ref Guid riid, out IntPtr ppv);
    void GetAttributes([In] SIATTRIBFLAGS dwAttribFlags, [In] uint sfgaoMask, out uint psfgaoAttribs);
    void GetCount(out uint pdwNumItems);
    void GetItemAt([In] uint dwIndex, [MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
    void EnumItems([MarshalAs(UnmanagedType.Interface)] out IntPtr ppenumShellItems);
}

// Method order matches Vanara.PInvoke.Shell32 (ShObjIdl.IExplorerCommand.cs), a
// widely used, well-tested P/Invoke library - see README for the source.
// EnumSubCommands' out parameter is a raw IntPtr here (instead of a typed
// IEnumExplorerCommand) since this command never has subcommands and always
// returns null - no need to declare the enumerator interface at all.
[ComImport, Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IExplorerCommand
{
    [PreserveSig] int GetTitle(IShellItemArray psiItemArray, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
    [PreserveSig] int GetIcon(IShellItemArray psiItemArray, [MarshalAs(UnmanagedType.LPWStr)] out string ppszIcon);
    [PreserveSig] int GetToolTip(IShellItemArray psiItemArray, [MarshalAs(UnmanagedType.LPWStr)] out string ppszInfotip);
    [PreserveSig] int GetCanonicalName(out Guid pguidCommandName);
    [PreserveSig] int GetState(IShellItemArray psiItemArray, [MarshalAs(UnmanagedType.Bool)] bool fOkToBeSlow, out int pCmdState);
    [PreserveSig] int Invoke(IShellItemArray psiItemArray, IntPtr pbc);
    [PreserveSig] int GetFlags(out int pFlags);
    [PreserveSig] int EnumSubCommands(out IntPtr ppEnum);
}

[ComImport, Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IClassFactory
{
    void CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);
    void LockServer([MarshalAs(UnmanagedType.Bool)] bool fLock);
}

internal static class ServerLifetime
{
    private static int s_lastActivityTicks = Environment.TickCount;

    public static void Touch()
    {
        s_lastActivityTicks = Environment.TickCount;
    }

    public static int IdleMillis()
    {
        return Environment.TickCount - s_lastActivityTicks;
    }
}

internal static class GpuLaunchLogic
{
    private const string ShimNvidia = "0x800000001";

    public static List<string> GetSelectedPaths(IShellItemArray psiItemArray)
    {
        var result = new List<string>();
        if (psiItemArray == null) return result;

        uint count;
        psiItemArray.GetCount(out count);
        for (uint i = 0; i < count; i++)
        {
            IShellItem item;
            psiItemArray.GetItemAt(i, out item);
            string path;
            item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out path);
            if (!string.IsNullOrEmpty(path))
                result.Add(path);
        }
        return result;
    }

    public static void LaunchWithNvidia(string targetPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = targetPath,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(targetPath))
        };
        psi.EnvironmentVariables["SHIM_MCCOMPAT"] = ShimNvidia;
        psi.EnvironmentVariables["SHIM_RENDERING_MODE"] = ShimNvidia;

        Process.Start(psi);
    }
}

[ComVisible(true)]
internal sealed class RunWithNvidiaCommand : IExplorerCommand
{
    public const string ClassId = "3ef04f46-10ca-46c7-9115-f82e19572bd6";
    private static readonly Guid CanonicalName = new Guid("86c621d4-65e8-451c-a286-577016f7ee88");

    public int GetTitle(IShellItemArray psiItemArray, out string ppszName)
    {
        ppszName = "Run with NVIDIA GPU";
        return HResult.S_OK;
    }

    public int GetIcon(IShellItemArray psiItemArray, out string ppszIcon)
    {
        ppszIcon = null;
        return HResult.E_NOTIMPL;
    }

    public int GetToolTip(IShellItemArray psiItemArray, out string ppszInfotip)
    {
        ppszInfotip = null;
        return HResult.E_NOTIMPL;
    }

    public int GetCanonicalName(out Guid pguidCommandName)
    {
        pguidCommandName = CanonicalName;
        return HResult.S_OK;
    }

    public int GetState(IShellItemArray psiItemArray, bool fOkToBeSlow, out int pCmdState)
    {
        ServerLifetime.Touch();
        pCmdState = (int)EXPCMDSTATE.ECS_ENABLED;
        return HResult.S_OK;
    }

    public int Invoke(IShellItemArray psiItemArray, IntPtr pbc)
    {
        ServerLifetime.Touch();
        try
        {
            foreach (string path in GpuLaunchLogic.GetSelectedPaths(psiItemArray))
                GpuLaunchLogic.LaunchWithNvidia(path);
            return HResult.S_OK;
        }
        catch
        {
            return HResult.E_FAIL;
        }
    }

    public int GetFlags(out int pFlags)
    {
        pFlags = (int)EXPCMDFLAGS.ECF_DEFAULT;
        return HResult.S_OK;
    }

    public int EnumSubCommands(out IntPtr ppEnum)
    {
        ppEnum = IntPtr.Zero;
        return HResult.S_OK;
    }
}

[ComVisible(true)]
internal sealed class RunWithNvidiaClassFactory : IClassFactory
{
    public void CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
    {
        ppvObject = IntPtr.Zero;
        if (pUnkOuter != IntPtr.Zero)
            Marshal.ThrowExceptionForHR(HResult.CLASS_E_NOAGGREGATION);

        ServerLifetime.Touch();

        object comObject = new RunWithNvidiaCommand();
        IntPtr pUnk = Marshal.GetIUnknownForObject(comObject);
        try
        {
            int hr = Marshal.QueryInterface(pUnk, ref riid, out ppvObject);
            if (hr != 0)
            {
                ppvObject = IntPtr.Zero;
                Marshal.ThrowExceptionForHR(hr);
            }
        }
        finally
        {
            Marshal.Release(pUnk);
        }
    }

    public void LockServer(bool fLock)
    {
        ServerLifetime.Touch();
    }
}

internal static class Program
{
    private const uint CLSCTX_LOCAL_SERVER = 0x4;
    private const uint REGCLS_MULTIPLEUSE = 1;
    private const uint COINIT_APARTMENTTHREADED = 0x2;
    private const uint WM_TIMER = 0x0113;
    private const int IdleTimeoutMillis = 15000;
    private const uint MB_ICONINFORMATION = 0x40;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject(ref Guid rclsid, [MarshalAs(UnmanagedType.IUnknown)] object pUnk, uint dwClsContext, uint flags, out uint lpdwRegister);

    [DllImport("ole32.dll")]
    private static extern int CoRevokeClassObject(uint dwRegister);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr SetTimer(IntPtr hWnd, IntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);

    [DllImport("user32.dll")]
    private static extern bool KillTimer(IntPtr hWnd, IntPtr nIDEvent);

    private static int Main(string[] args)
    {
        bool isEmbedding = false;
        foreach (string a in args)
        {
            if (string.Equals(a, "-Embedding", StringComparison.OrdinalIgnoreCase))
                isEmbedding = true;
        }

        if (!isEmbedding)
        {
            MessageBoxW(IntPtr.Zero,
                "This is a COM server component for the Windows 11 context menu\n" +
                "entry \"Run with NVIDIA GPU\". It is started automatically by\n" +
                "explorer.exe and is not meant to be launched directly.",
                "GpuContextMenuHandler", MB_ICONINFORMATION);
            return 0;
        }

        CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);
        ServerLifetime.Touch();

        Guid clsid = new Guid(RunWithNvidiaCommand.ClassId);
        var factory = new RunWithNvidiaClassFactory();
        uint cookie;
        int hr = CoRegisterClassObject(ref clsid, factory, CLSCTX_LOCAL_SERVER, REGCLS_MULTIPLEUSE, out cookie);
        if (hr != 0)
        {
            CoUninitialize();
            return hr;
        }

        IntPtr timerId = SetTimer(IntPtr.Zero, IntPtr.Zero, 2000, IntPtr.Zero);

        MSG msg;
        while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_TIMER)
            {
                if (ServerLifetime.IdleMillis() > IdleTimeoutMillis)
                    break;
                continue;
            }
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        KillTimer(IntPtr.Zero, timerId);
        CoRevokeClassObject(cookie);
        CoUninitialize();
        return 0;
    }
}
