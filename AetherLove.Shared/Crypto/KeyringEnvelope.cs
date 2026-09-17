using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AetherLove.Shared.Crypto;

public static class KeyringEnvelope
{
    public static byte[] Derive(byte[] secret, Guid accountId, string purpose)
        => HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 32,
            Encoding.UTF8.GetBytes(accountId.ToString("N")), Encoding.UTF8.GetBytes("AetherLove-keyring-v1/" + purpose));

    public static (byte[] Ciphertext, byte[] Nonce) Seal(byte[] secret, byte[] plaintext, string context)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var output = new byte[plaintext.Length + 16];
        using var aes = new AesGcm(secret, 16);
        aes.Encrypt(nonce, plaintext, output.AsSpan(0, plaintext.Length), output.AsSpan(plaintext.Length), Encoding.UTF8.GetBytes(context));
        return (output, nonce);
    }

    public static byte[] Open(byte[] secret, byte[] ciphertext, byte[] nonce, string context)
    {
        if (secret.Length != 32 || nonce.Length != 12 || ciphertext.Length < 16 || ciphertext.Length > KeyEnvelopeLimits.MaxKeyringBytes)
        {
            throw new CryptographicException("Invalid keyring envelope.");
        }
        var output = new byte[ciphertext.Length - 16];
        using var aes = new AesGcm(secret, 16);
        aes.Decrypt(nonce, ciphertext.AsSpan(0, output.Length), ciphertext.AsSpan(output.Length), output, Encoding.UTF8.GetBytes(context));
        return output;
    }

    public static string Context(Guid accountId, Guid rootId, string purpose)
        => $"AetherLove-keyring-v1/{accountId:N}/{rootId:N}/{purpose}";

    public static void Merge(AccountKeyringContents target, AccountKeyringContents source)
    {
        if (target.AccountId != source.AccountId)
        {
            throw new CryptographicException("Keyring account mismatch.");
        }
        foreach (var key in source.Identities)
        {
            var existing = target.Identities.Find(k => k.Surface == key.Surface && k.OwnerId == key.OwnerId && k.KeyId == key.KeyId);
            if (existing is null)
            {
                target.Identities.Add(key);
            }
            else if (!CryptographicOperations.FixedTimeEquals(existing.PrivateKey, key.PrivateKey))
            {
                throw new CryptographicException("Conflicting identity material.");
            }
        }
        foreach (var key in source.UnmatchedLocal)
        {
            if (!target.UnmatchedLocal.Any(k => k.Surface == key.Surface && k.OwnerId == key.OwnerId
                && k.PublicKey.AsSpan().SequenceEqual(key.PublicKey) && k.PrivateKey.AsSpan().SequenceEqual(key.PrivateKey)))
            {
                target.UnmatchedLocal.Add(key);
            }
        }
        foreach (var key in source.Groups)
        {
            var existing = target.Groups.Find(k => k.GroupId == key.GroupId && k.Epoch == key.Epoch);
            if (existing is null)
            {
                target.Groups.Add(key);
            }
            else if (!CryptographicOperations.FixedTimeEquals(existing.Key, key.Key))
            {
                throw new CryptographicException("Conflicting group epoch.");
            }
        }
        target.Unresolved = target.Unresolved.Union(source.Unresolved).ToList();
        foreach (var kek in source.LegacyKeks)
        {
            if (!target.LegacyKeks.Any(k => CryptographicOperations.FixedTimeEquals(k, kek)))
            {
                target.LegacyKeks.Add(kek);
            }
        }
    }
}

public sealed record RecoveryFile(Guid AccountId, Guid RootId, byte[] Secret, int Version = 1)
{
    private const string Begin = "-----BEGIN AETHERKEY-----";
    private const string End = "-----END AETHERKEY-----";

    public string Encode()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(this);
        var payload = Convert.ToBase64String(bytes.Concat(SHA256.HashData(bytes)).ToArray());
        var lines = string.Join("\n", Enumerable.Range(0, (payload.Length + 63) / 64).Select(i => payload.Substring(i * 64, Math.Min(64, payload.Length - i * 64))));
        return "       .       *       .\n          /\\\n     .   /  \\   .\n        / /\\ \\\n        \\ \\/ /\n     *   \\  /   *\n          \\/\n     A E T H E R L O V E\n       Your pocket crystal.\n\nKeep this spare key somewhere safe. Anyone with it can use it to recover your encryption keys.\n\n" + Begin + "\n" + lines + "\n" + End + "\n";
    }

    public static RecoveryFile Decode(string text)
    {
        if (text.Length > 8192)
        {
            throw new FormatException("Recovery file is too large.");
        }
        var start = text.IndexOf(Begin, StringComparison.Ordinal);
        var end = text.IndexOf(End, StringComparison.Ordinal);
        if (start < 0 || end <= start)
        {
            throw new FormatException("Invalid recovery file.");
        }
        var bytes = Convert.FromBase64String(text[(start + Begin.Length)..end]);
        if (bytes.Length < 33 || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)), bytes.AsSpan(bytes.Length - 32)))
        {
            throw new FormatException("Damaged recovery file.");
        }
        var file = JsonSerializer.Deserialize<RecoveryFile>(bytes.AsSpan(0, bytes.Length - 32));
        if (file is not { Version: 1, Secret.Length: 32 } || file.AccountId == Guid.Empty || file.RootId == Guid.Empty)
        {
            throw new FormatException("Unsupported recovery file.");
        }
        return file;
    }
}
