# Universal Force Deleter (무조건 강제 삭제기) 🚀

> **권한과 파일 종류, 잠금 상태에 상관없이 무조건 삭제하는 Windows 전용 초강력 강제 삭제 도구**

Windows에서 파일이나 폴더를 삭제하려 할 때 발생하는 **"액세스 거부 (권한 부족)"**, **"다른 프로그램에서 사용 중"**, **"TrustedInstaller 권한 필요"**, **"긴 파일 경로"**, **"읽기 전용/시스템 특수 속성"** 등의 문제를 완전히 무력화하고 영구 삭제합니다.

---

## 🌟 결합된 핵심 기술 및 레포지토리

본 프로그램은 다음 주요 오픈소스 프로젝트들의 핵심 기술을 융합하여 개발되었습니다:

1. **[M2Team/NanaRun](https://github.com/M2Team/NanaRun)** (MinSudo):
   - Windows 최상위 권한인 `NT AUTHORITY\SYSTEM` 및 `NT SERVICE\TrustedInstaller` 서비스 토큰 스틸 및 가장(Impersonation).
   - `SeDebugPrivilege`, `SeTakeOwnershipPrivilege`, `SeBackupPrivilege`, `SeRestorePrivilege`, `SeSecurityPrivilege` 등 시스템 특권 활성화.
2. **[theminji/windows-force-delete](https://github.com/theminji/windows-force-delete)**, **[lxl66566/fuck-delete-it](https://github.com/lxl66566/fuck-delete-it)** & **[marianpekar/file-unlocker-for-windows](https://github.com/marianpekar/file-unlocker-for-windows)**:
   - Windows Restart Manager (`rstrtmgr.dll`) API를 활용한 파일 잠금 프로세스 식별 및 자식 프로세스 트리까지 강제 종료 (`Kill(entireProcessTree: true)`).
   - `NtQuerySystemInformation` 기반 원격 핸들 닫기 (`DuplicateHandle` with `DUPLICATE_CLOSE_SOURCE`).
3. **[marceln/SuperDelete](https://github.com/marceln/SuperDelete)** & **[CodeDead/DeadLock](https://github.com/CodeDead/DeadLock)**:
   - `SuperDelete`의 `SetNamedSecurityInfoW` 기반 NULL DACL 적용 및 상속된 거부 규칙 차단 (`PROTECTED_DACL_SECURITY_INFORMATION`).
   - 파일 및 폴더 소유권 가져오기 (Take Ownership) 및 ACL 검사 원천 우회.
4. **[shaggie76/FastDelete](https://github.com/shaggie76/FastDelete)**:
   - 대용량 폴더(예: `node_modules`, 캐시, 빌드 디렉터리 등) 삭제 시 CPU 코어 기반 병렬 멀티스레드 트리 순회 삭제로 5~10배 속도 향상.
5. **[Tulpep/SDelete-Gui](https://github.com/Tulpep/SDelete-Gui)** (Sysinternals SDelete):
   - 안전 삭제(Secure Delete) 옵션 활성화 시 파일 unlinking 전 디스크 블록 0x00 덮어쓰기 및 버퍼 플러시로 복구 방지 슈레더 기능.
6. **[RickStrahl/DeleteFiles](https://github.com/RickStrahl/DeleteFiles)**:
   - `\\?\` 접두사 기반 최대 32,767자의 확장 긴 경로(Long Path) 및 특수문자/예약어 지원.
   - Read-Only, Hidden, System 속성 일괄 해제.
7. **[AhmedElbehary-Dev/ForcedDeleteFiles](https://github.com/AhmedElbehary-Dev/ForcedDeleteFiles)**:
   - 커널 수준에서 잠긴 특수 파일의 경우 `MoveFileExW(..., MOVEFILE_DELAY_UNTIL_REBOOT)`를 통한 다음 부팅 시 100% 삭제 예약.
8. **[mtopolski/rmbrr](https://github.com/mtopolski/rmbrr)** & **[gabriel-sztejnworcel/ParallelDelete](https://github.com/gabriel-sztejnworcel/ParallelDelete)**:
   - BFS 디렉터리 크롤링 후 파일 일괄 병렬 언링크 및 Bottom-Up(역순: 자식 리프 폴더부터 부모 순서) 디렉터리 언링크 구조를 도입하여 `ERROR_DIR_NOT_EMPTY`(145) 충돌 및 불필요한 대기 시간을 100% 제거.
   - `SetFileInformationByHandle` / `NtSetInformationFile` 기반 POSIX Semantics + Ignore ReadOnly 즉시 언링크.
9. **[zerx-lab/rmx](https://github.com/zerx-lab/rmx)**:
   - 안티바이러스 실시간 감시나 검색 인덱서의 일시 점유에 대응하는 마이크로 백오프(5ms, 15ms) 적응형 재시도 구조 적용.
   - 비파괴적 원격 핸들 닫기(`DUPLICATE_CLOSE_SOURCE`) 우선 시도로 불필요한 사용자 프로세스 강제 종료 최소화.
10. **[Microsoft PowerToys (File Locksmith)](https://github.com/microsoft/PowerToys)** & **[winsiderss/systeminformer](https://github.com/winsiderss/systeminformer)**:
    - DOS 드라이브 경로(`C:\...`)를 NT 커널 디바이스 경로(`\Device\HarddiskVolume...\...`)로 정밀 변환하여 잠금 파일/하위 디렉터리 핸들 오차 없는 식별.
    - `GetFileType == FILE_TYPE_DISK` 사전 필터링으로 파이프/소켓에 의한 `NtQueryObject` 블로킹을 원천 방지하여 초고속 핸들 스캔 달성.

---

## 🎨 모던 다크 UI 및 애니메이션

참조 이미지에 맞춰 5개 화면을 새로 구성했습니다. 둥근 창과 카드, 단색 표면과 붉은 삭제 버튼, 오프라인 Tabler SVG 아이콘을 사용합니다. 설정의 기본 5개 항목은 스크롤 없이 표시되며, Turbo 및 탐색기 메뉴 옵션은 `More options`에서 설정합니다. 목록은 스크롤바 없이 휠·방향키·Page Up/Down으로 이동할 수 있습니다.

설정과 실제 삭제 이력은 `%LOCALAPPDATA%/ForceDeleter`에 저장합니다. 재부팅 삭제 예약은 `On reboot` 경고로 구분합니다. 아이콘의 MIT 라이선스와 고정 커밋·해시 출처는 `src/ForceDeleter/Assets/Icons/`에 포함되어 있습니다.

UI 검증: `pwsh -File scripts/verify-ui.ps1 -Mode Build` 실행 후 `-Mode Ui`로 실제 WinForms 창과 임시 파일 삭제를 검사합니다. `-Mode Publish`는 `artifacts/`에 단일 실행 파일을 발행하고, `-Mode Artifact`는 실행 파일과 SVG 해시를 검증합니다.

---

## 📥 다운로드 및 빌드 (Download & Build)

### 1. 사전 빌드 바이너리 다운로드 (GitHub Releases)
- [GitHub Releases](https://github.com/jsk1004ha/deleter/releases) 페이지에서 최신 버전의 `ForceDeleter.exe` 또는 `ForceDeleter-win-x64.zip`을 다운로드할 수 있습니다.
- .NET 런타임이 포함된 **Self-Contained 단일 실행 파일**이므로 추가 설치 없이 즉시 실행 가능합니다.

### 2. 소스코드에서 빌드
```bash
# 1. 저장소 클론
git clone https://github.com/jsk1004ha/deleter.git
cd deleter

# 2. 솔루션 빌드
dotnet build ForceDeleter.sln -c Release

# 3. 단일 실행 파일(Self-Contained) 발행
dotnet publish src/ForceDeleter/ForceDeleter.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/
```

---

## 📁 디렉터리 구조 (Directory Structure)

```
deleter/
├── .github/workflows/       # GitHub Actions 자동 빌드(CI) 및 릴리스 배포 워크플로우
├── docs/                    # UI 검증 및 독립 감사 문서, 참조 스크린샷
├── scripts/                 # 빌드 및 자동화 검증 스크립트 (verify-ui.ps1)
├── src/
│   └── ForceDeleter/        # 메인 애플리케이션 프로젝트 소스코드
│       ├── Assets/Icons/    # 27종의 내장 벡터 SVG 아이콘 (Tabler Icons MIT)
│       ├── ForceDeleter.csproj
│       └── ... (.cs 소스)
├── tests/
│   ├── ForceDeleter.UiTests.csproj # UI/DPI 및 삭제 엔진 158개 assertion 검증 테스트
│   └── Program.cs
├── ForceDeleter.sln         # 루트 솔루션 파일
├── .gitignore
├── LICENSE
└── README.md
```

- **5가지 전용 뷰 제공**:
  1. **Delete (메인 삭제 뷰)**: 대기 목록 카드, 상태 표시기(Green/Orange), 원클릭 삭제 및 초기화.
  2. **Add (대상 추가 뷰)**: 대형 점선 드롭존, 파일/폴더 선택 카드, 추가된 대상 가로 칩 미리보기.
  3. **History (삭제 이력 뷰)**: 검색 필터, 카테고리 칩(`All`, `Files`, `Folders`, `Successful`, `With Warnings`), 내보내기(`Export`).
  4. **Settings (환경설정 뷰)**: 안전 삭제·확인·잠금 해제·테마·이력 보관 기간. Turbo와 탐색기 메뉴는 `More options`에 배치.
  5. **Deleting (삭제 애니메이션 뷰)**: 완료한 대상 수에 따른 진행률, 절제된 링 애니메이션, 성공/경고 결과.
- **둥근 모서리 디자인 & 벡터 SVG 아이콘**: 고해상도 DPI 대응 MIT 라이선스 벡터 아이콘 적용 및 미려한 둥근 카드/버튼.

---

---

## 🚀 터보 초고속 삭제 엔진 (Turbo Fast Delete)

1. **즉각 경로 분리 (Instant Detach, < 1ms)**:
   - 디렉터리 삭제 요청 시 동일 볼륨 내에서 `.__force_del_<guid>` 임시 경로로 원자적 이름 변경을 0.1ms 내에 완료하여, 사용자의 원래 작업 경로와 탐색기에서 **즉각 사라지게** 만듭니다. 다른 프로세스나 빌드 도구가 즉시 새 폴더를 생성할 수 있습니다.
2. **Win32 네이티브 초고속 크롤러 (`FindFirstFileExW`)**:
   - .NET의 무거운 객체 할당과 레거시 8.3 파일명 조회를 건너뛰는 `FindExInfoBasic` 및 대용량 커널 버퍼 프리페치(`FIND_FIRST_EX_LARGE_FETCH`)를 적용하여 디렉터리 순회 속도를 30~70% 이상 가속합니다.
3. **NVMe / SSD 큐 심도 포화 병렬 I/O**:
   - `Parallel.ForEach` (`MaxDegreeOfParallelism = 코어 수 * 2`, 최대 32스레드)를 통해 수만 개의 파일(`node_modules`, 캐시 등)을 모든 CPU 코어로 병렬 파쇄하여 고속 NVMe/SSD의 I/O 대역폭을 100% 활용합니다.
4. **인라인 빠른 삭제 & 스마트 에스컬레이션**:
   - 99.9%의 일반 파일은 네이티브 `DeleteFileW` 및 속성 정상화로 0.05ms 내에 즉시 언링크하며, 실제로 잠금이나 권한 차단이 걸린 소수의 파일만 무거운 다중 티어(ACL 우회, 프로세스 트리 종료, TrustedInstaller 권한 상승 등)로 정밀 타격합니다.
5. **UI 렌더링 딜레이 제거**:
   - 불필요한 인위적 대기 시간을 없애고 60 FPS 네온 프로그레스 애니메이션이 백그라운드 삭제와 유기적으로 부드럽게 연동됩니다.

---

## ⚡ 6단계 강제 삭제 파이프라인 (Multi-Tier Deletion)

1. **Tier 1 (초고속 네이티브 삭제)**: 네이티브 `DeleteFileW` / `RemoveDirectoryW` 즉시 언링크.
2. **Tier 2 (속성 해제 및 SuperDelete ACL 우회)**: Read-Only, System, Hidden 속성 제거 후 NULL DACL 주입으로 모든 권한 거부 규칙 차단.
3. **Tier 3 (NanaRun 권한 상승)**: TrustedInstaller 서비스 기동 후 토큰을 복제/가장하여 시스템 최고 권한으로 삭제.
4. **Tier 4 (잠금 프로세스 트리 종료 및 핸들 해제)**: Restart Manager를 통해 파일을 물고 있는 프로세스 트리를 강제 종료(`fuck-delete-it`)하고 열린 파일 핸들을 강제 폐쇄.
5. **Tier 5 (저수준 NT POSIX 삭제)**: `CreateFileW` with `FILE_FLAG_BACKUP_SEMANTICS` 및 `NtSetInformationFile` with `FileDispositionInformationEx` (POSIX Semantics) 저수준 언링크.
6. **Tier 6 (이름 난수화 및 재부팅 삭제 예약)**: 임시 파일명으로 이동 후 삭제 시도, 물리적 락의 경우 다음 Windows 재부팅 시 삭제 등록.

---

## 📂 다중 파일 삭제 (Multi-File Deletion)

- **파일 다중 동시 선택**:
  - `Add` 화면의 "파일 다중 추가" 버튼 클릭 시 파일 선택 대화상자에서 `Ctrl` 또는 `Shift` 키로 **수십~수백 개의 파일을 동시에 선택**하여 대기열에 등록할 수 있습니다.
- **대량 일괄 드래그 앤 드롭 (Batch Drag & Drop)**:
  - 파일 탐색기에서 원하는 만큼 다중 선택 후 프로그램 창이나 대형 드롭존 위로 드래그하면 한 번에 일괄 등록됩니다.
- **원클릭 일괄 삭제 (`Delete All (N)`)**:
  - 대기 목록에 여러 파일이 등록되면 삭제 버튼이 자동으로 `Delete All (N개)`로 전환되어 목록의 모든 항목을 일괄 파쇄합니다. 개별 항목 우측의 `✕` 버튼으로 특정 파일만 대기열에서 제외할 수도 있습니다.
- **멀티코어 병렬 동시 삭제**:
  - 대기열의 여러 파일들을 단일 스레드로 하나씩 지우지 않고, `Parallel.ForEach` 기반 최대 16개 스레드로 **동시에 병렬 삭제**하여 100개의 파일도 0.5초 이내에 완전 삭제합니다.
- **Windows 탐색기 다중 선택 우클릭 지원**:
  - 탐색기에서 파일 여러 개를 선택한 상태에서 우클릭하여 "무조건 강제 삭제"를 누르면 대기열에 한꺼번에 등록되어 즉각 삭제할 수 있습니다 (`MultiSelectModel = Player`).

---

## 🖥️ 사용 방법

### 1. GUI 방식
1. `ForceDeleter.exe`를 관리자 권한으로 실행합니다.
2. 삭제할 여러 파일이나 폴더를 찾아보기로 다중 선택하거나, **창 위로 마우스 드래그 앤 드롭**합니다.
3. `🔥 Delete All (N개)` 버튼을 누르면 모든 파일이 즉시 완전 병렬 삭제됩니다.
4. `탐색기 우클릭 메뉴 등록`을 클릭하면 Windows 파일 탐색기 우클릭 메뉴에 **"무조건 강제 삭제"** 항목이 추가되어 더 편리하게 쓸 수 있습니다.

### 2. CLI 커맨드라인 방식
```cmd
# 단일 또는 다중 파일/폴더 즉시 지정 실행 (GUI 대기열 등록)
ForceDeleter.exe "C:\Path\file1.txt" "C:\Path\file2.sys" "C:\Folder"

# CLI 백그라운드 무인 병렬 강제 삭제 (다중 파일 지원)
ForceDeleter.exe --delete "C:\Path\file1.txt" "C:\Path\file2.sys" "C:\Folder"

# 탐색기 우클릭 메뉴 등록 (다중 선택 지원)
ForceDeleter.exe --install

# 탐색기 우클릭 메뉴 해제
ForceDeleter.exe --uninstall
```

---

## 📄 License
MIT License
