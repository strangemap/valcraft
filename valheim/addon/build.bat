@echo off
rem Builds ValCraft.addon64 (ReShade add-on) with MSVC into build\.
setlocal
set HERE=%~dp0
set GTA=%HERE%..\..\gta
if not defined VCVARS for /f "usebackq delims=" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VCVARS=%%i\VC\Auxiliary\Build\vcvars64.bat"
call "%VCVARS%" >nul || exit /b 1
if not exist "%HERE%build" mkdir "%HERE%build"
cl /nologo /LD /O2 /EHsc /std:c++20 /MT /W3 /DWIN32_LEAN_AND_MEAN /DNOMINMAX ^
  /I "%GTA%\third_party\reshade" ^
  "%HERE%addon.cpp" "%HERE%compositor.cpp" ^
  /Fo"%HERE%build\\" /Fe"%HERE%build\ValCraft.addon64" ^
  /link user32.lib
