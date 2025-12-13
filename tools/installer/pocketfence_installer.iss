; Inno Setup script to create PocketFence Windows installer
[Setup]
AppName=PocketFence
AppVersion=1.0.0
DefaultDirName={pf64}\PocketFence
DefaultGroupName=PocketFence
OutputBaseFilename=PocketFence-Setup
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Copy the release folder contents
Source: "{#ReleaseDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs; Excludes: "*.pdb"
; Include exported signer certificate so installer can trust the app automatically
Source: "{#SignerPath}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\PocketFence"; Filename: "{app}\pocketfence.exe"
Name: "{group}\Uninstall PocketFence"; Filename: "{uninstallexe}"

[Run]
; Install signer certificate into LocalMachine Trusted Root (requires admin)
Filename: "{sys}\cmd.exe"; Parameters: "/C certutil -addstore -f Root ""{app}\\signer.cer"""; Flags: runhidden shellexec waituntilterminated; StatusMsg: "Installing trusted certificate..."; Check: InstallCertChecked

Filename: "{app}\pocketfence.exe"; Description: "Launch PocketFence"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{app}\signer.cer"

[Code]
var
	CertOptPage: TInputOptionWizardPage;

procedure InitializeWizard();
begin
	CertOptPage := CreateInputOptionPage(wpSelectDir,
		'Install Trusted Certificate',
		'Trust PocketFence on this machine',
		'Installing the signer certificate into the Trusted Root Certification Authorities store will allow Windows to trust the application. Only enable this if you trust the source of this installer.',
		True, False);
	CertOptPage.Add('Install signer certificate into Trusted Root (recommended for local testing)');
	CertOptPage.Values[0] := False; // default: opt-out
end;

function InstallCertChecked(): Boolean;
begin
	Result := CertOptPage.Values[0];
end;

begin
end.

; ReleaseDir is substituted by the build script before calling ISCC
