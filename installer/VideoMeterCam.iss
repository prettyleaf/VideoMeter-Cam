; Установщик ВидеоМетр-Кам (Inno Setup 6). Сборка: ../build.sh или ISCC.exe VideoMeterCam.iss
#define AppName "ВидеоМетр-Кам"
#define AppNameLat "VideoMeterCam"
#ifndef AppVersion
  #define AppVersion "1.2.0"
#endif
#define AppExe "VideoMeterCam.exe"
#ifndef BinDir
  #define BinDir "..\src\bin\Release\net35"
#endif

[Setup]
AppId={{FA083620-8745-40BB-B195-95138EAFF2A5}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=ТУСУР, кафедра телевидения и управления (ТУ)
AppComments=Учебная программа кафедры ТУ ТУСУР. Разработчик: Колесников И.Д.
AppCopyright=© 2026 ТУСУР, кафедра телевидения и управления
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName={#AppNameLat}
VersionInfoDescription={#AppName} — установка
VersionInfoCompany=ТУСУР, кафедра телевидения и управления
DefaultDirName={autopf}\{#AppNameLat}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; установка возможна и без прав администратора (в профиль пользователя)
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=6.1sp1
OutputDir=..\..\dist
OutputBaseFilename={#AppNameLat}-{#AppVersion}-setup
SetupIconFile=..\src\Resources\app.ico
LicenseFile=..\src\Resources\license.txt
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName} {#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=no
CloseApplications=yes

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать значок на рабочем столе"; GroupDescription: "Дополнительно:"

[Files]
Source: "{#BinDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BinDir}\{#AppExe}.config"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Измерение и коррекция координатных искажений ТВ-изображения"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Запустить {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
const
  // AppId версий под прежним названием (СигмаЛаб-Кам)
  OldAppId = '{7B5022DF-2391-4617-81C8-5A1619099FB3}';

function NetInstalled(const Key: String): Boolean;
var
  V: Cardinal;
begin
  Result := (RegQueryDWordValue(HKLM, Key, 'Install', V) and (V = 1));
  // HKLM64 в 32-битной Windows вызывает исключение
  if not Result and IsWin64 then
    Result := (RegQueryDWordValue(HKLM64, Key, 'Install', V) and (V = 1));
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  // Программе нужен .NET Framework 4.x (есть в Windows 8/10/11) или 3.5 (входит в Windows 7)
  if not (NetInstalled('SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full') or
          NetInstalled('SOFTWARE\Microsoft\NET Framework Setup\NDP\v3.5')) then
  begin
    Result := MsgBox('Не найден .NET Framework 3.5 или 4.x, необходимый для работы программы.' + #13#10 + #13#10 +
      'В Windows 7 включите компонент «Microsoft .NET Framework 3.5.1» (Панель управления › Программы и компоненты › ' +
      'Включение или отключение компонентов Windows) или установите .NET Framework 4.8 с сайта Microsoft.' + #13#10 + #13#10 +
      'Продолжить установку?', mbConfirmation, MB_YESNO) = IDYES;
  end;
end;

// Деинсталлятор версии под прежним названием или '', если она не установлена
function OldUninstaller(): String;
var
  Key, S: String;
begin
  Result := '';
  Key := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\' + OldAppId + '_is1';
  if not RegQueryStringValue(HKCU, Key, 'UninstallString', S) then
    if not RegQueryStringValue(HKLM32, Key, 'UninstallString', S) then
    begin
      if not IsWin64 then Exit;
      if not RegQueryStringValue(HKLM64, Key, 'UninstallString', S) then Exit;
    end;
  S := RemoveQuotes(S);
  if FileExists(S) then Result := S;
end;

// У прежней версии другой AppId, поэтому без удаления обе стояли бы рядом.
// Настройки и профили камер в %APPDATA% деинсталлятор не трогает — программа переносит их сама.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Uninst: String;
  Code: Integer;
begin
  Result := '';
  Uninst := OldUninstaller();
  if Uninst = '' then Exit;
  if not ShellExec('', Uninst, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
    Result := 'Не удалось удалить прежнюю версию программы (СигмаЛаб-Кам). Удалите её в «Панель управления › ' +
      'Программы и компоненты» и запустите установку снова.';
end;
