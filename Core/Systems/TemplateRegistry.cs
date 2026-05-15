using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;

namespace Hedron.Core.Systems
{
    /// <summary>
    /// Default <see cref="ITemplateRegistry"/>. Thread-safe for concurrent reads and
    /// occasional writes (registration happens at startup and during <c>@reload</c>).
    /// </summary>
    public sealed class TemplateRegistry : ITemplateRegistry
    {
        private readonly EntityService _entityService;
        private readonly ConcurrentDictionary<string, IEntityTemplate> _templates =
            new(StringComparer.OrdinalIgnoreCase);

        public TemplateRegistry(EntityService entityService)
        {
            _entityService = entityService;
        }

        public void Register(string blueprintId, IEntityTemplate template)
        {
            if (string.IsNullOrWhiteSpace(blueprintId))
                throw new ArgumentException("Blueprint id must not be blank.", nameof(blueprintId));
            _templates[blueprintId] = template;
        }

        public bool TryGet(string blueprintId, [NotNullWhen(true)] out IEntityTemplate? template)
        {
            return _templates.TryGetValue(blueprintId, out template);
        }

        public Entity Spawn(string blueprintId)
            => Spawn(blueprintId, overrides: null);

        public Entity Spawn(string blueprintId, IDictionary<string, object>? overrides)
        {
            if (!_templates.TryGetValue(blueprintId, out var template))
                throw new KeyNotFoundException($"No template registered for blueprint id '{blueprintId}'.");

            var entity = _entityService.CreateEntity();
            _entityService.AddComponent(entity.Id, new BlueprintComponent { BlueprintId = blueprintId });
            template.Apply(entity, _entityService);
            // overrides are reserved for future slices (item modifiers, mob variants);
            // no template consumes them yet.
            return entity;
        }

        public IReadOnlyCollection<string> AllBlueprintIds()
            => _templates.Keys.ToArray();

        public void Clear() => _templates.Clear();
    }
}
