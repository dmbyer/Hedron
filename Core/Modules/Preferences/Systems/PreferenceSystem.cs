using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.Modules.Preferences.Components;

namespace Hedron.Core.Modules.Preferences.Systems
{
    /// <inheritdoc cref="IPreferenceSystem"/>
    public sealed class PreferenceSystem : IPreferenceSystem
    {
        private readonly EntityService _entityService;

        public PreferenceSystem(EntityService entityService)
        {
            _entityService = entityService;
        }

        public bool IsEnabled(uint entityId, PreferenceId preference)
            => _entityService.TryGet<PlayerConfigurationComponent>(entityId, out var config)
               && config.Preferences.TryGetValue(preference, out var enabled)
                ? enabled
                : PreferenceRegistry.DefaultFor(preference);

        public void Set(uint entityId, PreferenceId preference, bool enabled)
        {
            if (!_entityService.TryGet<PlayerConfigurationComponent>(entityId, out var config))
            {
                config = new PlayerConfigurationComponent();
                _entityService.AddComponent(entityId, config);
            }

            config.Preferences[preference] = enabled;
        }

        public IReadOnlyList<PreferenceState> GetAll(uint entityId)
        {
            var states = new List<PreferenceState>(PreferenceRegistry.All.Count);
            foreach (var definition in PreferenceRegistry.All)
                states.Add(new PreferenceState(definition, IsEnabled(entityId, definition.Id)));

            return states;
        }
    }
}
