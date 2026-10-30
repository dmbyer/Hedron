namespace Hedron.Core.Modules.Persistence
{
    /// <summary>
    /// Settings bound from the <c>Persistence:</c> configuration section.
    /// Override via environment variable: <c>HEDRON_Persistence__DatabasePath</c>,
    /// <c>HEDRON_Persistence__FlushIntervalSeconds</c>.
    /// </summary>
    public sealed class PersistenceOptions
    {
        /// <summary>
        /// Path to the SQLite database file: absolute, or relative to the Hedron root; when unset in configuration the host derives it under <c>Paths:DataDirectory</c> (see <c>Server/DataPaths</c>) as
        /// <c>hedron.db</c>. Use <c>:memory:</c> for an in-memory database (tests only). The class default
        /// below only applies when the options are constructed directly (tests).
        /// </summary>
        public string DatabasePath { get; set; } = "data/hedron.db";

        /// <summary>
        /// How often the periodic flush timer writes dirty entities to SQLite.
        /// Default: <c>60</c> seconds. Use a smaller value (e.g. 5) for fast dev iteration.
        /// </summary>
        public int FlushIntervalSeconds { get; set; } = 60;
    }
}
