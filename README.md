# Universal Force Deleter (무조건 강제 삭제기) 🚀

> **권한과 파일 종류, 잠금 상태에 상관없이 무조건 삭제하는 Windows 전용 초강력 강제 삭제 도구**

Windows에서 파일이나 폴더를 삭제하려 할 때 발생하는 **"액세스 거부 (권한 부족)"**, **"다른 프로그램에서 사용 중"**, **"TrustedInstaller 권한 필요"**, **"긴 파일 경로"**, **"읽기 전용/시스템 특수 속성"** 등의 문제를 완전히 무력화하고 영구 삭제합니다.

---

## 🌟 결합된 핵심 기술 및 레포지토리

본 프로그램은 다음 주요 오픈소스 프로젝트들의 핵심 기술을 융합하여 개발되었습니다:

1. **[M2Team/NanaRun](https://github.com/M2Team/NanaRun)** (MinSudo):
   - Windows 최상위 권한인 `NT AUTHORITY\SYSTEM` 및 `NT SERVICE\TrustedInstaller` 서비스 토큰 스틸 및 가장(Impersonation).
   - `SeDebugPrivilege`, `SeTakeOwnershipPrivilege`, `SeBackupPrivilege`, `SeRestorePrivilege`, `SeSecurityPrivilege` 등 시스템 특권 활성화.
2. **[theminji/windows-force-delete](https://github.com/theminji/windows-force-delete)** & **[marianpekar/file-unlocker-for-windows](https://github.com/marianpekar/file-unlocker-for-windows)**:
   - Windows Restart Manager (`rstrtmgr.dll`) API를 활용한 파일 잠금 프로세스 식별 및 원클릭 프로세스 종료.
   - `NtQuerySystemInformation` 기반 원격 핸들 닫기 (`DuplicateHandle` with `DUPLICATE_CLOSE_SOURCE`).
3. **[CodeDead/DeadLock](https://github.com/CodeDead/DeadLock)**:
   - 파일 및 폴더 소유권 가져오기 (Take Ownership) 및 ACL `Everyone:FullControl` 강제 부여.
4. **[RickStrahl/DeleteFiles](https://github.com/RickStrahl/DeleteFiles)**:
   - `\\?\` 접두사 기반 최대 32,767자의 확장 긴 경로(Long Path) 및 특수문자/예약어 지원.
   - Read-Only, Hidden, System 속성 일괄 해제.
5. **[AhmedElbehary-Dev/ForcedDeleteFiles](https://github.com/AhmedElbehary-Dev/ForcedDeleteFiles)**:
   - 커널 수준에서 잠긴 특수 파일의 경우 `MoveFileExW(..., MOVEFILE_DELAY_UNTIL_REBOOT)`를 통한 다음 부팅 시 100% 삭제 예약.

---

## ⚡ 6단계 강제 삭제 파이프라인 (Multi-Tier Deletion)

1. **Tier 1 (표준 삭제)**: 일반 `.NET` 표준 삭제 시도 (정상 파일 1ms 이내 즉각 삭제).
2. **Tier 2 (속성 및 ACL 재설정)**: Read-Only, System, Hidden 속성 제거 후 소유권을 Administrators로 변경하고 Everyone Full Control 권한 부여 후 삭제.
3. **Tier 3 (NanaRun 권한 상승)**: TrustedInstaller 서비스 기동 후 토큰을 복제/가장하여 시스템 최고 권한으로 삭제.
4. **Tier 4 (잠금 프로세스 종료 및 핸들 해제)**: Restart Manager를 통해 파일을 물고 있는 프로세스를 종료하고 열린 파일 핸들을 강제 폐쇄 후 삭제.
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
