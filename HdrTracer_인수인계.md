## 1. 프로젝트 개요

- **앱**: HdrTracer — NTFS 드라이브의 파일을 이름으로 즉시 찾는 Windows 검색 도구 (Everything 계열)
- **위치**: `D:\VSCWorkSpace\HdrTracer_C#`
- **저장소**: https://github.com/jhlee3629/HdrTracer (branch: `main`)
- **현재 버전**: v1.2.2 (릴리스 발행 완료)
- **구성**
  - `HdrTracer\` — WPF 앱 (.NET 10, 네임스페이스 `HdrTracer.App`)
  - `HdrTracer.Core\` — 검색 엔진·인덱스·설정·다국어 (`HdrTracer.Core`)
  - `HdrTracer.MftProbe\` — 진단용 도구
  - `HdrTracer.iss` — Inno Setup 설치 스크립트
- **동작 원리**: NTFS의 MFT를 직접 읽어 인덱스를 만들고, USN 저널로 변경을 실시간 반영.
  관리자 권한 필요(읽기 전용 사용). 인덱스·설정은 `%LocalAppData%\HdrTracer\`에만 저장.
- **원칙**: 네트워크 통신 없음(업데이트 확인 기능도 넣지 않음). 속도가 최우선 가치.

## 2. 작업 방식 (중요)

- 사용자가 Windows에서 빌드·테스트하고, 결과를 알려줌.
  - 빌드: `dotnet build HdrTracer.sln -c Debug` → "성공 빌드"
  - 실행: `dotnet run --project HdrTracer\HdrTracer.csproj -c Debug`
- **파일을 새로 만들어 주기 전에 반드시 현재 파일을 요청할 것.**
  (예전에 옛 사본 기준으로 파일을 만들어 기능이 사라지는 회귀가 여러 번 발생함)
- 사용자는 XAML/코드의 정밀한 손편집을 어려워하므로, 수정은 **완성된 파일 전체**로 전달.
- 파일 생성 후에는 다음을 검증하고 결과를 보고할 것:
  - C#: 중괄호 개수 균형
  - XAML: XML 파싱, **Grid 행/열 정의 수 > 사용된 최대 인덱스**,
    이벤트 핸들러가 코드에 존재하는지, `x:Name` 필수 컨트롤 존재 여부,
    최근 기능 흔적(툴팁·히스토리 템플릿·SelectionChanged 등) 유지 여부
- **WPF/WinForms 이름 충돌 주의**: 이 프로젝트는 둘 다 참조하므로
  `Orientation`, `MouseEventArgs`, `DataObject`, `DataFormats`, `DragDropEffects`,
  `SelectionMode`, `ScrollBar`, `Thumb`, `Canvas`, `Mouse`, `VisualTreeHelper` 등은
  **처음부터 전체 이름으로** 작성할 것.
- 작동하는 코드는 분석으로 문제가 증명될 때만 수정.

## 3. 릴리스 절차

1. 버전을 **두 곳**에 동일하게: `HdrTracer.iss` 9행 `#define MyAppVersion`,
   `HdrTracer\HdrTracer.csproj`의 `Version` / `AssemblyVersion` / `FileVersion`
2. `dotnet build HdrTracer.sln -c Debug`
3. `dotnet publish HdrTracer\HdrTracer.csproj -c Release -r win-x64 --self-contained true`
4. `& "D:\다운로드(E)\InnoSetup\Inno Setup 6\ISCC.exe" HdrTracer.iss`
   → `Installer\HdrTracer_Setup_X.Y.Z.exe`
5. 설치 후 `앱_확인_체크리스트.txt`로 확인
6. `git add -A` → commit → `git push origin main`
7. GitHub → Draft a new release → 태그 `vX.Y.Z`(Create new tag on publish, Target main)
   → 노트 작성 → exe 첨부 → **Publish release**

## 4. 구현된 기능 (요약)

**검색**: 이름·확장자(`*.jpg`), 제외(`-단어`, `-*.ext`), 폴더 한정(`D:\백업\`, 따옴표 지원),
크기(`>100MB`), 날짜(`>2026-01`, `>week`), 이름 와일드카드(`IMG_*_편집.txt`, `?`),
폴더만/파일만(`folder:` / `file:`), 검색 도움말 창(2열 예시)

**결과**: 정렬(상태 기억), 컬럼 너비 비율 유지, 잘린 이름·경로 툴팁, 선택 요약(개수·총 크기,
2만 개 초과 시 개수만), CSV 내보내기(전체/선택), 탐색기로 드래그 앤 드롭,
우클릭(열기·관리자 실행·다른 프로그램·폴더에서 보기·이 폴더에서만 검색·경로/이름 복사·
휴지통 삭제·이름 바꾸기), 삭제 시 시스템 경로 ⚠ 경고(Windows/Program Files/ProgramData 등)

**UI/UX**: 다크 테마 커스텀 창, 커스텀 스크롤바(비례 손잡이·드래그·트랙 페이지 이동·
누르고 유지·▲▼, 결과 목록과 히스토리 팝업 공통), 검색창 placeholder, 0건 힌트,
Esc 3단계(선택 해제 → 검색어 지움 → 트레이), 인덱싱 진행 표시(경과 초·드라이브별 상태)

**편의**: 검색 히스토리 + 📌 고정 검색(Ctrl+1~9), 트레이 상주(우클릭 메뉴에 고정 검색·설정),
전역 단축키 Win+Alt+S(항상 소환, 숨기기는 Esc), Windows 시작 시 자동 실행(작업 스케줄러,
UAC 없음), 마지막 검색어 복원(옵션), 창 위치·크기·컬럼·정렬 기억

**설정**: 이동식 드라이브 인덱싱 / 닫기 시 트레이 / 숨김·시스템 항목 표시 / 자동 실행 /
전역 단축키 / 마지막 검색어 복원 / 검색 제외 폴더 이름(세미콜론 구분)

**다국어**: 7개 언어(ko, en, zh-Hans, ja, es, de, fr). `Localization.{코드}.cs` 파일 분리 +
영어 폴백. 최초 실행 시 Windows 표시 언어 자동 감지. 설치 프로그램도 언어 자동 선택.

## 5. 의도적으로 넣지 않은 것

- 업데이트 자동 확인 (네트워크 없음 원칙과 충돌)
- 파일 내용 검색, 네트워크 드라이브 (정체성·성능 문제)
- 결과 폴더 그룹핑, 중복 파일 찾기 (목록 구조 변경 → 속도 위험, 사용자 보류 결정)
- 정규식 검색 (수요 미확인), 라이트 테마 (효용 대비 작업량)
- 결과 목록의 "형식" 컬럼 (이름에 확장자가 이미 보이므로 중복)

## 6. 보류·후보

- **파일 형식 아이콘** (탐색기처럼 확장자별 아이콘) — 사용자 보류 중.
  구현 시 확장자 단위 캐시 + 가상화 유지가 관건
- 명령줄 인자로 검색 실행 — 소수 사용자용, 보류
- 블로그(Blogger)로 일반 사용자용 다운로드 안내 페이지 — 진행 중.
  설치 파일은 GitHub 릴리스에 두고 블로그는 안내·링크만

## 7. 알려진 제약

- 코드 서명이 없어 SmartScreen 경고와 UAC의 "게시자: 알 수 없음"은 피할 수 없음
- 대량 선택(2,000개 초과)은 다른 앱으로 전환할 때 자동 해제됨 (WPF 성능 한계 우회)
- 선택 2만 개 초과 시 총 크기 합산 생략(개수만 표시)
- 관리자 권한으로 실행되므로 일부 환경에서 탐색기로의 드래그 앤 드롭이 제한될 수 있음
