using WinAppInspector.Core.Scanning;
using WinAppInspector.UI.Localization;

namespace WinAppInspector.UI.ViewModels;

/// <summary>§34 error line with the cause localised when the scanner supplied a code.</summary>
public sealed record ScanErrorRow(string Source, string Target, string Text)
{
    public static ScanErrorRow From(ScanError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ScanErrorRow(error.Source, error.Target, error.Code switch
        {
            ScanErrorCodes.CustomDirectoryMissing => Localize.Get("ScanError.CustomDirectoryMissing"),
            ScanErrorCodes.CustomDirectoryProtected => Localize.Format("ScanError.CustomDirectoryProtected", Localize.Get("Protection." + error.Detail)),
            ScanErrorCodes.CustomDirectoryLooksLikeProgram => Localize.Get("ScanError.CustomDirectoryLooksLikeProgram"),
            _ => error.Message,
        });
    }
}
