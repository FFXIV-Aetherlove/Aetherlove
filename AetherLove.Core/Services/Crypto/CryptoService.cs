using System;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

namespace AetherLove.Services.Crypto;

/// <summary>Pure-managed E2E crypto: AES-GCM, HKDF, X25519 ECDH, Argon2id. Stateless.</summary>
public sealed class CryptoService
{
    public const int X25519KeyLength = 32;
    public const int AesGcmKeyLength = 32;
    public const int AesGcmNonceLength = 12;
    public const int KdfSaltLength = 16;
    public const int AesGcmTagLength = 16;

    private static readonly byte[] MessageKeyInfo =
        Encoding.UTF8.GetBytes("AetherLove-chat-msg-key-v1");

    public (byte[] PublicKey, byte[] PrivateKey) GenerateIdentityKeyPair()
    {
        var privKey = new byte[X25519KeyLength];
        RandomNumberGenerator.Fill(privKey);
        var privParams = new X25519PrivateKeyParameters(privKey, 0);
        var pubParams = privParams.GeneratePublicKey();
        var pubBytes = new byte[X25519KeyLength];
        pubParams.Encode(pubBytes, 0);
        return (pubBytes, privKey);
    }

    public byte[] DeriveKEK(string passphrase, byte[] salt, int memoryKb, int iterations, int parallelism)
    {
        var pwBytes = Encoding.UTF8.GetBytes(passphrase);
        var output = new byte[AesGcmKeyLength];
        var gen = new Argon2BytesGenerator();
        gen.Init(new Argon2Parameters.Builder(Argon2Parameters.Argon2id)
            .WithVersion(Argon2Parameters.Version13)
            .WithSalt(salt)
            .WithIterations(iterations)
            .WithMemoryAsKB(memoryKb)
            .WithParallelism(parallelism)
            .Build());
        gen.GenerateBytes(pwBytes, output);
        return output;
    }

    public (byte[] EncryptedPrivateKey, byte[] WrapNonce) WrapPrivateKey(byte[] privateKey, byte[] kek)
    {
        var nonce = new byte[AesGcmNonceLength];
        RandomNumberGenerator.Fill(nonce);
        var ciphertext = new byte[privateKey.Length];
        var tag = new byte[AesGcmTagLength];
        using var aes = new AesGcm(kek, AesGcmTagLength);
        aes.Encrypt(nonce, privateKey, ciphertext, tag);
        // Wire layout: ciphertext || tag.
        var combined = new byte[ciphertext.Length + tag.Length];
        Buffer.BlockCopy(ciphertext, 0, combined, 0, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, combined, ciphertext.Length, tag.Length);
        return (combined, nonce);
    }

    public byte[]? UnwrapPrivateKey(byte[] encryptedPrivateKey, byte[] wrapNonce, byte[] kek)
    {
        if (encryptedPrivateKey.Length < AesGcmTagLength)
        {
            return null;
        }
        var ctLen = encryptedPrivateKey.Length - AesGcmTagLength;
        var ciphertext = new byte[ctLen];
        var tag = new byte[AesGcmTagLength];
        Buffer.BlockCopy(encryptedPrivateKey, 0, ciphertext, 0, ctLen);
        Buffer.BlockCopy(encryptedPrivateKey, ctLen, tag, 0, AesGcmTagLength);
        var plaintext = new byte[ctLen];
        try
        {
            using var aes = new AesGcm(kek, AesGcmTagLength);
            aes.Decrypt(wrapNonce, ciphertext, tag, plaintext);
            return plaintext;
        }
        catch (CryptographicException)
        {
            // Auth-tag mismatch == wrong passphrase.
            return null;
        }
    }

    public byte[] DeriveSharedSecret(byte[] myPrivateKey, byte[] peerPublicKey)
    {
        var priv = new X25519PrivateKeyParameters(myPrivateKey, 0);
        var pub = new X25519PublicKeyParameters(peerPublicKey, 0);
        var agreement = new X25519Agreement();
        agreement.Init(priv);
        var shared = new byte[agreement.AgreementSize];
        agreement.CalculateAgreement(pub, shared, 0);
        return shared;
    }

    /// <summary>HKDF-SHA256 on the shared secret. Salt must be deterministic across the pair.</summary>
    public byte[] DeriveMessageKey(byte[] sharedSecret, byte[] salt)
        => HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, AesGcmKeyLength, salt, MessageKeyInfo);

    private static readonly byte[] AccountWrapInfo =
        Encoding.UTF8.GetBytes("AetherLove-account-key-wrap-v1");

    /// <summary>Wrap key for the account messenger private key, derived from a PROFILE private key: any device
    /// with that profile unlocked (the precondition for chatting at all) can open the account bundle without
    /// ever holding the passphrase KEK.</summary>
    public byte[] DeriveAccountWrapKey(byte[] profilePrivateKey, byte[] accountPublicKey)
        => HKDF.DeriveKey(HashAlgorithmName.SHA256, profilePrivateKey, AesGcmKeyLength,
            SHA256.HashData(accountPublicKey), AccountWrapInfo);

    private static readonly byte[] SiblingWrapInfo =
        Encoding.UTF8.GetBytes("AetherLove-sibling-profile-key-wrap-v1");

    /// <summary>Wrap key for a NEW profile's private key, derived from an existing sibling profile's private
    /// key. Lets an account whose device never captured the passphrase KEK (every account migrated from 1.x)
    /// provision a second profile, while recovery stays passphrase-backed through the sibling. A separate info
    /// string from <see cref="DeriveAccountWrapKey"/> keeps the two derivations domain-separated.</summary>
    public byte[] DeriveSiblingWrapKey(byte[] siblingPrivateKey, byte[] newProfilePublicKey)
        => HKDF.DeriveKey(HashAlgorithmName.SHA256, siblingPrivateKey, AesGcmKeyLength,
            SHA256.HashData(newProfilePublicKey), SiblingWrapInfo);

    public (byte[] Ciphertext, byte[] Nonce) Encrypt(byte[] messageKey, byte[] plaintext)
    {
        var nonce = new byte[AesGcmNonceLength];
        RandomNumberGenerator.Fill(nonce);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[AesGcmTagLength];
        using var aes = new AesGcm(messageKey, AesGcmTagLength);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        var combined = new byte[ciphertext.Length + tag.Length];
        Buffer.BlockCopy(ciphertext, 0, combined, 0, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, combined, ciphertext.Length, tag.Length);
        return (combined, nonce);
    }

    public byte[] Decrypt(byte[] messageKey, byte[] nonce, byte[] ciphertextAndTag)
    {
        if (ciphertextAndTag.Length < AesGcmTagLength)
        {
            throw new CryptographicException("Ciphertext shorter than auth tag.");
        }
        var ctLen = ciphertextAndTag.Length - AesGcmTagLength;
        var ciphertext = new byte[ctLen];
        var tag = new byte[AesGcmTagLength];
        Buffer.BlockCopy(ciphertextAndTag, 0, ciphertext, 0, ctLen);
        Buffer.BlockCopy(ciphertextAndTag, ctLen, tag, 0, AesGcmTagLength);
        var plaintext = new byte[ctLen];
        using var aes = new AesGcm(messageKey, AesGcmTagLength);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    /// <summary>Deterministic per-pair salt: SHA-256 of the two public keys ordered by raw bytes (so both peers derive the same value), truncated to the salt length.</summary>
    public static byte[] DeriveConversationSalt(byte[] publicKeyA, byte[] publicKeyB)
    {
        var aFirst = CompareBytes(publicKeyA, publicKeyB) <= 0;
        var first = aFirst ? publicKeyA : publicKeyB;
        var second = aFirst ? publicKeyB : publicKeyA;

        var buf = new byte[first.Length + second.Length];
        Buffer.BlockCopy(first, 0, buf, 0, first.Length);
        Buffer.BlockCopy(second, 0, buf, first.Length, second.Length);

        var hash = SHA256.HashData(buf);
        var salt = new byte[KdfSaltLength];
        Buffer.BlockCopy(hash, 0, salt, 0, KdfSaltLength);
        return salt;
    }

    private static readonly byte[] PassphraseVerifierPlain =
        SHA256.HashData(Encoding.UTF8.GetBytes("AetherLove-passphrase-verifier-v1"));

    /// <summary>A known constant wrapped under the account KEK; stored server-side so a new device can check a
    /// typed passphrase before touching any profile key.</summary>
    public (byte[] Verifier, byte[] Nonce) CreatePassphraseVerifier(byte[] kek)
        => WrapPrivateKey(PassphraseVerifierPlain, kek);

    public bool CheckPassphraseVerifier(byte[] verifier, byte[] nonce, byte[] kek)
    {
        var plain = UnwrapPrivateKey(verifier, nonce, kek);
        return plain is not null && plain.AsSpan().SequenceEqual(PassphraseVerifierPlain);
    }

    private static readonly byte[] VerificationDomain =
        Encoding.UTF8.GetBytes("AetherLove-verify-v1");

    /// <summary>Full 32-byte domain-separated fingerprint of a conversation's two public keys, ordered by raw bytes so both peers compute an identical value. Drives the verification weave image and safety code.</summary>
    public static byte[] VerificationFingerprint(byte[] publicKeyA, byte[] publicKeyB)
    {
        var aFirst = CompareBytes(publicKeyA, publicKeyB) <= 0;
        var first = aFirst ? publicKeyA : publicKeyB;
        var second = aFirst ? publicKeyB : publicKeyA;

        var buf = new byte[VerificationDomain.Length + first.Length + second.Length];
        Buffer.BlockCopy(VerificationDomain, 0, buf, 0, VerificationDomain.Length);
        Buffer.BlockCopy(first, 0, buf, VerificationDomain.Length, first.Length);
        Buffer.BlockCopy(second, 0, buf, VerificationDomain.Length + first.Length, second.Length);

        return SHA256.HashData(buf);
    }

    private static int CompareBytes(byte[] a, byte[] b)
    {
        var n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
        {
            if (a[i] != b[i])
            {
                return a[i] < b[i] ? -1 : 1;
            }
        }
        return a.Length.CompareTo(b.Length);
    }
}
