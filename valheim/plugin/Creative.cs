using HarmonyLib;
using UnityEngine;

namespace ValCraft
{
	/// <summary>
	/// Minecraft's creative mode on the Viking: nothing hurts, stamina never runs out, and a double jump flies (Space up,
	/// Shift down, Ctrl faster; double jump again or land to stop). Not Valheim's god mode or debug fly: with those the
	/// game marks the creatures you hit as cheated.
	/// </summary>
	internal static class Creative
	{
		private const float DoubleJump = 0.3f;
		internal static bool flying;
		private static float lastJump = -1f;
		private static readonly System.Reflection.MethodInfo debugFly = AccessTools.Method(typeof(Character), "UpdateDebugFly");

		internal static bool On => Plugin.I != null && Plugin.I.On && (Plugin.gameMode == "creative" || Plugin.gameMode == "spectator");

		/// <summary>Every frame, from the plugin's update.</summary>
		internal static void Tick(Player player)
		{
			if (!On)
			{
				flying = false;
				return;
			}
			Plugin.bypass = true;
			bool jump = ZInput.GetButtonDown("Jump");
			Plugin.bypass = false;
			if (jump && !Plugin.InGuiPublic())
			{
				if (Time.time - lastJump < DoubleJump)
				{
					flying = !flying;
					lastJump = -1f;
					return;
				}
				lastJump = Time.time;
			}
			// touching down while sinking ends the flight, as in Minecraft
			if (flying && player.IsOnGround() && Plugin.Sneaking())
				flying = false;
		}

		[HarmonyPatch(typeof(Character), "UpdateMotion")]
		private static class FlyPatch
		{
			private static bool Prefix(Character __instance, float dt)
			{
				if (!flying || __instance != Player.m_localPlayer || __instance.IsDead() || debugFly == null)
					return true;
				Remap.inFly = true;
				try { debugFly.Invoke(__instance, new object[] { dt }); }
				finally { Remap.inFly = false; }
				return false;
			}
		}

		/// <summary>Valheim's flight sinks on Ctrl; Minecraft's on Shift (Ctrl runs).</summary>
		[HarmonyPatch(typeof(ZInput), nameof(ZInput.GetKey))]
		private static class Remap
		{
			internal static bool inFly;

			private static bool Prefix(KeyCode key, ref bool __result)
			{
				if (!inFly || key != KeyCode.LeftControl)
					return true;
				__result = Plugin.Sneaking();
				return false;
			}
		}

		[HarmonyPatch(typeof(Player), nameof(Player.UseStamina))]
		private static class StaminaPatch
		{
			private static bool Prefix(Player __instance) => !(On && __instance == Player.m_localPlayer);
		}

		[HarmonyPatch(typeof(Player), nameof(Player.HaveStamina))]
		private static class HaveStaminaPatch
		{
			private static bool Prefix(Player __instance, ref bool __result)
			{
				if (!(On && __instance == Player.m_localPlayer))
					return true;
				__result = true;
				return false;
			}
		}
	}
}

namespace ValCraft
{
	/// <summary>
	/// Valheim's "3D resolution limit" renders the world into a smaller texture and scales it up; Minecraft is composited
	/// into the camera's picture at full size, so while it runs the world renders at full size.
	/// </summary>
	[HarmonyLib.HarmonyPatch(typeof(UpscaledFrameBuffer), "UpdateCameraTarget")]
	internal static class FullResolutionPatch
	{
		private static readonly System.Reflection.FieldInfo scaled = HarmonyLib.AccessTools.Field(typeof(UpscaledFrameBuffer), "m_isUsingScaledRendering");

		private static bool Prefix(UpscaledFrameBuffer __instance)
		{
			if (Plugin.I == null || !Plugin.I.On)
				return true;
			if (scaled != null && (bool)scaled.GetValue(__instance))
			{
				HarmonyLib.AccessTools.Method(typeof(UpscaledFrameBuffer), "ReleaseTextureIfExists")?.Invoke(__instance, null);
				HarmonyLib.AccessTools.Method(typeof(UpscaledFrameBuffer), "DestroyClearCameraIfExists")?.Invoke(__instance, null);
				scaled.SetValue(__instance, false);
			}
			return false;
		}
	}
}
