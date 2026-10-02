# PR22 작업 인계 — 2026-10-02

## 이어서 작업할 기준

- 저장소: https://github.com/skwlrna/Mbi_auto_update
- 브랜치: `fix/live-cli-crafting-schema`
- PR: https://github.com/skwlrna/Mbi_auto_update/pull/22 (draft)
- 실행 로직 최신 커밋: `c55b412a04d83008d0b97ab0e30b96e6d469f541`
- main에는 PR21이 병합되어 있으며, PR22 실게임 수정은 아직 병합하지 않았다.
- 다음 작업은 이 브랜치의 최신 HEAD에서 시작한다. main만 받으면 이번 실게임 수정이 빠진다.
- 검증용 실행 ZIP은 GitHub draft release `V2.0.2-PR22-handoff-20261002`에 첨부한다. 자동 업데이트용 정식 릴리스가 아니다.

## 구현 및 실게임 반영

- 제작 UI, CLI 전체 제작 목록 조회, 최대 10회 배치, 퀘스트 재료 수급, 기존 가공/채집 연동, 가방 100개 경로를 PR21에서 반영했다.
- 실제 CLI 목록은 1835행이고 카테고리 메타데이터가 없다. 같은 이름 중복 레시피는 버리지 않고 보존하며, 모호한 선택은 실행을 차단한다. 카테고리는 사용자가 선택한다.
- 검색은 돋보기 → 입력칸 클릭 → 품목 입력 → Enter → 적용하기 → exact match 선택 순서다.
- 업로드 이미지는 부분 캡처다. 원본 이미지 좌표를 게임 입력 좌표로 저장하지 않는다. 800×1000 클라이언트 안에서 OCR과 화면 앵커로 위치를 찾는다.
- 상급 붕대 퀘스트는 재료가 준비된 경우 `약품 제작대에서 제작`만 표시하며 0/N이 없을 수 있다. 이를 인식한다.
- 캠프파이어 키트의 `바로 제작 진행`은 일반 제작대 경로와 다르다. 별도 검증 전에는 안전 정지하며 자동 지원 완료로 간주하지 않는다.
- `execute_crafting`, `execute_gathering`은 각 5 정령의 날개 소모 설명이 실제 capabilities에서 확인됐다. 비용 Note를 보존하고 실제 명령 실행은 계속 차단한다.

## 실제 확인한 것과 아직 확인하지 못한 것

- 사용자가 게임 UI에서 상급 붕대를 1회 제작했다. 제작 완료 화면의 결과물은 10개이며, 읽기 전용 CLI로 실제 재고 3 → 13, +10을 확인했다.
- 위 결과는 사용자 수동 조작 검증이다. 프로그램의 자동 제작 성공이 아니다.
- 사용자가 초기 조회 이후 날개 9개는 자신이 따로 썼다고 확인했다. 자동화 조회/시도 중 날개 변화는 없었다.
- 프로그램 실행은 음식/아이템 모두 퀘스트 준비 중 정지했다. 상세 로그 수정 후 실제 이유는 `입력 직전 화면이 오래되었거나 실행이 종료되어 정지합니다.`로 확인됐다.
- 원인은 OCR 처리 후 입력 가드의 5초 유효 시간이 만료되는 경로다. c55b412에서 입력 직전 다시 캡처하고, 인식 영역의 이미지가 유지되는지 비교한 뒤 입력한다. 5초 가드는 유지한다.
- 이 최신 수정본의 사용자 재실행은 아직 하지 않았다. 따라서 자동 제작 완료, 부족 재료 획득, 100개 반복의 실제 게임 성공은 미확인이다.

## 다음 실게임 검증

1. 최신 검증 ZIP을 새 폴더에 풀어 START.cmd로 실행한다.
2. 게임 클라이언트 영역은 800×1000이어야 한다. 창 모드 상태로 유지하고 자동 실행 중 수동 조작하지 않는다.
3. 제작 → 아이템 → 상급 붕대 → 목표 추가 10개로 시작한다. 1회 생산량이 10개이므로 제작 1회다.
4. 시작 전 CLI 재고를 새로 조회한다. 이전 13개가 유지된다면 완료 후 23개 이상이어야 한다.
5. 안전 정지 시 현재 단계, 상세 이유, 진단 이미지와 실제 화면을 대조한다. 가드 시간만 늘리거나 오래된 OCR 좌표로 클릭하지 않는다.
6. 정상 제작 확인 후 부족 재료, 최대 10회 및 잔여 배치, 0개 최초 확보 → 100개 전환, 초과 수량 허용, 기존 기능 회귀를 진행한다.

## 검증 결과와 제한

- 최신 변경: 앱 Release 빌드와 win-x64 self-contained publish 성공.
- `dotnet run --project tests/crafting/Regression.csproj -c Release --no-restore`: 32 checks 통과.
- PR21 기준 전체 CI 단계 로컬 실행 통과. 최근 스크린샷 기반 PR22 수정 후에는 위 제작 회귀와 앱 빌드/게시를 확인했다. 전체 최신 실게임 회귀 통과로 표현하지 않는다.
- 빌드에는 기존 analyzer/nullable 및 NuGet 감사 조회 관련 경고가 있으나 오류는 없었다.
- Windows 로컬 CLI는 sandbox 안에서 game_off를 잘못 반환한 적이 있으며, 승인된 외부 실행에서는 정상 연결됐다. 읽기 전용 CLI만 사용한다.
- 외부 데스크톱 캡처 도구는 타임아웃으로 사용하지 못했다. 사용자가 직접 조작/첨부한 이미지와 CLI 조회로 검증했다. 프로그램 내부 캡처의 작동 여부는 별도로 검증해야 한다.
- 이전 ChatGPT의 `/mnt/data/Mbi_auto_update.zip`은 이 Windows 작업 환경에서 확인된 기준 파일이 아니다. 원본 ZIP 대조 요구는 완료했다고 주장하지 말고, 원본이 확보되면 최신 브랜치와 네 분류(압축본만/GitHub만/동일/충돌)로 대조한다.
- 개인 CLI 원본 덤프, 계정 데이터 및 사용자 설정은 저장소에 올리지 않는다.

## 주요 파일

- `source/FishingAutomation/MainForm.Crafting.cs`: 실행 및 안전 정지 이유 표시
- `source/FishingAutomation/dungeon/CraftingScreen.cs`: 화면 검색/퀘스트/제작, 입력 직전 이미지 재검증
- `source/FishingAutomation/CraftingQuestText.cs`: exact 퀘스트 제목 및 일반/즉시 제작 단계
- `source/FishingAutomation/CraftingAutomation.cs`: 배치 및 실제 재고 증가 검증
- `source/FishingAutomation/InventoryBulkGatheringAutomation.cs`: 독립 100개 채집 흐름
- `source/FishingAutomation/ZeroWingScreenGuards.cs`: 날개 변화 검증
- `tests/crafting`: 제작 회귀와 선택적 live readonly 검증
