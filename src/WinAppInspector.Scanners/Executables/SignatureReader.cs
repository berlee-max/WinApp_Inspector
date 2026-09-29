using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Parsing;
using WinAppInspector.Core.Scanning;

namespace WinAppInspector.Scanners.Executables;

/// <summary>
/// Verifies the embedded Authenticode signature with <c>WinVerifyTrust</c> and reads the signer certificate (§7.4).
/// Files signed only through a security catalog (most Windows system binaries) report <see cref="SignatureStatus.NotSigned"/>
/// with an explanatory <see cref="SignatureInfo.Error"/>; catalog lookup is out of scope for V1.
/// </summary>
public sealed class SignatureReader : ISignatureReader
{
    public SignatureInfo Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = WindowsPath.Normalize(path);
        if (!File.Exists(normalized))
        {
            return new SignatureInfo { Status = SignatureStatus.ReadFailed, Error = "File not found." };
        }

        int trustResult;
        try
        {
            trustResult = WinTrust.VerifyEmbeddedSignature(normalized);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ExternalException)
        {
            return new SignatureInfo { Status = SignatureStatus.ReadFailed, Error = ex.Message };
        }

        if (trustResult is WinTrust.TRUST_E_NOSIGNATURE or WinTrust.TRUST_E_SUBJECT_FORM_UNKNOWN or WinTrust.TRUST_E_PROVIDER_UNKNOWN)
        {
            return new SignatureInfo
            {
                Status = SignatureStatus.NotSigned,
                Error = trustResult == WinTrust.TRUST_E_NOSIGNATURE ? "No embedded signature (the file may be catalog-signed)." : null,
            };
        }

        var status = trustResult == 0 ? SignatureStatus.Valid : SignatureStatus.Invalid;
        var info = new SignatureInfo
        {
            Status = status,
            Error = status == SignatureStatus.Invalid ? DescribeTrustError(trustResult) : null,
        };

        try
        {
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(normalized));
            return info with
            {
                SubjectName = certificate.Subject,
                IssuerName = certificate.Issuer,
                Publisher = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false) is { Length: > 0 } cn
                    ? cn
                    : DistinguishedName.GetCommonName(certificate.Subject),
                Thumbprint = certificate.Thumbprint,
                NotBefore = new DateTimeOffset(certificate.NotBefore.ToUniversalTime(), TimeSpan.Zero),
                NotAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero),
            };
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException)
        {
            // The trust verdict stands even when the certificate cannot be extracted.
            return info with { Error = info.Error ?? ("Certificate could not be read: " + ex.Message) };
        }
    }

    private static string DescribeTrustError(int hresult) => hresult switch
    {
        WinTrust.CERT_E_EXPIRED => "The signing certificate has expired.",
        WinTrust.CERT_E_UNTRUSTEDROOT => "The certificate chain ends in an untrusted root.",
        WinTrust.CERT_E_REVOKED => "The signing certificate has been revoked.",
        WinTrust.TRUST_E_BAD_DIGEST => "The file has been modified after signing (digest mismatch).",
        WinTrust.TRUST_E_EXPLICIT_DISTRUST => "The certificate is explicitly distrusted.",
        WinTrust.CERT_E_CHAINING => "The certificate chain could not be built.",
        _ => Marshal.GetExceptionForHR(hresult)?.Message ?? $"WinVerifyTrust failed with 0x{hresult:X8}.",
    };

    private static class WinTrust
    {
        public const int TRUST_E_PROVIDER_UNKNOWN = unchecked((int)0x800B0001);
        public const int TRUST_E_SUBJECT_FORM_UNKNOWN = unchecked((int)0x800B0003);
        public const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100);
        public const int CERT_E_EXPIRED = unchecked((int)0x800B0101);
        public const int CERT_E_UNTRUSTEDROOT = unchecked((int)0x800B0109);
        public const int CERT_E_CHAINING = unchecked((int)0x800B010A);
        public const int TRUST_E_EXPLICIT_DISTRUST = unchecked((int)0x800B0111);
        public const int CERT_E_REVOKED = unchecked((int)0x800B010C);
        public const int TRUST_E_BAD_DIGEST = unchecked((int)0x80096010);

        private const uint WTD_UI_NONE = 2;
        private const uint WTD_REVOKE_NONE = 0;
        private const uint WTD_CHOICE_FILE = 1;
        private const uint WTD_STATEACTION_VERIFY = 1;
        private const uint WTD_STATEACTION_CLOSE = 2;
        private const uint WTD_REVOCATION_CHECK_NONE = 0x10;
        private const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x1000;

        private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
        private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, IntPtr pWVTData);

        /// <summary>Returns 0 when the embedded signature is valid, otherwise the HRESULT from WinVerifyTrust.</summary>
        public static int VerifyEmbeddedSignature(string path)
        {
            var fileInfo = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
                pcwszFilePath = path,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero,
            };

            var pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
            var pData = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_DATA>());
            try
            {
                Marshal.StructureToPtr(fileInfo, pFile, false);

                var data = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                    dwUIChoice = WTD_UI_NONE,
                    fdwRevocationChecks = WTD_REVOKE_NONE,
                    dwUnionChoice = WTD_CHOICE_FILE,
                    pFile = pFile,
                    dwStateAction = WTD_STATEACTION_VERIFY,
                    // Offline-friendly: no revocation download, cached URL retrieval only. Signature validity is an attribution hint, not a security verdict (§7.4).
                    dwProvFlags = WTD_REVOCATION_CHECK_NONE | WTD_CACHE_ONLY_URL_RETRIEVAL,
                };
                Marshal.StructureToPtr(data, pData, false);

                var action = WINTRUST_ACTION_GENERIC_VERIFY_V2;
                var result = WinVerifyTrust(IntPtr.Zero, ref action, pData);

                // Release the state handle WinVerifyTrust allocated.
                data = Marshal.PtrToStructure<WINTRUST_DATA>(pData);
                data.dwStateAction = WTD_STATEACTION_CLOSE;
                Marshal.StructureToPtr(data, pData, true);
                _ = WinVerifyTrust(IntPtr.Zero, ref action, pData);

                return result;
            }
            finally
            {
                Marshal.DestroyStructure<WINTRUST_FILE_INFO>(pFile);
                Marshal.FreeHGlobal(pFile);
                Marshal.FreeHGlobal(pData);
            }
        }
    }
}
