---
ticket: BE-15-followup
title: Tấm lọc ND trong capture_config (D-R23 B) + baseline đếm theo đêm (D-05)
status: ready
phase: 1
owner: claude
branch: feat/BE-15-followup
contract_refs:
  - "docs/contract-drift.md — BE-15 Phase 1 (02/10) và Quyết định khảo sát 07/10 (D-R23, D-05)"
  - "Survey ingest / review chưa có trong Contract — bề mặt SELF-SIGNED, chờ FW"
depends_on:
  - "D-R23 (chốt 07/10) — hướng B trước"
  - "D-05 đếm theo đêm (duyệt 07/10)"
---

## Bối cảnh

Hai việc khảo sát còn lại sau quyết định 07/10, gộp chung vì cùng đụng dựng baseline lúc duyệt phiên
(`SurveyReviewService.BuildBaselines`) và tra baseline lúc xử lý (`SurveyBaselineLookup`).

1. **Tấm lọc ND (D-R23 hướng B).** WP4 cần chứng minh độ nhạy cảm biến + thuật toán `dim` bằng tấm lọc
   độ truyền 0,9 / 0,8 / 0,7 trên cột **đã có baseline**. BE phải xong **trước buổi quay có tấm lọc**:
   khai tấm lọc trong `capture_config`, loại lượt có tấm lọc khỏi baseline, xuất tỉ lệ kỳ vọng.
2. **Đếm baseline theo đêm (D-05).** `SurveyPublicationRules.Members` hiện `GroupBy(RunId)`: ba phiên
   trong một đêm đã đủ baseline. Luật mới: mỗi đêm góp **tối đa một** thành viên (mỗi cột / chiều), và
   baseline dùng để phân loại chỉ dựng từ **các đêm trước**.

Không còn mục `lux_sensor`: D-R20 chốt 07/10 **giữ `source_channel = cv`**.

Consumer: WP4 (buổi quay B, chấm `dim`), FO (lịch quay — số đêm cần cho baseline).

## Phải đọc trước

- `docs/contract-drift.md` — mục "BE-15 Phase 1 — chốt" và "Quyết định khảo sát 07/10/2026"
- `docs/field/BE-15-pilot-drive.md` v3 (quy trình đi–về mỗi đêm)
- `CLAUDE.md` — "Quy ước cho MỌI cột `double precision` đo được", "ĐỌC migration sinh ra TRƯỚC khi apply",
  "Phiên đêm cắt qua nửa đêm"
- `src/LuxMap.Modules.Survey/Ingest/SurveyRawParser.cs` (kiểm `capture_config`)
- `src/LuxMap.Modules.Survey/Review/SurveyPublicationRules.cs` (`Eligible`, `Members`, `Median`)
- `src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs` (`BuildBaselines`, `Publish`)
- `src/LuxMap.Modules.Survey/Review/SurveyBaselineLookup.cs` (`o.ObservedAt < query.Before`)
- `src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs` (`NightOf`, `NightStartsAt` 12:00 Asia/Ho_Chi_Minh) —
  Survey đã tham chiếu WorkOrders, **dùng lại**, đừng định nghĩa "đêm" lần thứ hai

## Câu hỏi Phase 1 phải trả lời (ghi D-item, không tự chọn)

**Tấm lọc**
- F-1. Tấm lọc khai ở **cấp phiên** (`capture_config`) hay **cấp cột / lượt**? Buổi quay B đặt tấm lọc trên
  module lux (cả phiên) hay chỉ khi qua một số cột? Cấp phiên đơn giản hơn nhưng buộc phiên B chỉ đi các
  cột đã có baseline.
- F-2. 🔴 **Lượt có tấm lọc có được CÔNG BỐ không?** Duyệt phiên hiện ghi `luminance_history`, đổi
  `pole_current_status` và sinh sự cố `lamp_dim`. Đèn thật vẫn sáng bình thường; công bố lượt có tấm lọc
  sẽ để một sự cố `dim` giả và trạng thái giả trên cột thực địa. Cần chọn: không công bố trạng thái / sự cố
  (chỉ lưu kết quả để chấm), hay công bố với `data_source` khác — đối chiếu luật tách `data_source`
  (dữ liệu tấm lọc **không** phải `field` thuần?).
- F-3. "Tỉ lệ kỳ vọng" xuất ở đâu: `GET /sweeps/{id}/results` (thêm trường `expected_ratio`?) hay chỉ trong
  báo cáo WP4? Thêm trường là **chạm API** → SELF-SIGNED, báo WP5.
- F-4. Giá trị hợp lệ: `(0, 1]`, hữu hạn; vắng = không có tấm lọc. Có cần lưu thành cột (CHECK NaN/Infinity
  theo quy ước) hay đọc lại từ object `capture_config`?

**Đếm theo đêm**
- N-1. Đêm của một lượt tính theo `ObservedAt` của quan sát hay theo giờ bắt đầu phiên? (Một phiên cắt qua
  12:00 trưa là không thực tế, nhưng phải nói rõ.)
- N-2. Hai phiên cùng đêm cùng chiều: chọn thành viên nào — `Best` hiện có, áp trên nhóm theo đêm?
- N-3. "Chỉ từ đêm trước": đổi `Before` của `SurveyBaselineLookup` thành mốc bắt đầu đêm hiện tại, hay
  thêm điều kiện riêng? Ảnh hưởng tới duyệt muộn (D-08) và baseline dựng hôm nay từ ảnh cũ.
- N-4. Baseline đã dựng theo luật cũ trên DB dev / Supabase: dựng lại hay để nguyên (baseline là lịch sử
  bất biến)? Hiện có bao nhiêu baseline có ≥ 2 thành viên cùng đêm — **đo, dán output**.
- N-5. Có cần migration không? (Dự kiến: đếm theo đêm thì không; tấm lọc thì tuỳ F-4.)

## Yêu cầu (Phase 2, sau khi D-item chốt)

1. `capture_config` nhận trường tấm lọc theo F-1/F-4; giá trị sai → 400 có `reason` như các trường khác.
2. Lượt có tấm lọc **không bao giờ** thành thành viên baseline.
3. Tỉ lệ kỳ vọng xuất theo F-3; hành vi công bố theo F-2.
4. `Members` nhóm theo đêm, tối đa một thành viên mỗi đêm / chiều.
5. Tra baseline chỉ nhận baseline có mọi thành viên thuộc các đêm **trước** đêm của lượt đang phân loại.

Tiêu chí xong:

- [ ] `dotnet test` xanh trên `luxmap_test`
- [ ] Test: ba phiên đã duyệt **cùng một đêm** → chưa có baseline; ba đêm khác nhau → có
- [ ] Test: phiên đêm thứ tư phân loại được; một phiên khác **cùng đêm** với một thành viên không dùng
      baseline chứa thành viên đó
- [ ] Test: hai lượt hai bên nửa đêm (23:30 và 00:30 giờ VN) là **cùng một đêm**
- [ ] Test: lượt có tấm lọc không vào `baseline_member`, kể cả khi `classified_as = normal`
- [ ] Test sabotage: bỏ điều kiện tấm lọc trong `Eligible`/`Members` → test trên đỏ
- [ ] Nếu có migration: đã đọc `Up()`/`Down()`, không `DropIndex` lạ; bảng mới (nếu có) vào `PLAN` của
      `scripts/copy_dev_to_supabase.py`
- [ ] Drift log ghi bề mặt mới (SELF-SIGNED); `openapi` sinh lại nếu chạm API; `tracking.html` cập nhật

## KHÔNG ĐƯỢC làm

- Không thêm giá trị enum nào (`source_channel` giữ nguyên — D-R20).
- Không sửa `docs/api-contract-v1.1.md`; bề mặt mới ghi drift, SELF-SIGNED.
- Không định nghĩa "đêm" lần hai — dùng `WorkOrderAgenda`/options sẵn có.
- Không sửa / xoá `luminance_baseline` hay `baseline_member` đã có trên DB (bất biến) khi chưa chốt N-4.
- Không đụng ánh xạ video (D-06/D-07) hay chặn `VIDEO_DEVICE_MAPPING_PENDING`.
- Không gán `data_source = field` cho dữ liệu tấm lọc khi F-2 chưa chốt.

## Phase 1 dừng ở đâu

Trả lời F-1…F-4 và N-1…N-5 kèm bằng chứng (dòng code, output truy vấn), ghi
`.ai/results/BE-15-followup-p1.md`, **dừng**, chờ Mỹ chốt.
