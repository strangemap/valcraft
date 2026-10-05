@echo off
rem ValCraft: Minecraft inside Valheim. Starts the Minecraft half (Prism instance "ValCraft"), then Valheim on D3D11
rem (ReShade composites Minecraft into it). In game: F6 Minecraft hands / Valheim weapons, F7 on/off, F8 re-level.
setlocal
set HERE=%~dp0
set VALHEIM=D:\SteamLibrary\steamapps\common\Valheim
tasklist /fi "imagename eq valheim.exe" | find /i "valheim.exe" >nul && (echo Valheim is already running & goto :eof)
start "" "%HERE%prism\prismlauncher.exe" --launch ValCraft
rem Minecraft first: Valheim connects whenever it is ready (the link keeps retrying either way)
timeout /t 5 /nobreak >nul
start "" /d "%VALHEIM%" "%VALHEIM%\valheim.exe" -force-d3d11
