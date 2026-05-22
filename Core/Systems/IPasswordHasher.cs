namespace Hedron.Core.Systems
{
    /// <summary>
    /// Hashes and verifies passwords. Implementation is pluggable; default is PBKDF2-SHA256.
    /// </summary>
    public interface IPasswordHasher
    {
        /// <summary>
        /// Produces a self-contained hash string that includes the algorithm parameters and salt.
        /// </summary>
        string Hash(string password);

        /// <summary>
        /// Returns <c>true</c> if <paramref name="password"/> matches the stored <paramref name="hash"/>.
        /// </summary>
        bool Verify(string password, string hash);
    }
}
