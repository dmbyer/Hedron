﻿using Hedron.Core.Entities.Living;
using Hedron.Core.Entities.Properties;
using Hedron.Core.Locale;
using Hedron.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hedron.Core.System.Autogeneration
{
    public static class AutogenMob
    {
        /// <summary>
        /// Creates a mob of all levels and instantiates them
        /// </summary>
        /// <param name="name">The base name of the mob</param>
        /// <param name="parentID">The prototype ID of the parent room</param>
        /// <returns>A list of instance mob IDs</returns>
        public static List<uint?> CreateAndInstantiateAllLevels(string name, uint? parentID)
        {
            var mobs = new List<uint?>();
            var parentRoom = DataAccess.Get<Room>(parentID, CacheType.Instance);
            var protoRoom = DataAccess.Get<Room>(parentRoom?.Prototype, CacheType.Prototype);

            // Fail gracefully upon an invalid room ID
            if (parentRoom == null)
            {
                Logger.Error(nameof(AutogenMob), nameof(CreateAndInstantiateAllLevels), "Invalid Room ID. Cannot create and instantiate mobs.");
                return new List<uint?>();
            }

            foreach (var level in Enum.GetValues(typeof(MobLevel)))
            {
                var mob = Mob.NewPrototype((uint)parentID, (MobLevel)level);
                var levelName = MobLevelModifier.MapName((MobLevel)level);
                var desc = $"{levelName} {name}";

                protoRoom.Animates.AddEntity(mob.Prototype, mob, true);

                mob.ShortDescription = $"a {desc}";
                mob.LongDescription = $"A {desc} is here.";
                mob.Name = $"{levelName} {name}";

                DataPersistence.SaveObject(mob);

                mobs.Add(mob.Spawn(true, (uint)parentRoom.Animates.Instance));
            }

            DataPersistence.SaveObject(protoRoom);

            return mobs;
        }

        /// <summary>
        /// Automatically generates a variet of mobs from a given prototype.
        /// </summary>
        /// <param name="mobPrototype">The prototype of the mob to generate from.</param>
        /// <param name="forArea">The prototype of the area these mobs should be associated with. May be null.</param>
        /// <returns>A list of generated prototype mob IDs.</returns>
        /// <remarks>Use {Mob.Level} in name and/or description texts as a placeholder to be automatically completed with the mob level.</remarks>
        public static List<uint?> AutogenerateFromPrototype(Mob mobPrototype, Area forArea)
        {
            if (mobPrototype == null ||
                mobPrototype.WasAutoGenerated == true ||
                mobPrototype.CacheType == CacheType.Instance ||
                mobPrototype.Level != MobLevel.Fair ||
                forArea?.CacheType == CacheType.Instance)
                return new List<uint?>();

            var mobIDs = new List<uint?>();
            mobPrototype.WasAutoGenerated = true;

            foreach (var level in Enum.GetValues(typeof(MobLevel)))
            {
                var levelName = MobLevelModifier.MapName((MobLevel)level);
                Mob mobToUse;

                if ((MobLevel)level == MobLevel.Fair)
				{
                    mobToUse = mobPrototype;
                }
                else
				{
                    mobToUse = Mob.NewPrototype(null, (MobLevel)level);
                    mobPrototype.CopyTo(mobToUse);
                    mobToUse.Level = (MobLevel)level;
                }

                Logger.Info(nameof(AutogenMob), nameof(AutogenerateFromPrototype), $"Autogenerate modified {mobToUse.Name} [{mobToUse.Prototype}]");

                mobToUse.Name = mobToUse.Name.Replace("{Mob.Level}", levelName);
                mobToUse.ShortDescription = mobToUse.ShortDescription.Replace("{Mob.Level}", levelName);
                mobToUse.LongDescription = mobToUse.LongDescription.Replace("{Mob.Level}", levelName);

                if (forArea?.AutogeneratedPrototypeMobs.GetEntity<Mob>(mobToUse.Prototype) == null)
                    forArea?.AutogeneratedPrototypeMobs.AddEntity(mobToUse.Prototype, mobToUse, true);

                DataPersistence.SaveObject(mobToUse);
                mobIDs.Add(mobToUse.Prototype);
            }

            return mobIDs;
        }
    }
}