# 흩어진 물길 v1 — 기존 연구·소스 재활용 검토

조사 기준: 2026-10-11. 클라이언트 변조 없이 기존 소스 재활용을 우선하지만, 패킷 감청이나 입력 자동화가 운영정책상 허용된다는 의미는 아닙니다.

## 실증 자료
- artist012/mmbl: 2025년 코드가 패킷 헤더 9바이트(타입 4/크기 4/압축 1), Brotli, 몬스터 1033, 스킬 10041, 데미지 1283을 정의합니다.
  https://github.com/artist012/mmbl
  2026년 현재 형식인지는 미검증. 코드를 복사하지 않고 데이터 형식을 참고해 독립 구현한 오프라인 파서를 추가했습니다.
- teto-ri/Mobinogi_Meter: 비공식 전투 분석 도구. 외부 바이너리 실행이나 게임 주입 없이 설계만 참고합니다.
  https://github.com/teto-ri/Mobinogi_Meter
- JungoLee/mabinogiMobile-auto: OpenCV/OCR 상태 머신과 실시간 모니터 개념. 기존 Mabi_Auto 인식 계층이 이미 지원하므로 재작성하지 않습니다.
  https://github.com/JungoLee/mabinogiMobile-auto
- rahw96m/Mobi_Living_Supporter: 공식 CLI 데이터 조회 및 명령 래퍼. 기존 MabinogiMobileCli와 중복 구현하지 않습니다. 공식 CLI에 몬스터/발판 상태가 있다고 가정하지 않습니다.
  https://github.com/rahw96m/Mobi_Living_Supporter
- MinamiChiwa/MabinogiMobile-JPVoiceResourceTransfer: 타 지역 리소스 .blob/UnityFS 인덱스의 읽기 전용 분석. 한국 버전과 같은지는 미확인입니다. 패키지 수정·재패킹·프로세스 주입을 하지 않습니다.
  https://github.com/MinamiChiwa/MabinogiMobile-JPVoiceResourceTransfer
- pblyat/MabinogiTools: 패킷/데미지 프린터. README에 패치 후 작동하지 않는다고 기록되어 최신 구조 검증 자료에서 제외했습니다.
  https://github.com/pblyat/MabinogiTools

## 기존 Mabi_Auto에서 그대로 유지하는 계층
- WindowCapture / TargetDetector: 화면 캡처와 OpenCV/OCR
- ScatteredWaterwayMechanics: 화면상의 표식·파도·발판 읽기
- GuardedInputController: 기존 F10 취소, 800x1000, 포커스 안전 검사
- MabinogiMobileCli: 기존 공식 데이터 조회
- WaterwayPacketTraceDecoder: 독립된 오프라인 전투 이벤트 연구용 모듈. 실시간 입력과 분리

## 검증 순서
1. 오프라인 패킷 프레이밍 단위 테스트: dotnet run --project tests/waterway-packet/Regression.csproj
2. 틱택토 의사결정 테스트: dotnet run --project tests/waterway/Regression.csproj
3. 실제 게임 800x1000 영상에서 화면별 기믹 시작·종료·성공 근거 확인
4. 최신 패킷 연구 데이터가 있을 때 이벤트 ID/종류와 화면 시각 비교
5. 이동 키·포탈과 발판의 실제 좌표를 확인한 후 제한된 입력 활성화 검토

현재 packet trace는 PCAP 파일을 읽지 않습니다. 허가받아 저장한 헤더/페이로드 연속 스트림을 tests/waterway-packet 테스트 실행의 선택적 인수로 전달할 수 있습니다. 최대 32 MiB, 출력 최대 50건입니다. ID 및 스킬 이름 등 이용자 관련 정보는 외부 공유 전 익명화해야 합니다.

실게임 프로토콜·기믹 매핑 미확인, 템플릿 파일 없음, 이동 검증 없음. 기본 Enabled=false, ObserveOnly=true 유지. 마스터 브랜치 병합 및 정식 배포 금지.

라이선스가 불명확한 외부 저장소의 소스는 복사하지 않으며, 자료를 출처와 함께 참고하고 독립 구현합니다.
