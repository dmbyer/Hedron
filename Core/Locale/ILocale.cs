using Hedron.Core.Entities.Properties;
using System;
using System.Collections.Generic;
using System.Text;

namespace Hedron.Core.Locale
{
	public interface ILocale
	{
		public bool AllowRandomlyGeneratedEntities { get; set; }
		public MobLevelModifier LevelModifier { get; set; }
	}
}
