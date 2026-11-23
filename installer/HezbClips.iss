; Inno Setup script for Hezb Clips. Build with installer\build.ps1 (publishes the app first).
; ffmpeg is not bundled (about 100 MB, GPL build): the installer downloads a pinned release and checks its SHA-256.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "Hezb Clips"
#define FfmpegVersion "8.1.1"
#define FfmpegUrl "https://github.com/GyanD/codexffmpeg/releases/download/8.1.1/ffmpeg-8.1.1-essentials_build.zip"
#define FfmpegSha256 "6f58ce889f59c311410f7d2b18895b33c03456463486f3b1ebc93d97a0f54541"
#define FfmpegDir "ffmpeg-8.1.1-essentials_build"
; OCR language data (Apache-2.0, tessdata_fast tag 4.1.0), about 4.6 MB; same files and hashes the app downloads by itself on first use
#define OcrBase "https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/4.1.0/"
#define VieSha256 "79df64caf7bcfb2a27df5042ecb6121e196eada34da774956995747636d5bfa1"
#define EngSha256 "7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2"

[Setup]
AppId={{B7A4C1E2-5D3F-4F7A-9C61-2E8D0A4B7F10}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Hezb
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\HezbClips.exe
SetupIconFile=..\src\HezbClips.App\Assets\app-light.ico
OutputDir=..\artifacts\installer
OutputBaseFilename=HezbClips-Setup-{#AppVersion}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\LICENSE
; the app holds this mutex while running: setup offers to close it before updating files
AppMutex=HezbClips.SingleInstance
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "ffmpeg"; Description: "Download ffmpeg {#FfmpegVersion} (needed for video recording, GIF and upscaling; about 110 MB)"; GroupDescription: "Components:"
Name: "ocrdata"; Description: "Download OCR data for Vietnamese and English text (Copy text, hide sensitive data; about 5 MB)"; GroupDescription: "Components:"
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\HezbClips.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\HezbClips.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\HezbClips.exe"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\tools"

[Code]
procedure InstallFfmpeg;
var
  zip, dest: String;
  code: Integer;
  ok: Boolean;
begin
  dest := ExpandConstant('{app}\tools');
  if FileExists(dest + '\ffmpeg.exe') then exit; // update: keep what is already there
  ok := False;
  WizardForm.StatusLabel.Caption := 'Downloading ffmpeg...';
  WizardForm.FileNameLabel.Caption := '';
  try
    DownloadTemporaryFile('{#FfmpegUrl}', 'ffmpeg.zip', '{#FfmpegSha256}', nil);
    zip := ExpandConstant('{tmp}\ffmpeg.zip');
    WizardForm.StatusLabel.Caption := 'Extracting ffmpeg...';
    ForceDirectories(dest);
    // tar.exe ships with Windows 10 and reads zip files
    if Exec(ExpandConstant('{sys}\tar.exe'),
        '-xf "' + zip + '" -C "' + ExpandConstant('{tmp}') + '" "{#FfmpegDir}/bin/ffmpeg.exe" "{#FfmpegDir}/bin/ffprobe.exe" "{#FfmpegDir}/LICENSE"',
        '', SW_HIDE, ewWaitUntilTerminated, code) and (code = 0) then
    begin
      CopyFile(ExpandConstant('{tmp}\{#FfmpegDir}\bin\ffmpeg.exe'), dest + '\ffmpeg.exe', False);
      CopyFile(ExpandConstant('{tmp}\{#FfmpegDir}\bin\ffprobe.exe'), dest + '\ffprobe.exe', False);
      CopyFile(ExpandConstant('{tmp}\{#FfmpegDir}\LICENSE'), dest + '\ffmpeg-LICENSE.txt', False);
      ok := FileExists(dest + '\ffmpeg.exe');
    end;
  except
    Log('ffmpeg download failed: ' + GetExceptionMessage);
  end;
  if not ok and not WizardSilent then
    SuppressibleMsgBox('ffmpeg could not be downloaded or verified, so video recording is disabled for now.' + #13#10 +
      'Put ffmpeg.exe in:' + #13#10 + dest + #13#10 + 'or run setup again with a working internet connection.',
      mbInformation, MB_OK, IDOK);
end;

procedure InstallOcrData;
var
  dest: String;
  ok: Boolean;
begin
  dest := ExpandConstant('{app}\tools\tessdata');
  if FileExists(dest + '\vie.traineddata') and FileExists(dest + '\eng.traineddata') then exit; // update: keep what is already there
  ok := False;
  WizardForm.StatusLabel.Caption := 'Downloading OCR data...';
  WizardForm.FileNameLabel.Caption := '';
  try
    DownloadTemporaryFile('{#OcrBase}vie.traineddata', 'vie.traineddata', '{#VieSha256}', nil);
    DownloadTemporaryFile('{#OcrBase}eng.traineddata', 'eng.traineddata', '{#EngSha256}', nil);
    ForceDirectories(dest);
    CopyFile(ExpandConstant('{tmp}\vie.traineddata'), dest + '\vie.traineddata', False);
    CopyFile(ExpandConstant('{tmp}\eng.traineddata'), dest + '\eng.traineddata', False);
    ok := FileExists(dest + '\vie.traineddata') and FileExists(dest + '\eng.traineddata');
  except
    Log('OCR data download failed: ' + GetExceptionMessage);
  end;
  if not ok and not WizardSilent then
    SuppressibleMsgBox('The OCR data could not be downloaded or verified. Hezb Clips will download it the first time you use Copy text.',
      mbInformation, MB_OK, IDOK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('ffmpeg') then InstallFfmpeg;
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('ocrdata') then InstallOcrData;
end;
