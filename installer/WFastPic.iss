; Setup di WFastPic (Inno Setup 6). Non si compila a mano: lo fa tools\release.ps1, che passa
; versione e cartella del publish:  ISCC /DVersione=0.9.2 /DCartellaPublish=... /O<uscita> WFastPic.iss
;
; Installazione per il solo utente, senza permessi di amministratore (in %LocalAppData%\Programs\WFastPic),
; con la possibilità di sceglierla per tutti gli utenti. L'app è autonoma (.NET e Windows App SDK inclusi):
; nessun prerequisito. La disinstallazione toglie solo il programma: impostazioni, persone e modelli in
; %LocalAppData%\WFastPic e i sidecar .wfastpic accanto alle foto restano.

#ifndef Versione
  #error Manca /DVersione (usa tools\release.ps1)
#endif
#ifndef CartellaPublish
  #error Manca /DCartellaPublish (usa tools\release.ps1)
#endif

#define NomeApp "WFastPic"
#define ExeApp "WFastPic.App.exe"

[Setup]
; Non cambiare mai AppId: è quello che fa riconoscere a Windows gli aggiornamenti della stessa app.
AppId={{D08F5AB4-6917-4C06-B82F-421B41B73F2E}
AppName={#NomeApp}
AppVersion={#Versione}
AppVerName={#NomeApp} {#Versione}
AppPublisher=Walt7
AppPublisherURL=https://github.com/Walt7/WFastPic
AppSupportURL=https://github.com/Walt7/WFastPic/issues
AppUpdatesURL=https://github.com/Walt7/WFastPic/releases
VersionInfoVersion={#Versione}
DefaultDirName={autopf}\{#NomeApp}
DefaultGroupName={#NomeApp}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
SetupIconFile=..\assets\icon.ico
UninstallDisplayIcon={app}\{#ExeApp}
UninstallDisplayName={#NomeApp}
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
; Aggiornando con l'app aperta, la chiude (a fine installazione si può riaprire dall'ultima pagina).
CloseApplications=yes
RestartApplications=no
; Stesso nome dello zip delle release (WFastPic-0.9.2-win-x64.zip), con -setup.
OutputBaseFilename=WFastPic-{#Versione}-win-x64-setup

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#CartellaPublish}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Licenza MIT accanto al programma: non serve una pagina da "accettare", basta che ci sia.
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"

[Icons]
Name: "{autoprograms}\{#NomeApp}"; Filename: "{app}\{#ExeApp}"
Name: "{autodesktop}\{#NomeApp}"; Filename: "{app}\{#ExeApp}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#ExeApp}"; Description: "{cm:LaunchProgram,{#NomeApp}}"; Flags: nowait postinstall skipifsilent
