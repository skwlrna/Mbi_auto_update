# 흩어진 물길 v3.1.84 — 읽기 전용 기믹 관찰

기본 설정: Enabled=false / ObserveOnly=true / AllowKeyboardActions=false.
기존 UI·가공·낚시·채집 기능을 변경하지 않습니다. 영상 좌표와 키 이동이 검증되기 전에는 입력 금지입니다.

## 1번방: 플레이 영상 및 사용자 확인 사항

1. 보스 머리 위에 정답 문양이 등장한다.
2. 여섯 사하긴 사제의 문양별 위치는 매판 변경된다.
3. 투창 표적이 되면 내 캐릭터 머리 위에도 문양이 나타난다.
4. 표적 지정 직후 보스와 같은 문양의 사제에게 빠르게 이동한다.
5. 투창 범위가 나를 따라오다 발사 직전 고정된다.
6. 나와 정답 사제가 공격 범위 안에서 함께 맞아야 한다.

구현: BossMarkers(보스 문양), PriestMarkers(6종 사제 문양 위치),
PlayerMarkers(내 머리 위 표적 문양). 보스·사제·플레이어 ROI를 분리해
별도로 인식하고, 사제 문양 중심 좌표를 실시간 갱신한다.
사제 좌표는 1.2초, 보스 문양은 4초 경과 시 만료되며 판마다 초기화한다.
표적 지정 전에는 사제 위치만 사전 탐지하고 이동하지 않는다.
현재는 *사제 문양의 화면 좌표*만 계산하며 발밑 위치·투창 범위와
안전한 WASD 경로가 검증되지 않아 자동 이동을 금지한다.
다른 속성으로 고정 키를 누르는 옛 KeysByMark 방식도 사용하지 않는다.

## 템플릿 준비
클라이언트 리소스 및 공개 GitHub 분석 자료를 읽기 전용으로 참고하되,
실제 PNG는 사용자 로컬에만 둔다. 게임 리소스를 GitHub에 재배포하지 않는다.
기본 BossRoi / PriestRoi / PlayerRoi는 크기 0이므로 실제 800x1000
프레임 분석으로 설정해야 감지가 된다. 속성 6종 이름은 임시 표기이며
실제 아이콘 모양과 맞춰야 한다.

## 다른 기믹
2번방 파도/포탈, 3번방 3x3 O/X·침수발판, 승천 보호막/금색 구역
감시 로직은 이전 초안에서 이식했다. 3번방 발판은 카메라에 따라
움직이므로 고정 9칸 ROI 기반 실게임 자동 이동은 허용하지 않는다.

## 회귀 검사
- dotnet run --project tests/waterway/Regression.csproj
- dotnet run --project tests/waterway-packet/Regression.csproj
- dotnet build source/FishingAutomation/FishingAutomation.csproj -c Release

CI 성공은 실제 게임 자동 파훼가 성공한다는 뜻이 아니다.
