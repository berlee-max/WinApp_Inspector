using System.Runtime.InteropServices;

namespace WinAppInspector.Actions.Cleanup;

/// <summary>
/// Sends files or directories to the recycle bin through <c>SHFileOperationW</c> (§24).
/// The struct uses the default (8-byte) packing, which is the x64 layout of <c>SHFILEOPSTRUCTW</c>;
/// the application ships as win-x64 only. <see cref="CanRecycle"/> refuses volumes without a recycle bin
/// up front, and FOF_WANTNUKEWARNING makes the shell ask instead of silently deleting anything it cannot recycle.
/// </summary>
internal static class RecycleBin
{
    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;
    private const ushort FOF_WANTNUKEWARNING = 0x4000;

    /// <summary>
    /// True when the item's volume is a fixed drive with a recycle bin. Removable, network and RAM disks have none,
    /// so a "recycle" there would be a permanent delete (§24 requires that to be explicit).
    /// </summary>
    public static bool CanRecycle(string path, out string? reason)
    {
        reason = null;
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
            {
                reason = "The path has no drive root.";
                return false;
            }

            var drive = new DriveInfo(root);
            if (drive.DriveType != DriveType.Fixed)
            {
                reason = $"Drive {root} is {drive.DriveType}; items on it cannot be recycled.";
                return false;
            }

            if (!Directory.Exists(Path.Combine(root, "$Recycle.Bin")))
            {
                reason = $"Drive {root} has no recycle bin.";
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            reason = ex.Message;
            return false;
        }
    }

    /// <summary>Returns <c>null</c> on success, otherwise a description of why the item could not be recycled.</summary>
    public static string? Send(string path)
    {
        if (IntPtr.Size != 8)
        {
            return "Recycle bin deletion is only supported in the 64-bit build.";
        }

        var op = new SHFILEOPSTRUCT
        {
            hwnd = IntPtr.Zero,
            wFunc = FO_DELETE,
            pFrom = path + "\0\0",
            pTo = null,
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING,
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

        return Describe(code);
    }

    /// <summary>Return codes documented for SHFileOperationW, plus the plain Win32 codes it also returns.</summary>
    internal static string Describe(int code) => code switch
    {
        0x2 => "The file or directory was not found.",
        0x3 => "The path was not found.",
        0x5 => "Access denied.",
        0x20 => "The file is in use by another process.",
        0x71 => "Source and destination are the same file (DE_SAMEFILE).",
        0x72 => "Multiple file paths were specified for a single-file operation (DE_MANYSRC1DEST).",
        0x73 => "Rename with multiple sources (DE_DIFFDIR).",
        0x74 => "The path is a root directory and cannot be recycled (DE_ROOTDIR).",
        0x75 => "The operation was cancelled (DE_OPCANCELLED).",
        0x76 => "The destination is a subtree of the source (DE_DESTSUBTREE).",
        0x78 => "Access denied (DE_ACCESSDENIEDSRC).",
        0x79 => "The path is too deep (DE_PATHTOODEEP).",
        0x7A => "Many destinations (DE_MANYDEST).",
        0x7C => "The path is invalid (DE_INVALIDFILES).",
        0x7D => "Destination is the same as source (DE_DESTSAMETREE).",
        0x7E => "The destination folder is a file (DE_FLDDESTISFILE).",
        0x80 => "The destination file is a folder (DE_FILEDESTISFLD).",
        0x81 => "The file name is too long (DE_FILENAMETOOLONG).",
        0x82 => "The destination is a CD-ROM (DE_DEST_IS_CDROM).",
        0x83 => "The destination is a DVD (DE_DEST_IS_DVD).",
        0x84 => "The destination is a writable CD (DE_DEST_IS_CDRECORD).",
        0x85 => "The file is too large for the destination (DE_FILE_TOO_LARGE).",
        0x86 => "The source is a CD-ROM (DE_SRC_IS_CDROM).",
        0x87 => "The source is a DVD (DE_SRC_IS_DVD).",
        0x88 => "The source is a writable CD (DE_SRC_IS_CDRECORD).",
        0xB7 => "The path is too long (DE_ERROR_MAX / MAX_PATH exceeded).",
        0x402 => "Unknown error or invalid path (DE_ERROR_MAX).",
        0x10000 => "An unspecified error occurred on the destination (ERRORONDEST).",
        0x10074 => "The root directory is invalid for this operation (DE_ROOTDIR | ERRORONDEST).",
        _ => $"SHFileOperation failed with code 0x{code:X}.",
    };

    // x64 layout (pshpack8): hwnd@0, wFunc@8, pFrom@16, pTo@24, fFlags@32, fAnyOperationsAborted@36, hNameMappings@40, lpszProgressTitle@48.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
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
