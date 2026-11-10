# 음식 레시피 등록 (2026-11-10)

## 범위와 근거

`CraftingFoodRecipeCatalog`에 완성 음식 52종의 **1회 직접 투입 재료**를 등록했습니다.
`달걀 프라이`/`달걀프라이`, `토마토 리조토`/`토마토 리소토`는 명시적인 별칭입니다.
현재 `VerifiedFoodNames` 56개 이름 중 54개가 등록 레시피로 연결되고 2개는 아래 수량 충돌 때문에 보류됩니다.

이 데이터는 외부 공개 자료를 확인·대조한 참고 레시피입니다. 게임에서 직접 캡처하거나 현재 CLI 원본으로 전체 재료를 검증했다는 의미가 아닙니다.
`MissingIngredients`는 보유 중인 재료가 빠질 수 있으므로 전체 레시피로 사용하지 않았습니다.
생크림·마요네즈 같은 직접 투입 중간재를 우유·달걀 등 원재료로 치환하지 않습니다.
1회 생산량, 필요 시설, 요리 레벨은 이번 입력 재료 등록에서 추정하지 않았습니다.

## 코드 적용 범위

- 레시피 입력 목록, 출처 URL, 검토일과 1~10회 필요량 계산 API를 등록했습니다.
- 제작 화면의 기존 재료명 후보 목록에 등록 레시피의 재료명을 먼저 공급합니다. CLI의 1회 부족 목록이 비어 있어도 후보가 사라지지 않습니다.
- 이번 변경은 레시피 데이터 등록과 이름 후보 연결입니다. 등록 수량만으로 음식의 실행·수급·완료 판정을 바꾸거나 재료명 OCR 실패 시의 중단 조건을 제거하지 않습니다. CLI 중심 판정 전환은 별도 구현이 필요합니다.
- 가공 중간재의 기존 수급 정책은 수정하지 않습니다. 생강·셀러리 등 자동 수급 미지원 재료도 전체 레시피에는 포함됩니다.

## 수량 충돌 — 사용 가능한 레시피에서 제외

| 음식 | 충돌 | 근거 |
| --- | --- | --- |
| 매콤 파프리카 스튜 | 밥 3개 / 4개 | 공식 커뮤니티 사용자 공략은 3, 생활 스킬 위키 표는 4 |
| 파이크퍼치 필레 리소토 | 치즈 3개 / 12개 | 같은 공략은 3, 위키 표와 별도 CLI 덤프 기반 목록은 12 |

공식 커뮤니티에 게시된 사용자 글은 개발사 검증 자료로 취급하지 않습니다.
게임 상세창 또는 현재 원본 데이터로 수량을 확인하기 전에는 `TryGet`이 false를 반환합니다.

- 사용자 공략: https://mabinogimobile.nexon.com/Community/ScreenShot/3485736
- 생활 스킬 표: https://d.namu.moe/w/마비노기%20모바일/생활%20스킬
- 부분 CLI 덤프 목록: https://mabi.mspark2.com/recipes/ (전체 레시피가 아닌 개별 치즈 수량의 교차 확인에만 사용)

## 등록 목록 — 1회 기준

| 음식 | 직접 투입 재료 | 확인 출처 |
| --- | --- | --- |
| 달걀프라이 | 달걀 1 · 소금 1 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobibook.site/crafting#crafting-item-달걀-프라이-356) |
| 삶은 달걀 | 달걀 1 · 물이 든 병 1 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobibook.site/crafting#crafting-item-삶은-달걀-357) |
| 옥수수차 | 옥수수 16 · 물이 든 병 4 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) |
| 녹차 | 말린 찻잎 8 · 물이 든 병 4 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) |
| 홍차 | 발효된 찻잎 8 · 물이 든 병 4 · 옷감 2 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) |
| 바삭한 초코 카나페 | 오트밀 5 · 조각 초콜릿 3 · 파프리카 3 · 치즈 3 · 달걀 1 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) |
| 마요네즈 고기볶음 | 고기 4 · 양배추 1 · 마요네즈 1 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobibook.site/crafting#crafting-item-마요네즈-고기볶음-359) |
| 포크 인 밀크 | 고기 8 · 우유 4 · 마늘 2 · 허브 2 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobibook.site/crafting#crafting-item-포크-인-밀크-360) |
| 치즈 퐁뒤 | 고기 8 · 치즈 2 · 감자 4 · 소금 2 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobibook.site/crafting#crafting-item-치즈-퐁뒤-361) |
| 조개찜 | 조개 10 · 물이 든 병 5 · 레몬 4 · 허브 3 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobibook.site/crafting#crafting-item-조개찜-362) |
| 크림소스 스테이크 | 고기 10 · 생크림 2 · 마늘 2 · 후추 3 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobibook.site/crafting#crafting-item-크림소스-스테이크-363) / [자료3](https://mobile.mabi.zip/keurimsoseuseuteikeu) |
| 샤와르마 | 담백한 고기 10 · 콩 12 · 밀가루 3 · 양배추 5 · 레몬 2 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobibook.site/crafting#crafting-item-샤와르마-364) |
| 미트 파르미자나 | 담백한 고기 10 · 치즈 3 · 토마토 1 · 후추 1 · 소금 1 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobibook.site/crafting#crafting-item-미트-파르미자나-365) |
| 카레라이스 | 고기 10 · 밥 3 · 당근 3 · 감자 8 · 카레 가루 2 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobile.mabi.zip/kareraiseu) |
| 미트 포트파이 | 담백한 고기 10 · 완두콩 2 · 파스닙 6 · 밀가루 4 · 우유 4 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobile.mabi.zip/miteupoteupai) |
| 포레스트 스테이크 | 커다란 고기 2 · 개암 버섯 10 · 마늘 6 · 생강 2 · 소금 2 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) |
| 감자 샐러드 | 감자 4 · 양배추 2 · 마요네즈 1 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobibook.site/crafting#crafting-item-감자-샐러드-367) |
| 야채볶음 | 감자 8 · 양파 3 · 양배추 6 · 허브 2 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobibook.site/crafting#crafting-item-야채볶음-368) |
| 리코타 치즈 샐러드 | 양배추 4 · 치즈 2 · 마요네즈 1 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobibook.site/crafting#crafting-item-리코타-치즈-샐러드-369) |
| 감자수프 | 감자 12 · 우유 5 · 허브 3 · 후추 6 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobibook.site/crafting#crafting-item-감자수프-370) |
| 알리오 올리오 | 마늘 7 · 면 2 · 양파 2 · 후추 2 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobibook.site/crafting#crafting-item-알리오-올리오-371) |
| 두부 스테이크 | 두부 5 · 파스닙 6 · 마늘 4 · 후추 3 · 식용유 2 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobibook.site/crafting#crafting-item-두부-스테이크-372) |
| 두부 국수 | 두부 5 · 물이 든 병 5 · 양파 5 · 무 1 · 소금 3 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobibook.site/crafting#crafting-item-두부-국수-373) |
| 토마토 리소토 | 밥 4 · 토마토 2 · 양파 3 · 치즈 8 · 소금 3 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) |
| 호박 수프 | 호박 10 · 감자 6 · 양파 6 · 생강 2 · 생크림 5 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobile.mabi.zip/hobaksupeu) |
| 오트밀 영양죽 | 오트밀 5 · 물에 불린 콩 4 · 사과 8 · 당근 5 · 소금 2 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) |
| 가든 샐러드 | 양배추 8 · 파스닙 10 · 셀러리 2 · 마요네즈 4 · 허브 10 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) |
| 사과 샐러드 | 사과 4 · 양배추 2 · 마요네즈 1 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobibook.site/crafting#crafting-item-사과-샐러드-375) |
| 콘치즈 | 옥수수 10 · 치즈 2 · 허브 2 · 설탕 3 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobibook.site/crafting#crafting-item-콘치즈-376) |
| 사과 수플레 | 사과 8 · 밀가루 2 · 달걀 4 · 설탕 4 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobibook.site/crafting#crafting-item-사과-수플레-377) |
| 얼음 딸기주스 | 얼음 10 · 딸기 6 · 허브 3 · 설탕 4 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobibook.site/crafting#crafting-item-얼음-딸기주스-378) |
| 사과 생크림케이크 | 사과 12 · 생크림 2 · 밀가루 4 · 설탕 9 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobibook.site/crafting#crafting-item-사과-생크림케이크-379) |
| 두유 빙수 | 두유 4 · 얼음 10 · 레몬 2 · 설탕 6 · 허브 3 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobibook.site/crafting#crafting-item-두유-빙수-380) |
| 두유 파스닙 케이크 | 두유 4 · 파스닙 6 · 생크림 2 · 밀가루 4 · 설탕 9 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobibook.site/crafting#crafting-item-두유-파스닙-케이크-381) |
| 녹차 크레이프 케이크 | 말린 찻잎 10 · 딸기 6 · 생크림 6 · 밀가루 4 · 설탕 6 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobile.mabi.zip/nokchakeureipeukeikeu) |
| 호박 파이 | 호박 10 · 생강 2 · 생크림 3 · 밀가루 4 · 설탕 2 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobile.mabi.zip/hobakpai) |
| 달콤쌉쌀 푸딩 | 헤이즐넛 10 · 두유 6 · 말린 찻잎 4 · 딸기 8 · 설탕 7 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) |
| 넛츠 초코 비스킷 | 헤이즐넛 10 · 밀가루 8 · 조각 초콜릿 5 · 달걀 4 · 생크림 6 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) |
| 부야베스 | 무지개 송어 4 · 은어 4 · 토마토 6 · 조개 5 · 양파 2 · 마늘 4 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobile.mabi.zip/buyabeseu) |
| 고등어와 연어 스테이크 | 고등어 5 · 연어 3 · 마요네즈 2 · 아스파라거스 4 · 소금 2 · 후추 3 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobile.mabi.zip/godeungeowayeoneoseuteikeu) |
| 메기 피시 앤 칩스 | 메기 6 · 감자 6 · 밀가루 3 · 완두콩 4 · 레몬 2 · 소금 3 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) |
| 농어 매운탕 | 농어 6 · 무 4 · 양파 6 · 물이 든 병 5 · 고춧가루 4 · 후추 2 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobile.mabi.zip/nongeomaeuntang) |
| 대만찬: 꺼지지 않는 불의 뵈프 부르기뇽 | 만찬 재료: 꺼지지 않는 불 4 · 숙성된 커다란 고기 8 · 묵직한 감자 24 · 토마토 16 · 양파 16 · 파스닙 16 · 소금 4 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobile.mabi.zip/daemanchankkeojijianhneunbuluiboepeubureuginyong) |
| 금은 매운탕 | 금린어 4 · 은붕어 4 · 무 6 · 물이 든 병 5 · 양파 10 · 고춧가루 4 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) / [자료2](https://mobile.mabi.zip/geumeunmaeuntang) |
| 송어 파피요트 | 갈색송어 6 · 아스파라거스 4 · 마늘 6 · 레몬 5 · 소금 2 · 후추 4 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) |
| 대만찬: 거센 바람의 페어리 플로스 | 만찬 재료: 휘몰아치는 바람 4 · 구름결 막대 8 · 설탕 24 · 증폭 버섯 진액 16 · 솔솔 버섯 진액 16 · 응축된 바람 16 · 마력 기폭제 4 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) |
| 토마토 크림 생선 커넬 | 민물꼬치고기 4 · 생크림 4 · 토마토 6 · 달걀 12 · 셀러리 4 · 후추 3 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) |
| 풍미 가득 스테이크 | 숙성된 커다란 고기 4 · 헤이즐넛 오일 6 · 셀러리 6 · 개암 버섯 12 · 소금 4 · 라벤더 꽃 2 | [자료1](https://d.namu.moe/w/마비노기%20모바일/생활%20스킬) |
| 구운 고기 | 고기 4 · 허브 2 · 소금 1 | [자료1](https://mobibook.site/crafting#crafting-item-구운-고기-358) |
| 통감자구이 | 감자 4 · 허브 2 · 설탕 2 | [자료1](https://mobibook.site/crafting#crafting-item-통감자구이-366) |
| 사과 주스 | 사과 4 · 허브 2 · 물이 든 병 1 · 설탕 2 | [자료1](https://mobibook.site/crafting#crafting-item-사과-주스-374) |
| 흰살생선 뫼니에르 | 브리흐네 잉어 5 · 은붕어 3 · 감자 5 · 밀가루 3 · 레몬 5 · 소금 3 | [자료1](https://mobile.mabi.zip/huinsalsaengseonmoeniereu) |

## 확인

- `dotnet run --project tests/crafting/Regression.csproj`: 171개 검사 통과.
- 호박 수프 10회 전체 입력, 가든 샐러드의 비채집·가공 입력, 별칭, 알 수 없는 품목 거부, 충돌 레시피 제외, 잘못된 횟수 거부를 확인했습니다.
- 실제 게임 구동 검증은 하지 않았습니다.
