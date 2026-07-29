# AetherLove

*Because "LFG casual RP, maybe more" in Party Finder deserves better.*

---

## The idea

FFXIV has always been more than a game. It is a place where people build real friendships, find roleplay partners,  play content together, hunt for ridiculous achivements together (hello fishers),  and, yes, look for something a little more. Anyone who has spent time around Eorzea knows this. The venues know this. The Party Finder posts that start with three dots and end with a `/tell me` know this.

What FFXIV does not have is a good way to actually find those people. You can hang around a venue and hope. You can post something vague in Party Finder and watch it scroll off. You can ask in Limsa's /yell chat and make it weird. None of these are good options.

AetherLove is the option that should have always existed: a proper matching experience, built directly into the game. You set up a profile, tell it what you are looking for (content partners, someone to chat with, a roleplay companion, or something that stays between you and your match), and it finds people who are looking for the same thing. No alt-tabbing. No third-party websites. No sharing your character name with strangers before you are ready.

It is private by design, encrypted end-to-end, and entirely opt-in at every step. The details are below if you want them, we actively encourage you to read them.

---

## What's in this repo

This is the source code for the plugin: the part that runs on your machine, inside the game. The server is closed-source and operated privately. Publishing the client is the least we could do for something that handles your messages and photos. [Dalamud](https://github.com/goatcorp/Dalamud) is open-source. [XIVLauncher](https://github.com/goatcorp/FFXIVQuickLauncher) is open-source. Publishing only a zip file on top of that foundation felt like a choice we did not want to make.

**You play FFXIV and just want the plugin**: you do not need any of this. Add the custom repo to Dalamud and move on:

```
https://puni.sh/api/repository/aetherlove
```
---

## Privacy-first

AetherLove matches players based on what they actually tell it during onboarding: what they are looking for (chatting, casual play buddies, roleplay partners, in-game romance, something longer term, or ERP if that is your thing), the content they enjoy (everything from Savage raiding and Extreme trials to Gpose, fishing, housing, and Triple Triad), the region they play in, the languages they speak, and when they are usually online. That is the basis for matching. Nothing more. We are deliberate about what we do and do not collect.

### What AetherLove does not share or expose

- **Your character name.** You choose a display name for your AetherLove profile. Your real FFXIV character name is never transmitted to other players.
- **Your home world or server.** Only the region you select during onboarding is used for matching. Your specific server is never transmitted to other players.
- **Your account, character, or Lodestone ID.** Aetherlove does not share your account, character or lodestone ID with other players. Ever.
- **Your photos to the general public.** Profile photos are only visible to players who are eligible matches: not indexed, not publicly accessible, not cached on third-party infrastructure.
- **NSFW content you did not ask for.** Adult content matching is 100% opt-in during onboarding. The default experience contains none of it.

---

## End-to-end encryption

### Plain language

When you send a message to a match, it is encrypted on your device before it leaves the plugin. It travels to the AetherLove server as an unreadable blob, gets stored as an unreadable blob, and is only decrypted on your match's device when they receive it. AetherLove staff cannot read your messages. Not because we say we won't. Because we technically cannot.

### Technical implementation

All cryptographic logic lives in [`Services/Crypto/CryptoService.cs`](Services/Crypto/CryptoService.cs).
The shared data contracts (what the server actually stores and transmits) are in
[`AetherLove.Shared/Messaging/MessagingDtos.cs`](AetherLove.Shared/Messaging/MessagingDtos.cs).

#### Key generation

During onboarding ([`Screens/Onboarding/OnboardingScreen.Step3aPassphrase.cs`](Screens/Onboarding/OnboardingScreen.Step3aPassphrase.cs)),
each client generates an **X25519** key pair using Bouncy Castle (`BouncyCastle.Cryptography`).
The 32-byte public key is uploaded to the server. The private key is never transmitted in plaintext.

#### Private key protection (passphrase + key wrapping)

The private key is encrypted client-side before it ever leaves the device:

1. A random 16-byte salt is generated.
2. **Argon2id** (v1.3, 64 MB memory, 3 iterations, parallelism 1) derives a 256-bit
   Key Encryption Key (KEK) from your passphrase and the salt. This takes roughly 300 ms
   by design: slow enough to resist brute-force, fast enough to not be annoying.
3. **AES-256-GCM** wraps the private key using the KEK and a random 12-byte nonce.
   The wire format is `ciphertext ‖ 16-byte auth tag`.

The server stores the ciphertext, nonce, salt, and Argon2 parameters, never the KEK or the
plaintext private key. An incorrect passphrase produces an AES-GCM authentication-tag failure;
there is no oracle to tell an attacker they are close.

#### Multi-device key restore

When you log in on a new device, the client detects that the server has a key bundle but the
local machine has no unwrapped private key
([`Services/Auth/SessionBootstrapper.cs`](Services/Auth/SessionBootstrapper.cs), `NeedsPassphraseUnlock`).
The app routes you to the passphrase screen
([`Screens/PassphraseUnlockScreen.cs`](Screens/PassphraseUnlockScreen.cs)):
you enter your passphrase → Argon2id re-derives the KEK → AES-GCM unwraps the private key →
the plaintext key is stored only in local Dalamud config. The server sees nothing useful.

**Your passphrase is the sole recovery mechanism.** If you lose it, your private key is gone
and past messages cannot be recovered. We cannot reset it for you. Write it down somewhere offline.

#### Message encryption

Every message follows this path before leaving the plugin:

1. **ECDH shared secret**: X25519 agreement between your private key and your match's
   public key (`DeriveSharedSecret`).
2. **Conversation salt**: SHA-256 of both public keys concatenated in a deterministic order
   (lexicographically lower key first), so both sides independently arrive at the same salt
   without coordination (`DeriveConversationSalt`).
3. **Message key**: HKDF-SHA256 over the shared secret, conversation salt, and the domain
   label `"AetherLove-chat-msg-key-v1"` (`DeriveMessageKey`). This produces a fresh 256-bit
   symmetric key.
4. **AES-256-GCM encryption**: random 12-byte nonce per message; wire format is
   `ciphertext ‖ 16-byte auth tag` (`Encrypt` / `Decrypt`).

The server stores only the ciphertext, nonce, sender/recipient identifiers, and timestamp;
see the `Message` model on the server side and the `EncryptedMessageDto` / `SendMessageRequest`
records in `AetherLove.Shared/`.

#### Algorithm summary

| Purpose | Algorithm | Key / nonce size | Library |
|---|---|---|---|
| Identity key pair | X25519 ECDH | 32 bytes | Bouncy Castle 2.6.2 |
| Passphrase → KEK | Argon2id v1.3 | 32-byte output | Bouncy Castle 2.6.2 |
| Private key wrapping | AES-256-GCM | 32-byte key, 12-byte nonce | `System.Security.Cryptography` |
| Conversation salt | SHA-256 | - | `System.Security.Cryptography` |
| Message key | HKDF-SHA256 | 32-byte output | `System.Security.Cryptography` |
| Message encryption | AES-256-GCM | 32-byte key, 12-byte nonce | `System.Security.Cryptography` |

**On passphrases:** Your passphrase is the single point of recovery. If you forget it, your private key is gone and so are your message histories. We cannot reset it for you. Choose something you will remember; write it down somewhere offline if necessary. The passphrase unlock screen in the plugin is the only entry point to this process.

---

## Moderation

AetherLove operates in an opt-in adult content space. That comes with responsibility.

Profile texts and uploaded images are screened using a combination of automated tools and human review before they become visible to other users. Automated screening catches obvious violations immediately. A human reviews edge cases and anything the automated system flags for uncertainty. Accounts that violate the community guidelines are actioned by a real person, not a pure algorithm.

This is not a perfect system (no moderation system is), but it is an active one. Reports from users are taken seriously. The goal is for AetherLove to be a space where adults can engage safely and on their own terms, without it becoming a vector for harassment or illegal content.

---

## AI use [![AI-DECLARATION: pair](https://img.shields.io/badge/䷼%20AI--DECLARATION-pair-ffedd5?labelColor=ffedd5)](https://ai-declaration.md)

See [AI-DECLARATION.md](AI-DECLARATION.md). Development was AI-assisted and we are transparent about exactly where and how.

---

## License

[AGPL v3](LICENSE)
