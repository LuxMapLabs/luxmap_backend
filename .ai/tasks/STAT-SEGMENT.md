---
ticket: STAT-SEGMENT
title: GET /statistics/repair-timeliness nhận thêm segment_id
status: ready
phase: 2
owner: codex
branch: feat/STAT-segment-repair-timeliness
contract_refs:
  - "Chưa lên Contract — họ ST-* trong docs/contract-drift.md (SELF-SIGNED, nền tạm); fixture-status và warranty đã có segment_id"
depends_on: []
---

## Bối cảnh

WP5 (dashboard) cần số đúng hạn / trễ hạn / mở quá hạn **của từng tuyến**. Hiện `repair-timeliness` chỉ nhận
`commune_id`, FE tự nhân tỷ lệ khi chọn tuyến. Yêu cầu từ FE (đã đánh giá): thêm `segment_id` tùy chọn, giống
`fixture-status` và `warranty`.

## Quyết định đã chốt (Mỹ đồng ý 06/11/2026) — không phải D-item

- Lọc theo **`work_order.segment_id`** — cùng nghĩa với `GET /work-orders?segment_id=` (`WorkOrderService.cs`, dòng
  `query.Where(x => x.SegmentId == segment)`). Phiếu repair tạo từ nhiều sự cố mang tuyến của sự cố ưu tiên cao nhất
  (`Insert`), nên một phiếu đếm vào **đúng một** tuyến, không đếm lặp. Phiếu `segment_id` NULL không bao giờ khớp.
- `segment_id` áp cho **cả ba số**: `completed / on_time / late / no_due_date` **và** `open_overdue`.
- Chuỗi rỗng / khoảng trắng = không lọc (đúng như `fixture-status`: `string.IsNullOrWhiteSpace(segmentId) ? null : segmentId.Trim()`).
- `group_by=segment` vẫn **400** cho endpoint này (ST-9) — không đổi.
- Không có cột / enum / shape response mới. Đây chỉ là tham số query tùy chọn, cộng thêm.

## Phải đọc trước

- `CLAUDE.md` (lõi) + `docs/agents/traps/commune-scope.md` + `docs/agents/traps/testing.md`
- `docs/contract-drift.md` mục ST-1…ST-10 (đặc biệt ST-4…ST-7, ST-9)
- `src/LuxMap.Modules.Statistics/StatisticsController.cs`, `StatisticsService.cs` (`RepairTimelinessAsync`, `Poles(...)` để xem cách `fixture-status` xử lý `segment_id`), `StatisticsContracts.cs`
- `tests/LuxMap.Api.Tests/StatisticsTests.cs` (`PlantOrder`, nhóm test repair-timeliness)

## Yêu cầu

1. Controller: thêm `[FromQuery(Name = "segment_id")] string? segmentId` cho `RepairTimelinessAsync`, chuẩn hoá giống `fixture-status`.
2. `RepairTimelinessQuery` thêm `SegmentId`; service lọc cả truy vấn `finished` lẫn `overdue` trên `repairs` (một chỗ, không lọc hai lần).
3. Test mới trong `StatisticsTests.cs` (đọc tay, assert giá trị cụ thể):
   - hai tuyến cùng xã, phiếu repair trồng ở cả hai: lọc tuyến A chỉ ra số của A; `open_overdue` cũng chỉ của A.
   - phiếu `segment_id` NULL không xuất hiện khi lọc tuyến, nhưng vẫn có khi không lọc.
   - `segment_id` rỗng/khoảng trắng = như không lọc.
   - tuyến không tồn tại / không có phiếu → 200, mọi số 0, `rows` rỗng (hành vi hiện tại của "chỉ trả nhóm có dữ liệu"), KHÔNG 404.
   - tuyến của xã ngoài scope người gọi không rò số (xã khác scope → vẫn bị query filter chặn; không cần 403 mới).
4. **Phá thử** theo CLAUDE.md: bỏ bộ lọc ở `overdue` → test đỏ; bỏ ở `finished` → test đỏ; khôi phục bằng `cp`. Dán output vào results.
5. Tài liệu:
   - `docs/contract-drift.md`: thêm mục mới ngay sau ST-10 (mã **ST-11**, ghi rõ cộng thêm / SELF-SIGNED, quyết định lọc theo `work_order.segment_id`, một phiếu một tuyến, áp cho cả `open_overdue`). Không sửa dòng cũ.
   - OpenAPI: chạy lại theo `.ai/context/commands.md` (xuất `luxmap-v1.json` → `gen_consolidated_spec.py` → redocly lint), commit hai file sinh.
   - `tracking.html`: thêm mục hoàn thành (ngày, task, file, commit).
6. Cập nhật XML-doc của action (`<remarks>`) cho khớp.

Tiêu chí xong — mỗi dòng kiểm chứng bằng một lệnh:

- [ ] `dotnet build --disable-build-servers` không warning mới
- [ ] `dotnet test --disable-build-servers` xanh (đọc mã thoát, không chỉ dòng Passed)
- [ ] Test mới đỏ khi phá thử hai chỗ lọc, xanh sau khi khôi phục
- [ ] `npx @redocly/cli lint docs/openapi/luxmap-v1.5.json` sạch
- [ ] `git diff --stat` chỉ gồm: controller, service, `StatisticsContracts`(nếu cần), `StatisticsTests.cs`, drift, hai file OpenAPI, `tracking.html`, `.ai/results/STAT-SEGMENT.md`

## KHÔNG ĐƯỢC làm

- Không tạo migration; không đổi schema, index, enum, response shape.
- Không thêm `segment` vào `group_by` của endpoint này.
- Không sửa `docs/api-contract-v1.1.md` (statistics chưa nằm trong Contract).
- Không chạm `fixture-status`, `warranty`, citizen, `work-orders/summary` (các ý khác của FE chưa được duyệt).
- Không chạy test trên `luxmap_dev`; không `git push`; không `git add -A` / `git add .` — stage theo đường dẫn.
- Commit message Conventional Commits, **không** dòng `Co-Authored-By`.
- Không tự chọn phương án khác với mục "Quyết định đã chốt"; chỗ mơ hồ ghi thành D-item ở cuối results.

## Kết quả

Ghi `.ai/results/STAT-SEGMENT.md`: đã làm gì, output thật của build/test/phá thử/lint, D-item (nếu có). Dừng sau khi commit (không push).
