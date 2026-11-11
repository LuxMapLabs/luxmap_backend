# MULTI-FIXTURE — kết quả Pha 2

**Cách giới hạn hai bóng:** `fixture.slot` (1..2, `ck_fixture_slot`) + UNIQUE partial `ux_fixture_pole_slot_active (pole_id, slot) WHERE removed_date IS NULL`. Chọn slot thay vì đếm dòng vì hai request đua nhau vẫn vượt được bước đếm, còn index thì không. Service chọn slot trống thấp nhất (`FixtureSlots.FirstFree`) để trả 409 có thông điệp trước khi index phải từ chối.

**Đổi:** entity + config + migration `AddFixtureSlot` (backfill 1, bỏ default); `CreateFixtureAsync`, import bóng, inventory (`active_fixtures[]`), bản đồ (`fixture_count`), chi tiết cột (`fixtures[]`), cột của phiếu (`fixture_count`), thống kê bảo hành (một hàng mỗi bóng), khoá baseline (bóng mới nhất đang dùng — `SurveyBaselineLookup`, `SurveyReviewService`, `PoleDetailService`), `seed_mock_set.py` (cột `slot`).

**Test:** `FixtureCardinalityTests` viết lại (8 test: bóng 2 nhận / bóng 3 → 409, ngừng dùng một bóng giải phóng slot, dòng lịch sử, đọc `fixtures[]` / `active_fixtures[]` / `fixture_count`, neo baseline, import, DB từ chối slot lặp và slot 3). Sửa theo luật mới: `AssetImportTests` ×2, ba test pin tập key (`PoleDetailEndpointTests`, `AssetReadShapeTests`, `MapEndpointTests`). Phá thử `FirstFree` → 7/8 đỏ, đã khôi phục.

**Chưa làm / lỗ hổng:** MF-5 (ngừng dùng bóng cũ hơn không thay → baseline vẫn hiệu lực). Migration chưa áp lên `luxmap_dev` / VPS. Mock `mocks/*.json` chưa có cột đôi. WP5 / WP6 chưa báo.
