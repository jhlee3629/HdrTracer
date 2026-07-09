; ============================================================
;  HdrTracer 설치 스크립트 (Inno Setup 6)
;  - Program Files에 설치
;  - 시작 메뉴 + (선택) 바탕화면 바로가기
;  - 제어판 "프로그램 추가/제거"에서 제거 가능
; ============================================================

#define MyAppName "HdrTracer"
#define MyAppVersion "1.0.5"
#define MyAppPublisher "HdrTracer"
#define MyAppExeName "HdrTracer.exe"

; 게시(publish)된 exe가 있는 폴더
#define PublishDir "HdrTracer\bin\Release\net10.0-windows\win-x64\publish"

; 설치 프로그램(setup.exe)이 만들어질 폴더
#define OutputDir "D:\VSCWorkSpace\HdrTracer_C#\Installer"

[Setup]
; AppId는 이 앱을 고유하게 식별하는 GUID. 업데이트 설치/제거 추적에 사용됨.
; 한 번 정하면 바꾸지 마세요. (아래는 이 앱 전용으로 고정)
AppId={{8B5F3A2C-7D14-4E9B-A6F1-2C3D4E5F6A7B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
; 제어판 "프로그램 및 기능"에 표시될 이름: "HdrTracer 1.0.0"
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
; 64비트 전용 앱이므로 64비트 모드로 설치 (Program Files, 레지스트리 모두 64비트)
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; 설치 프로그램 자체도 관리자 권한 필요 (Program Files에 쓰려면)
PrivilegesRequired=admin
OutputDir={#OutputDir}
OutputBaseFilename=HdrTracer_Setup_{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; 설치 마법사에 표시될 아이콘
SetupIconFile=D:\VSCWorkSpace\HdrTracer_C#\HdrTracer\Assets\sun.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
; 제어판 "프로그램 및 기능"에 표시할 크기를 명시 (exe 크기 기준, 약 70MB).
; 지정하지 않으면 Windows가 폴더를 스캔해 계산하는데, 이 값으로 고정 표시된다.
UninstallDisplaySize=73400320
; [UninstallDelete]에서 {localappdata}(사용자별 캐시 폴더)를 의도적으로 사용함.
; 관리자 설치 모드에서 사용자 영역을 다룰 때 나오는 경고를 끈다.
; (단일 사용자 PC에서는 정확히 동작. 멀티유저 엣지 케이스에선 캐시가 남을 수
;  있으나 무해함 — 단순 데이터 파일이라 보안/동작 문제 없음)
UsedUserAreasWarning=no
; Windows 11만 지원하려면 최소 버전 지정 (10.0 = Win10/11)
MinVersion=10.0

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
; 바탕화면 바로가기는 선택 (체크박스로 사용자가 끌 수 있음)
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; 단일 self-contained exe 하나만 설치하면 됨
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
; 바로가기 아이콘용 .ico 파일도 함께 설치 (압축 exe의 아이콘 추출 실패 방지)
Source: "D:\VSCWorkSpace\HdrTracer_C#\HdrTracer\Assets\sun.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; 시작 메뉴 바로가기 (아이콘을 exe가 아닌 별도 .ico로 지정 — 재부팅 후 흰 종이 방지)
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\sun.ico"
; 시작 메뉴에 제거 바로가기
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
; 바탕화면 바로가기 (위 Task가 체크됐을 때만)
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\sun.ico"; Tasks: desktopicon

[Run]
; 설치 완료 후 "지금 실행" 체크박스 제공.
; shellexec: exe를 직접 CreateProcess로 띄우지 않고 셸(ShellExecute)을 통해 실행한다.
;   → Windows가 앱의 manifest(requireAdministrator)를 읽고 UAC 권한 상승을 정상 처리.
;   (runasoriginaluser나 기본 실행은 manifest 권한 요구와 충돌해 코드 740 오류 발생)
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallDelete]
; 제거 시 인덱스 캐시/설정 폴더도 함께 삭제 (찌꺼기 안 남기기)
; %LocalAppData%\HdrTracer 전체 (indexes\*.dat, settings.json 등)
Type: filesandordirs; Name: "{localappdata}\{#MyAppName}"
