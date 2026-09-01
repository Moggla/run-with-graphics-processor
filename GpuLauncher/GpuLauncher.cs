using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// Launches an arbitrary program with a specific GPU preference for NVIDIA Optimus
// by setting SHIM_MCCOMPAT / SHIM_RENDERING_MODE only in the child process environment.
// No registry entry, no permanent change - applies to this one launch only.
internal static class GpuLauncher
{
    private const string ShimNvidia = "0x800000001";
    private const string ShimIntegrated = "0x800000000";

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private const uint MB_ICONERROR = 0x10;

    private static void ShowError(string message)
    {
        MessageBoxW(IntPtr.Zero, message, "GpuLauncher", MB_ICONERROR);
    }

    private static string QuoteArgument(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            return arg;

        var sb = new StringBuilder();
        sb.Append('"');
        int backslashes = 0;
        foreach (char c in arg)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            if (c == '"')
            {
                sb.Append('\\', backslashes * 2 + 1);
                sb.Append('"');
                backslashes = 0;
                continue;
            }
            sb.Append('\\', backslashes);
            backslashes = 0;
            sb.Append(c);
        }
        sb.Append('\\', backslashes * 2);
        sb.Append('"');
        return sb.ToString();
    }

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            ShowError(
                "Usage:\n" +
                "  GpuLauncher.exe nvidia <Program.exe> [Arguments...]\n" +
                "  GpuLauncher.exe integrated <Program.exe> [Arguments...]");
            return 1;
        }

        string shimValue;
        switch (args[0].Trim().ToLowerInvariant())
        {
            case "nvidia":
            case "dgpu":
                shimValue = ShimNvidia;
                break;
            case "integrated":
            case "igpu":
            case "intel":
                shimValue = ShimIntegrated;
                break;
            default:
                ShowError("Unknown GPU mode: '" + args[0] + "'. Allowed: nvidia, integrated.");
                return 1;
        }

        string targetPath = args[1];
        if (!File.Exists(targetPath))
        {
            ShowError("File not found:\n" + targetPath);
            return 1;
        }

        var argSb = new StringBuilder();
        for (int i = 2; i < args.Length; i++)
        {
            if (i > 2) argSb.Append(' ');
            argSb.Append(QuoteArgument(args[i]));
        }

        var psi = new ProcessStartInfo
        {
            FileName = targetPath,
            Arguments = argSb.ToString(),
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(targetPath))
        };
        psi.EnvironmentVariables["SHIM_MCCOMPAT"] = shimValue;
        psi.EnvironmentVariables["SHIM_RENDERING_MODE"] = shimValue;

        try
        {
            Process.Start(psi);
            return 0;
        }
        catch (Exception ex)
        {
            ShowError("Failed to start:\n" + targetPath + "\n\n" + ex.Message);
            return 1;
        }
    }
}
