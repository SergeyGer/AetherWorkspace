using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Aether.Security.Secrets;

/// <summary>
/// Optional Windows Credential Manager adapter (v1.3 AW-067). Secrets are read from Generic
/// credentials named <c>AetherWorkspace/&lt;variable-name&gt;</c>; they are never persisted by this
/// provider, never included in diagnostic strings and never written to the repository.
/// Development-only environment injection remains available through
/// <see cref="EnvironmentSecretProvider"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialManagerSecretProvider : ISecretProvider
{
    public const string DefaultTargetPrefix = "AetherWorkspace/";
    private const uint CredentialTypeGeneric = 1;

    private readonly string _targetPrefix;

    public WindowsCredentialManagerSecretProvider(string targetPrefix = DefaultTargetPrefix)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows Credential Manager is only available on Windows.");
        }

        _targetPrefix = string.IsNullOrWhiteSpace(targetPrefix) ? DefaultTargetPrefix : targetPrefix;
    }

    /// <summary>Returns the UTF-8/UTF-16 blob for the named secret, or null when absent.</summary>
    public string? GetSecret(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var target = _targetPrefix + name;
        if (!CredRead(target, CredentialTypeGeneric, 0, out var credentialPointer) || credentialPointer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return null;
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            var value = DecodeBlob(bytes).Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public bool HasSecret(string name) => GetSecret(name) is not null;

    private static string DecodeBlob(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return "";
        }

        // cmdkey-style Generic credentials commonly store UTF-16LE; other tooling writes UTF-8.
        var looksUtf16 = bytes.Length >= 2 && (bytes.Length % 2 == 0) && bytes[1] == 0 && bytes[0] != 0;
        return looksUtf16
            ? Encoding.Unicode.GetString(bytes)
            : Encoding.UTF8.GetString(bytes);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);
}
