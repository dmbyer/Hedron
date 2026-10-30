namespace Hedron.Core.Modules.BalanceInspection
{
    /// <summary>
    /// Settings bound from the <c>Balance:</c> configuration section.
    /// Override via environment variable: <c>HEDRON_Balance__StandardsPath</c>.
    /// </summary>
    public sealed class BalanceOptions
    {
        /// <summary>
        /// Path to the balance-standards YAML file: absolute, or relative to the Hedron root; when unset in configuration the host derives it under <c>Paths:DataDirectory</c> (see <c>Server/DataPaths</c>) as
        /// <c>balance/standards.yaml</c>. Absent file falls back to <see cref="Standards.BalanceStandardsDefaults"/>.
        /// </summary>
        public string StandardsPath { get; set; } = "data/balance/standards.yaml";
    }
}
