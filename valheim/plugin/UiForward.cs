using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace ValCraft
{
	/// <summary>
	/// While a Minecraft screen is open (inventory, chat, crafting) Valheim keeps the focus (a fullscreen Valheim would
	/// drop out of fullscreen otherwise): it shows the cursor, stops the Viking, and forwards the mouse and keys to the
	/// screen (Minecraft's HostUi). Esc or E closes it, as in Minecraft.
	/// </summary>
	internal static class UiForward
	{
		// Unity key -> SDL scancode (what Minecraft's key events carry)
		private static readonly Dictionary<KeyCode, int> keys = new Dictionary<KeyCode, int>
		{
			{ KeyCode.Escape, 41 }, { KeyCode.Return, 40 }, { KeyCode.Backspace, 42 }, { KeyCode.Tab, 43 }, { KeyCode.Space, 44 },
			{ KeyCode.Delete, 76 }, { KeyCode.RightArrow, 79 }, { KeyCode.LeftArrow, 80 }, { KeyCode.DownArrow, 81 }, { KeyCode.UpArrow, 82 },
			{ KeyCode.LeftShift, 225 },
		};
		private static Vector2 last = new Vector2(-1f, -1f);

		static UiForward()
		{
			for (int i = 0; i < 26; i++)
				keys[KeyCode.A + i] = 4 + i;
			for (int i = 1; i <= 9; i++)
				keys[KeyCode.Alpha0 + i] = 29 + i;
			keys[KeyCode.Alpha0] = 39;
		}

		internal static void Tick(Plugin plugin)
		{
			if (!Plugin.mcScreen)
			{
				last = new Vector2(-1f, -1f);
				return;
			}
			Plugin.bypass = true;
			try
			{
				bool shift = ZInput.GetKey(KeyCode.LeftShift, false) || ZInput.GetKey(KeyCode.RightShift, false);
				string sh = shift ? "true" : "false";
				Vector3 mp = ZInput.pointerPosition;
				float x = Mathf.Clamp01(mp.x / Mathf.Max(1, Screen.width)), y = Mathf.Clamp01(1f - mp.y / Mathf.Max(1, Screen.height));
				string at = string.Format(CultureInfo.InvariantCulture, "\"x\":{0:F5},\"y\":{1:F5}", x, y);
				if (new Vector2(x, y) != last)
				{
					last = new Vector2(x, y);
					plugin.SendRaw("{\"t\":\"ui\"," + at + ",\"shift\":" + sh + "}");
				}
				int[] buttons = { 1, 3, 2 }; // Unity left, right, middle -> SDL 1, 3, 2
				for (int i = 0; i < 3; i++)
				{
					if (ZInput.GetMouseButtonDown(i))
						plugin.SendRaw("{\"t\":\"ui\"," + at + ",\"b\":" + buttons[i] + ",\"down\":true,\"shift\":" + sh + "}");
					if (ZInput.GetMouseButtonUp(i))
						plugin.SendRaw("{\"t\":\"ui\"," + at + ",\"b\":" + buttons[i] + ",\"down\":false,\"shift\":" + sh + "}");
				}
				float wheel = ZInput.GetMouseScrollWheel();
				if (Mathf.Abs(wheel) > 0.01f)
					plugin.SendRaw("{\"t\":\"ui\"," + at + ",\"wheel\":" + (wheel > 0f ? "1" : "-1") + "}");
				foreach (var kv in keys)
				{
					if (ZInput.GetKeyDown(kv.Key, false))
					{
						plugin.SendRaw("{\"t\":\"ui\",\"key\":" + kv.Value + ",\"down\":true,\"shift\":" + sh + "}");
						// letters, digits and space also type (search fields, chat)
						char c = kv.Key == KeyCode.Space ? ' ' : kv.Key >= KeyCode.A && kv.Key <= KeyCode.Z ? (char)((shift ? 'A' : 'a') + (kv.Key - KeyCode.A))
							: kv.Key >= KeyCode.Alpha0 && kv.Key <= KeyCode.Alpha9 ? (char)('0' + (kv.Key - KeyCode.Alpha0)) : '\0';
						if (c != '\0')
							plugin.SendRaw("{\"t\":\"ui\",\"char\":" + (int)c + "}");
					}
					if (ZInput.GetKeyUp(kv.Key, false))
						plugin.SendRaw("{\"t\":\"ui\",\"key\":" + kv.Value + ",\"down\":false,\"shift\":" + sh + "}");
				}
			}
			finally
			{
				Plugin.bypass = false;
			}
		}

		/// <summary>The cursor is free and shown while a Minecraft screen is open.</summary>
		[HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
		private static class CursorPatch
		{
			private static bool Prefix()
			{
				if (!Plugin.mcScreen)
					return true;
				ZCursor.LockState = CursorLockMode.None;
				ZCursor.Show();
				return false;
			}
		}

		/// <summary>The Viking stands still (no moving, looking, attacking) while a Minecraft screen is open.</summary>
		[HarmonyPatch(typeof(PlayerController), "TakeInput")]
		private static class TakeInputPatch
		{
			private static bool Prefix(ref bool __result)
			{
				if (!Plugin.mcScreen)
					return true;
				__result = false;
				return false;
			}
		}

		/// <summary>Valheim's own keys (Esc menu, Tab inventory, map...) do nothing while a Minecraft screen is open.</summary>
		[HarmonyPatch(typeof(ZInput), nameof(ZInput.GetKeyDown))]
		private static class KeyDownPatch
		{
			private static bool Prefix(ref bool __result)
			{
				if (!Plugin.mcScreen || Plugin.bypass)
					return true;
				__result = false;
				return false;
			}
		}
	}
}
