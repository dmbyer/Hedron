using System;
using System.Security.Cryptography;

namespace Hedron.Core.Systems
{
    /// <summary>
    /// PBKDF2-SHA256 password hasher. Stores salt + hash together as a single Base64 string
    /// so no separate salt column or lookup is needed. No external NuGet dependency.
    /// </summary>
    /// <remarks>
    /// Format: Base64( salt[16] || hash[32] ) where hash = PBKDF2(password, salt, 100_000, SHA256).
    /// </remarks>
    public sealed class PasswordHasher : IPasswordHasher
    {
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int Iterations = 100_000;

        public string Hash(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var hash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                HashSize);

            var combined = new byte[SaltSize + HashSize];
            Buffer.BlockCopy(salt, 0, combined, 0, SaltSize);
            Buffer.BlockCopy(hash, 0, combined, SaltSize, HashSize);
            return Convert.ToBase64String(combined);
        }

        public bool Verify(string password, string storedHash)
        {
            var combined = Convert.FromBase64String(storedHash);
            if (combined.Length != SaltSize + HashSize)
                return false;

            var salt = combined[..SaltSize];
            var expectedHash = combined[SaltSize..];

            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                HashSize);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
    }
}
