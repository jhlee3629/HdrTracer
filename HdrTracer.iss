#define MyAppName "HdrTracer"
#define MyAppVersion "1.3.0"
#define MyAppPublisher "HaeDream"
#define MyAppExeName "HdrTracer.exe"

#define PublishDir "HdrTracer\bin\Release\net10.0-windows\win-x64\publish"

#define OutputDir "D:\VSCWorkSpace\HdrTracer_C#\Installer"

[Setup]
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
; AppId는 이 앱을 고유하게 식별하는 GUID. 업데이트 설치/제거 추적에 사용됨.
; 한 번 정하면 바꾸지 마세요. (아래는 이 앱 전용으로 고정)
AppId={{8B5F3A2C-7D14-4E9B-A6F1-2C3D4E5F6A7B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
OutputDir={#OutputDir}
OutputBaseFilename=HdrTracer_Setup_{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=D:\VSCWorkSpace\HdrTracer_C#\HdrTracer\Assets\sun.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplaySize=73400320
; [UninstallDelete]에서 {localappdata}(사용자별 캐시 폴더)를 의도적으로 사용함.
; 관리자 설치 모드에서 사용자 영역을 다룰 때 나오는 경고를 끈다.
; (단일 사용자 PC에서는 정확히 동작. 멀티유저 엣지 케이스에선 캐시가 남을 수
;  있으나 무해함 — 단순 데이터 파일이라 보안/동작 문제 없음)
UsedUserAreasWarning=no
MinVersion=10.0
ShowLanguageDialog=no
LanguageDetectionMethod=uilanguage

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "german";   MessagesFile: "compiler:Languages\German.isl"
Name: "spanish";  MessagesFile: "compiler:Languages\Spanish.isl"
Name: "french";   MessagesFile: "compiler:Languages\French.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "D:\VSCWorkSpace\HdrTracer_C#\HdrTracer\Assets\sun.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; 시작 메뉴 바로가기 (아이콘을 exe가 아닌 별도 .ico로 지정 — 재부팅 후 흰 종이 방지)
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\sun.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\sun.ico"; Tasks: desktopicon

[Run]
; 설치 완료 후 "지금 실행" 체크박스 제공.
; shellexec: exe를 직접 CreateProcess로 띄우지 않고 셸(ShellExecute)을 통해 실행한다.
;   → Windows가 앱의 manifest(requireAdministrator)를 읽고 UAC 권한 상승을 정상 처리.
;   (runasoriginaluser나 기본 실행은 manifest 권한 요구와 충돌해 코드 740 오류 발생)
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\{#MyAppName}"

[UninstallRun]
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""HdrTracer AutoStart"" /F"; Flags: runhidden; RunOnceId: "DelAutoStart"
