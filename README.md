# Geuneda Analytics Airbridge

`com.geuneda.analytics` 의 Airbridge sink 입니다. 이벤트 이름을 카테고리로, 파라미터를 시맨틱·커스텀 속성으로 나눠 보내고, 표준 이벤트(구매, 광고 노출 수익)를 Airbridge 표준 카테고리로 바꿉니다.

| 타입 | 설명 |
|------|------|
| `AirbridgeAnalyticsSink` | sink(`AnalyticsSinks.Airbridge`). 항상 준비됨 |
| `AirbridgeAnalyticsSinkOptions` | `HandleEvent`, `Categories`(이벤트 → 카테고리), `SemanticAttributes`(파라미터 → 시맨틱 속성 키) |

네임스페이스는 `Geuneda.Analytics.Airbridge` 입니다.

## 설치

Airbridge Unity SDK(4.x, `AirbridgeSDK` 어셈블리)가 프로젝트에 있어야 합니다. 그다음 의존 패키지와 함께 추가합니다.

```json
{
  "dependencies": {
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
    "com.geuneda.core": "https://github.com/geuneda/geuneda-core.git",
    "com.geuneda.analytics": "https://github.com/geuneda/geuneda-analytics.git",
    "com.geuneda.analytics.airbridge": "https://github.com/geuneda/geuneda-analytics-airbridge.git"
  }
}
```

SDK 없이 설치하면 컴파일 오류가 납니다. define `GEUNEDA_SKIP_VENDOR_ADAPTERS` 가 있으면 컴파일되지 않습니다(호스트용).

## 사용 예

```csharp
var options = new AirbridgeAnalyticsSinkOptions();
options.Categories["tutorial_complete"] = AirbridgeCategory.COMPLETE_TUTORIAL;
analytics.AddSink(new AirbridgeAnalyticsSink(options));

analytics.LogEvent("shop_open", new Dictionary<string, object> { { "action", "Shop" }, { "label", "GoldShop" }, { "tab", 2 } });
// → TrackEvent("shop_open", { action: "Shop", label: "GoldShop" }, { tab: 2 })
```

## 동작

- **준비**: Airbridge SDK 는 앱 시작 때 네이티브에서 스스로 초기화되므로 항상 보냅니다(에디터에서는 SDK 가 아무것도 하지 않음).
- **일반 이벤트**: 카테고리는 `Categories` 에 있으면 그 값, 없으면 이벤트 이름입니다. `SemanticAttributes`(기본 `action`, `label`, `value`, `currency`)에 있는 파라미터는 시맨틱 속성(리포트에서 나눠 볼 수 있음), 나머지는 커스텀 속성입니다. 옵션의 사전은 sink 를 만들 때 복사합니다.
- **구매**: `AirbridgeCategory.ORDER_COMPLETED`. 시맨틱 속성 `value`(단가 × 수량), `currency`, `totalQuantity`, `inAppPurchased: true`, `transactionID`, `products`(`productID`, `name`, `price`, `quantity`, `currency`). 추가 파라미터는 커스텀 속성입니다.
- **광고 노출 수익**: `AirbridgeCategory.AD_IMPRESSION`. 시맨틱 속성 `value`, `currency`, 플랫폼이 AppLovin(MAX)이면 `adPartners.appLovin`(`revenue`, `network_name`, `adunit_identifier`, `placement`, 추가 파라미터에 있으면 `country_code`·`network_placement`·`creative_identifier`·`dsp_name`). 광고 필드(`ad_platform` 등)와 추가 파라미터는 커스텀 속성입니다. 다른 플랫폼의 파트너 상세가 필요하면 `HandleEvent` 로 직접 보냅니다.
- **사용자**: `SetUserID`/`ClearUserID`, `SetUserAttribute`/`RemoveUserAttribute`. 이벤트마다 기록할 때의 사용자 ID·속성으로 SDK 를 맞춘 뒤 보내고(바뀐 것만), 배치가 끝나면 지금 상태로 되돌립니다. 저장했던 이벤트가 그 뒤에 로그인한 사용자에게 붙지 않습니다.
- **게임 처리**: `HandleEvent` 가 true 를 돌려준 이벤트는 보내지 않습니다(예: 운영 스위치로 구매 이벤트를 끄거나 표준 카테고리를 직접 만듦).
- 이벤트 이름·속성 길이 제한은 SDK 와 서버 규칙을 따릅니다(이 sink 는 바꾸지 않음).
