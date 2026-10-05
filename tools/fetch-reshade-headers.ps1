# Fetches ReShade's add-on API headers (crosire/reshade, BSD-3) into valheim/addon/third_party/reshade (not in the repo).
$ErrorActionPreference = 'Stop'
$v = '6.8.0'
$dst = Join-Path $PSScriptRoot '..\valheim\addon\third_party\reshade'
New-Item -ItemType Directory -Force $dst | Out-Null
foreach ($f in 'reshade.hpp', 'reshade_api.hpp', 'reshade_api_device.hpp', 'reshade_api_pipeline.hpp', 'reshade_api_resource.hpp',
	'reshade_api_format.hpp', 'reshade_events.hpp', 'reshade_overlay.hpp') {
	Invoke-WebRequest "https://raw.githubusercontent.com/crosire/reshade/v$v/include/$f" -OutFile (Join-Path $dst $f)
}
"ReShade $v headers in $dst"
