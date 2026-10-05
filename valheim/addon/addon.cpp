// ValCraft ReShade add-on: Minecraft's frame (shared memory "Local\MCPassthroughFrame") composited into Valheim's
// picture by MCPassthrough.fx. The compositor itself is the GTA example's (gta/src/compositor.cpp); here it is loaded
// by ReShade as an .addon64, and the BepInEx plugin drives it through the C exports below (it finds this module with
// GetModuleHandle + GetProcAddress, since both live in valheim.exe).
#include "compositor.h"
#include <windows.h>
#include <reshade.hpp>

extern "C" __declspec(dllexport) const char *NAME = "ValCraft";
extern "C" __declspec(dllexport) const char *DESCRIPTION = "Composites Minecraft (ValCraft passthrough) into Valheim.";

extern "C"
{
	__declspec(dllexport) void vc_set_active(int active) { compositor::set_active(active != 0); }
	__declspec(dllexport) void vc_set_host_planes(float n, float f) { compositor::set_host_planes(n, f); }
	__declspec(dllexport) void vc_set_host_pose(float yaw, float pitch, float roll, float fov, double x, double y, double z)
	{
		compositor::set_host_pose(yaw, pitch, roll, fov, x, y, z);
	}
	__declspec(dllexport) void vc_set_pose_lag(int frames) { compositor::set_pose_lag(frames); }
	__declspec(dllexport) void vc_set_look(float light, float bias, float slope) { compositor::set_look(light, bias, slope); }
	__declspec(dllexport) void vc_set_screen_fx(float sx, float sy, float roll, float warp) { compositor::set_screen_fx(sx, sy, roll, warp); }
	__declspec(dllexport) void vc_backbuffer_size(int *w, int *h) { compositor::backbuffer_size(*w, *h); }
	__declspec(dllexport) void vc_set_unity_depth(void *texture) { compositor::set_unity_depth(texture); }
	__declspec(dllexport) void *vc_render_event_func() { return reinterpret_cast<void *>(&compositor::render_event); }
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
	switch (reason)
	{
	case DLL_PROCESS_ATTACH:
		if (!compositor::try_register(module))
			return FALSE;
		break;
	case DLL_PROCESS_DETACH:
		compositor::unregister(module);
		break;
	}
	return TRUE;
}
