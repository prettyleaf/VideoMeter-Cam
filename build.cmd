@echo off
rem Сборка ВидеоМетр-Кам на Windows: нужен .NET SDK 8+ и (для установщика) Inno Setup 6.
chcp 65001 >nul
cd /d "%~dp0"
dotnet run --project tests\CoreTests.csproj -c Release || exit /b 1
dotnet build src\VideoMeterCam.csproj -c Release -nologo -v q || exit /b 1
set ISCC="%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist %ISCC% set ISCC="%ProgramFiles%\Inno Setup 6\ISCC.exe"
if exist %ISCC% (%ISCC% installer\VideoMeterCam.iss) else (echo Inno Setup 6 не найден: собран только src\bin\Release\net35\VideoMeterCam.exe)
