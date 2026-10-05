using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ValCraft
{
	/// <summary>
	/// Each Valheim world's vertical offset to Minecraft's block grid (BepInEx/config/valcraft.offsets): picked once, so
	/// the blocks built in that world land on the same spot (and collide) every time it is played. F8 picks it anew.
	/// </summary>
	internal static class Offsets
	{
		private static string File => Path.Combine(BepInEx.Paths.ConfigPath, "valcraft.offsets");

		private static string World => ZNet.instance != null ? ZNet.instance.GetWorldName() : null;

		private static Dictionary<string, float> Load()
		{
			var d = new Dictionary<string, float>();
			if (!System.IO.File.Exists(File))
				return d;
			foreach (var line in System.IO.File.ReadAllLines(File))
			{
				int i = line.LastIndexOf('=');
				if (i > 0 && float.TryParse(line.Substring(i + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
					d[line.Substring(0, i)] = v;
			}
			return d;
		}

		public static bool TryGet(out float offset)
		{
			offset = 0f;
			return World != null && Load().TryGetValue(World, out offset);
		}

		public static void Save(float offset)
		{
			if (World == null)
				return;
			var d = Load();
			d[World] = offset;
			var lines = new List<string>();
			foreach (var kv in d)
				lines.Add(kv.Key + "=" + kv.Value.ToString("R", CultureInfo.InvariantCulture));
			System.IO.File.WriteAllLines(File, lines);
		}
	}
}
