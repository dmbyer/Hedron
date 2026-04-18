using System;

namespace Hedron.Core.ECS
{
    /// <summary>
    /// Global access point for the app-lifetime <see cref="EntityService"/>.
    /// </summary>
    /// <remarks>
    /// DI registers the world instance via <see cref="SetWorld"/> at startup so the static
    /// accessor and any injected <see cref="EntityService"/> resolve to the same object.
    /// Doc-example code in <c>docs/architecture/</c> uses <c>EcsManager.World</c> as shorthand;
    /// the real implementation type is <see cref="EntityService"/>.
    /// </remarks>
    public static class EcsManager
    {
        private static EntityService _world;
        private static readonly object _lock = new object();

        /// <summary>
        /// Returns the process-wide <see cref="EntityService"/>. If DI has registered one via
        /// <see cref="SetWorld"/>, that instance is returned; otherwise a lazily-created default
        /// instance is used so static code paths still function.
        /// </summary>
        public static EntityService World
        {
            get
            {
                if (_world != null)
                    return _world;

                lock (_lock)
                {
                    if (_world == null)
                        _world = new EntityService();
                    return _world;
                }
            }
        }

        /// <summary>
        /// Assigns the app-lifetime <see cref="EntityService"/>. Intended to be called once from
        /// composition root (DI) so the static accessor and the injected service resolve to the
        /// same instance.
        /// </summary>
        public static void SetWorld(EntityService world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            lock (_lock)
            {
                _world = world;
            }
        }
    }
}
