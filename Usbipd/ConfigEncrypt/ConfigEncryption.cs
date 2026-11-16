// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

// The in-house ConfigEncrypt library (ConfigEncrypt.Services.ConfigEncryption), copied as source.
// Only the password-based (AedXChaCha20Poly1305) path is removed; the rest is unchanged.
// Marked as generated code in .editorconfig, which needs this directive for its nullable annotations.

#nullable enable annotations

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace ConfigEncrypt.Services {
    /// <summary>
    /// Provides helpers for encrypting and decrypting configuration values stored as strings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The encrypted output is serialized as a Base64 string. Public entry points support versioned payloads so that decryption can select the appropriate algorithm from the
    /// leading version byte.
    /// </para>
    /// <para>
    /// Supported encryption modes are:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <see cref="CryptographyVersion.RsaCertificateKeyPair"/>: uses hybrid encryption by encrypting the plaintext with AES-GCM and encrypting the AES key with the RSA public key
    /// from the supplied certificate.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// RSA certificate payloads include the certificate thumbprint hash so that decryption can locate the matching certificate in the local machine certificate store.
    /// </para>
    /// </remarks>
    public static class ConfigEncryption {
        public const string ConfigPrefix = "enc:";
        
        private const int _aesTagLen = 16;
        private const int _aesKeyLen = 32; // 256-bit key
        private const int _aesNonceLen = 12;

        private static readonly RSAEncryptionPadding _encryptionPadding = RSAEncryptionPadding.OaepSHA256;

        /// <summary>
        /// Encrypts a plaintext configuration value using the requested cryptography version.
        /// </summary>
        /// <param name="plaintext">
        ///     The plaintext value to encrypt. If the value is <see langword="null"/>, empty, or whitespace, it is returned unchanged.
        /// </param>
        /// <param name="version">
        ///     The encryption format and algorithm version to use.
        /// </param>
        /// <param name="storeLocation">Which store location, Local Machine or Local User, for <see cref="StoreLocation"/></param>
        /// <param name="certificate">
        ///     The certificate used for RSA certificate key-pair encryption. This parameter is required when <paramref name="version"/> is <see cref="CryptographyVersion.RsaCertificateKeyPair"/> and ignored for password-derived symmetric encryption.
        /// </param>
        /// <param name="includePrefix">
        ///     <see langword="true"/> to prepend the configured encryption prefix to the returned payload; otherwise, <see langword="false"/>.
        /// </param>
        /// <param name="storeName">Which store name the cert was located in for <see cref="StoreName"/></param>
        /// <returns>
        /// A Base64-encoded encrypted payload, optionally prefixed with the configured encryption marker.
        /// </returns>
        /// <remarks>
        /// Decryption determines the encryption method from the version byte stored in the encrypted payload.
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="version"/> requires a certificate and <paramref name="certificate"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="NotSupportedException">
        /// Thrown when <paramref name="version"/> is not supported.
        /// </exception>
        /// <exception cref="CryptographicException">
        /// Thrown when encryption fails, such as when the certificate key cannot perform the requested RSA operation.
        /// </exception>
        public static string Encrypt(string plaintext, CryptographyVersion version, StoreName storeName = StoreName.Root, StoreLocation storeLocation = StoreLocation.LocalMachine, X509Certificate2? certificate = null, bool includePrefix = true) {
            if (string.IsNullOrWhiteSpace(plaintext)) {
                return plaintext;
            }
            
            
            string prefix = includePrefix ? ConfigPrefix : string.Empty;
            
            switch (version) {
                case CryptographyVersion.RsaCertificateKeyPair:
                    if (certificate == null) {
                        throw new ArgumentException(nameof(certificate));
                    }
                    return prefix + EncryptWithRsaKeypair(plaintext, certificate, storeName, storeLocation);
                default:
                    throw new  NotSupportedException($"Unsupported cipher version {version}.");
            }
        }
        
        /// <summary>
        /// Decrypts a Base64-encoded encrypted configuration payload.
        /// </summary>
        /// <param name="payload">
        /// The encrypted payload to decrypt. If the value is <see langword="null"/>, empty, or whitespace, it is returned unchanged.
        /// </param>
        /// <returns>
        /// The decrypted plaintext string.
        /// </returns>
        /// <remarks>
        /// The first byte of the decoded payload identifies the cryptography version used to encrypt the value. The appropriate private decryption routine is selected from that version byte.
        /// </remarks>
        /// <exception cref="FormatException">
        /// Thrown when <paramref name="payload"/> is not valid Base64 or the decoded payload is too short for the selected encryption format.
        /// </exception>
        /// <exception cref="NotSupportedException">
        /// Thrown when the payload contains an unsupported cryptography version.
        /// </exception>
        /// <exception cref="CryptographicException">
        /// Thrown when authentication, key derivation, RSA decryption, or symmetric decryption fails.
        /// </exception>
        public static string Decrypt(string payload) {
            if (string.IsNullOrWhiteSpace(payload)) {
                return payload;
            }

            if (payload.StartsWith(ConfigPrefix)) {
                payload = payload[ConfigPrefix.Length..].Trim();
            }
            
            byte[] payloadBytes = Convert.FromBase64String(payload);
            CryptographyVersion version = (CryptographyVersion)payloadBytes[0];
            switch (version) {
                case CryptographyVersion.RsaCertificateKeyPair:
                    return DecryptWithRsaKeypair(payloadBytes);
                default:
                    throw new NotSupportedException($"Unsupported cipher version {version}.");
            }
            
        }

        #region Hybrid RSA/AES Cryptography

        /// <summary>
        /// Encrypts plaintext using hybrid RSA and AES-GCM encryption.
        /// </summary>
        /// <param name="plaintext">
        /// The plaintext value to encrypt.
        /// </param>
        /// <param name="certificate">
        /// The certificate containing the RSA public key used to encrypt the generated AES key.
        /// </param>
        /// <param name="storeName">What cert store for the store location the cert is stored in.</param>
        /// <param name="storeLocation">What store location the cert is stored in.</param>
        /// <returns>
        /// A Base64-encoded payload containing the version, certificate thumbprint, encrypted AES key, AES-GCM nonce, authentication tag, and ciphertext.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The plaintext is encrypted with a randomly generated AES-256 key using AES-GCM. The AES key is then encrypted with the RSA public key from <paramref name="certificate"/> using the configured RSA encryption padding.
        /// </para>
        /// <para>
        /// The serialized payload layout is:
        /// <c>version | Cert Store Name | Cert Store Location | thumbprint length | thumbprint | encrypted AES key length | encrypted AES key | nonce length | nonce | tag length | tag | ciphertext length | ciphertext</c>.
        /// </para>
        /// <para>
        /// The certificate thumbprint is stored as raw hash bytes from <see cref="X509Certificate.GetCertHash()"/> so that decryption can locate the matching certificate.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="plaintext"/> is <see langword="null"/>, empty, or whitespace.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when <paramref name="certificate"/> does not expose an RSA public key.
        /// </exception>
        /// <exception cref="CryptographicException">
        /// Thrown when AES-GCM encryption or RSA encryption fails.
        /// </exception>
        private static string EncryptWithRsaKeypair(string plaintext, X509Certificate2 certificate, StoreName storeName, StoreLocation storeLocation) {
            ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
            
            using RSA? rsa = certificate.GetRSAPublicKey();

            if (rsa is null) {
                throw new Exception("The cert provided does not use the RSA algorithm. Please provide a cert thumbprint that uses an RSA algorithm that has both the public and private key pair available or use the legacy encryption.");
            }
            
            byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

            byte[] aesKey = RandomNumberGenerator.GetBytes(_aesKeyLen);
            byte[] nonce = RandomNumberGenerator.GetBytes(_aesNonceLen);
            byte[] tag = new byte[_aesTagLen];
            byte[] ciphertext = new byte[plaintextBytes.Length];

            using (AesGcm aes = new AesGcm(aesKey, tag.Length)) {
                aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);
            }

            byte[] encryptedAesKey = rsa.Encrypt(aesKey, _encryptionPadding);
            byte[] thumbprint = certificate.GetCertHash();

            using MemoryStream stream = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(stream);
            
            writer.Write((byte)CryptographyVersion.RsaCertificateKeyPair);
            writer.Write((byte)storeName);
            writer.Write((byte)storeLocation);
            writer.Write(thumbprint.Length);
            writer.Write(thumbprint);
            writer.Write(encryptedAesKey.Length);
            writer.Write(encryptedAesKey);
            writer.Write(nonce.Length);
            writer.Write(nonce);
            writer.Write(tag.Length);
            writer.Write(tag);
            writer.Write(ciphertext.Length);
            writer.Write(ciphertext);

            CryptographicOperations.ZeroMemory(aesKey);

            return Convert.ToBase64String(stream.ToArray());
        }

        /// <summary>
        /// Decrypts a hybrid RSA and AES-GCM encrypted payload.
        /// </summary>
        /// <param name="encryptedPayload">
        /// The decoded encrypted payload bytes.
        /// </param>
        /// <returns>
        /// The decrypted plaintext string.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The method reads the certificate thumbprint from the payload, locates the matching certificate in the <see cref="StoreName.Root"/> store for <see cref="StoreLocation.LocalMachine"/>, and uses the certificate's RSA private key to decrypt the AES key.
        /// </para>
        /// <para>
        /// After the AES key is recovered, the ciphertext is decrypted and authenticated using AES-GCM with the stored nonce and authentication tag.
        /// </para>
        /// <para>
        /// The expected serialized payload layout is:
        /// <c>version | thumbprint length | thumbprint | encrypted AES key length | encrypted AES key | nonce length | nonce | tag length | tag | ciphertext length | ciphertext</c>.
        /// </para>
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the payload version is unsupported, the matching certificate cannot be found, or the matching certificate does not expose an RSA private key.
        /// </exception>
        /// <exception cref="EndOfStreamException">
        /// Thrown when the payload is truncated or does not contain the expected serialized fields.
        /// </exception>
        /// <exception cref="CryptographicException">
        /// Thrown when RSA decryption fails or AES-GCM authentication/decryption fails.
        /// </exception>
        private static string DecryptWithRsaKeypair(byte[] encryptedPayload) {
            using MemoryStream stream = new MemoryStream(encryptedPayload);
            using BinaryReader reader = new BinaryReader(stream);
            CryptographyVersion version = (CryptographyVersion)reader.ReadByte();

            if (version != CryptographyVersion.RsaCertificateKeyPair) {
                throw new InvalidOperationException($"Unsupported encryption version: {version}");
            }
            StoreName storeName = (StoreName)reader.ReadByte();
            StoreLocation storeLocation = (StoreLocation)reader.ReadByte();
            int thumbprintLength = reader.ReadInt32();
            byte[] thumbprintBytes = reader.ReadBytes(thumbprintLength);
            string thumbprint = Convert.ToHexString(thumbprintBytes);

            int encryptedAesKeyLength = reader.ReadInt32();
            byte[] encryptedAesKey = reader.ReadBytes(encryptedAesKeyLength);

            int nonceLength = reader.ReadInt32();
            byte[] nonce = reader.ReadBytes(nonceLength);

            int tagLength = reader.ReadInt32();
            byte[] tag = reader.ReadBytes(tagLength);

            int ciphertextLength = reader.ReadInt32();
            byte[] ciphertext = reader.ReadBytes(ciphertextLength);
            
            using X509Store store = new X509Store(storeName, storeLocation);
            store.Open(OpenFlags.ReadOnly);
            X509Certificate2 certificate = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false).FirstOrDefault() ?? throw new InvalidOperationException($"Certificate with thumbprint {thumbprint} was not found in Store Name {storeName.ToString()}, Store Location {storeLocation.ToString()}");
            store.Close();
            using RSA? rsa = certificate.GetRSAPrivateKey();

            if (rsa is null) {
                throw new InvalidOperationException($"Certificate with thumbprint {thumbprint} does not have an available RSA private key.");
            }

            byte[] aesKey = rsa.Decrypt(encryptedAesKey, _encryptionPadding);

            byte[] plaintextBytes = new byte[ciphertext.Length];

            using (AesGcm aes = new AesGcm(aesKey, tag.Length)) {
                aes.Decrypt(nonce, ciphertext, tag, plaintextBytes);
            }

            CryptographicOperations.ZeroMemory(aesKey);

            return Encoding.UTF8.GetString(plaintextBytes);

        }

        #endregion
        
    }
}