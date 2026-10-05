# ValCraft — Minecraft inside Valheim (passthrough) — MODLOG

## Setup (2026-10-05)
- Valheim: `D:\SteamLibrary\steamapps\common\Valheim`, Unity 6000.0.75 Mono. Normally runs Vulkan (Steam launch choice);
  we launch `valheim.exe -force-d3d11` (ReShade dxgi.dll path).
- BepInExPack_Valheim 5.4.2351 installed into the game folder (file list: `backup/bepinex-installed-files.txt`).
- Saves backup: `backup/valheim-saves-20261005-1023.zip` (of `%USERPROFILE%\AppData\LocalLow\IronGate\Valheim`).
- JDK 25 (Temurin 25.0.4) for Minecraft 26.3 + Fabric 0.19.5 + Fabric API 0.161.0+26.3; .NET SDK 8; VS 18 C++ tools.
- Base: universal-modder `examples/minecraft-gta5-passthrough` (mc/ Fabric mod used unchanged, compositor reused).

## Route
Passthrough. Minecraft (Fabric mod `mc/`) runs a void world, gets Valheim's camera over WebSocket 127.0.0.1:25599,
exports colour+depth into shared memory `Local\MCPassthroughFrame`.
Valheim side:
- `valheim/plugin` BepInEx plugin (C#): camera/player pose -> `cam`, ground raycasts -> barrier `ground` columns,
  MC blocks -> invisible BoxColliders (layer static_solid), sword swing/arrows/fireworks/explosions -> Valheim HitData,
  explosions dig terrain with the game's own TerrainOp prefab. Hides Valheim's player renderers (Steve is drawn by MC).
  Input: F6 Minecraft hands vs Valheim weapons, F7 on/off, F8 re-level.
- `valheim/addon` ReShade add-on (= GTA compositor + C exports), loaded as `ValCraft.addon64`; the plugin finds it via
  GetModuleHandle and feeds pose/planes. Effect `MCPassthrough.fx` depth-composites against Unity's reversed-Z depth.
- Coordinates: MC (x,y,z) = (-unity x, unity y + yOffset, unity z); yaw = Unity yaw; pitch = pitch; roll = -roll.

## Install / remove
`powershell -File valheim/install.ps1` (or `-Remove`). BepInEx removal: delete files listed in backup/bepinex-installed-files.txt.

## Gotchas
1. ilspycmd 9+ needs .NET 9; use `ilspycmd --version 8.2.0.7535` with `DOTNET_ROLL_FORWARD=Major`.
2. Gradle wrapper download timed out; seeded `~/.gradle/wrapper/dists/gradle-9.7.1-bin/<hash>/` with a curl'd zip.
3. Valheim auto-continues into the last world on start here (world "пвапва").
