using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace IPWatcherPro.Infrastructure;

public static class AutoStartManager
{
    private const string ShortcutName = "IPWatcherPro.lnk";

    public static string StartupShortcutPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            ShortcutName);

    public static bool IsEnabled()
    {
        return File.Exists(StartupShortcutPath);
    }

    public static void Apply(bool enabled, JsonLinesLogger logger)
    {
        try
        {
            if (enabled)
                Enable(logger);
            else
                Disable(logger);
        }
        catch (Exception ex)
        {
            logger.Write("AutoStartError", new { Error = ex.ToString() });
        }
    }

    public static void Enable(JsonLinesLogger logger)
    {
        var exePath = Application.ExecutablePath;

        CreateShortcut(
            StartupShortcutPath,
            exePath,
            "",
            Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory,
            "IPWatcherPro");

        logger.Write("AutoStartEnabled", new
        {
            Shortcut = StartupShortcutPath,
            Target = exePath
        });
    }

    public static void Disable(JsonLinesLogger logger)
    {
        if (File.Exists(StartupShortcutPath))
            File.Delete(StartupShortcutPath);

        logger.Write("AutoStartDisabled", new
        {
            Shortcut = StartupShortcutPath
        });
    }

    private static void CreateShortcut(
    string shortcutPath,
    string targetPath,
    string arguments,
    string workingDirectory,
    string description)
    {
        var shellLink = (IShellLinkW)(object)new CShellLink();

        shellLink.SetPath(targetPath);
        shellLink.SetArguments(arguments);
        shellLink.SetWorkingDirectory(workingDirectory);
        shellLink.SetDescription(description);

        var file = (IPersistFile)shellLink;
        file.Save(shortcutPath, true);
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class CShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
            int cchMaxPath,
            IntPtr pfd,
            uint fFlags);

        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);

        void GetDescription(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName,
            int cchMaxName);

        void SetDescription(
            [MarshalAs(UnmanagedType.LPWStr)] string pszName);

        void GetWorkingDirectory(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir,
            int cchMaxPath);

        void SetWorkingDirectory(
            [MarshalAs(UnmanagedType.LPWStr)] string pszDir);

        void GetArguments(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs,
            int cchMaxPath);

        void SetArguments(
            [MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);

        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);

        void GetIconLocation(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath,
            int cchIconPath,
            out int piIcon);

        void SetIconLocation(
            [MarshalAs(UnmanagedType.LPWStr)] string pszIconPath,
            int iIcon);

        void SetRelativePath(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPathRel,
            uint dwReserved);

        void Resolve(IntPtr hwnd, uint fFlags);

        void SetPath(
            [MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void IsDirty();

        void Load(
            [MarshalAs(UnmanagedType.LPWStr)] string pszFileName,
            uint dwMode);

        void Save(
            [MarshalAs(UnmanagedType.LPWStr)] string pszFileName,
            bool fRemember);

        void SaveCompleted(
            [MarshalAs(UnmanagedType.LPWStr)] string pszFileName);

        void GetCurFile(
            [MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}