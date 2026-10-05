using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValCraft
{
	/// <summary>
	/// How the two games share the player: the camera (first person / behind / in front, F5), Steve's body and pose,
	/// Minecraft's controls (Ctrl runs, Shift sneaks, E opens Minecraft's inventory), the two hotbars and the health.
	/// </summary>
	public partial class Plugin
	{
		/// <summary>0: first person, 1: behind (Valheim's own camera), 2: in front, looking back.</summary>
		private int camMode;
		private ConfigEntry<KeyboardShortcut> keyCamera, keyGameMode, keyMode;
		private ConfigEntry<bool> autoStart;
		private ConfigEntry<string> launchCommand, launchArgs;
		private static readonly System.Reflection.FieldInfo playerPosField = AccessTools.Field(typeof(GameCamera), "m_playerPos");
		private static readonly System.Reflection.MethodInfo setCrouch = AccessTools.Method(typeof(Player), "SetCrouch");
		private float eyeHeight = float.NaN;
		private bool crouchSet;
		private ConfigEntry<int> poseLag;
		private ConfigEntry<int> maxPixels;
		internal static ConfigEntry<bool> hideGrass;
		private bool grassHidden;
		private float bodyYaw = float.NaN;
		/// <summary>Minecraft's game mode ("creative", "survival", ...), from its "mcstate".</summary>
		internal static string gameMode = "creative";
		private bool mcDead;
		private bool godSet;
		/// <summary>A Minecraft screen (inventory, chat) has the mouse: Minecraft's window is in front.</summary>
		internal static bool mcScreen;
		private string viewSentKey;

		[DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
		[DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
		[DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h, ref POINT p);
		[StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }
		[StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
		private IntPtr hwnd;

		private delegate bool EnumProc(IntPtr h, IntPtr l);
		[DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc f, IntPtr l);
		[DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
		[DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
		[DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
		[DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr h);

		[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
		[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
		private float nextWindowCheck;

		/// <summary>Minecraft's window never shows in Alt-Tab or the taskbar (a tool window): it only exists for its screens.</summary>
		private void HideMinecraftWindow()
		{
			if (Time.unscaledTime < nextWindowCheck)
				return;
			nextWindowCheck = Time.unscaledTime + 2f;
			IntPtr mc = FindMinecraft();
			if (mc == IntPtr.Zero)
				return;
			const int GWL_EXSTYLE = -20;
			const long WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000;
			long style = GetWindowLongPtr(mc, GWL_EXSTYLE).ToInt64();
			long want = (style | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
			if (want != style)
				SetWindowLongPtr(mc, GWL_EXSTYLE, new IntPtr(want));
		}

		private static IntPtr FindMinecraft()
		{
			IntPtr found = IntPtr.Zero;
			var sb = new System.Text.StringBuilder(256);
			EnumWindows((h, l) =>
			{
				sb.Clear();
				GetWindowText(h, sb, sb.Capacity);
				if (sb.ToString().StartsWith("Minecraft"))
				{
					found = h;
					return false;
				}
				return true;
			}, IntPtr.Zero);
			return found;
		}

		/// <summary>A Minecraft screen opened: hand it the focus (Valheim is in front, so Windows lets it give the focus away).</summary>
		internal static void FocusMinecraft()
		{
			IntPtr found = IntPtr.Zero;
			var sb = new System.Text.StringBuilder(256);
			EnumWindows((h, l) =>
			{
				if (!IsWindowVisible(h))
					return true;
				sb.Clear();
				GetWindowText(h, sb, sb.Capacity);
				if (sb.ToString().StartsWith("Minecraft"))
				{
					found = h;
					return false;
				}
				return true;
			}, IntPtr.Zero);
			if (found == IntPtr.Zero)
				return;
			BringWindowToTop(found);
			SetForegroundWindow(found);
		}

		private void BindControls()
		{
			keyCamera = Config.Bind("Keys", "Camera", new KeyboardShortcut(KeyCode.F5), "First person / behind / in front");
			keyGameMode = Config.Bind("Keys", "GameMode", new KeyboardShortcut(KeyCode.F9), "Minecraft creative <-> survival");
			keyMode = Config.Bind("Keys", "Mode", new KeyboardShortcut(KeyCode.R), "Minecraft mode (mouse, 1-9, wheel = Minecraft) <-> Valheim mode (Valheim's weapons and hotbar)");
			autoStart = Config.Bind("Minecraft", "AutoStart", true, "Start Minecraft (hidden) with Valheim when it isn't running");
			launchCommand = Config.Bind("Minecraft", "Launcher", @"D:\Dev\ValCraft\prism\prismlauncher.exe", "Launcher that starts the Minecraft half");
			launchArgs = Config.Bind("Minecraft", "LauncherArgs", "--launch ValCraft", "Its arguments");
			hideGrass = Config.Bind("Render", "HideValheimGrass", false, "No Valheim grass while Minecraft runs (it hides Minecraft's blocks standing in it)");
			maxPixels = Config.Bind("Render", "MaxMinecraftPixels", 1600 * 900, "Most pixels Minecraft renders (its picture is scaled up to Valheim's); lower is smoother");
			poseLag = Config.Bind("Render", "PoseLag", 1, "Frames between Valheim's camera update and its picture (Unity's render thread runs a frame behind)");
		}

		/// <summary>The camera mode in use: Valheim's weapons are drawn on Valheim's own body, which has no first person.</summary>
		private int CamMode => mcHands ? camMode : (camMode == 0 ? 1 : camMode);

		/// <summary>
		/// Where the player is drawn: Valheim's camera follows a smoothed copy of the body (which moves in 50 Hz physics
		/// steps); Steve and the first-person eyes use the same one, or he would shake against the camera.
		/// </summary>
		internal Vector3 SmoothPos(Player player)
		{
			if (GameCamera.instance != null && playerPosField != null)
			{
				var v = (Vector3)playerPosField.GetValue(GameCamera.instance);
				if ((v - player.transform.position).sqrMagnitude < 25f)
					return v;
			}
			return player.transform.position;
		}

		private bool spin;
		private bool gmsOpen;
		private float spinSpeed = 90f;

		/// <summary>Test oracle: while BepInEx/config/valcraft.spin exists the view turns by itself (camera-sync checks).</summary>
		private void DebugSpin(Player player)
		{
			if (Time.frameCount % 60 == 0)
			{
				string f = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "valcraft.spin");
				spin = System.IO.File.Exists(f);
				if (spin)
				{
					// "<camera mode> <pose lag> <degrees per second>"
					var a = System.IO.File.ReadAllText(f).Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
					if (a.Length > 0 && int.TryParse(a[0], out int cm)) camMode = cm;
					if (a.Length > 1 && int.TryParse(a[1], out int lag)) poseLag.Value = lag;
					if (a.Length > 2 && float.TryParse(a[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float sp)) spinSpeed = sp;
				}
			}
			if (!spin)
				return;
			Vector3 d = player.GetLookDir();
			player.SetLookDir(Quaternion.Euler(0f, spinSpeed * Time.deltaTime, 0f) * d);
		}

		private void ControlsUpdate(Player player)
		{
			DebugSpin(player);
			// Valheim's grass off while Minecraft runs: rebuild the patches when that changes
			if (hideGrass.Value != grassHidden && ClutterSystem.instance != null)
			{
				grassHidden = hideGrass.Value;
				ClutterSystem.instance.ResetGrass(player.transform.position, 1000f);
			}
			if (Key(keyMode))
				SetHands(!mcHands);
			// Minecraft's sneak: crouched while Shift is held (Valheim toggles on each press)
			bool sneak = Sneaking() && !InGui() && !mcScreen && !Creative.flying;
			if (sneak != crouchSet || player.IsCrouching() != sneak)
			{
				crouchSet = sneak;
				setCrouch?.Invoke(player, new object[] { sneak });
			}
			if (Key(keyCamera))
				camMode = (camMode + 1) % 3;
			if (Key(keyGameMode))
				Send("{\"t\":\"cmd\",\"c\":\"gamemode " + (gameMode == "creative" ? "survival" : "creative") + " @a\"}");
			// F3+F4, as in Minecraft: its game mode switcher over the picture; F4 again picks the next, letting go of F3 applies
			bypass = true;
			bool f3 = ZInput.GetKey(KeyCode.F3, false), f4 = ZInput.GetKeyDown(KeyCode.F4, false);
			bypass = false;
			if (f3 && f4)
			{
				Send(gmsOpen ? "{\"t\":\"gms\",\"op\":\"next\"}" : "{\"t\":\"gms\",\"op\":\"open\"}");
				gmsOpen = true;
			}
			else if (gmsOpen && !f3)
			{
				Send("{\"t\":\"gms\",\"op\":\"apply\"}");
				gmsOpen = false;
			}
			HideMinecraftWindow();
			Addon.SetPoseLag(poseLag.Value);

			// creative: no damage, endless stamina, double-jump flight (see Creative); never Valheim's god mode
			if (player.InGodMode() && godSet)
				player.SetGodMode(false);
			godSet = false;
			Creative.Tick(player);

			// Minecraft's window lies exactly over Valheim's picture (invisible), for its screens to take the mouse
			if (hwnd == IntPtr.Zero)
				hwnd = GetActiveWindow();
			if (hwnd != IntPtr.Zero && GetClientRect(hwnd, out var rc))
			{
				var at = new POINT();
				ClientToScreen(hwnd, ref at);
				int w = rc.R - rc.L, h = rc.B - rc.T;
				// the picture's own size (a fullscreen mode can differ from the window's): Minecraft must match its shape
				if (Screen.width > 0 && Screen.height > 0)
				{
					w = Screen.width;
					h = Screen.height;
				}
				// Minecraft renders at most ~MaxPixels (the effect scales its picture up): at 1440p and above a full-size
				// frame is ~45 MB a frame to read back and upload, and both games stutter. Its screens (inventory, chat)
				// need the window over the whole picture for the mouse, so it grows only while one is open.
				if ((long)w * h > maxPixels.Value)
				{
					double k = Math.Sqrt(maxPixels.Value / ((double)w * h));
					w = (int)(w * k + 0.5);
					h = (int)(h * k + 0.5);
				}
				string key = $"{at.X},{at.Y},{w},{h}";
				if (w > 0 && h > 0 && key != viewSentKey)
				{
					viewSentKey = key;
					Send($"{{\"t\":\"view\",\"w\":{w},\"h\":{h},\"x\":{at.X},\"y\":{at.Y},\"hwnd\":{hwnd.ToInt64()}}}");
				}
			}
		}

		internal void OnMcState(Dictionary<string, object> m)
		{
			gameMode = m.TryGetValue("gm", out var gm) ? gm as string ?? "creative" : "creative";
			bool dead = m.TryGetValue("dead", out var d) && d is bool b && b;
			var player = Player.m_localPlayer;
			if (dead && !mcDead && player != null && !player.IsDead() && gameMode == "survival")
			{
				// Minecraft's hearts ran out: the Viking falls too
				player.SetGodMode(false);
				godSet = false;
				var hit = new HitData();
				hit.m_damage.m_damage = 99999f;
				DamagePatch.passThrough = true;
				try { player.Damage(hit); }
				finally { DamagePatch.passThrough = false; }
			}
			mcDead = dead;
		}

		/// <summary>The camera this frame: first person at the eyes, Valheim's own behind, or in front looking back.</summary>
		private void PlaceCamera(Camera cam, Player player)
		{
			int mode = CamMode;
			Transform t = cam.transform;
			if (mode == 0)
			{
				// the eyes, steady: smoothed body + eye height (the head bone bobs with the animation)
				float h = player.m_eye.position.y - player.transform.position.y;
				eyeHeight = float.IsNaN(eyeHeight) ? h : Mathf.Lerp(eyeHeight, h, 1f - Mathf.Exp(-10f * Time.deltaTime));
				t.position = SmoothPos(player) + Vector3.up * eyeHeight;
				cam.nearClipPlane = 0.05f;
			}
			else if (mode == 2)
			{
				Vector3 eye = player.m_eye.position, fwd = t.forward;
				float dist = 3.5f;
				if (Physics.SphereCast(eye, 0.2f, fwd, out var hit, dist, groundMask, QueryTriggerInteraction.Ignore))
					dist = Mathf.Max(hit.distance - 0.1f, 0.5f);
				t.position = eye + fwd * dist;
				t.rotation = Quaternion.LookRotation(-fwd, Vector3.up);
				cam.nearClipPlane = 0.05f;
			}
		}

		/// <summary>
		/// Steve's body yaw, the way Minecraft turns a player's body: towards where he walks, and otherwise it follows the
		/// head once the head has turned more than 50 degrees away.
		/// </summary>
		private float BodyYaw(Player player, float headYaw)
		{
			Vector3 v = player.GetVelocity();
			v.y = 0f;
			if (float.IsNaN(bodyYaw))
				bodyYaw = headYaw;
			if (v.sqrMagnitude > 0.5f)
			{
				float move = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
				// walking backwards: the body keeps facing forwards, as in Minecraft
				if (Mathf.Abs(Mathf.DeltaAngle(headYaw, move)) > 100f)
					move += 180f;
				bodyYaw = Mathf.MoveTowardsAngle(bodyYaw, move, 720f * Time.deltaTime);
			}
			float diff = Mathf.DeltaAngle(bodyYaw, headYaw);
			if (Mathf.Abs(diff) > 50f)
				bodyYaw = headYaw - Mathf.Sign(diff) * 50f;
			return Wrap(bodyYaw);
		}

		// ---------------------------------------------------------------- input

		private void ControlsInput(Player player)
		{
			if (InGui() || mcScreen)
				return;
			bypass = true;
			try
			{
				// Minecraft mode: 1-9 and the wheel are Minecraft's hotbar; Valheim mode: Valheim's own (R switches)
				if (mcHands)
				{
					for (int i = 1; i <= 9; i++)
						if (ZInput.GetKeyDown(KeyCode.Alpha0 + i, false))
							Send($"{{\"t\":\"slot\",\"n\":{i - 1}}}");
					float wheel = ZInput.GetMouseScrollWheel();
					if (Mathf.Abs(wheel) > 0.01f)
						Send(wheel > 0f ? "{\"t\":\"scroll\",\"d\":1}" : "{\"t\":\"scroll\",\"d\":-1}");
				}

				// E: Valheim's interaction when looking at something of Valheim's, else Minecraft's inventory
				if (ZInput.GetButtonDown("Use") && player.GetHoverObject() == null)
				{
					Send("{\"t\":\"key\",\"k\":\"inventory\",\"down\":true}");
					Send("{\"t\":\"key\",\"k\":\"inventory\",\"down\":false}");
				}

				if (!mcHands)
					return;
				Button("Attack", "attack");
				Button("SecondaryAttack", "use");
				Button("Block", "use");
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

		private void SetHands(bool minecraft)
		{
			if (mcHands == minecraft)
				return;
			mcHands = minecraft;
			Message(minecraft ? "Minecraft hands" : "Valheim weapons");
		}

		/// <summary>The Valheim buttons Minecraft owns while it runs (the original is skipped, the result replaced).</summary>
		internal static bool Remap(string name, ref bool result, bool down)
		{
			if (bypass || I == null || !I.On || InGui() || mcScreen)
				return false;
			switch (name)
			{
				case "Run":
					bypass = true;
					try { result = down ? ZInput.GetKeyDown(KeyCode.LeftControl, false) : ZInput.GetKey(KeyCode.LeftControl, false); }
					finally { bypass = false; }
					return true;
				case "Crouch":
					// Minecraft: Shift held sneaks (set directly in ControlsUpdate); Valheim's toggle never fires
					result = false;
					return true;
				case "Use":
					// E with nothing of Valheim's to use opens Minecraft's inventory instead (ControlsInput)
					var p = Player.m_localPlayer;
					if (p != null && p.GetHoverObject() == null)
					{
						result = false;
						return true;
					}
					return false;
				case "Hotbar1": case "Hotbar2": case "Hotbar3": case "Hotbar4":
				case "Hotbar5": case "Hotbar6": case "Hotbar7": case "Hotbar8":
				case "Attack":
				case "SecondaryAttack":
				case "Block":
					if (mcHands)
					{
						result = false;
						return true;
					}
					return false;
			}
			return false;
		}

		internal static bool InGuiPublic() => InGui();

		internal static bool Sneaking()
		{
			bypass = true;
			try { return ZInput.GetKey(KeyCode.LeftShift, false); }
			finally { bypass = false; }
		}

		internal static bool Sprinting()
		{
			bypass = true;
			try { return ZInput.GetKey(KeyCode.LeftControl, false); }
			finally { bypass = false; }
		}
	}

	/// <summary>Survival: hits on the Viking go to Minecraft's hearts (the HUD shows them), not Valheim's health.</summary>
	[HarmonyPatch(typeof(Character), nameof(Character.Damage))]
	internal static class DamagePatch
	{
		internal static bool passThrough;

		private static bool Prefix(Character __instance, HitData hit)
		{
			if (passThrough || hit == null || __instance != Player.m_localPlayer || Plugin.I == null || !Plugin.I.On)
				return true;
			// creative and spectator: nothing hurts the Viking
			if (Plugin.gameMode == "creative" || Plugin.gameMode == "spectator")
				return false;
			if (Plugin.gameMode != "survival" && Plugin.gameMode != "adventure")
				return true;
			// Valheim's ~100 health is Minecraft's 20 half-hearts
			float amount = hit.GetTotalDamage() / 5f;
			if (amount > 0.01f)
				Plugin.I.SendRaw(string.Format(CultureInfo.InvariantCulture, "{{\"t\":\"cmd\",\"c\":\"damage @p {0:F2} minecraft:generic\"}}", amount));
			return false;
		}
	}

	/// <summary>No Valheim grass where Minecraft's blocks stand (it would poke through them).</summary>
	[HarmonyPatch(typeof(ClutterSystem), nameof(ClutterSystem.GetGroundInfo))]
	internal static class GrassPatch
	{
		private static bool Prefix(Vector3 p, ref bool __result)
		{
			if (Plugin.I == null || !Plugin.I.On || !(Plugin.hideGrass.Value || Plugin.I.HasBlockColumn(p)))
				return true;
			__result = false;
			return false;
		}
	}
}
