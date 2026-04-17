using Core.ECS.Entities.Living;
using Core.Modules.Locale;
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
		public Mob CurrentMob;

		public uint? SelectedWorld;
		public uint? SelectedArea;
		public uint? SelectedRoom;
		public uint? SelectedMob;
	}
}