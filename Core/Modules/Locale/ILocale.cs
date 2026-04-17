using Core.ECS.Properties;
using System;
using System.Collections.Generic;
using System.Text;

namespace Core.Modules.Locale
{
	public interface ILocale
	{
		public bool AllowRandomlyGeneratedEntities { get; set; }
		public MobLevelModifier LevelModifier { get; set; }
	}
}
