namespace Hedron.Core.Systems.Definitions
{
    /// <summary>Severity of a <see cref="ContentDiagnostic"/>. Neither refuses a load.</summary>
    public enum ContentDiagnosticSeverity
    {
        Warning,
        Error,
    }

    /// <summary>
    /// One finding a <see cref="IContentReloadParticipant"/> reports while rebuilding: an
    /// <see cref="ContentDiagnosticSeverity.Error"/> is a definition that was skipped, a
    /// <see cref="ContentDiagnosticSeverity.Warning"/> is one that loaded with a problem.
    /// </summary>
    /// <param name="Source">Where it came from, e.g. <c>world/kill.yaml</c>.</param>
    public sealed record ContentDiagnostic(ContentDiagnosticSeverity Severity, string Source, string Message)
    {
        public override string ToString() =>
            $"[{(Severity == ContentDiagnosticSeverity.Error ? "error" : "warning")}] {Source}: {Message}";
    }
}
