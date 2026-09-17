using System;
using System.Security.Cryptography;
using System.Text;

namespace AetherLove.Config;

public static class LocalSecretProtection
{
    private static readonly byte[] Purpose = Encoding.UTF8.GetBytes("AetherLove-local-secrets-v1");
    public static byte[] Protect(byte[] plain) => plain.Length == 0 ? [] : ProtectedData.Protect(plain, Purpose, DataProtectionScope.CurrentUser);
    public static byte[] Open(byte[] encrypted)
    {
        if (encrypted.Length == 0) { return []; }
        try { return ProtectedData.Unprotect(encrypted, Purpose, DataProtectionScope.CurrentUser); }
        catch (CryptographicException) { return []; }
    }
}
