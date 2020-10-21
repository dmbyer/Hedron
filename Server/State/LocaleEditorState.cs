using Hedron.Core.Entities.Living;
using Hedron.Core.Locale;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Server
{
	public class LocaleEditorState
	{
		public World CurrentWorld;
		public Area CurrentArea;
		public Room CurrentRoom;

		public uint? SelectedWorld;
		public uint? SelectedArea;
		public uint? SelectedRoom;
	}
}