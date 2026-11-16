; Accessible Trader, the Windows installer. Built by packaging/windows/package.ps1 with Inno Setup 6
; (preinstalled on GitHub's Windows runners), from the same staged folder the portable zip is made
; from:
;
;     Accessible Trader.exe   the launcher (AccessibleTrader.Launcher)
;     README.txt
;     app\                    the published desktop app
;
; Per-user: it installs to %LocalAppData%\Programs\Accessible Trader and never asks for admin.
; The app is framework-dependent (SelfContained=false in the BlazorClient csproj): it needs
; Microsoft's .NET 10 Desktop Runtime, which installs machine-wide and needs admin, so this setup
; does not carry it. It checks for it and offers the download page instead ([Code] below).
; The app writes nothing into its install folder (settings, workspaces and logs are under
; %LocalAppData%\AccessibleTrader), so installing, upgrading and uninstalling leave them alone.
;
; Passed in by package.ps1:
;   AppVersion      the full version, e.g. 2.13.1 or 2.13.1-rc1
;   NumericVersion  the same without a pre-release suffix (Windows' file version is numbers only)
;   SourceDir       the staged folder above
;   OutputBaseName  AccessibleTrader-<version>-Windows-Setup
;   SetupIcon       optional, the app's .ico

#ifndef AppVersion
  #error Pass /DAppVersion=<version>
#endif
#ifndef NumericVersion
  #define NumericVersion AppVersion
#endif
#ifndef SourceDir
  #error Pass /DSourceDir=<staged folder>
#endif
#ifndef OutputBaseName
  #define OutputBaseName "AccessibleTrader-" + AppVersion + "-Windows-Setup"
#endif

#define AppExe "app\AccessibleTrader.BlazorClient.exe"

[Setup]
; Never change AppId: it is how a newer setup finds the older install to replace.
AppId={{F054B804-E08E-4B45-8974-8BB9F73F101E}
AppName=Accessible Trader
AppVersion={#AppVersion}
AppVerName=Accessible Trader {#AppVersion}
AppPublisher=Cody Hurst
AppPublisherURL=https://accessibletrader.com
AppSupportURL=https://github.com/churst90/accessible-trade-terminal/issues
AppUpdatesURL=https://accessibletrader.com/download.php
VersionInfoVersion={#NumericVersion}
VersionInfoProductName=Accessible Trader
VersionInfoDescription=Accessible Trader setup
PrivilegesRequired=lowest
DefaultDirName={autopf}\Accessible Trader
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 1809, the app's own minimum (TargetPlatformMinVersion in the BlazorClient csproj).
MinVersion=10.0.17763
OutputBaseFilename={#OutputBaseName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=Accessible Trader
UninstallDisplayIcon={app}\{#AppExe}
SetupLogging=yes
; A running copy is closed (with the user's say) before its files are replaced.
CloseApplications=yes
#ifdef SetupIcon
SetupIconFile={#SetupIcon}
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; An upgrade starts from an empty app folder, so no file of the older version is left beside the
; new one. A leftover plugin DLL would be refused by the plugin trust check at every start.
Type: filesandordirs; Name: "{app}\app"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Accessible Trader"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}\app"
Name: "{autodesktop}\Accessible Trader"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}\app"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; WorkingDir: "{app}\app"; Description: "{cm:LaunchProgram,Accessible Trader}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Files the app made in its own folder after install (none expected; the Strategies folder is
; created empty at start-up). Your settings under %LocalAppData%\AccessibleTrader stay.
Type: filesandordirs; Name: "{app}\app"

[Code]
// The runtime's own installer puts each version in a folder of this name. A false "missing" only
// costs a message; the app's exe would say the same when started.
function DesktopRuntimeInstalled(): Boolean;
var
  F: TFindRec;
begin
  Result := FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), F);
  if Result then
    FindClose(F);
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  // Silent installs (/SUPPRESSMSGBOXES) take No: nothing opens on its own.
  if not DesktopRuntimeInstalled() then
    if SuppressibleMsgBox(
         'Accessible Trader needs Microsoft''s .NET 10 Desktop Runtime for Windows x64, and it is not '
         + 'installed on this computer.' + #13#10#13#10
         + 'Setup will go on and install Accessible Trader, but it will not start until the runtime is '
         + 'installed.' + #13#10#13#10
         + 'Open Microsoft''s download page now? On it, choose .NET Desktop Runtime 10, Windows, x64.',
         mbConfirmation, MB_YESNO, IDNO) = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/10.0', '', '', SW_SHOWNORMAL,
        ewNoWait, ErrorCode);
end;
