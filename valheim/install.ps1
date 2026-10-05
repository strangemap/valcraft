# Installs the Valheim half of ValCraft: the BepInEx plugin, ReShade (as dxgi.dll, add-on build) and the ValCraft
# add-on + effect. Only adds files; `install.ps1 -Remove` deletes exactly those (BepInEx itself stays).
param([switch]$Remove, [string]$Valheim = 'D:\SteamLibrary\steamapps\common\Valheim')
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$gta = Join-Path $here '..\gta'
$files = @('dxgi.dll', 'ReShade.ini', 'ReShadePreset.ini', 'ValCraft.addon64', 'BepInEx\plugins\ValCraft.dll',
	'reshade-shaders\Shaders\MCPassthrough.fx', 'reshade-shaders\Shaders\ReShade.fxh', 'reshade-shaders\Shaders\ReShadeUI.fxh')
if (-not (Test-Path "$Valheim\valheim.exe")) { throw "valheim.exe not found in $Valheim" }
if ($Remove) {
	foreach ($f in $files) { $p = Join-Path $Valheim $f; if (Test-Path $p) { Remove-Item $p; "removed $f" } }
	return
}
$dxgi = Join-Path $Valheim 'dxgi.dll'
if ((Test-Path $dxgi) -and ((Get-FileHash $dxgi).Hash -ne (Get-FileHash "$gta\third_party\runtime\ReShade64.dll").Hash)) {
	throw "a dxgi.dll that isn't this ReShade is already there; not replacing it"
}
New-Item -ItemType Directory -Force "$Valheim\reshade-shaders\Shaders", "$Valheim\BepInEx\plugins" | Out-Null
Copy-Item "$gta\third_party\runtime\ReShade64.dll" $dxgi
Copy-Item "$here\addon\build\ValCraft.addon64" $Valheim
Copy-Item "$here\plugin\bin\Release\netstandard2.1\ValCraft.dll" "$Valheim\BepInEx\plugins\"
Copy-Item "$here\shaders\MCPassthrough.fx", "$gta\third_party\ReShade.fxh", "$gta\third_party\ReShadeUI.fxh" "$Valheim\reshade-shaders\Shaders\"
if (-not (Test-Path "$Valheim\ReShade.ini")) {
	# Unity (D3D11) uses reversed Z; the overlay stays closed (no tutorial), add-ons load from the game folder
	@"
[GENERAL]
EffectSearchPaths=.\reshade-shaders\Shaders\
TextureSearchPaths=.\reshade-shaders\Textures\
PresetPath=.\ReShadePreset.ini
PreprocessorDefinitions=RESHADE_DEPTH_INPUT_IS_REVERSED=1,RESHADE_DEPTH_INPUT_IS_UPSIDE_DOWN=0,RESHADE_DEPTH_INPUT_IS_LOGARITHMIC=0,RESHADE_DEPTH_LINEARIZATION_FAR_PLANE=1000
NoReloadOnInit=0

[ADDON]
AddonPath=.\
DisabledAddons=

[DEPTH]
DepthCopyBeforeClears=1
UseAspectRatioHeuristics=1

[OVERLAY]
TutorialProgress=4
ShowClock=0
ShowFPS=0

[SCREENSHOT]
SavePath=.\
"@ | Set-Content -Encoding ascii "$Valheim\ReShade.ini"
}
"Techniques=MCPassthrough@MCPassthrough.fx`r`nTechniqueSorting=MCPassthrough@MCPassthrough.fx" | Set-Content -Encoding ascii "$Valheim\ReShadePreset.ini"
"installed into $Valheim"
