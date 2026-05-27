using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SiteDownWindows.Services;

public static class DpapiSettingsProtector
{
    private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DATA_BLOB pDataIn,
        string? szDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        ref DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(
        ref DATA_BLOB pDataIn,
        IntPtr ppszDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        ref DATA_BLOB pDataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public static byte[] Protect(byte[] plainBytes)
    {
        if (plainBytes.Length == 0)
        {
            return Array.Empty<byte>();
        }

        var inBlob = ToBlob(plainBytes);
        var outBlob = new DATA_BLOB();

        try
        {
            if (!CryptProtectData(
                    ref inBlob,
                    "SiteDown local settings",
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CRYPTPROTECT_UI_FORBIDDEN,
                    ref outBlob))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not encrypt SiteDown settings.");
            }

            return FromBlob(outBlob);
        }
        finally
        {
            FreeBlob(inBlob);
            FreeBlob(outBlob, localFree: true);
        }
    }

    public static byte[] Unprotect(byte[] encryptedBytes)
    {
        if (encryptedBytes.Length == 0)
        {
            return Array.Empty<byte>();
        }

        var inBlob = ToBlob(encryptedBytes);
        var outBlob = new DATA_BLOB();

        try
        {
            if (!CryptUnprotectData(
                    ref inBlob,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CRYPTPROTECT_UI_FORBIDDEN,
                    ref outBlob))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not decrypt SiteDown settings.");
            }

            return FromBlob(outBlob);
        }
        finally
        {
            FreeBlob(inBlob);
            FreeBlob(outBlob, localFree: true);
        }
    }

    private static DATA_BLOB ToBlob(byte[] bytes)
    {
        var blob = new DATA_BLOB
        {
            cbData = bytes.Length,
            pbData = Marshal.AllocHGlobal(bytes.Length)
        };

        Marshal.Copy(bytes, 0, blob.pbData, bytes.Length);
        return blob;
    }

    private static byte[] FromBlob(DATA_BLOB blob)
    {
        if (blob.pbData == IntPtr.Zero || blob.cbData <= 0)
        {
            return Array.Empty<byte>();
        }

        var bytes = new byte[blob.cbData];
        Marshal.Copy(blob.pbData, bytes, 0, blob.cbData);
        return bytes;
    }

    private static void FreeBlob(DATA_BLOB blob, bool localFree = false)
    {
        if (blob.pbData == IntPtr.Zero)
        {
            return;
        }

        if (localFree)
        {
            LocalFree(blob.pbData);
        }
        else
        {
            Marshal.FreeHGlobal(blob.pbData);
        }
    }
}
