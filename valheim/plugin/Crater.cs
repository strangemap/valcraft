using System.Linq;
using UnityEngine;

namespace ValCraft
{
	/// <summary>
	/// Minecraft explosions dig into Valheim's terrain with the game's own terrain operations (the ones a pickaxe uses),
	/// so the craters are saved and synced like any dig.
	/// </summary>
	internal static class Crater
	{
		private static GameObject dig;
		private static bool searched;

		private static GameObject Prefab()
		{
			if (searched || ZNetScene.instance == null)
				return dig;
			searched = true;
			// the strongest lowering op that isn't a level/flatten tool
			var best = ZNetScene.instance.m_prefabs
				.Select(p => (p, op: p != null ? p.GetComponent<TerrainOp>() : null))
				.Where(x => x.op != null && x.op.m_settings.m_raise && x.op.m_settings.m_raiseDelta < 0f && !x.op.m_settings.m_level)
				.OrderBy(x => x.op.m_settings.m_raiseDelta * x.op.m_settings.m_raiseRadius)
				.FirstOrDefault();
			dig = best.p;
			Plugin.Log(dig != null ? "crater op: " + dig.name : "no terrain dig op found");
			return dig;
		}

		public static void Dig(Vector3 at, float radius)
		{
			var prefab = Prefab();
			if (prefab == null)
				return;
			var op = prefab.GetComponent<TerrainOp>().m_settings;
			float r = Mathf.Max(op.m_raiseRadius, 0.5f);
			int rings = Mathf.Clamp(Mathf.CeilToInt(radius / r), 1, 3);
			for (int i = -rings; i <= rings; i++)
				for (int j = -rings; j <= rings; j++)
				{
					var p = at + new Vector3(i * r, 0f, j * r);
					if ((p - at).magnitude > radius + 0.1f)
						continue;
					// down to the ground there (blasts in the air leave the ground alone)
					if (!Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 6f, LayerMask.GetMask("terrain")))
						continue;
					Object.Instantiate(prefab, hit.point, Quaternion.identity);
				}
		}
	}
}
