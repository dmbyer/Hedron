namespace Hedron.Core.Modules.BalanceInspection
{
    /// <summary>
    /// Settings bound from the <c>Balance:</c> configuration section.
    /// Override via environment variable: <c>HEDRON_Balance__StandardsPath</c>.
    /// </summary>
    public sealed class BalanceOptions
    {
        /// <summary>
        /// Path to the balance-standards YAML file. May be an absolute path or relative to the
        /// working directory. Absent file falls back to <see cref="Standards.BalanceStandardsDefaults"/>.
        /// Default: <c>data/balance/standards.yaml</c>.
        /// </summary>
        public string StandardsPath { get; set; } = "data/balance/standards.yaml";
    }
}
