using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinAppInspector.UI.Services;

/// <summary>
/// Asks the shell for the image it shows for a file or a Start-menu app (§13.2): the same icons Explorer, the Start
/// menu and Task Manager use. For packaged apps the parsing name is <c>shell:AppsFolder\{AppUserModelId}</c>, which
/// works without any access to the WindowsApps folder and returns the logo on its plate colour.
/// </summary>
internal static class ShellImage
{
    private const uint SiigbfBiggerSizeOk = 0x1;
    private const uint SiigbfIconOnly = 0x4;
    private const uint SiigbfIconBackground = 0x80;

    private static readonly Guid ImageFactoryIid = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    /// <summary>Returns a frozen bitmap of <paramref name="size"/> pixels, or null when the shell has nothing for the name.</summary>
    public static BitmapSource? Load(string parsingName, int size, bool plated)
    {
        var flags = SiigbfIconOnly | SiigbfBiggerSizeOk | (plated ? SiigbfIconBackground : 0);
        var iid = ImageFactoryIid;
        IShellItemImageFactory? factory = null;
        var hBitmap = IntPtr.Zero;
        try
        {
            SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out factory);
            factory.GetImage(new Size { Width = size, Height = size }, flags, out hBitmap);
            return hBitmap == IntPtr.Zero ? null : ToBitmapSource(hBitmap);
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
            {
                _ = DeleteObject(hBitmap);
            }

            if (factory is not null)
            {
                _ = Marshal.ReleaseComObject(factory);
            }
        }
    }

    /// <summary>
    /// The shell hands back a 32-bit DIB whose alpha channel GDI+ ignores, so the pixels are re-read as BGRA. Without this
    /// the transparent corners of every icon come out black.
    /// </summary>
    private static BitmapSource ToBitmapSource(IntPtr hBitmap)
    {
        using var raw = Image.FromHbitmap(hBitmap);
        var rect = new Rectangle(0, 0, raw.Width, raw.Height);
        var data = raw.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var bytes = new byte[stride * raw.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var hasAlpha = false;
            for (var i = 3; i < bytes.Length && !hasAlpha; i += 4)
            {
                hasAlpha = bytes[i] != 0;
            }

            // A DIB with an all-zero alpha channel is an opaque image (older icon formats): treat it as such.
            System.Windows.Media.PixelFormat format = hasAlpha ? PixelFormats.Bgra32 : PixelFormats.Bgr32;
            var source = BitmapSource.Create(raw.Width, raw.Height, 96, 96, format, null, bytes, stride);
            source.Freeze();
            return source;
        }
        finally
        {
            raw.UnlockBits(data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size
    {
        public int Width;
        public int Height;
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(Size size, uint flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(string lpszFile, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>Icon <paramref name="index"/> of an exe / dll / ico (registry <c>DisplayIcon</c> values such as <c>app.exe,1</c>).</summary>
    public static BitmapSource? LoadIndexedIcon(string file, int index, int size)
    {
        var count = ExtractIconExW(file, index, out var large, out var small, 1);
        if (count == 0 || count == uint.MaxValue)
        {
            return null;
        }

        try
        {
            var handle = large != IntPtr.Zero ? large : small;
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(size, size));
            source.Freeze();
            return source;
        }
        finally
        {
            if (large != IntPtr.Zero)
            {
                _ = DestroyIcon(large);
            }

            if (small != IntPtr.Zero)
            {
                _ = DestroyIcon(small);
            }
        }
    }
}
