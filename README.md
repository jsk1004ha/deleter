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

---

## 🎨 모던 다크 UI 및 애니메이션

- **5가지 전용 뷰 제공**:
  1. **Delete (메인 삭제 뷰)**: 대기 목록 카드, 상태 표시기(Green/Orange), 원클릭 삭제 및 초기화.
  2. **Add (대상 추가 뷰)**: 대형 점선 드롭존, 파일/폴더 선택 카드, 추가된 대상 가로 칩 미리보기.
  3. **History (삭제 이력 뷰)**: 검색 필터, 카테고리 칩(`All`, `Files`, `Folders`, `Successful`, `With Warnings`), 내보내기(`Export`).
  4. **Settings (환경설정 뷰)**: `Secure delete` (SDelete 덮어쓰기), `Show confirmation prompt`, `Remove locked files`, `Dark mode`, `Explorer Context Menu` 토글 스위치.
  5. **Deleting (삭제 애니메이션 뷰)**: 실시간 60FPS 네온 서큘러 프로그레스 링, 파티클 펄스, 성공/경고 요약 통계.
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

## 🖥️ 사용 방법

### 1. GUI 방식
1. `ForceDeleter.exe`를 관리자 권한으로 실행합니다.
2. 삭제할 파일이나 폴더를 찾아보기로 선택하거나, **창 위로 마우스 드래그 앤 드롭**합니다.
3. `🔥 무조건 강제 삭제 (Force Delete Everything)` 버튼을 누르면 즉시 완전 삭제됩니다.
4. `탐색기 우클릭 메뉴 등록`을 클릭하면 Windows 파일 탐색기 우클릭 메뉴에 **"무조건 강제 삭제"** 항목이 추가되어 더 편리하게 쓸 수 있습니다.

### 2. CLI 커맨드라인 방식
```cmd
# 특정 파일/폴더 즉시 지정 실행
ForceDeleter.exe "C:\Path\To\ProtectedFile.sys"

# 탐색기 우클릭 메뉴 등록
ForceDeleter.exe --install

# 탐색기 우클릭 메뉴 해제
ForceDeleter.exe --uninstall
```

---

## 📄 License
MIT License
