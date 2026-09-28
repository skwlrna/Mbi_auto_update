# V0.1.76 - GitHub account migration

- Auto-update repository owner changed from `insubi` to `skwlrna`.
- No gameplay automation behavior was intentionally changed from the Astra V0.1.75 stability source.
- This build is the one-time bridge to the new GitHub release channel.
- After V0.1.76 is installed manually once, future updates can be discovered from `skwlrna/Mbi_auto_update`.

# Mabi Auto V0.1.74

이번 버전은 V0.1.73 실사용 오류 로그를 기준으로 두 가지를 보완합니다.

## 1. 회복 물약 부족 팝업 ESC 재시도
- V0.1.73에서 실제 팝업이 남아 있는데 ESC 1회만 전송하고 닫힘 확인 실패로 전체 매크로가 정지한 사례가 반복 확인됨.
- 동일 팝업을 2프레임 확인한 뒤 ESC를 최대 3회까지 재시도.
- 각 ESC 입력 뒤 2프레임 연속으로 팝업이 사라졌는지 확인.
- ESC 전마다 게임 창을 다시 foreground로 가져오고 짧은 안정화 대기 추가.
- 거래소 구매 버튼은 절대 클릭하지 않음.
- ESC 3회 뒤에도 같은 팝업이 남아 있으면 전체 매크로를 정지시키지 않고 다음 감시 주기에 다시 시도.

## 2. 전리품 중복 카운트 방지
- 어비스 특수 전리품은 한 판에 하나만 나온다는 규칙을 적용.
- 이미지 템플릿이 2프레임 연속으로 여러 개 동시에 잡혀도 최소 점수가 가장 높은 1개만 최종 카운트.
- 유사한 룬 장식 일반/+ 교차매칭으로 한 판에 2개가 올라가던 현상 방지.
- OCR fallback에서도 복수 후보가 생기면 1개만 최종 인정.
- 기존 /item, /itemreset 누적 형식 유지.

## 유지 사항
- V0.1.73 네트워크 자동 재접속 유지.
- V0.1.73 전리품 이미지 템플릿 11개 유지.
- V0.1.72 클리어 타이틀 fallback 유지.
- 사망 로직 제거 상태 유지.
- 어비스 판당 10분 제한 유지.
