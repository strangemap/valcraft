using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValCraft
{
	/// <summary>
	/// Valheim half of the Minecraft passthrough. Every frame it sends Valheim's camera and the player's feet to
	/// Minecraft (which draws Steve and its blocks from that camera, see mc/), hands the same pose to the ReShade
	/// add-on that composites Minecraft's picture against Valheim's depth, sends Valheim's ground around the player as
	/// barrier columns, turns Minecraft's blocks into Valheim colliders and acts out Minecraft's hits in Valheim.
	///
	/// Coordinates: 1 m = 1 block. Unity is left-handed, Minecraft right-handed, both Y up:
	/// Minecraft (x, y, z) = (-unity x, unity y + yOffset, unity z); Minecraft yaw = Unity yaw, pitch = pitch, roll = -roll.
	/// </summary>
	[BepInPlugin("valcraft.passthrough", "ValCraft", "0.1.0")]
	public class Plugin : BaseUnityPlugin
	{
		private const int GroundRadius = 28;
		private const int GroundDepth = 3;
		private const int ProbeBudget = 220;
		private const double MaxMinecraftPixels = 1920.0 * 1080.0;

		internal static Plugin I;
		private static readonly Dictionary<string, bool> blocked = new Dictionary<string, bool>();

		private Link link;
		private ConfigEntry<KeyboardShortcut> keyToggle, keyRelevel, keyHands;
		private ConfigEntry<float> meleeDamage, arrowDamage, explosionDamage;
		private bool passOn = true;
		/// <summary>Mouse buttons, wheel and number keys go to Minecraft (its hands) instead of Valheim's weapons.</summary>
		internal static bool mcHands = true;
		private int generation = -1;
		private bool haveOffset;
		private float yOffset;
		private bool relevel;
		private readonly HashSet<long> sampled = new HashSet<long>();
		private int viewSent;
		private float nextHide;
		private bool playerHidden;
		private bool hudHiddenSent;
		private bool lastHands = true;
		private readonly Dictionary<long, GameObject> blocks = new Dictionary<long, GameObject>();
		private readonly Dictionary<int, Vector3> projectiles = new Dictionary<int, Vector3>();
		private readonly HashSet<int> projectilesSeen = new HashSet<int>();
		private GameObject blockRoot;
		private int blockLayer;
		private int groundMask, hitMask;
		internal static bool bypass;

		internal static void Log(string m) => I?.Logger.LogInfo(m);

		private void Awake()
		{
			I = this;
			keyToggle = Config.Bind("Keys", "Toggle", new KeyboardShortcut(KeyCode.F7), "Passthrough on/off");
			keyRelevel = Config.Bind("Keys", "Relevel", new KeyboardShortcut(KeyCode.F8), "Re-level Minecraft's ground to where you stand");
			keyHands = Config.Bind("Keys", "Hands", new KeyboardShortcut(KeyCode.F6), "Mouse/hotbar: Minecraft's hands or Valheim's weapons");
			meleeDamage = Config.Bind("Combat", "MeleeDamage", 35f, "Valheim damage of a Minecraft sword swing");
			arrowDamage = Config.Bind("Combat", "ArrowDamage", 30f, "Valheim damage of a Minecraft arrow");
			explosionDamage = Config.Bind("Combat", "ExplosionDamage", 120f, "Valheim damage at the centre of a Minecraft explosion (TNT, creepers)");
			foreach (var b in new[] { "Attack", "SecondaryAttack", "Block", "Hotbar1", "Hotbar2", "Hotbar3", "Hotbar4", "Hotbar5", "Hotbar6", "Hotbar7", "Hotbar8" })
				blocked[b] = true;
			link = new Link("ws://127.0.0.1:" + Config.Bind("Link", "Port", 25599, "Minecraft passthrough port").Value + "/");
			link.Start();
			new Harmony("valcraft.passthrough").PatchAll();
			Log("ValCraft loaded");
		}

		private void OnDestroy()
		{
			link?.Stop();
			Addon.SetActive(false);
		}

		/// <summary>Whether the passthrough runs right now (connected, in a world, on).</summary>
		private bool On => passOn && link.Connected && Player.m_localPlayer != null;

		/// <summary>Valheim's own menus and text fields own the mouse and keys: nothing goes to Minecraft then.</summary>
		private static bool InGui()
		{
			return Menu.IsVisible() || InventoryGui.IsVisible() || Minimap.IsOpen() || TextInput.IsVisible() || Console.IsVisible()
				|| (Chat.instance != null && Chat.instance.HasFocus()) || StoreGui.IsVisible() || Hud.IsPieceSelectionVisible();
		}

		/// <summary>While Minecraft has the hands, Valheim doesn't see its attack/block/hotbar buttons.</summary>
		internal static bool Swallow(string name)
		{
			return !bypass && mcHands && I != null && I.On && blocked.ContainsKey(name) && !InGui();
		}

		private static bool Key(ConfigEntry<KeyboardShortcut> k)
		{
			bypass = true;
			try { return ZInput.GetKeyDown(k.Value.MainKey, false); }
			finally { bypass = false; }
		}

		private void Update()
		{
			Addon.TryLoad(Time.unscaledTime);
			if (Key(keyToggle))
			{
				passOn = !passOn;
				Message(passOn ? "Minecraft passthrough ON" : "Minecraft passthrough OFF");
			}
			if (Key(keyRelevel))
				relevel = true;
			if (Key(keyHands))
			{
				mcHands = !mcHands;
				Message(mcHands ? "Minecraft hands" : "Valheim weapons");
			}

			if (!On)
			{
				Addon.SetActive(false);
				if (playerHidden)
					HidePlayer(false);
				while (link.Poll(out _))
				{
				}
				return;
			}

			var player = Player.m_localPlayer;
			if (link.Generation != generation)
			{
				generation = link.Generation;
				haveOffset = false;
				viewSent = 0;
				hudHiddenSent = !mcHands;
				lastHands = !mcHands;
				Message("Minecraft connected");
			}

			// Minecraft's window = Valheim's picture, at up to ~1080p worth of pixels (the effect scales it up)
			Addon.BackbufferSize(out int bw, out int bh);
			if (bw <= 0)
			{
				bw = Screen.width;
				bh = Screen.height;
			}
			if (bw > 0 && bh > 0 && bw * 65536 + bh != viewSent)
			{
				viewSent = bw * 65536 + bh;
				double scale = Math.Min(1.0, Math.Sqrt(MaxMinecraftPixels / ((double)bw * bh)));
				Send($"{{\"t\":\"view\",\"w\":{(int)(bw * scale + 0.5)},\"h\":{(int)(bh * scale + 0.5)}}}");
			}

			if (!haveOffset || relevel)
			{
				relevel = false;
				if (Ground(player.transform.position.x, player.transform.position.z, player.transform.position.y, out float g))
				{
					yOffset = Mathf.Round(g) - g;
					haveOffset = true;
					sampled.Clear();
					ClearBlocks();
					Send("{\"t\":\"clear\"}");
					Send("{\"t\":\"blocksync\",\"r\":64}");
				}
			}

			if (Time.unscaledTime >= nextHide)
			{
				nextHide = Time.unscaledTime + 0.5f;
				HidePlayer(true);
			}

			Input(player);
			if (haveOffset)
			{
				SampleGround(player.transform.position);
				mobBridge.Tick(this, yOffset);
			}

			while (link.Poll(out var m))
			{
				try
				{
					Handle(m);
				}
				catch (Exception e)
				{
					Logger.LogWarning("bad message " + (m.Length > 120 ? m.Substring(0, 120) : m) + ": " + e.Message);
				}
			}
		}

		/// <summary>Right after Valheim's camera moved for this frame: the pose this frame renders with.</summary>
		internal void AfterCamera(Camera cam)
		{
			bool on = On && haveOffset && cam != null;
			Addon.SetActive(on);
			if (!on)
				return;
			var player = Player.m_localPlayer;
			Transform t = cam.transform;
			Vector3 e = t.eulerAngles;
			float yaw = Wrap(e.y), pitch = Wrap(e.x), roll = -Wrap(e.z);
			Vector3 c = t.position;
			double mx = -c.x, my = c.y + yOffset, mz = c.z;
			Addon.SetPlanes(cam.nearClipPlane, cam.farClipPlane);
			if (Time.frameCount % 600 == 0)
				Log($"camera near {cam.nearClipPlane} far {cam.farClipPlane} fov {cam.fieldOfView} hdr {cam.allowHDR} target {(cam.targetTexture != null ? cam.targetTexture.name : "screen")} mode {cam.actualRenderingPath}");
			Addon.SetPose(yaw, pitch, roll, cam.fieldOfView, mx, my, mz);
			Vector3 p = player.transform.position;
			float body = Wrap(player.transform.eulerAngles.y);
			Send(string.Format(CultureInfo.InvariantCulture,
				"{{\"t\":\"cam\",\"f\":{0},\"p\":[{1:F4},{2:F4},{3:F4}],\"r\":[{4:F3},{5:F3},{6:F3}],\"fov\":{7:F3},\"fp\":false,\"pl\":[{8:F4},{9:F4},{10:F4}],\"h\":{11:F3} }}",
				Time.frameCount, mx, my, mz, yaw, pitch, roll, cam.fieldOfView, -p.x, p.y + yOffset, p.z, body));
		}

		private static float Wrap(float a)
		{
			a %= 360f;
			if (a > 180f) a -= 360f;
			if (a < -180f) a += 360f;
			return a;
		}

		private void Send(string m) => link.Send(m);
		internal void SendRaw(string m) => link.Send(m);
		private readonly MobBridge mobBridge = new MobBridge();

		private static void Message(string text)
		{
			if (MessageHud.instance != null)
				MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, text);
		}

		// ---------------------------------------------------------------- input

		private void Input(Player player)
		{
			if (mcHands != lastHands)
			{
				lastHands = mcHands;
				// Minecraft's hotbar shows only while its hands are in use
				Send(mcHands ? "{\"t\":\"hud\",\"hidden\":false}" : "{\"t\":\"hud\",\"hidden\":true}");
			}
			if (!mcHands || InGui())
				return;
			bypass = true;
			try
			{
				Button("Attack", "attack");
				Button("SecondaryAttack", "use");
				Button("Block", "use");
				float wheel = ZInput.GetMouseScrollWheel();
				if (wheel > 0.01f) Send("{\"t\":\"scroll\",\"d\":1}");
				else if (wheel < -0.01f) Send("{\"t\":\"scroll\",\"d\":-1}");
				for (int i = 1; i <= 8; i++)
					if (ZInput.GetButtonDown("Hotbar" + i))
						Send($"{{\"t\":\"slot\",\"n\":{i - 1}}}");
				if (ZInput.GetKeyDown(KeyCode.Alpha9, false))
					Send("{\"t\":\"slot\",\"n\":8}");
				if (ZInput.GetKeyDown(KeyCode.Q, false))
					Send("{\"t\":\"key\",\"k\":\"drop\",\"down\":true}");
				if (ZInput.GetKeyUp(KeyCode.Q, false))
					Send("{\"t\":\"key\",\"k\":\"drop\",\"down\":false}");
			}
			finally
			{
				bypass = false;
			}
		}

		private void Button(string valheim, string mc)
		{
			if (ZInput.GetButtonDown(valheim))
				Send($"{{\"t\":\"key\",\"k\":\"{mc}\",\"down\":true}}");
			if (ZInput.GetButtonUp(valheim))
				Send($"{{\"t\":\"key\",\"k\":\"{mc}\",\"down\":false}}");
		}

		// ---------------------------------------------------------------- the player model

		/// <summary>Minecraft draws Steve where Valheim's player stands: Valheim's own model is hidden (it still moves and collides).</summary>
		private void HidePlayer(bool hide)
		{
			var player = Player.m_localPlayer;
			if (player == null)
			{
				playerHidden = false;
				return;
			}
			foreach (var r in player.GetComponentsInChildren<Renderer>(true))
				if (r.enabled == hide)
					r.enabled = !hide;
			playerHidden = hide;
		}

		// ---------------------------------------------------------------- ground

		private void EnsureMasks()
		{
			if (groundMask != 0)
				return;
			groundMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
			hitMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle", "character", "character_net", "character_noenv", "hitbox");
			blockLayer = LayerMask.NameToLayer("static_solid");
		}

		/// <summary>The walkable surface at (x, z): the highest hit that isn't a roof or tree top above the player.</summary>
		private bool Ground(float x, float z, float playerY, out float y)
		{
			EnsureMasks();
			y = 0f;
			var hits = Physics.RaycastAll(new Vector3(x, playerY + 40f, z), Vector3.down, 120f, groundMask, QueryTriggerInteraction.Ignore);
			var p = Player.m_localPlayer.transform.position;
			float limit = playerY + 2.5f + new Vector2(x - p.x, z - p.z).magnitude;
			bool found = false;
			foreach (var h in hits)
			{
				if (blockRoot != null && h.collider.transform.parent == blockRoot.transform)
					continue; // Minecraft's own blocks aren't Valheim's ground
				if (h.point.y > limit)
					continue;
				if (!found || h.point.y > y)
				{
					y = h.point.y;
					found = true;
				}
			}
			return found;
		}

		private static long Column(int x, int z) => ((long)x << 32) ^ (uint)z;

		/// <summary>Valheim's ground around the player as barrier columns in Minecraft, nearest first, a budget per frame.</summary>
		private void SampleGround(Vector3 player)
		{
			int cx = Mathf.FloorToInt(-player.x), cz = Mathf.FloorToInt(player.z);
			int budget = ProbeBudget;
			var sb = new System.Text.StringBuilder();
			for (int r = 0; r <= GroundRadius && budget > 0; r++)
				for (int dx = -r; dx <= r && budget > 0; dx++)
					for (int dz = -r; dz <= r && budget > 0; dz++)
					{
						if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r)
							continue;
						int x = cx + dx, z = cz + dz;
						long key = Column(x, z);
						if (sampled.Contains(key))
							continue;
						budget--;
						sampled.Add(key);
						if (!Ground(-(x + 0.5f), z + 0.5f, player.y, out float g))
							continue;
						int top = Mathf.FloorToInt(g + yOffset + 0.5f) - 1;
						if (sb.Length > 0) sb.Append(',');
						sb.Append(x).Append(',').Append(z).Append(',').Append(top - GroundDepth + 1).Append(',').Append(top);
					}
			if (sb.Length > 0)
				Send("{\"t\":\"ground\",\"c\":[" + sb + "]}");
			// far from where it was sampled: forget columns behind (they'll be sampled again if we come back)
			if (sampled.Count > 20000)
				sampled.Clear();
		}

		// ---------------------------------------------------------------- Minecraft -> Valheim

		private void Handle(string message)
		{
			var m = Json.Parse(message) as Dictionary<string, object>;
			if (m == null || !m.TryGetValue("t", out var t))
				return;
			switch (t as string)
			{
				case "blocks":
					Blocks(Json.L(m.TryGetValue("set", out var s) ? s : null), true);
					Blocks(Json.L(m.TryGetValue("clear", out var c) ? c : null), false);
					break;
				case "mobs":
					mobBridge.OnMobs(Json.L(m["m"]), ToUnity);
					break;
				case "mobhit":
				{
					var f = Json.L(m["from"]);
					mobBridge.OnHit((int)Json.D(m["h"]), (float)Json.D(m["d"]), ToUnity(Json.D(f[0]), Json.D(f[1]), Json.D(f[2])));
					break;
				}
				case "melee":
					Melee();
					break;
				case "proj":
					Projectiles(Json.L(m["p"]));
					break;
				case "explosion":
				{
					var p = Json.L(m["pos"]);
					Explosion(ToUnity(Json.D(p[0]), Json.D(p[1]), Json.D(p[2])), (float)Json.D(m["r"]));
					break;
				}
			}
		}

		private Vector3 ToUnity(double x, double y, double z) => new Vector3((float)-x, (float)(y - yOffset), (float)z);

		private string ToMc(Vector3 u) => string.Format(CultureInfo.InvariantCulture, "[{0:F3},{1:F3},{2:F3}]", -u.x, u.y + yOffset, u.z);

		/// <summary>Minecraft's solid blocks become invisible colliders: Valheim's player and creatures stand on and bump into them.</summary>
		private void Blocks(List<object> xyz, bool solid)
		{
			EnsureMasks();
			if (blockRoot == null)
			{
				blockRoot = new GameObject("ValCraft blocks");
				DontDestroyOnLoad(blockRoot);
			}
			for (int i = 0; i + 2 < xyz.Count; i += 3)
			{
				int x = (int)Json.D(xyz[i]), y = (int)Json.D(xyz[i + 1]), z = (int)Json.D(xyz[i + 2]);
				long key = ((long)(x & 0x3FFFFF) << 42) | ((long)(y & 0xFFFFF) << 22) | (uint)(z & 0x3FFFFF);
				if (blocks.TryGetValue(key, out var go))
				{
					if (!solid)
					{
						Destroy(go);
						blocks.Remove(key);
					}
					continue;
				}
				if (!solid || blocks.Count > 6000)
					continue;
				go = new GameObject("b");
				go.layer = blockLayer;
				go.transform.SetParent(blockRoot.transform, false);
				go.transform.position = new Vector3(-(x + 0.5f), y + 0.5f - yOffset, z + 0.5f);
				go.AddComponent<BoxCollider>().size = Vector3.one;
				blocks[key] = go;
			}
		}

		private void ClearBlocks()
		{
			foreach (var go in blocks.Values)
				if (go != null)
					Destroy(go);
			blocks.Clear();
		}

		private static HitData Hit(Vector3 point, Vector3 dir, float slash, float blunt, float pierce, float push)
		{
			var hit = new HitData();
			hit.m_damage.m_slash = slash;
			hit.m_damage.m_blunt = blunt;
			hit.m_damage.m_pierce = pierce;
			hit.m_point = point;
			hit.m_dir = dir.normalized;
			hit.m_pushForce = push;
			hit.m_staggerMultiplier = 1f;
			hit.m_skill = Skills.SkillType.Swords;
			if (Player.m_localPlayer != null)
				hit.SetAttacker(Player.m_localPlayer);
			return hit;
		}

		/// <summary>A Minecraft sword swing hits the creatures in front of the player.</summary>
		private void Melee()
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
				ch.Damage(Hit(ch.GetCenterPoint(), d, meleeDamage.Value, 0f, 0f, 40f));
				hits++;
			}
			if (hits == 0)
			{
				// a swing at the scenery: trees, rocks and buildings take it as a hit too
				if (Physics.Raycast(cam.position, cam.forward, out var rh, 6f, groundMask, QueryTriggerInteraction.Ignore))
				{
					var dest = rh.collider.GetComponentInParent<IDestructible>();
					if (dest != null)
					{
						var hit = Hit(rh.point, cam.forward, meleeDamage.Value, meleeDamage.Value, 0f, 0f);
						hit.m_toolTier = 2;
						hit.m_damage.m_chop = meleeDamage.Value;
						hit.m_damage.m_pickaxe = meleeDamage.Value;
						dest.Damage(hit);
					}
				}
			}
		}

		/// <summary>Steve's arrows and fireworks in flight: traced through Valheim's world between reports.</summary>
		private void Projectiles(List<object> list)
		{
			projectilesSeen.Clear();
			foreach (var o in list)
			{
				var e = Json.L(o);
				int id = (int)Json.D(e[0]);
				string kind = e[1] as string;
				Vector3 at = ToUnity(Json.D(e[2]), Json.D(e[3]), Json.D(e[4]));
				projectilesSeen.Add(id);
				if (projectiles.TryGetValue(id, out var from))
				{
					Vector3 d = at - from;
					if (d.sqrMagnitude > 1e-6f && Physics.Raycast(from, d.normalized, out var rh, d.magnitude + 0.3f, hitMask, QueryTriggerInteraction.Ignore)
						&& (blockRoot == null || rh.collider.transform.parent != blockRoot.transform))
					{
						var ch = rh.collider.GetComponentInParent<Character>();
						if (ch == Player.m_localPlayer)
							ch = null;
						if (kind == "firework")
						{
							Explosion(rh.point, 2.5f);
							Send($"{{\"t\":\"projhit\",\"id\":{id},\"pos\":{ToMc(rh.point)},\"stick\":false}}");
						}
						else if (ch != null)
						{
							var hit = Hit(rh.point, d, 0f, 0f, arrowDamage.Value, 15f);
							hit.m_ranged = true;
							hit.m_skill = Skills.SkillType.Bows;
							ch.Damage(hit);
							Send($"{{\"t\":\"projhit\",\"id\":{id},\"pos\":{ToMc(rh.point)},\"stick\":false}}");
						}
						else
						{
							var dest = rh.collider.GetComponentInParent<IDestructible>();
							if (dest != null)
							{
								var hit = Hit(rh.point, d, 0f, 0f, arrowDamage.Value, 0f);
								hit.m_ranged = true;
								dest.Damage(hit);
							}
							Send($"{{\"t\":\"projhit\",\"id\":{id},\"pos\":{ToMc(rh.point - d.normalized * 0.1f)},\"stick\":true}}");
						}
						projectiles.Remove(id);
						continue;
					}
				}
				projectiles[id] = at;
			}
			var gone = new List<int>();
			foreach (var id in projectiles.Keys)
				if (!projectilesSeen.Contains(id))
					gone.Add(id);
			foreach (var id in gone)
				projectiles.Remove(id);
		}

		/// <summary>TNT, creepers, fireworks: Valheim's creatures, trees and buildings around take the blast, and the ground is cratered.</summary>
		private void Explosion(Vector3 at, float radius)
		{
			float reach = Math.Max(radius * 1.8f, 2f);
			foreach (var ch in Character.GetAllCharacters())
			{
				if (ch == null || ch.IsDead())
					continue;
				float d = Vector3.Distance(ch.GetCenterPoint(), at);
				if (d > reach)
					continue;
				float k = 1f - d / reach;
				// the player is in on the joke, but not killed by it
				float dmg = explosionDamage.Value * k * (ch == Player.m_localPlayer ? 0.15f : 1f);
				var hit = Hit(ch.GetCenterPoint(), ch.GetCenterPoint() - at, 0f, dmg, 0f, 120f * k);
				if (ch == Player.m_localPlayer)
					hit.m_attacker = ZDOID.None;
				ch.Damage(hit);
			}
			foreach (var col in Physics.OverlapSphere(at, reach, groundMask, QueryTriggerInteraction.Ignore))
			{
				if (blockRoot != null && col.transform.parent == blockRoot.transform)
					continue;
				var dest = col.GetComponentInParent<IDestructible>();
				if (dest == null || dest is Character)
					continue;
				var hit = Hit(col.ClosestPoint(at), col.transform.position - at, 0f, explosionDamage.Value, 0f, 0f);
				hit.m_damage.m_chop = explosionDamage.Value;
				hit.m_damage.m_pickaxe = explosionDamage.Value;
				hit.m_toolTier = 4;
				dest.Damage(hit);
			}
			Crater.Dig(at, radius);
		}
	}

	[HarmonyPatch(typeof(GameCamera), "LateUpdate")]
	internal static class CameraPatch
	{
		private static Camera cam;
		private static Camera hooked;

		private static void Postfix(GameCamera __instance)
		{
			if (cam == null)
				cam = __instance.GetComponent<Camera>();
			if (cam != null && cam != hooked && Addon.RenderEvent != System.IntPtr.Zero)
			{
				// composite right after Valheim's post effects, before the UI is drawn over the picture
				hooked = cam;
				var cb = new UnityEngine.Rendering.CommandBuffer { name = "ValCraft composite" };
				cb.IssuePluginEvent(Addon.RenderEvent, 1);
				cam.AddCommandBuffer(UnityEngine.Rendering.CameraEvent.AfterImageEffects, cb);
				Plugin.Log("composite hooked to " + cam.name);
			}
			Plugin.I?.AfterCamera(cam);
		}
	}

	[HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButton))]
	internal static class ButtonPatch
	{
		private static bool Prefix(string name, ref bool __result)
		{
			if (!Plugin.Swallow(name))
				return true;
			__result = false;
			return false;
		}
	}

	[HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonDown))]
	internal static class ButtonDownPatch
	{
		private static bool Prefix(string name, ref bool __result)
		{
			if (!Plugin.Swallow(name))
				return true;
			__result = false;
			return false;
		}
	}
}
