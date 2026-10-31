using System;
using System.Threading.Tasks;
using Hedron.Core.Output;

namespace Hedron.Web.Services;

/// <summary>
/// Renders a help topic exactly as a telnet player would see it, colour off, through the engine's
/// own <see cref="IOutputFormatterRegistry"/> — so the editor preview can never drift from the game.
/// </summary>
public static class HelpTopicPreview
{
    public static string Render(IOutputFormatterRegistry formatters, IOutputMessage message)
    {
        var session = new PreviewSession();
        return formatters.Resolve(session).Format(message, session);
    }

    private sealed class PreviewSession : Hedron.Core.Sessions.ISession
    {
        public Guid SessionId { get; } = Guid.NewGuid();
        public uint PlayerEntityId => 0;
        public string TransportKey => "telnet";
        public bool SupportsColor => false;
        public Task SendLineAsync(string text) => Task.CompletedTask;
    }
}
