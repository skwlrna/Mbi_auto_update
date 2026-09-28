MABI AUTO V0.1.76 - 새 GitHub 원클릭 이전

목표 계정:
  skwlrna

대상 저장소:
  skwlrna/Mbi_auto_update
  skwlrna/Mbi_auto_log

실행 방법:
1. 이 ZIP을 완전히 압축 해제합니다.
2. PUSH_TO_NEW_GITHUB.cmd 를 더블클릭합니다.
3. Git/GitHub CLI가 없으면 winget으로 자동 설치를 시도합니다.
4. GitHub 브라우저 인증이 뜨면 반드시 새 계정 'skwlrna'로 승인합니다.
5. 스크립트가 전체 V0.1.76 소스/템플릿/Actions를 Mbi_auto_update에 업로드합니다.
6. Release V0.1.76 workflow를 실행하고 완료될 때까지 기다립니다.
7. 성공하면 V0.1.76 Release URL이 표시됩니다.

중요:
- 기존 insubi 저장소를 사용하지 않습니다.
- V0.1.76 UpdateManager는 skwlrna/Mbi_auto_update를 바라보도록 변경되어 있습니다.
- 기존 V0.1.75는 예전 저장소를 바라보므로 V0.1.76은 최초 1회 수동 설치가 필요합니다.
- 런타임 오류 업로드용 Mbi_Auto_log는 별도의 fine-grained PAT 설정이 필요합니다.
- 실패 시 GITHUB_MIGRATION_LOG.txt를 보내주세요.


v2 수정
- 새 로컬 Git 저장소에 origin이 없을 때 발생하던
  'error: No such remote: origin' 중단 문제 수정
- GITHUB_MIGRATION_LOG.txt는 GitHub에 커밋되지 않도록 제외
- 실제 생성한 로그 저장소 이름 skwlrna/Mbi_auto_log로 통일
