; Установщик СигмаЛаб-Кам (Inno Setup 6). Сборка: ../build.sh или ISCC.exe SigmaLabCam.iss
#define AppName "СигмаЛаб-Кам"
#define AppNameLat "SigmaLabCam"
#ifndef AppVersion
  #define AppVersion "1.0.1"
#endif
#define AppExe "SigmaLabCam.exe"
#ifndef BinDir
  #define BinDir "..\src\bin\Release\net35"
#endif

[Setup]
AppId={{7B5022DF-2391-4617-81C8-5A1619099FB3}
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
function NetInstalled(const Key: String): Boolean;
var
  V: Cardinal;
begin
  Result := (RegQueryDWordValue(HKLM, Key, 'Install', V) and (V = 1));
  if not Result then
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
