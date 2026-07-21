# ActionFit Cookie Cleanup

`com.actionfit.cookie-cleanup`은 프로젝트 중립 CookieCleanup 엔진과 canonical CSV를 포함합니다. 일정 구간, 스키마 버전이 있는 상태, 결정론적 보드 배치, 스프레이 및 라운드 전환, 보상 트랜잭션, 마이그레이션 입력과 재시작 복구를 소유합니다. UI, 프리팹, 스프라이트는 `com.actionfit.cookie-cleanup.ui`가 소유하고, 생성된 Row/Table 코드와 Table SO, 해금 규칙, 분석 전송, Addressables와 인벤토리 어댑터는 사용하는 프로젝트가 소유합니다.

## 설치

Public 저장소와 불변 태그가 게시된 후 사용하는 프로젝트에 Git UPM 패키지를 추가합니다.

```json
{
  "dependencies": {
    "com.actionfit.cookie-cleanup": "https://github.com/ActionFit-Editor/CookieCleanup.git#0.2.2"
  }
}
```

## 런타임 경계

- `CookieCleanupEngine`은 명령과 상태의 최종 권한을 가집니다.
- `CookieCleanupCatalog`은 오브젝트, 라운드, 보드 크기, 스프레이 값과 라운드/상자 보상 스냅샷을 고정합니다.
- `CookieCleanupPlacementEngine`은 기존 `System.Random`, 후보 순서, Fisher-Yates, 재시도, 회전 및 first-fit 계약을 보존합니다.
- `IContentStateStore`는 하나의 완전한 런타임 스냅샷을 영속화하며, 중요한 전환은 `IFlushableContentStateStore`를 통해 즉시 기록합니다.
- `IContentRewardService`는 이벤트 인스턴스 범위의 라운드 및 상자 트랜잭션을 정확히 한 번 지급합니다.
- `ActionFit.Time.IClock`은 새 이벤트의 UTC를 제공하고 calendar는 생성자에서 명시적으로 주입합니다. server mode는 UTC calendar, device mode는 `TimeZoneInfo.Local`을 사용합니다.
- `ICookieCleanupLegacyLocalClock`은 마이그레이션 전 활성 로컬 tick 비교에만 사용합니다.

최초 적용은 기존 구현에 추가하는 방식입니다. 기존 Cat Merge 스크립트는 호환 facade로 유지하며, 모든 레거시 키와 프로젝트 에셋은 롤백을 위해 보존합니다.

비활성 레거시 스냅샷이 남아 있어도 새 이벤트의 활성 요일과 예상 시간은 주입된 신규 달력으로 판단합니다. 진행 중인 레거시 타이머만 기존 숫자 축을 사용하며, 시작 거절은 basis나 저장 상태를 변경하지 않습니다.

canonical CSV는 `Data/CSV/`에 포함되며, 생성된 결과는 소비 프로젝트의 `Assets/_Data/_CookieCleanup/`에 둡니다.

## Canonical CSV 독립 구성

`Data/CSV/`의 여섯 파일에서 읽은 `TextAsset.text`를 `CookieCleanupCatalogCsvData`에 전달하고 `CookieCleanupCatalogFactory.Create`를 호출하면 CSV Importer, 생성 Row/Table 코드와 프로젝트 Table SO 없이 `Catalog`과 `SchedulePolicy`를 얻습니다. `segment`를 생략하면 기본 밸런스, `CookieCleanupCatalogFactory.RewardSegment`를 전달하면 Reward 밸런스를 구성합니다. 팩터리는 파일을 자동 탐색하거나 로드하지 않으며 비어 있거나 잘못된 CSV는 예외로 즉시 차단합니다.

```csharp
CookieCleanupStandaloneCatalog standalone = CookieCleanupCatalogFactory.Create(
    new CookieCleanupCatalogCsvData(
        eventSettings.text,
        objects.text,
        boxRewards.text,
        roundRewards.text,
        rounds.text,
        sprayOrders.text),
    CookieCleanupCatalogFactory.RewardSegment);
```

CSV Importer 생성 결과는 계속 `Assets/_Data/_CookieCleanup/`에 둘 수 있으며, 패키지 팩터리와 기본·Reward `_Data` SO의 동등성은 프로젝트 EditMode 테스트로 검증합니다.

런타임 의존성은 `com.actionfit.content-core@0.2.3`과 `com.actionfit.time@1.0.4`로 고정되어 있습니다.
