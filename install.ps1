# ValCraft installer: Minecraft inside Valheim.
# Run from the unpacked release folder (install.bat does it). Adds files only; `install.ps1 -Remove` takes them out.
#   1. finds Valheim (Steam libraries), installs BepInExPack_Valheim if missing
#   2. installs ReShade (add-on build, as dxgi.dll), the ValCraft add-on + effect and the ValCraft plugin
#   3. downloads portable Prism Launcher to %USERPROFILE%\ValCraft\prism with a "ValCraft" instance
#      (Minecraft 26.3 + Fabric Loader + Fabric API + the ValCraft mod)
param([switch]$Remove, [string]$Valheim)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$here = $PSScriptRoot
$files = Join-Path $here 'files'
$home2 = Join-Path $env:USERPROFILE 'ValCraft'  # not under AppData\Local: Fabric's class loading breaks there
$prism = Join-Path $home2 'prism'
$tmp = Join-Path $env:TEMP 'valcraft-install'
$ua = @{ 'User-Agent' = 'ValCraft-installer' }
$mcVersion = '26.3'
$loaderVersion = '0.19.5'
$reshadeVersion = '6.8.0'

function Say($m) { Write-Host "==> $m" -ForegroundColor Cyan }

function Find-Valheim {
	$steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
	$libs = @()
	if ($steam) {
		$libs += $steam
		$vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
		if (Test-Path $vdf) {
			$libs += Select-String -Path $vdf -Pattern '"path"\s+"(.+)"' | % { $_.Matches[0].Groups[1].Value -replace '\\\\', '\' }
		}
	}
	foreach ($l in $libs) {
		$p = Join-Path $l 'steamapps\common\Valheim'
		if (Test-Path (Join-Path $p 'valheim.exe')) { return $p }
	}
	return $null
}

if (-not $Valheim) { $Valheim = Find-Valheim }
if (-not $Valheim -or -not (Test-Path (Join-Path $Valheim 'valheim.exe'))) {
	throw "Valheim not found. Run: install.ps1 -Valheim 'X:\path\to\Valheim'"
}
Say "Valheim: $Valheim"
if (Get-Process valheim -ErrorAction SilentlyContinue) { throw 'Close Valheim first, then run the installer again.' }

$ours = @('dxgi.dll', 'ReShade.ini', 'ReShadePreset.ini', 'ValCraft.addon64', 'BepInEx\plugins\ValCraft.dll',
	'reshade-shaders\Shaders\MCPassthrough.fx', 'reshade-shaders\Shaders\ReShade.fxh', 'reshade-shaders\Shaders\ReShadeUI.fxh')

if ($Remove) {
	foreach ($f in $ours) { $p = Join-Path $Valheim $f; if (Test-Path $p) { Remove-Item $p; "removed $f" } }
	Say "ValCraft removed from Valheim (BepInEx stays; Prism stays in $prism)"
	return
}

New-Item -ItemType Directory -Force $tmp, $home2 | Out-Null

# --- BepInEx ---------------------------------------------------------------------------------------------------------
if (-not (Test-Path (Join-Path $Valheim 'BepInEx\core\BepInEx.dll'))) {
	Say 'Installing BepInExPack_Valheim (Thunderstore)'
	$pkg = Invoke-RestMethod 'https://thunderstore.io/api/experimental/package/denikson/BepInExPack_Valheim/' -Headers $ua
	Invoke-WebRequest $pkg.latest.download_url -OutFile "$tmp\bep.zip" -Headers $ua
	Expand-Archive -Force "$tmp\bep.zip" "$tmp\bep"
	Copy-Item -Recurse -Force "$tmp\bep\BepInExPack_Valheim\*" $Valheim
}

# --- ReShade (add-on build) as dxgi.dll ----------------------------------------------------------------------------------
$dxgi = Join-Path $Valheim 'dxgi.dll'
if (-not (Test-Path $dxgi)) {
	Say "Downloading ReShade $reshadeVersion (add-on build)"
	Invoke-WebRequest "https://reshade.me/downloads/ReShade_Setup_${reshadeVersion}_Addon.exe" -OutFile "$tmp\reshade.exe" -Headers @{ 'User-Agent' = 'Mozilla/5.0'; 'Referer' = 'https://reshade.me/' }
	# the setup exe carries its DLLs as an appended zip
	Add-Type -AssemblyName System.IO.Compression.FileSystem
	$zip = [IO.Compression.ZipFile]::OpenRead("$tmp\reshade.exe")
	try { [IO.Compression.ZipFileExtensions]::ExtractToFile(($zip.Entries | ? Name -eq 'ReShade64.dll'), $dxgi, $true) } finally { $zip.Dispose() }
}
New-Item -ItemType Directory -Force "$Valheim\reshade-shaders\Shaders", "$Valheim\BepInEx\plugins" | Out-Null
foreach ($f in 'ReShade.fxh', 'ReShadeUI.fxh') {
	$dst = "$Valheim\reshade-shaders\Shaders\$f"
	if (-not (Test-Path $dst)) { Invoke-WebRequest "https://raw.githubusercontent.com/crosire/reshade-shaders/slim/Shaders/$f" -OutFile $dst }
}
Copy-Item "$files\ValCraft.addon64" $Valheim -Force
Copy-Item "$files\MCPassthrough.fx" "$Valheim\reshade-shaders\Shaders\" -Force
Copy-Item "$files\ValCraft.dll" "$Valheim\BepInEx\plugins\" -Force
if (-not (Test-Path "$Valheim\ReShade.ini")) {
	@"
[GENERAL]
EffectSearchPaths=.\reshade-shaders\Shaders\
TextureSearchPaths=.\reshade-shaders\Textures\
PresetPath=.\ReShadePreset.ini
PreprocessorDefinitions=RESHADE_DEPTH_INPUT_IS_REVERSED=1,RESHADE_DEPTH_INPUT_IS_UPSIDE_DOWN=0,RESHADE_DEPTH_INPUT_IS_LOGARITHMIC=0,RESHADE_DEPTH_LINEARIZATION_FAR_PLANE=1000

[ADDON]
AddonPath=.\

[OVERLAY]
TutorialProgress=4
ShowClock=0
ShowFPS=0
"@ | Set-Content -Encoding ascii "$Valheim\ReShade.ini"
}
"Techniques=MCPassthrough@MCPassthrough.fx`r`nTechniqueSorting=MCPassthrough@MCPassthrough.fx" | Set-Content -Encoding ascii "$Valheim\ReShadePreset.ini"

# --- Prism Launcher + the ValCraft instance -------------------------------------------------------------------------------
if (-not (Test-Path "$prism\prismlauncher.exe")) {
	Say 'Downloading Prism Launcher (portable)'
	$rel = Invoke-RestMethod 'https://api.github.com/repos/PrismLauncher/PrismLauncher/releases/latest' -Headers $ua
	$asset = $rel.assets | ? name -match 'Windows-MSVC-Portable.*\.zip$' | select -First 1
	Invoke-WebRequest $asset.browser_download_url -OutFile "$tmp\prism.zip" -Headers $ua
	Expand-Archive -Force "$tmp\prism.zip" $prism
}
# close Prism after it starts the game (Valheim starts it hidden)
$cfg = "$prism\prismlauncher.cfg"
if (-not (Test-Path $cfg)) { "[General]`r`nCloseAfterLaunch=true`r`nAutomaticJavaDownload=true`r`nAutomaticJavaSwitch=true" | Set-Content -Encoding utf8 $cfg }

$inst = "$prism\instances\ValCraft"
New-Item -ItemType Directory -Force "$inst\.minecraft\mods" | Out-Null
@"
{
    "components": [
        { "cachedName": "LWJGL 3", "cachedVersion": "3.4.3", "cachedVolatile": true, "dependencyOnly": true, "uid": "org.lwjgl3", "version": "3.4.3" },
        { "cachedName": "Minecraft", "cachedRequires": [ { "suggests": "3.4.3", "uid": "org.lwjgl3" } ], "cachedVersion": "$mcVersion", "important": true, "uid": "net.minecraft", "version": "$mcVersion" },
        { "cachedName": "Intermediary Mappings", "cachedRequires": [ { "equals": "$mcVersion", "uid": "net.minecraft" } ], "cachedVersion": "$mcVersion", "cachedVolatile": true, "dependencyOnly": true, "uid": "net.fabricmc.intermediary", "version": "$mcVersion" },
        { "cachedName": "Fabric Loader", "cachedRequires": [ { "uid": "net.fabricmc.intermediary" } ], "cachedVersion": "$loaderVersion", "uid": "net.fabricmc.fabric-loader", "version": "$loaderVersion" }
    ],
    "formatVersion": 1
}
"@ | Set-Content -Encoding utf8 "$inst\mmc-pack.json"
@"
[General]
InstanceType=OneSix
name=ValCraft
iconKey=default
notes=Minecraft half of ValCraft. Valheim starts it by itself.
OverrideJavaArgs=true
JvmArgs=--enable-native-access=ALL-UNNAMED -Dpassthrough.startHidden=true
OverrideMemory=true
MinMemAlloc=512
MaxMemAlloc=4096
OverrideConsole=true
ShowConsole=false
AutoCloseConsole=false
ShowConsoleOnError=true
"@ | Set-Content -Encoding utf8 "$inst\instance.cfg"
Get-ChildItem "$inst\.minecraft\mods" -ErrorAction SilentlyContinue | ? { $_.Name -like 'fabric-api-*.jar' -or $_.Name -like 'passthrough-*.jar' -or $_.Name -eq 'valcraft-mc.jar' } | Remove-Item
Say 'Downloading Fabric API (Modrinth)'
$versions = Invoke-RestMethod "https://api.modrinth.com/v2/project/fabric-api/version?game_versions=%5B%22$mcVersion%22%5D&loaders=%5B%22fabric%22%5D" -Headers $ua
$file = $versions[0].files | ? primary | select -First 1
if (-not $file) { $file = $versions[0].files[0] }
Invoke-WebRequest $file.url -OutFile "$inst\.minecraft\mods\$($file.filename)" -Headers $ua
Copy-Item "$files\valcraft-mc.jar" "$inst\.minecraft\mods\" -Force

# --- plugin config: where Prism is --------------------------------------------------------------------------------------
$pcfg = "$Valheim\BepInEx\config\valcraft.passthrough.cfg"
New-Item -ItemType Directory -Force (Split-Path $pcfg) | Out-Null
if (Test-Path $pcfg) {
	(Get-Content $pcfg) -replace '^Launcher = .*', "Launcher = $prism\prismlauncher.exe" | Set-Content $pcfg
} else {
	"[Minecraft]`r`nLauncher = $prism\prismlauncher.exe`r`n" | Set-Content -Encoding utf8 $pcfg
}

Say 'Done.'
Write-Host ''
Write-Host 'One last step: open Prism once and add your Microsoft account:' -ForegroundColor Yellow
Write-Host "   $prism\prismlauncher.exe  ->  Accounts -> Add Microsoft (make it the default), then close Prism." -ForegroundColor Yellow
Write-Host 'Then start Valheim with the launch option  -force-d3d11  (Steam: Valheim -> Properties -> Launch options).' -ForegroundColor Yellow
Start-Process "$prism\prismlauncher.exe" -WorkingDirectory $prism
