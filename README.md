# ActionFit Cookie Cleanup

`com.actionfit.cookie-cleanup`은 코드만 포함하는 공개 CookieCleanup 엔진입니다. 일정 구간, 스키마 버전이 있는 상태, 결정론적 보드 배치, 스프레이 및 라운드 전환, 보상 트랜잭션, 마이그레이션 입력과 재시작 복구를 소유합니다. UI, 프리팹, 스프라이트, 생성된 CSV/SO 에셋, 해금 규칙, 분석 전송, Addressables와 인벤토리 어댑터는 사용하는 프로젝트가 소유합니다.

## 설치

Public 저장소와 불변 태그가 게시된 후 사용하는 프로젝트에 Git UPM 패키지를 추가합니다.

```json
{
  "dependencies": {
    "com.actionfit.cookie-cleanup": "https://github.com/ActionFit-Editor/CookieCleanup.git#0.1.5"
  }
}
```

## 런타임 경계

- `CookieCleanupEngine`은 명령과 상태의 최종 권한을 가집니다.
- `CookieCleanupCatalog`은 오브젝트, 라운드, 보드 크기, 스프레이 값과 라운드/상자 보상 스냅샷을 고정합니다.
- `CookieCleanupPlacementEngine`은 기존 `System.Random`, 후보 순서, Fisher-Yates, 재시도, 회전 및 first-fit 계약을 보존합니다.
- `IContentStateStore`는 하나의 완전한 런타임 스냅샷을 영속화하며, 중요한 전환은 `IFlushableContentStateStore`를 통해 즉시 기록합니다.
- `IContentRewardService`는 이벤트 인스턴스 범위의 라운드 및 상자 트랜잭션을 정확히 한 번 지급합니다.
- `ActionFit.Time.IClock`은 새 이벤트에 사용할 UTC와 명시적인 UTC+09:00 서비스 달력을 제공합니다.
- `ICookieCleanupLegacyLocalClock`은 마이그레이션 전 활성 로컬 tick 비교에만 사용합니다.

최초 적용은 기존 구현에 추가하는 방식입니다. 기존 Cat Merge 스크립트는 호환 facade로 유지하며, 모든 레거시 키와 프로젝트 에셋은 롤백을 위해 보존합니다.

런타임 의존성은 `com.actionfit.content-core@0.2.1`과 `com.actionfit.time@1.0.3`으로 고정되어 있습니다.
