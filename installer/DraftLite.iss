; ---------------------------------------------------------------------------
;  DraftLite - script di installazione (Inno Setup 6)
;
;  Compilazione:
;    ISCC.exe /DMyAppVersion=1.5.0 installer\DraftLite.iss
;
;  Si aspetta di trovare l'eseguibile gia' pubblicato in publish\DraftLite.exe
;  (dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true).
; ---------------------------------------------------------------------------

; La versione arriva da chi compila (/DMyAppVersion=1.5.0, che e' quello che fa
; il workflow partendo dal tag o dal csproj). Se non la passa nessuno - build a
; mano - la si legge direttamente dall'eseguibile che stiamo impacchettando:
; cosi' l'installer non puo' dichiarare una versione diversa dal programma.
#ifndef MyAppVersion
  #define ExeToPack AddBackslash(SourcePath) + "..\publish\DraftLite.exe"
  #if FileExists(ExeToPack)
    #define MyAppVersion GetVersionNumbersString(ExeToPack)
  #else
    #define MyAppVersion "0.0.0"
  #endif
#endif

#define MyAppName "DraftLite"
#define MyAppPublisher "Tastiere Digitali srls"
#define MyAppURL "https://github.com/Giuseppe-TD/DraftLite"
#define MyAppExeName "DraftLite.exe"

[Setup]
; Questo GUID identifica l'applicazione: non va mai cambiato, altrimenti
; gli aggiornamenti si installano accanto alla versione vecchia invece di sostituirla.
AppId={{8F3C2B47-6D19-4E8A-9C42-D1A17E000001}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases

DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=no
AllowNoIcons=yes
PrivilegesRequired=admin
MinVersion=10.0

; x64compatible esiste dalla 6.3: con i compilatori piu' vecchi si usa la forma classica
#if Ver >= EncodeVer(6,3,0)
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#else
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
#endif

OutputDir=Output
OutputBaseFilename=DraftLite-Setup-{#MyAppVersion}
SetupIconFile=..\src\DraftLite\Resources\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} {#MyAppVersion}

Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=no
ChangesAssociations=yes
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"

[CustomMessages]
italian.AssocGroup=Associazioni file:
italian.AssocDlite=Apri i copioni .dlite con DraftLite
italian.AssocFountain=Apri anche i file .fountain con DraftLite
italian.AssocFdx=Apri anche i file .fdx (attenzione: li toglie a Final Draft)
italian.LaunchApp=Avvia DraftLite
italian.ShortcutGroup=Collegamenti:

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:ShortcutGroup}"
Name: "assocdlite";  Description: "{cm:AssocDlite}";  GroupDescription: "{cm:AssocGroup}"
Name: "assocfountain"; Description: "{cm:AssocFountain}"; GroupDescription: "{cm:AssocGroup}"; Flags: unchecked
Name: "assocfdx";    Description: "{cm:AssocFdx}";    GroupDescription: "{cm:AssocGroup}"; Flags: unchecked

[Files]
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "copione.ico";                DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md";               DestDir: "{app}"; DestName: "LEGGIMI.md"; Flags: ignoreversion
Source: "..\docs\esempio.fountain";   DestDir: "{app}\Esempi"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; --- l'applicazione si presenta al sistema (serve per "Apri con" e per le associazioni)
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\{#MyAppExeName}"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\{#MyAppExeName}"; ValueType: string; ValueName: "Path"; ValueData: "{app}"

Root: HKLM; Subkey: "Software\Classes\Applications\{#MyAppExeName}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".dlite"; ValueData: ""
Root: HKLM; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".fountain"; ValueData: ""
Root: HKLM; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".spmd"; ValueData: ""
Root: HKLM; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".fdx"; ValueData: ""

; --- copioni .dlite
Root: HKLM; Subkey: "Software\Classes\DraftLite.Screenplay"; ValueType: string; ValueName: ""; ValueData: "Copione DraftLite"; Flags: uninsdeletekey; Tasks: assocdlite
Root: HKLM; Subkey: "Software\Classes\DraftLite.Screenplay\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\copione.ico"; Tasks: assocdlite
Root: HKLM; Subkey: "Software\Classes\DraftLite.Screenplay\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: assocdlite
Root: HKLM; Subkey: "Software\Classes\.dlite"; ValueType: string; ValueName: ""; ValueData: "DraftLite.Screenplay"; Flags: uninsdeletevalue; Tasks: assocdlite
Root: HKLM; Subkey: "Software\Classes\.dlite\OpenWithProgids"; ValueType: string; ValueName: "DraftLite.Screenplay"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assocdlite

; --- Fountain (facoltativo)
Root: HKLM; Subkey: "Software\Classes\DraftLite.Fountain"; ValueType: string; ValueName: ""; ValueData: "Copione Fountain"; Flags: uninsdeletekey; Tasks: assocfountain
Root: HKLM; Subkey: "Software\Classes\DraftLite.Fountain\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\copione.ico"; Tasks: assocfountain
Root: HKLM; Subkey: "Software\Classes\DraftLite.Fountain\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: assocfountain
Root: HKLM; Subkey: "Software\Classes\.fountain"; ValueType: string; ValueName: ""; ValueData: "DraftLite.Fountain"; Flags: uninsdeletevalue; Tasks: assocfountain
Root: HKLM; Subkey: "Software\Classes\.fountain\OpenWithProgids"; ValueType: string; ValueName: "DraftLite.Fountain"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assocfountain
Root: HKLM; Subkey: "Software\Classes\.spmd"; ValueType: string; ValueName: ""; ValueData: "DraftLite.Fountain"; Flags: uninsdeletevalue; Tasks: assocfountain

; --- Final Draft (facoltativo, spento di default)
Root: HKLM; Subkey: "Software\Classes\DraftLite.Fdx"; ValueType: string; ValueName: ""; ValueData: "Copione Final Draft"; Flags: uninsdeletekey; Tasks: assocfdx
Root: HKLM; Subkey: "Software\Classes\DraftLite.Fdx\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\copione.ico"; Tasks: assocfdx
Root: HKLM; Subkey: "Software\Classes\DraftLite.Fdx\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: assocfdx
Root: HKLM; Subkey: "Software\Classes\.fdx"; ValueType: string; ValueName: ""; ValueData: "DraftLite.Fdx"; Flags: uninsdeletevalue; Tasks: assocfdx
Root: HKLM; Subkey: "Software\Classes\.fdx\OpenWithProgids"; ValueType: string; ValueName: "DraftLite.Fdx"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assocfdx

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[Code]
// Le preferenze e il salvataggio automatico stanno in %APPDATA%\DraftLite:
// alla disinstallazione si chiede se buttarli via o tenerli per la prossima volta.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\DraftLite');
    if DirExists(DataDir) then
    begin
      if MsgBox('Vuoi eliminare anche le preferenze e il salvataggio automatico di DraftLite?' + #13#10 +
                '(I tuoi copioni non vengono toccati.)', mbConfirmation, MB_YESNO) = IDYES then
        DelTree(DataDir, True, True, True);
    end;
  end;
end;
