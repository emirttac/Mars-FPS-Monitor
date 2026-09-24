; =============================================================================
;  Mars FPS Monitor — Inno Setup 6 installer
;  Product: Mars FPS Monitor v3.0.0
;  Payload: publish\win-x64  (framework-dependent, win-x64)
; =============================================================================

#define MyAppName        "Mars FPS Monitor"
#define MyAppVersion     "3.0.0"
#define MyAppPublisher   "emirttac"
#define MyAppURL         "https://github.com/emirttac/Mars-FPS-Monitor"
#define MyAppSupportURL  "https://github.com/emirttac/Mars-FPS-Monitor/issues"
#define MyAppUpdatesURL  "https://github.com/emirttac/Mars-FPS-Monitor/releases"
#define MyAppExeName     "FPSOverlay.exe"
#define PublishDir       "publish\win-x64"
#define RtssZipUrl       "https://ftp.nluug.nl/pub/games/PC/guru3d/afterburner/%5BGuru3D%5D-RTSSSetup737Build28314.zip"
#define RtssZipSha256    "9b084a8cb3e53ec1a673894d0b66e22b16c9fd8785636b020b2d422f3f2a820e"

[Setup]
AppId={{A7C3E91F-4B2D-4E8A-9F16-8C2D1B0A9E77}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppSupportURL}
AppUpdatesURL={#MyAppUpdatesURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=dist
OutputBaseFilename=MarsFPSMonitor_Setup_v{#MyAppVersion}
SetupIconFile=app.ico
UninstallDisplayIcon={app}\app.ico
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoCopyright=Copyright (C) 2026 {#MyAppPublisher}
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
WizardStyle=modern
WizardSizePercent=100
ShowLanguageDialog=yes
; Code signing: see build-installer.ps1 (MARS_SIGN_TOOL / MARS_SIGN_CERT / MARS_SIGN_PASSWORD).
; Do not enable SignTool here unless those env vars define a working Authenticode toolchain.
AllowNoIcons=yes
CloseApplications=force
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "german";  MessagesFile: "compiler:Languages\German.isl"
Name: "french";  MessagesFile: "compiler:Languages\French.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Messages]
english.WelcomeLabel2=This will install [name/ver] on your computer.%n%nMars FPS Monitor shows FPS, frametime, CPU/GPU sensors, and optional GPU overclock controls as an always-on-top overlay.%n%nAdministrator rights are required (ETW FPS + hardware sensors).%n%nIf RivaTuner Statistics Server (RTSS) is missing, Setup will download and install it silently for exclusive-fullscreen OSD.%n%nClick Next to continue.
turkish.WelcomeLabel2=Bu sihirbaz [name/ver] uygulamasini bilgisayariniza kuracak.%n%nMars FPS Monitor; FPS, frametime, CPU/GPU sensorleri ve istege bagli GPU overclock kontrollerini her zaman ustte bir overlay olarak gosterir.%n%nYonetici izni gereklidir (ETW FPS + donanim sensorleri).%n%nRivaTuner Statistics Server (RTSS) yoksa Setup sessizce indirip kurar (tam ekran OSD icin).%n%nDevam etmek icin Ileri'ye basin.

[CustomMessages]
english.LaunchAfterInstall=Launch {#MyAppName}
english.DepTitle=Checking requirements
english.DepDesc=Downloading and installing missing components...
english.DepFail=Required components could not be downloaded or installed.%nPlease check your internet connection and try again.%n%n.NET 8 Desktop Runtime and VC++ 2015-2022 (x64) are required.
english.RtssFail=RivaTuner Statistics Server (RTSS) could not be installed automatically.%n{#MyAppName} will still install; exclusive-fullscreen OSD may be unavailable until you install RTSS manually.
english.RtssInstalling=Installing RivaTuner Statistics Server...
english.RtssExtracting=Extracting RTSS package...
english.RtssProgress=Installing RTSS silently — please wait...
english.WipeTitle=Preparing installation
english.WipeDesc=Closing Mars FPS Monitor, stopping its background service, and removing the previous installation...
english.WipeFail=Could not fully remove the previous {#MyAppName} installation.%nClose the app manually and try again.
turkish.LaunchAfterInstall={#MyAppName} uygulamasini baslat
turkish.DepTitle=Gereksinimler kontrol ediliyor
turkish.DepDesc=Eksik bilesenler indiriliyor ve kuruluyor...
turkish.DepFail=Gerekli bilesenler indirilemedi veya kurulamadi.%nInternet baglantinizi kontrol edip tekrar deneyin.%n%n.NET 8 Desktop Runtime ve VC++ 2015-2022 (x64) gereklidir.
turkish.RtssFail=RivaTuner Statistics Server (RTSS) otomatik kurulamadi.%n{#MyAppName} yine de kurulacak; tam ekran OSD icin RTSS'i elle kurmaniz gerekebilir.
turkish.RtssInstalling=RivaTuner Statistics Server kuruluyor...
turkish.RtssExtracting=RTSS paketi aciliyor...
turkish.RtssProgress=RTSS sessizce kuruluyor — lutfen bekleyin...
turkish.WipeTitle=Kuruluma hazirlaniyor
turkish.WipeDesc=Mars FPS Monitor kapatiliyor, arka plan servisi durduruluyor ve onceki kurulum siliniyor...
turkish.WipeFail=Onceki {#MyAppName} kurulumu tamamen kaldirilamadi.%nUygulamayi elle kapatip tekrar deneyin.
german.LaunchAfterInstall={#MyAppName} starten
german.DepTitle=Voraussetzungen werden geprueft
german.DepDesc=Fehlende Komponenten werden heruntergeladen und installiert...
german.DepFail=Erforderliche Komponenten konnten nicht heruntergeladen oder installiert werden.%nBitte Internetverbindung pruefen.%n%n.NET 8 Desktop Runtime und VC++ 2015-2022 (x64) werden benoetigt.
german.RtssFail=RTSS konnte nicht automatisch installiert werden.%n{#MyAppName} wird trotzdem installiert.
german.RtssInstalling=RivaTuner Statistics Server wird installiert...
german.RtssExtracting=RTSS-Paket wird entpackt...
german.RtssProgress=RTSS wird still installiert — bitte warten...
german.WipeTitle=Installation wird vorbereitet
german.WipeDesc=Mars FPS Monitor wird beendet, der Hintergrunddienst gestoppt und die vorherige Installation entfernt...
german.WipeFail=Vorherige Installation konnte nicht vollstaendig entfernt werden.
french.LaunchAfterInstall=Lancer {#MyAppName}
french.DepTitle=Verification des prerequisites
french.DepDesc=Telechargement et installation des composants manquants...
french.DepFail=Les composants requis n'ont pas pu etre telecharges ou installes.%nVerifiez votre connexion Internet.%n%n.NET 8 Desktop Runtime et VC++ 2015-2022 (x64) sont requis.
french.RtssFail=RTSS n'a pas pu etre installe automatiquement.%n{#MyAppName} sera quand meme installe.
french.RtssInstalling=Installation de RivaTuner Statistics Server...
french.RtssExtracting=Extraction du paquet RTSS...
french.RtssProgress=Installation silencieuse de RTSS — veuillez patienter...
french.WipeTitle=Preparation de l'installation
french.WipeDesc=Fermeture de Mars FPS Monitor, arret du service en arriere-plan et suppression de l'ancienne installation...
french.WipeFail=Impossible de supprimer completement l'ancienne installation.
spanish.LaunchAfterInstall=Iniciar {#MyAppName}
spanish.DepTitle=Comprobando requisitos
spanish.DepDesc=Descargando e instalando componentes faltantes...
spanish.DepFail=No se pudieron descargar o instalar los componentes necesarios.%nComprueba tu conexion a Internet.%n%nSe requieren .NET 8 Desktop Runtime y VC++ 2015-2022 (x64).
spanish.RtssFail=No se pudo instalar RTSS automaticamente.%n{#MyAppName} se instalara de todos modos.
spanish.RtssInstalling=Instalando RivaTuner Statistics Server...
spanish.RtssExtracting=Extrayendo paquete RTSS...
spanish.RtssProgress=Instalando RTSS en silencio — espere...
spanish.WipeTitle=Preparando instalacion
spanish.WipeDesc=Cerrando Mars FPS Monitor, deteniendo el servicio en segundo plano y eliminando la instalacion anterior...
spanish.WipeFail=No se pudo eliminar por completo la instalacion anterior.
russian.LaunchAfterInstall=Zapustit {#MyAppName}
russian.DepTitle=Proverka trebovanij
russian.DepDesc=Zagruzka i ustanovka otsutstvuyushchih komponentov...
russian.DepFail=Ne udalos zagruzit ili ustanovit neobhodimye komponenty.%nProverte internet-soedinenie.%n%nTrebuetsya .NET 8 Desktop Runtime i VC++ 2015-2022 (x64).
russian.RtssFail=Ne udalos avtomaticheski ustanovit RTSS.%n{#MyAppName} vse ravno budet ustanovlen.
russian.RtssInstalling=Ustanovka RivaTuner Statistics Server...
russian.RtssExtracting=Raspackovka paketa RTSS...
russian.RtssProgress=Tihaya ustanovka RTSS — podozhdite...
russian.WipeTitle=Podgotovka ustanovki
russian.WipeDesc=Zakrytie Mars FPS Monitor, ostanovka fonovoj sluzhby i udalenie predydushchej ustanovki...
russian.WipeFail=Ne udalos polnostyu udalit predydushchuyu ustanovku.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.log,config.json,oc_profiles.json,oc_debug.log"
Source: "app.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "installer\default-config.json"; DestDir: "{app}"; DestName: "config.json"; Flags: onlyifdoesntexist

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"; Comment: "FPS overlay & hardware monitor"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"; Tasks: desktopicon

[Run]
; Post-setup what's-new dialog (checkbox required), then normal app boot
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchAfterInstall}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallDelete]
Type: files; Name: "{app}\oc_debug.log"
Type: files; Name: "{app}\*.log"
Type: files; Name: "{app}\config.json"
Type: files; Name: "{app}\oc_profiles.json"

[Code]
#ifdef UNICODE
  #define AW "W"
#else
  #define AW "A"
#endif

const
  WAIT_TIMEOUT = $00000102;
  SEE_MASK_NOCLOSEPROCESS = $00000040;

type
  TShellExecuteInfo = record
    cbSize: DWORD;
    fMask: Cardinal;
    Wnd: HWND;
    lpVerb: string;
    lpFile: string;
    lpParameters: string;
    lpDirectory: string;
    nShow: Integer;
    hInstApp: THandle;
    lpIDList: DWORD;
    lpClass: string;
    hkeyClass: THandle;
    dwHotKey: DWORD;
    hIcon: THandle;
    hProcess: THandle;
  end;

function ShellExecuteEx(var lpExecInfo: TShellExecuteInfo): BOOL;
  external 'ShellExecuteEx{#AW}@shell32.dll stdcall';
function WaitForSingleObject(hHandle: THandle; dwMilliseconds: DWORD): DWORD;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function CloseHandle(hObject: THandle): BOOL;
  external 'CloseHandle@kernel32.dll stdcall';
function GetExitCodeProcess(hProcess: THandle; var lpExitCode: DWORD): BOOL;
  external 'GetExitCodeProcess@kernel32.dll stdcall';

var
  DownloadPage: TDownloadWizardPage;
  ProgressPage: TOutputProgressWizardPage;

function InstallExitOk(ResultCode: Integer): Boolean;
begin
  Result := (ResultCode = 0) or (ResultCode = 3010) or (ResultCode = 1638);
end;

function DirHasDotNet8Desktop(): Boolean;
var
  FindRec: TFindRec;
  BasePath: String;
begin
  Result := False;
  BasePath := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if not DirExists(BasePath) then
    BasePath := ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if DirExists(BasePath) then
  begin
    if FindFirst(BasePath + '\8.*', FindRec) then
    begin
      try
        repeat
          if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
          begin
            Result := True;
            Break;
          end;
        until not FindNext(FindRec);
      finally
        FindClose(FindRec);
      end;
    end;
  end;
end;

function IsDotNet8DesktopInstalled(): Boolean;
begin
  Result := DirHasDotNet8Desktop();
end;

function IsVCRedistX64Installed(): Boolean;
var
  Installed: Cardinal;
begin
  Result := False;
  if RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64', 'Installed', Installed) then
    Result := Installed = 1;
end;

function IsRtssInstalled(): Boolean;
var
  InstallDir: String;
begin
  Result :=
    FileExists(ExpandConstant('{commonpf32}\RivaTuner Statistics Server\RTSS.exe')) or
    FileExists(ExpandConstant('{commonpf}\RivaTuner Statistics Server\RTSS.exe')) or
    FileExists(ExpandConstant('{commonpf32}\MSI Afterburner\RTSS\RTSS.exe')) or
    FileExists(ExpandConstant('{commonpf32}\MSI Afterburner\RTSS.exe'));

  if Result then
    Exit;

  InstallDir := '';
  if RegQueryStringValue(HKLM32, 'SOFTWARE\Unwinder\RTSS', 'InstallDir', InstallDir) or
     RegQueryStringValue(HKLM32, 'SOFTWARE\Unwinder\RTSS', 'InstallPath', InstallDir) or
     RegQueryStringValue(HKLM64, 'SOFTWARE\WOW6432Node\Unwinder\RTSS', 'InstallDir', InstallDir) or
     RegQueryStringValue(HKLM64, 'SOFTWARE\WOW6432Node\Unwinder\RTSS', 'InstallPath', InstallDir) then
  begin
    if (InstallDir <> '') and FileExists(AddBackslash(InstallDir) + 'RTSS.exe') then
      Result := True;
  end;
end;

function FindRtssSetupExe(const Dir: String): String;
var
  FindRec: TFindRec;
begin
  Result := '';
  if FindFirst(Dir + '\RTSSSetup*.exe', FindRec) then
  begin
    try
      Result := Dir + '\' + FindRec.Name;
    finally
      FindClose(FindRec);
    end;
  end;
end;

procedure StopRtssProcesses();
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM RTSS.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/F /IM RTSSHooksLoader64.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/F /IM RTSSHooksLoader.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/F /IM EncoderServer.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure StopMarsProcesses();
var
  Code1, Code2, Attempt: Integer;
begin
  for Attempt := 1 to 5 do
  begin
    Exec('taskkill.exe', '/F /IM FPSOverlay.exe /T', '', SW_HIDE, ewWaitUntilTerminated, Code1);
    Exec('taskkill.exe', '/F /IM "Mars FPS Monitor.exe" /T', '', SW_HIDE, ewWaitUntilTerminated, Code2);
    if (Code1 <> 0) and (Code2 <> 0) then
      Break;
    Sleep(250);
  end;
end;

procedure StopOneService(const ServiceName: String);
var
  ResultCode: Integer;
begin
  { 0 = service exists. Missing services return non-zero and are skipped. }
  if not Exec('sc.exe', 'query ' + ServiceName, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Exit;
  if ResultCode <> 0 then
    Exit;

  Exec('sc.exe', 'stop ' + ServiceName, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(500);
  Exec('sc.exe', 'delete ' + ServiceName, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

{ Stop and remove any Mars background service, including the legacy R0FPSOverlay driver.
  PawnIO and RTSS are left alone. They are not part of this product. }
procedure StopMarsServices();
var
  ResultCode: Integer;
  ScriptPath: String;
begin
  StopOneService('R0FPSOverlay');
  StopOneService('FPSOverlay');
  StopOneService('MarsFPSMonitor');

  ScriptPath := ExpandConstant('{tmp}\mars-stop-services.ps1');
  SaveStringToFile(ScriptPath,
    '$svcs = Get-CimInstance Win32_Service | Where-Object { ' +
    '$_.Name -match ''R0FPS|FPSOverlay|MarsFPS'' -or ' +
    '($null -ne $_.PathName -and $_.PathName -match ''Mars FPS Monitor|FPSOverlay\.(exe|sys)'') }; ' +
    'foreach ($s in $svcs) { ' +
    'if ($s.Name -match ''PawnIO|RTSS'') { continue }; ' +
    'sc.exe stop $s.Name | Out-Null; Start-Sleep -Milliseconds 400; sc.exe delete $s.Name | Out-Null }' + #13#10,
    False);
  Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  DeleteFile(ExpandConstant('{commonpf}\{#MyAppName}\FPSOverlay.sys'));
  DeleteFile(ExpandConstant('{commonpf64}\{#MyAppName}\FPSOverlay.sys'));
  DeleteFile(ExpandConstant('{autopf}\{#MyAppName}\FPSOverlay.sys'));
  DeleteFile(ExpandConstant('{app}\FPSOverlay.sys'));
end;

function ExecHiddenWithProgress(const FileName, Params, StatusText: String; var ExitCode: Integer): Boolean;
var
  Info: TShellExecuteInfo;
  WaitRes: DWORD;
  Tick: Integer;
  Code: DWORD;
begin
  Result := False;
  ExitCode := -1;
  Tick := 0;

  ProgressPage.SetText(StatusText, ExpandConstant('{cm:RtssProgress}'));
  ProgressPage.SetProgress(0, 100);

  Info.cbSize := SizeOf(Info);
  Info.fMask := SEE_MASK_NOCLOSEPROCESS;
  Info.Wnd := 0;
  Info.lpVerb := '';
  Info.lpFile := FileName;
  Info.lpParameters := Params;
  Info.lpDirectory := '';
  Info.nShow := SW_HIDE;
  Info.hInstApp := 0;
  Info.lpIDList := 0;
  Info.lpClass := '';
  Info.hkeyClass := 0;
  Info.dwHotKey := 0;
  Info.hIcon := 0;
  Info.hProcess := 0;

  if not ShellExecuteEx(Info) then
    Exit;

  if Info.hProcess = 0 then
    Exit;

  repeat
    WaitRes := WaitForSingleObject(Info.hProcess, 200);
    if WaitRes = WAIT_TIMEOUT then
    begin
      Tick := Tick + 1;
      { Marquee-style progress so the wizard never looks frozen. }
      ProgressPage.SetProgress(Tick mod 100, 100);
      if (Tick mod 5) = 0 then
        ProgressPage.SetText(StatusText, ExpandConstant('{cm:RtssProgress}') + ' (' + IntToStr(Tick div 5) + 's)');
    end;
  until WaitRes <> WAIT_TIMEOUT;

  Code := 0;
  GetExitCodeProcess(Info.hProcess, Code);
  CloseHandle(Info.hProcess);
  ExitCode := Integer(Code);
  ProgressPage.SetProgress(100, 100);
  Result := True;
end;

function InstallRtssSilent(): Boolean;
var
  ZipPath, ExtractDir, SetupExe, PsCmd: String;
  ResultCode: Integer;
begin
  Result := False;
  ZipPath := ExpandConstant('{tmp}\rtss_setup.zip');
  ExtractDir := ExpandConstant('{tmp}\mars_rtss_pkg');

  if not FileExists(ZipPath) then
    Exit;

  ForceDirectories(ExtractDir);
  ProgressPage.SetText(ExpandConstant('{cm:RtssExtracting}'), '');
  ProgressPage.SetProgress(5, 100);

  PsCmd :=
    '-NoProfile -ExecutionPolicy Bypass -Command ' +
    '"Expand-Archive -LiteralPath ''' + ZipPath + ''' -DestinationPath ''' + ExtractDir + ''' -Force"';

  if not Exec('powershell.exe', PsCmd, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Exit;
  if ResultCode <> 0 then
    Exit;

  SetupExe := FindRtssSetupExe(ExtractDir);
  if SetupExe = '' then
    Exit;

  StopRtssProcesses();
  Sleep(400);

  if not ExecHiddenWithProgress(SetupExe, '/S', ExpandConstant('{cm:RtssInstalling}'), ResultCode) then
    Exit;

  Result := InstallExitOk(ResultCode) and IsRtssInstalled();
end;

procedure WipeDirWithRetry(const Dir: String);
var
  Attempt: Integer;
begin
  if (Dir = '') or (not DirExists(Dir)) then
    Exit;

  for Attempt := 1 to 6 do
  begin
    StopMarsProcesses();
    Sleep(300);
    if DelTree(Dir, True, True, True) then
      Exit;
    Sleep(400);
  end;
end;

function WipePreviousMarsInstall(): Boolean;
var
  AppDir, DefaultDir, UninstallPath, UninstallDir: String;
begin
  Result := True;
  ProgressPage.SetText(ExpandConstant('{cm:WipeTitle}'), ExpandConstant('{cm:WipeDesc}'));
  ProgressPage.SetProgress(10, 100);

  StopMarsProcesses();
  StopMarsServices();
  StopMarsProcesses();
  Sleep(400);

  AppDir := ExpandConstant('{app}');
  DefaultDir := ExpandConstant('{autopf}\{#MyAppName}');

  ProgressPage.SetProgress(40, 100);
  WipeDirWithRetry(AppDir);

  if not SameText(AppDir, DefaultDir) then
  begin
    ProgressPage.SetProgress(60, 100);
    WipeDirWithRetry(DefaultDir);
  end;

  { Also wipe path recorded by a previous Inno uninstall key, if present. }
  UninstallPath := '';
  if RegQueryStringValue(HKLM64,
      'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{A7C3E91F-4B2D-4E8A-9F16-8C2D1B0A9E77}_is1',
      'InstallLocation', UninstallPath) or
     RegQueryStringValue(HKLM32,
      'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{A7C3E91F-4B2D-4E8A-9F16-8C2D1B0A9E77}_is1',
      'InstallLocation', UninstallPath) then
  begin
    UninstallDir := RemoveBackslashUnlessRoot(UninstallPath);
    if (UninstallDir <> '') and (not SameText(UninstallDir, AppDir)) and (not SameText(UninstallDir, DefaultDir)) then
    begin
      ProgressPage.SetProgress(80, 100);
      WipeDirWithRetry(UninstallDir);
    end;
  end;

  ProgressPage.SetProgress(100, 100);

  if DirExists(AppDir) then
  begin
    { Still blocked — last force kill + delete attempt }
    StopMarsProcesses();
    Sleep(600);
    DelTree(AppDir, True, True, True);
  end;

  Result := not DirExists(AppDir);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  NeedsRestart := False;
  Result := '';

  ProgressPage.Show;
  try
    if not WipePreviousMarsInstall() then
      Result := ExpandConstant('{cm:WipeFail}');
  finally
    ProgressPage.Hide;
  end;
end;

procedure InitializeWizard();
begin
  DownloadPage := CreateDownloadPage(
    ExpandConstant('{cm:DepTitle}'),
    ExpandConstant('{cm:DepDesc}'),
    nil);
  ProgressPage := CreateOutputProgressPage(
    ExpandConstant('{cm:DepTitle}'),
    ExpandConstant('{cm:DepDesc}'));
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
  NeedDotNet: Boolean;
  NeedVcr: Boolean;
  NeedRtss: Boolean;
begin
  Result := True;

  if CurPageID = wpReady then
  begin
    { Kill the running app, remove its service, and delete the previous install
      before dependencies (including silent RTSS) and the file copy. }
    ProgressPage.Show;
    try
      if not WipePreviousMarsInstall() then
      begin
        MsgBox(ExpandConstant('{cm:WipeFail}'), mbError, MB_OK);
        Result := False;
        Exit;
      end;
    finally
      ProgressPage.Hide;
    end;

    NeedDotNet := not IsDotNet8DesktopInstalled();
    NeedVcr := not IsVCRedistX64Installed();
    NeedRtss := not IsRtssInstalled();

    if (not NeedDotNet) and (not NeedVcr) and (not NeedRtss) then
      Exit;

    DownloadPage.Clear;

    if NeedDotNet then
      DownloadPage.Add(
        'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe',
        'windowsdesktop-runtime-8-x64.exe',
        '');

    if NeedVcr then
      DownloadPage.Add(
        'https://aka.ms/vs/17/release/vc_redist.x64.exe',
        'vc_redist.x64.exe',
        '');

    if NeedRtss then
      DownloadPage.Add(
        '{#RtssZipUrl}',
        'rtss_setup.zip',
        '{#RtssZipSha256}');

    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
      except
        MsgBox(ExpandConstant('{cm:DepFail}'), mbError, MB_OK);
        Result := False;
        DownloadPage.Hide;
        Exit;
      end;
    finally
      { Keep page visible only through downloads; installs use ProgressPage. }
      DownloadPage.Hide;
    end;

    ProgressPage.Show;
    try
      try
        if NeedDotNet then
        begin
          ProgressPage.SetText('Microsoft .NET 8 Desktop Runtime', '');
          if not ExecHiddenWithProgress(
            ExpandConstant('{tmp}\windowsdesktop-runtime-8-x64.exe'),
            '/install /quiet /norestart',
            'Microsoft .NET 8 Desktop Runtime',
            ResultCode) then
          begin
            MsgBox(ExpandConstant('{cm:DepFail}'), mbError, MB_OK);
            Result := False;
            Exit;
          end;
          if (not InstallExitOk(ResultCode)) or (not IsDotNet8DesktopInstalled()) then
          begin
            MsgBox(ExpandConstant('{cm:DepFail}') + #13#10 + #13#10 +
              'NET Desktop Runtime exit code: ' + IntToStr(ResultCode), mbError, MB_OK);
            Result := False;
            Exit;
          end;
        end;

        if NeedVcr then
        begin
          ProgressPage.SetText('Microsoft Visual C++ Redistributable', '');
          if not ExecHiddenWithProgress(
            ExpandConstant('{tmp}\vc_redist.x64.exe'),
            '/install /quiet /norestart',
            'Microsoft Visual C++ Redistributable',
            ResultCode) then
          begin
            MsgBox(ExpandConstant('{cm:DepFail}'), mbError, MB_OK);
            Result := False;
            Exit;
          end;
          if (not InstallExitOk(ResultCode)) or (not IsVCRedistX64Installed()) then
          begin
            MsgBox(ExpandConstant('{cm:DepFail}') + #13#10 + #13#10 +
              'VC++ Redistributable exit code: ' + IntToStr(ResultCode), mbError, MB_OK);
            Result := False;
            Exit;
          end;
        end;

        if NeedRtss then
        begin
          if not InstallRtssSilent() then
            MsgBox(ExpandConstant('{cm:RtssFail}'), mbInformation, MB_OK);
        end;
      except
        MsgBox(ExpandConstant('{cm:DepFail}'), mbError, MB_OK);
        Result := False;
      end;
    finally
      ProgressPage.Hide;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  FlagDir: String;
begin
  if CurStep <> ssPostInstall then
    Exit;

  { First launch after this setup shows the release-notes window, then the app. }
  FlagDir := ExpandConstant('{localappdata}\Mars FPS Monitor');
  ForceDirectories(FlagDir);
  SaveStringToFile(FlagDir + '\show-whats-new', '{#MyAppVersion}', False);
end;
