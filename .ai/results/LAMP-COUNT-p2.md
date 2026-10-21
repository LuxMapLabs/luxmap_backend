# LAMP-COUNT — Phase 2 (Claude)

Hướng A (Mỹ, 21/10/2026), LMC-1…LMC-4 ở `docs/contract-drift.md`.

## Thay đổi

`fixture.lamp_count` (entity, cấu hình, CHECK `ck_fixture_lamp_count` 1..4) · `POST /assets/fixtures` `lamp_count?` (`[Range(1,4)]`, vắng = 1) ·
import bóng cột tuỳ chọn (`ImportRowReader.OptionalInt`) · bản đồ, chi tiết cột, danh sách tài sản, cột của phiếu, thống kê bảo hành ·
migration `AddFixtureLampCount` · spec viết tay + sinh lại · mock (+`lamp_count: 1`, round-trip từng byte) · seed · template import.

## Hai lỗi bắt được trước commit

1. **`HasDefaultValue(1)` trong model** → `ModelDefaultValueTests` đỏ: EF bỏ cột khỏi INSERT khi giá trị = 0, DB default biến 0 thành 1
   thay vì CHECK từ chối. Sửa theo khuôn `AddSurveyFrames`: model không default; migration backfill 1 rồi `DROP DEFAULT`.
2. **Migration sinh lại có `defaultValue: 0`** → trên DB có bóng, CHECK `1..4` sẽ gãy migration. Bắt khi đọc `Up()`; đã sửa thành 1.

Kiểm nhánh backfill trên dữ liệu có sẵn (container PostGIS tạm, đúng 3 câu SQL của `Up()`): hàng cũ = 1, cột không còn default,
INSERT thiếu `lamp_count` bị NOT NULL từ chối, `0` bị CHECK từ chối.

## Bằng chứng

```
$ MSBUILDDISABLENODEREUSE=1 dotnet test --disable-build-servers
Passed!  - Failed: 0, Passed:   45 - LuxMap.Persistence.Tests.dll
Passed!  - Failed: 0, Passed:  384 - LuxMap.Shared.Tests.dll
Passed!  - Failed: 0, Passed:   54 - LuxMap.Infrastructure.Storage.Tests.dll
Passed!  - Failed: 0, Passed: 1125, Skipped: 2 - LuxMap.Api.Tests.dll
exit=0
$ npx @redocly/cli lint docs/openapi/luxmap-v1.5.json   → valid, 9 warnings (nền)
```

Phá thử (`FixtureLampCountTests`, 7 test): bỏ `lamp_count` khỏi bản đồ → 2 đỏ; bỏ `[Range(1,4)]` → 3 đỏ; import bỏ kiểm khoảng → 1 đỏ.

## Còn

- Review chéo Codex **không chạy**: hạn mức Codex hết tới 27/10/2026 23:22; Claude tự review (mọi chỗ có `LampWatt` đều có `LampCount`;
  script chép DB đọc cột động; sync kế thừa `PoleProperties`).
- Contract §5.1 / §5.3 chưa sửa chữ (drift LMC là nguồn tới lần lên version). Báo WP5 / WP6.
