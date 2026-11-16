// SPDX-FileCopyrightText: 2026 QSI Banking
//
// SPDX-License-Identifier: GPL-3.0-only

// Part of the in-house ConfigEncrypt library, copied as source; only the RSA certificate version is supported here.

namespace ConfigEncrypt.Services {
    /// <summary>
    /// The encryption format and algorithm version, stored as the first byte of an encrypted payload.
    /// </summary>
    public enum CryptographyVersion
    {
        /// <summary>
        /// Hybrid encryption: AES-GCM for the plaintext, the AES key encrypted with a certificate's RSA public key.
        /// </summary>
        None = 0,
        RsaCertificateKeyPair = 2
    }
}
