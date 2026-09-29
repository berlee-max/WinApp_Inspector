using System.Runtime.InteropServices;

namespace WinAppInspector.Actions.Cleanup;

/// <summary>Sends files or directories to the recycle bin through <c>SHFileOperation</c> (§24). No confirmation UI, no fallback to permanent deletion.</summary>
internal static class RecycleBin
{
    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    /// <summary>Returns <c>null</c> on success, otherwise a description of why the item could not be recycled.</summary>
    public static string? Send(string path)
    {
        var op = new SHFILEOPSTRUCT
        {
            hwnd = IntPtr.Zero,
            wFunc = FO_DELETE,
            pFrom = path + "\0\0",
            pTo = null,
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
            fAnyOperationsAborted = false,
            hNameMappings = IntPtr.Zero,
            lpszProgressTitle = null,
        };

        var code = SHFileOperation(ref op);
        if (code == 0 && !op.fAnyOperationsAborted)
        {
            return null;
        }

        if (op.fAnyOperationsAborted)
        {
            return "The recycle operation was aborted.";
        }

        return code switch
        {
            0x71 => "Source and destination are the same file.",
            0x74 => "The path is a root directory and cannot be recycled.",
            0x75 => "The operation was cancelled.",
            0x76 => "The destination is a subtree of the source.",
            0x78 => "Access denied.",
            0x7C => "The path is invalid.",
            0x7E => "The file already exists.",
            0x80 => "The destination is a subtree of the source.",
            0x81 => "The security settings denied access.",
            0x82 => "The source is a root directory.",
            0x83 => "The operation involved multiple destination paths.",
            0x87 => "The path is too long or the item is too large for the recycle bin.",
            0x10000 => "An unspecified error occurred on the destination.",
            0x402 => "An unknown error occurred (DE_ERROR_MAX).",
            _ => $"SHFileOperation failed with code 0x{code:X}.",
        };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);
}
