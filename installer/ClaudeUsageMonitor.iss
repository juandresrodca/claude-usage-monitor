; ============================================================================
;  Claude Usage Monitor — Inno Setup script
;  Produces a per-user installer (no admin required).
;  Invoked by build/build-installer.ps1 which passes:
;    /DAppVersion=<x.y.z>
;    /DPublishDir=<path with the published self-contained EXE>
;    /DOutputDir=<where to drop the Setup.exe>
; ============================================================================

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\dist\ClaudeUsageMonitor-" + AppVersion + "-win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define AppName        "Claude Usage Monitor"
#define AppPublisher   "juandresrodca"
#define AppExeName     "ClaudeUsageMonitor.exe"
#define AppURL         "https://github.com/juandresrodca/claude-usage-monitor"
#define AppId          "{{B5C7E1D2-8F3A-4F1E-9C5A-7E2B4A0D6F11}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
VersionInfoVersion={#AppVersion}

; Per-user install — does not need admin, lands under %LOCALAPPDATA%\Programs
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
DisableProgramGroupPage=yes
DisableReadyPage=no
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes

OutputDir={#OutputDir}
OutputBaseFilename=ClaudeUsageMonitor-{#AppVersion}-Setup
SetupIconFile=..\Assets\app.ico

; Allow multi-language UI — fallback English if user's locale isn't supported
ShowLanguageDialog=auto

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon";  Description: "{cm:CreateDesktopIcon}";                       GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon";  Description: "Start Claude Usage Monitor when Windows starts"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Copy the entire publish output — the self-contained EXE plus WebView2Loader.dll
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}";       Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: startupicon

[Run]
; Launch the app on the final wizard page (user choice)
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
// ---------------------------------------------------------------------------
//  WebView2 runtime check
//  The app embeds claude.ai inside a WebView2 control. Edge's Evergreen
//  WebView2 runtime ships pre-installed on Windows 11 and current Windows 10
//  builds, but older systems may be missing it. We check the well-known
//  registry keys (per-machine + per-user, x64 + x86) and prompt the user to
//  download the bootstrapper if absent.
// ---------------------------------------------------------------------------

function IsWebView2Installed(): Boolean;
var
  Value: String;
begin
  Result :=
    (RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Value) and (Value <> '') and (Value <> '0.0.0.0')) or
    (RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Value) and (Value <> '') and (Value <> '0.0.0.0')) or
    (RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Value) and (Value <> '') and (Value <> '0.0.0.0'));
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then begin
    if not IsWebView2Installed() then begin
      if MsgBox('Claude Usage Monitor needs the Microsoft Edge WebView2 runtime ' +
                'to embed claude.ai for sign-in. It is not installed on this PC.' + #13#10 + #13#10 +
                'Open the official Microsoft download page now?',
                mbConfirmation, MB_YESNO) = IDYES then begin
        ShellExec('open',
                  'https://developer.microsoft.com/microsoft-edge/webview2/',
                  '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
      end;
    end;
  end;
end;
