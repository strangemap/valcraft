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
		/// <summary>Minecraft damage to Valheim damage (config Combat/DamageMultiplier: 6 makes a diamond sword's 7 Valheim's 42).</summary>
		private static float DamageScale => I != null && I.damageMultiplier != null ? I.damageMultiplier.Value : 6f;

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

		/// <summary>
		/// Minecraft's attack damage and attacks per second for an item (vanilla values: the client doesn't apply a held
		/// item's attribute modifiers, so they can't be read from Minecraft's player).
		/// </summary>
		private static (float damage, float speed) Stats(string item)
		{
			int t = Tier(item);
			bool gold = item.StartsWith("golden"), wood = item.StartsWith("wooden");
			if (item.EndsWith("_sword"))
				return (new[] { 4f, 5f, 6f, 7f, 8f }[t], 1.6f);
			if (item.EndsWith("_axe"))
				return (wood || gold ? 7f : new[] { 7f, 9f, 9f, 9f, 10f }[t], gold ? 1f : new[] { 0.8f, 0.8f, 0.9f, 1f, 1f }[t]);
			if (item.EndsWith("_pickaxe"))
				return (new[] { 2f, 3f, 4f, 5f, 6f }[t], 1.2f);
			if (item.EndsWith("_shovel"))
				return (new[] { 2.5f, 3.5f, 4.5f, 5.5f, 6.5f }[t], 1f);
			if (item.EndsWith("_hoe"))
				return (1f, gold || wood ? 1f : new[] { 1f, 2f, 3f, 4f, 4f }[t]);
			if (item.EndsWith("_spear"))
				return (new[] { 2f, 3f, 4f, 5f, 6f }[t] + 1f, 1.1f);
			if (item == "mace")
				return (6f, 0.6f);
			if (item == "trident")
				return (9f, 1.1f);
			return (1f, 4f); // a fist (or a block, a torch...)
		}

		private float lastSwing = -10f;
		private string lastItem;

		/// <summary>
		/// A Minecraft swing, judged the way Minecraft does: the attack cooldown (a spam click does a fifth), a critical
		/// hit when falling with a full swing (x1.5, crit sparks), and a sword's sweep on the ground (hits around the target).
		/// </summary>
		private void Swing(string item)
		{
			var player = Player.m_localPlayer;
			var (damage, speed) = Stats(item);
			float now = Time.time;
			// switching items starts the cooldown over, as in Minecraft
			float charge = item != lastItem ? 0.25f : Mathf.Clamp01((now - lastSwing) * speed);
			lastSwing = now;
			lastItem = item;
			bool full = charge > 0.9f;
			bool crit = full && !player.IsOnGround() && player.GetVelocity().y < -0.1f && !Creative.flying;
			bool sweep = full && !crit && item.EndsWith("_sword") && player.IsOnGround() && !Sprinting();
			float dealt = damage * (0.2f + 0.8f * charge * charge) * (crit ? 1.5f : 1f);
			Melee(item, dealt, full, crit, sweep);
		}

		private void Effect(string what, Vector3 at)
		{
			string pos = ToMc(at).Trim('[', ']').Replace(",", " ");
			Send("{\"t\":\"cmd\",\"c\":\"" + what.Replace("@", pos) + "\"}");
		}

		/// <summary>The creature in front of the player (one, as in Minecraft, plus the sweep), else what the crosshair is on.</summary>
		private void Melee(string item, float mcDamage, bool full, bool crit, bool sweep)
		{
			var player = Player.m_localPlayer;
			var cam = GameCamera.instance != null ? GameCamera.instance.transform : player.transform;
			Vector3 origin = player.transform.position + Vector3.up;
			Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
			Character target = null;
			float best = float.MaxValue;
			foreach (var ch in Character.GetAllCharacters())
			{
				if (ch == null || ch == player || ch.IsDead())
					continue;
				Vector3 d = ch.GetCenterPoint() - origin;
				float reach = 3.2f + ch.GetRadius();
				float facing = Vector3.Dot(Vector3.ProjectOnPlane(d, Vector3.up).normalized, fwd);
				if (d.magnitude > reach || facing < 0.35f)
					continue;
				float score = d.magnitude * (2f - facing);
				if (score < best)
				{
					best = score;
					target = ch;
				}
			}
			if (target != null)
			{
				Vector3 c = target.GetCenterPoint();
				target.Damage(WeaponHit(item, mcDamage, c, c - origin));
				if (crit)
				{
					Effect("particle minecraft:crit @ 0.4 0.5 0.4 0.5 24 force", c);
					Effect("playsound minecraft:entity.player.attack.crit player @a @ 1 1", c);
				}
				else
					Effect(full ? "playsound minecraft:entity.player.attack.strong player @a @ 1 1" : "playsound minecraft:entity.player.attack.weak player @a @ 1 1", c);
				if (sweep)
				{
					Effect("particle minecraft:sweep_attack @ 0 0 0 0 1 force", player.transform.position + Vector3.up * 1.1f + fwd * 1.2f);
					Effect("playsound minecraft:entity.player.attack.sweep player @a @ 1 1", c);
					foreach (var other in Character.GetAllCharacters())
					{
						if (other == null || other == player || other == target || other.IsDead())
							continue;
						if (Vector3.Distance(other.GetCenterPoint(), c) < 1.6f + other.GetRadius())
							other.Damage(WeaponHit(item, mcDamage * 0.35f, other.GetCenterPoint(), other.GetCenterPoint() - origin));
					}
				}
				return;
			}

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
