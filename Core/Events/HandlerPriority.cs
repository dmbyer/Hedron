namespace Hedron.Core.Events
{
    /// <summary>
    /// Canonical priority tiers for <see cref="IEventHandler{TEvent}"/>.
    /// Lower numbers dispatch first.
    /// </summary>
    /// <remarks>
    /// These match the tiers documented in <c>docs/reference/handlers.md</c> for
    /// <c>PlayerDeathEvent</c>. Gaps between tiers let new handlers slot in without
    /// renumbering. Use the named constants (or a value between them) rather than
    /// scattering magic numbers through handler code.
    /// </remarks>
    public static class HandlerPriority
    {
        /// <summary>
        /// First — clean up the state that caused the event (e.g. remove from combat).
        /// </summary>
        public const int State = 10;

        /// <summary>
        /// Primary domain response (e.g. apply death penalty, trigger respawn).
        /// </summary>
        public const int Domain = 20;

        /// <summary>
        /// Broadcast to interested observers after domain work is done.
        /// </summary>
        public const int Notification = 80;

        /// <summary>
        /// Write state changes after handlers have settled.
        /// </summary>
        public const int Persistence = 90;

        /// <summary>
        /// AI / late reactive systems running last (e.g. update NPC threat tables).
        /// </summary>
        public const int Ai = 95;
    }
}
