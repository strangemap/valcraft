using System;
using System.Runtime.InteropServices;

namespace ValCraft
{
	/// <summary>
	/// The ReShade add-on (ValCraft.addon64), already loaded into valheim.exe by ReShade: found by module name, its
	/// C exports called directly. Everything is a no-op until it is there.
	/// </summary>
	internal static class Addon
	{
		[DllImport("kernel32", CharSet = CharSet.Unicode)]
		private static extern IntPtr GetModuleHandleW(string name);

		[DllImport("kernel32", CharSet = CharSet.Ansi)]
		private static extern IntPtr GetProcAddress(IntPtr module, string name);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void IntFn(int v);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Float2Fn(float a, float b);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void PoseFn(float yaw, float pitch, float roll, float fov, double x, double y, double z);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SizeFn(out int w, out int h);

		private static IntFn setActive, setPoseLag;
		private static Float2Fn setPlanes;
		private static PoseFn setPose;
		private static SizeFn size;
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void PtrArgFn(IntPtr p);
		private static PtrArgFn setUnityDepth;
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr PtrFn();
		/// <summary>The add-on's Unity render-thread callback (composite before the UI), for GL.IssuePluginEvent.</summary>
		public static IntPtr RenderEvent { get; private set; }
		private static float nextTry;
		public static bool Loaded { get; private set; }

		public static void TryLoad(float now)
		{
			if (Loaded || now < nextTry)
				return;
			nextTry = now + 2f;
			IntPtr m = GetModuleHandleW("ValCraft.addon64");
			if (m == IntPtr.Zero)
				return;
			T F<T>(string n) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(GetProcAddress(m, n));
			setActive = F<IntFn>("vc_set_active");
			setPoseLag = F<IntFn>("vc_set_pose_lag");
			setPlanes = F<Float2Fn>("vc_set_host_planes");
			setPose = F<PoseFn>("vc_set_host_pose");
			size = F<SizeFn>("vc_backbuffer_size");
			if (GetProcAddress(m, "vc_set_unity_depth") != IntPtr.Zero)
				setUnityDepth = F<PtrArgFn>("vc_set_unity_depth");
			IntPtr re = GetProcAddress(m, "vc_render_event_func");
			if (re != IntPtr.Zero)
				RenderEvent = Marshal.GetDelegateForFunctionPointer<PtrFn>(re)();
			Loaded = true;
			Plugin.Log("ReShade add-on found");
		}

		public static void SetUnityDepth(IntPtr texture) => setUnityDepth?.Invoke(texture);

		public static void SetActive(bool on) => setActive?.Invoke(on ? 1 : 0);
		public static void SetPoseLag(int frames) => setPoseLag?.Invoke(frames);
		public static void SetPlanes(float n, float f) => setPlanes?.Invoke(n, f);
		public static void SetPose(float yaw, float pitch, float roll, float fov, double x, double y, double z) => setPose?.Invoke(yaw, pitch, roll, fov, x, y, z);

		public static void BackbufferSize(out int w, out int h)
		{
			w = h = 0;
			size?.Invoke(out w, out h);
		}
	}
}
