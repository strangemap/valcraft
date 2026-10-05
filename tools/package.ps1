# Builds everything and packs the release zip: dist\ValCraft-<version>.zip (install.bat + files\).
#   needs: .NET SDK 8, JDK 25 (JAVA_HOME), Visual Studio C++ tools, ReShade headers (tools\fetch-reshade-headers.ps1)
param([string]$Version = '0.1.0', [string]$Valheim = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim')
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Push-Location $root
try {
	dotnet build valheim\plugin -c Release -nologo -v q "-p:ValheimDir=$Valheim"
	if ($LASTEXITCODE) { throw 'plugin build failed' }
	cmd /c valheim\addon\build.bat
	if ($LASTEXITCODE) { throw 'add-on build failed' }
	Push-Location mc; .\gradlew.bat build --no-daemon -q; $code = $LASTEXITCODE; Pop-Location
	if ($code) { throw 'mod build failed' }

	$out = Join-Path $root "dist\ValCraft-$Version"
	if (Test-Path $out) { Remove-Item -Recurse -Force $out }
	New-Item -ItemType Directory -Force "$out\files" | Out-Null
	Copy-Item install.ps1, install.bat, README.md, LICENSE $out
	Copy-Item valheim\plugin\bin\Release\netstandard2.1\ValCraft.dll "$out\files\"
	Copy-Item valheim\addon\build\ValCraft.addon64 "$out\files\"
	Copy-Item valheim\shaders\MCPassthrough.fx "$out\files\"
	Copy-Item (Get-ChildItem mc\build\libs\*.jar | ? Name -notmatch 'sources' | select -First 1).FullName "$out\files\valcraft-mc.jar"
	$zip = Join-Path $root "dist\ValCraft-$Version.zip"
	if (Test-Path $zip) { Remove-Item $zip }
	Compress-Archive "$out\*" $zip
	"packed $zip"
} finally {
	Pop-Location
}
