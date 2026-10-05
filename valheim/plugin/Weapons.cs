using UnityEngine;

namespace ValCraft
{
	/// <summary>
	/// What a Minecraft swing does in Valheim, by the item in Steve's hand: its kind picks Valheim's damage types (a sword
	/// slashes, an axe chops, a pickaxe mines and digs the ground like Valheim's pickaxe, a mace crushes), its material
	/// the tool tier (which rocks and trees it can break), and Minecraft's own attack damage how hard it hits.
	/// </summary>
	public partial class Plugin
	{
		/// <summary>Minecraft damage to Valheim damage (config Combat/DamageMultiplier: 12 makes a diamond sword's 7 Valheim's 84).</summary>
		private static float DamageScale => I != null && I.damageMultiplier != null ? I.damageMultiplier.Value : 12f;

		private static int Tier(string item)
		{
			if (item.StartsWith("netherite")) return 4;
			if (item.StartsWith("diamond")) return 3;
			if (item.StartsWith("iron") || item.StartsWith("copper")) return 2;
			if (item.StartsWith("stone")) return 1;
			return 0; // wooden, golden, hand
		}

		private static HitData WeaponHit(string item, float mcDamage, Vector3 point, Vector3 dir)
		{
			float v = Mathf.Max(mcDamage, 1f) * DamageScale;
			var hit = Hit(point, dir, 0f, 0f, 0f, 20f);
			hit.m_toolTier = (short)Tier(item);
			var d = hit.m_damage;
			if (item.EndsWith("_sword"))
			{
				d.m_slash = v;
				hit.m_skill = Skills.SkillType.Swords;
			}
			else if (item.EndsWith("_pickaxe"))
			{
				d.m_pierce = v * 0.5f;
				d.m_pickaxe = v * 1.6f;
				hit.m_skill = Skills.SkillType.Pickaxes;
			}
			else if (item.EndsWith("_axe"))
			{
				d.m_slash = v * 0.7f;
				d.m_chop = v * 1.4f;
				hit.m_skill = Skills.SkillType.Axes;
			}
			else if (item.EndsWith("_shovel"))
			{
				d.m_blunt = v;
				d.m_pickaxe = v * 0.6f;
				hit.m_skill = Skills.SkillType.Pickaxes;
			}
			else if (item.EndsWith("_hoe"))
			{
				d.m_slash = v * 0.6f;
				hit.m_skill = Skills.SkillType.Knives;
			}
			else if (item == "mace")
			{
				d.m_blunt = v * 1.2f;
				hit.m_pushForce = 60f;
				hit.m_skill = Skills.SkillType.Clubs;
			}
			else if (item == "trident" || item.EndsWith("_spear"))
			{
				d.m_pierce = v;
				hit.m_skill = Skills.SkillType.Spears;
			}
			else
			{
				// a fist or anything else: a punch
				d.m_blunt = v;
				hit.m_skill = Skills.SkillType.Unarmed;
			}
			hit.m_damage = d;
			return hit;
		}

		private static bool Digs(string item) => item.EndsWith("_pickaxe") || item.EndsWith("_shovel");

		private static GameObject digPrefab;

		/// <summary>Valheim's pickaxe dig (the terrain operation its pickaxes spawn where they hit the ground).</summary>
		private static GameObject DigPrefab()
		{
			if (digPrefab != null || ObjectDB.instance == null)
				return digPrefab;
			foreach (var name in new[] { "PickaxeIron", "PickaxeBronze", "PickaxeAntler", "PickaxeStone" })
			{
				var go = ObjectDB.instance.GetItemPrefab(name);
				var drop = go != null ? go.GetComponent<ItemDrop>() : null;
				if (drop != null && drop.m_itemData.m_shared.m_spawnOnHitTerrain != null)
				{
					digPrefab = drop.m_itemData.m_shared.m_spawnOnHitTerrain;
					Log("pickaxe dig: " + name + " -> " + digPrefab.name);
					break;
				}
			}
			return digPrefab;
		}

		/// <summary>A Minecraft swing: the creatures in front of the player, else what the crosshair is on.</summary>
		private void Melee(string item, float mcDamage)
		{
			var player = Player.m_localPlayer;
			var cam = GameCamera.instance != null ? GameCamera.instance.transform : player.transform;
			Vector3 origin = player.transform.position + Vector3.up;
			Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
			int hits = 0;
			foreach (var ch in Character.GetAllCharacters())
			{
				if (ch == null || ch == player || ch.IsDead())
					continue;
				Vector3 d = ch.GetCenterPoint() - origin;
				float reach = 3.2f + ch.GetRadius();
				if (d.magnitude > reach || Vector3.Dot(Vector3.ProjectOnPlane(d, Vector3.up).normalized, fwd) < 0.35f)
					continue;
				ch.Damage(WeaponHit(item, mcDamage, ch.GetCenterPoint(), d));
				hits++;
			}
			if (hits > 0)
				return;

			// the scenery under the crosshair, within Minecraft's reach of the player (4.5 blocks)
			float reachFromCam = Vector3.Distance(cam.position, player.m_eye.position) + 4.5f;
			if (!Physics.Raycast(cam.position, cam.forward, out var rh, reachFromCam, groundMask, QueryTriggerInteraction.Ignore))
				return;
			if (blockRoot != null && rh.collider.transform.IsChildOf(blockRoot.transform))
				return; // a Minecraft block: Minecraft breaks it itself
			var dest = rh.collider.GetComponentInParent<IDestructible>();
			if (dest != null)
			{
				dest.Damage(WeaponHit(item, mcDamage, rh.point, cam.forward));
				return;
			}
			// the ground: a pickaxe or shovel digs it, as Valheim's pickaxe does
			if (Digs(item) && rh.collider.GetComponent<Heightmap>() != null && DigPrefab() != null)
				Attack.SpawnOnHitTerrain(rh.point, DigPrefab(), player, 0f, null, null);
		}
	}
}
