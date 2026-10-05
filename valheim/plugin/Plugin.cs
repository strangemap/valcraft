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
	public partial class Plugin : BaseUnityPlugin
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
			BindControls();
			link = new Link("ws://127.0.0.1:" + Config.Bind("Link", "Port", 25599, "Minecraft passthrough port").Value + "/");
			link.Start();
			StartMinecraft();
			new Harmony("valcraft.passthrough").PatchAll();
			Log("ValCraft loaded");
		}

		private void OnApplicationQuit()
		{
			// Minecraft was started for Valheim: it closes with it
			if (link.Connected && autoStart.Value)
			{
				link.Send("{\"t\":\"quit\"}");
				System.Threading.Thread.Sleep(300);
			}
		}

		/// <summary>Minecraft's half starts by itself (hidden) unless something already listens on the link's port.</summary>
		private void StartMinecraft()
		{
			if (!autoStart.Value)
				return;
			try
			{
				using (var c = new System.Net.Sockets.TcpClient())
				{
					if (c.ConnectAsync("127.0.0.1", 25599).Wait(500) && c.Connected)
						return;
				}
				if (!System.IO.File.Exists(launchCommand.Value))
				{
					Logger.LogWarning("Minecraft launcher not found: " + launchCommand.Value);
					return;
				}
				System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(launchCommand.Value, launchArgs.Value)
				{
					UseShellExecute = false,
					WindowStyle = System.Diagnostics.ProcessWindowStyle.Minimized,
					WorkingDirectory = System.IO.Path.GetDirectoryName(launchCommand.Value)
				});
				Log("starting Minecraft: " + launchCommand.Value + " " + launchArgs.Value);
			}
			catch (Exception e)
			{
				Logger.LogWarning("couldn't start Minecraft: " + e.Message);
			}
		}

		private void OnDestroy()
		{
			link?.Stop();
			Addon.SetActive(false);
		}

		/// <summary>Whether the passthrough runs right now (connected, in a world, on).</summary>
		internal bool On => passOn && link.Connected && Player.m_localPlayer != null;

		/// <summary>Valheim's own menus and text fields own the mouse and keys: nothing goes to Minecraft then.</summary>
		private static bool InGui()
		{
			return Menu.IsVisible() || InventoryGui.IsVisible() || Minimap.IsOpen() || TextInput.IsVisible() || Console.IsVisible()
				|| (Chat.instance != null && Chat.instance.HasFocus()) || StoreGui.IsVisible() || Hud.IsPieceSelectionVisible();
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

			ControlsUpdate(player);

			if (!haveOffset || relevel)
			{
				bool forced = relevel;
				if (!forced && Offsets.TryGet(out float saved))
				{
					// this world's offset from before: blocks built earlier stay where they were (and stay solid)
					yOffset = saved;
					haveOffset = true;
					sampled.Clear();
					ClearBlocks();
					Send("{\"t\":\"clear\"}");
					syncAt = new Vector3(float.NaN, 0f, 0f);
				}
				else if (Ground(player.transform.position.x, player.transform.position.z, player.transform.position.y, out float g))
				{
					yOffset = Mathf.Round(g) - g;
					Offsets.Save(yOffset);
					haveOffset = true;
					sampled.Clear();
					ClearBlocks();
					Send("{\"t\":\"clear\"}");
					syncAt = new Vector3(float.NaN, 0f, 0f);
				}
				relevel = false;
			}

			if (Time.unscaledTime >= nextHide)
			{
				nextHide = Time.unscaledTime + 0.25f;
				// Minecraft's hands: Steve is the player; Valheim's weapons: the Viking is (Steve hides)
				HidePlayer(mcHands);
			}

			ControlsInput(player);
			Input(player);
			if (haveOffset)
			{
				BlockSync(player);
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
			PlaceCamera(cam, player);
			int mode = CamMode;
			Transform t = cam.transform;
			Vector3 e = t.eulerAngles;
			float yaw = Wrap(e.y), pitch = Wrap(e.x), roll = -Wrap(e.z);
			Vector3 c = t.position;
			double mx = -c.x, my = c.y + yOffset, mz = c.z;
			Addon.SetPlanes(cam.nearClipPlane, cam.farClipPlane);
			occluders.Frame(cam, player.transform, blockRoot != null ? blockRoot.transform : null);
			OccluderDepth();

			// where Steve looks: the camera's way, or back at the camera when it is in front of him
			float headYaw = mode == 2 ? Wrap(yaw + 180f) : yaw, headPitch = mode == 2 ? -pitch : pitch;
			Vector3 p = SmoothPos(player);
			float body = BodyYaw(player, headYaw);
			// re-projection works in the player's frame (see FrameExporter): camera minus feet, in Minecraft coordinates
			Addon.SetPose(yaw, pitch, roll, cam.fieldOfView, mx - (-p.x), my - (p.y + yOffset), mz - p.z);
			link.SendCam(string.Format(CultureInfo.InvariantCulture,
				"{{\"t\":\"cam\",\"f\":{0},\"p\":[{1:F4},{2:F4},{3:F4}],\"r\":[{4:F3},{5:F3},{6:F3}],\"fov\":{7:F3},\"fp\":{8},\"pl\":[{9:F4},{10:F4},{11:F4}],\"h\":{12:F3},\"look\":[{13:F3},{14:F3}],\"sn\":{15},\"sp\":{16},\"hide\":{17} }}",
				Time.frameCount, mx, my, mz, yaw, pitch, roll, cam.fieldOfView, mode == 0 ? "true" : "false",
				-p.x, p.y + yOffset, p.z, body, headYaw, headPitch,
				Sneaking() ? "true" : "false", Sprinting() && player.GetVelocity().sqrMagnitude > 1f ? "true" : "false", mcHands ? "false" : "true"));
		}

		private static readonly int depthTexId = Shader.PropertyToID("_CameraDepthTexture");
		private Texture depthTex;
		private IntPtr depthPtr;

		private readonly Occluders occluders = new Occluders();
		private RenderTexture occluderTex;

		/// <summary>The depth Minecraft is tested against: Valheim's solid things only (see Occluders).</summary>
		private void OccluderDepth()
		{
			var t = occluders.Depth;
			if (t == null || t == occluderTex)
				return;
			occluderTex = t;
			depthPtr = t.GetNativeTexturePtr();
			Addon.SetUnityDepth(depthPtr);
			Log($"occluder depth {t.width}x{t.height}");
		}

		/// <summary>Unity's camera depth for the add-on to test Minecraft against (the whole opaque scene of this frame).</summary>
		private void UnityDepth()
		{
			var t = Shader.GetGlobalTexture(depthTexId);
			if (t == null)
				return;
			if (t != depthTex || Time.frameCount % 30 == 0)
			{
				depthTex = t;
				IntPtr p = t.GetNativeTexturePtr();
				if (p != depthPtr)
				{
					depthPtr = p;
					Addon.SetUnityDepth(p);
					Log($"Unity depth: {t.name} {t.width}x{t.height} {(t is RenderTexture rt ? rt.format.ToString() : t.GetType().Name)}");
				}
			}
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
			string type = t as string;
			if (type != "mobs" && type != "proj" && type != "blocks" && type != "mcstate")
				Log("from Minecraft: " + (message.Length > 160 ? message.Substring(0, 160) : message));
			switch (type)
			{
				case "blocks":
					Blocks(Json.L(m.TryGetValue("set", out var s) ? s : null), true);
					Blocks(Json.L(m.TryGetValue("clear", out var c) ? c : null), false);
					break;
				case "mcstate":
					OnMcState(m);
					break;
				case "screen":
					mcScreen = m.TryGetValue("open", out var so) && so is bool sb && sb;
					if (mcScreen)
						FocusMinecraft();
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
						ColumnChanged(x, z, -1, go.transform.position);
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
				ColumnChanged(x, z, 1, go.transform.position);
			}
		}

		private Vector3 syncAt = new Vector3(float.NaN, 0f, 0f);
		private float syncAfter;

		/// <summary>
		/// Minecraft's blocks around the player become colliders here: asked for once Minecraft's player stands where
		/// Valheim's does (a second after the link starts), and again every 24 m walked.
		/// </summary>
		private void BlockSync(Player player)
		{
			Vector3 p = player.transform.position;
			if (float.IsNaN(syncAt.x))
			{
				if (syncAfter == 0f)
					syncAfter = Time.time + 1.5f;
				if (Time.time < syncAfter)
					return;
			}
			else if ((p - syncAt).sqrMagnitude < 24f * 24f)
				return;
			syncAfter = 0f;
			syncAt = p;
			Send("{\"t\":\"blocksync\",\"r\":48}");
		}

		/// <summary>Columns (Minecraft x, z) with a Minecraft block in them, for the grass patch.</summary>
		private readonly Dictionary<long, int> blockColumns = new Dictionary<long, int>();

		internal bool HasBlockColumn(Vector3 unity)
		{
			if (blockColumns.Count == 0)
				return false;
			// the column and its neighbours: bushes and grass are wider than their root point
			int x = Mathf.FloorToInt(-unity.x), z = Mathf.FloorToInt(unity.z);
			for (int dx = -1; dx <= 1; dx++)
				for (int dz = -1; dz <= 1; dz++)
					if (blockColumns.ContainsKey(Column(x + dx, z + dz)))
						return true;
			return false;
		}

		private void ColumnChanged(int x, int z, int delta, Vector3 at)
		{
			long k = Column(x, z);
			blockColumns.TryGetValue(k, out int n);
			n += delta;
			if (n <= 0) blockColumns.Remove(k); else blockColumns[k] = n;
			if (ClutterSystem.instance != null && ((delta > 0 && n == 1) || n == 0))
				ClutterSystem.instance.ResetGrass(at, 2.5f);
		}

		private void ClearBlocks()
		{
			blockColumns.Clear();
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

	/// <summary>Diagnostics: what hurts the local player.</summary>
	[HarmonyPatch(typeof(Character), nameof(Character.Damage))]
	internal static class DamageLog
	{
		private static void Prefix(Character __instance, HitData hit)
		{
			if (__instance == Player.m_localPlayer && hit != null)
				Plugin.Log($"player hit: {hit.GetTotalDamage():F1} by {hit.m_attacker} type {hit.m_hitType}");
		}
	}

	[HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButton))]
	internal static class ButtonPatch
	{
		private static bool Prefix(string name, ref bool __result) => !Plugin.Remap(name, ref __result, false);
	}

	[HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonDown))]
	internal static class ButtonDownPatch
	{
		private static bool Prefix(string name, ref bool __result) => !Plugin.Remap(name, ref __result, true);
	}

	/// <summary>The wheel is Minecraft's hotbar while Minecraft runs (Valheim would zoom the camera).</summary>
	[HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
	internal static class WheelPatch
	{
		private static bool Prefix(ref float __result)
		{
			if (Plugin.bypass || Plugin.I == null || !Plugin.I.On || Plugin.mcScreen || Hud.IsPieceSelectionVisible() || InventoryGui.IsVisible())
				return true;
			__result = 0f;
			return false;
		}
	}
}
