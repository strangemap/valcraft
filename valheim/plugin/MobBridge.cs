using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace ValCraft
{
	/// <summary>
	/// Minecraft's mobs vs Valheim's creatures (the mod's MobWar): Valheim's creatures near the player are sent as
	/// "peds" (Minecraft gives each an invisible proxy its hostile mobs hunt); a mob's hit on a proxy ("mobhit") hurts
	/// the creature. Valheim's monsters hit back at Minecraft mobs they stand next to ("mobdmg").
	/// </summary>
	internal sealed class MobBridge
	{
		private const float Range = 60f;
		private const float SendEvery = 0.2f;
		private const float BiteRange = 2.2f;
		private const float BiteEvery = 1.2f;

		private readonly Dictionary<int, Character> byHandle = new Dictionary<int, Character>();
		private readonly Dictionary<int, float> nextBite = new Dictionary<int, float>();
		private List<(int id, string kind, Vector3 at)> mobs = new List<(int, string, Vector3)>();
		private float nextSend;

		public void Tick(Plugin plugin, float yOffset)
		{
			var player = Player.m_localPlayer;
			if (player == null || Time.time < nextSend)
				return;
			nextSend = Time.time + SendEvery;
			byHandle.Clear();
			var sb = new StringBuilder("{\"t\":\"peds\",\"p\":[");
			int n = 0;
			Vector3 me = player.transform.position;
			foreach (var ch in Character.GetAllCharacters())
			{
				if (ch == null || ch == player || ch.IsDead() || ch.IsPlayer() || ch.IsTamed())
					continue;
				Vector3 p = ch.transform.position;
				if ((p - me).sqrMagnitude > Range * Range)
					continue;
				int h = ch.GetInstanceID();
				byHandle[h] = ch;
				sb.Append(n++ == 0 ? "" : ",").AppendFormat(CultureInfo.InvariantCulture, "[{0},{1:F3},{2:F3},{3:F3}]", h, -p.x, p.y + yOffset, p.z);
			}
			plugin.SendRaw(sb.Append("]}").ToString());

			// Valheim's monsters bite the Minecraft mobs they stand next to
			foreach (var m in mobs)
			{
				if (nextBite.TryGetValue(m.id, out var t) && Time.time < t)
					continue;
				foreach (var ch in byHandle.Values)
				{
					if (!ch.m_faction.Equals(Character.Faction.Players) && Vector3.Distance(ch.transform.position, m.at) < BiteRange + ch.GetRadius())
					{
						nextBite[m.id] = Time.time + BiteEvery;
						plugin.SendRaw($"{{\"t\":\"mobdmg\",\"id\":{m.id},\"d\":4}}");
						// face it, so it looks like a fight
						ch.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(m.at - ch.transform.position, Vector3.up));
						break;
					}
				}
			}
		}

		public void OnMobs(List<object> list, System.Func<double, double, double, Vector3> toUnity)
		{
			var next = new List<(int, string, Vector3)>();
			foreach (var o in list)
			{
				var e = Json.L(o);
				next.Add(((int)Json.D(e[0]), e[1] as string, toUnity(Json.D(e[2]), Json.D(e[3]), Json.D(e[4]))));
			}
			mobs = next;
		}

		/// <summary>A Minecraft mob hit a creature's proxy: Valheim damage, scaled (Minecraft's hearts are small).</summary>
		public void OnHit(int handle, float amount, Vector3 from)
		{
			if (!byHandle.TryGetValue(handle, out var ch) || ch == null || ch.IsDead())
				return;
			var hit = new HitData();
			hit.m_damage.m_blunt = amount * 4f;
			hit.m_point = ch.GetCenterPoint();
			hit.m_dir = (ch.GetCenterPoint() - from).normalized;
			hit.m_pushForce = 20f;
			ch.Damage(hit);
		}
	}
}
