param([string]$ShortcutDirectory = [Environment]::GetFolderPath('Programs'))
$ErrorActionPreference = 'Stop'
$quotaExecutable = Join-Path $PSScriptRoot 'CodexQuotaBar.exe'
if (-not (Test-Path -LiteralPath $quotaExecutable)) {
    throw 'CodexQuotaBar.exe is missing. Extract the release ZIP or run build.ps1 first.'
}
if ([string]::IsNullOrWhiteSpace($ShortcutDirectory)) { throw 'A shortcut directory is required.' }
$quotaDirectory = New-Item -ItemType Directory -Path $ShortcutDirectory -Force
$quotaShortcutPath = Join-Path $quotaDirectory.FullName 'Codex + Quota Bar.lnk'
# Use the Unicode Shell Link interface so non-ASCII installation paths work.
if (-not ('CodexQuotaShortcut' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

public static class CodexQuotaShortcut
{
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, IntPtr findData, uint flags);
        void GetIDList(out IntPtr item);
        void SetIDList(IntPtr item);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string path);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    public static void Create(string path, string executable, string directory)
    {
        var shell = new ShellLink();
        try
        {
            var link = (IShellLinkW)shell;
            link.SetPath(executable);
            link.SetArguments("--launch");
            link.SetWorkingDirectory(directory);
            link.SetIconLocation(executable, 0);
            link.SetDescription("Start Codex with its weekly quota display");
            ((IPersistFile)shell).Save(path, true);
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }
}
'@
}
[CodexQuotaShortcut]::Create($quotaShortcutPath, $quotaExecutable, $PSScriptRoot)
Write-Output $quotaShortcutPath
