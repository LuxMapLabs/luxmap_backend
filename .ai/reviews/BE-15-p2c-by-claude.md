# Review BE-15 P2c — vòng 1 (Claude, 04/10/2026)

Đã chạy trên PostGIS `luxmap_test`: migration apply → rollback → apply sạch, `has-pending-model-changes` không có thay đổi,
migration chỉ AddColumn/CreateTable/Index/Check/FK (không Drop lạ, không xmin, không defaultValue).

## Đã sửa sẵn (Claude)

- **R0 — truy vấn chống sự cố trùng không dịch được** (`SurveyReviewService.Publish`): `(f.OverrideFaultType ?? f.FaultType) == type`
  và `FaultStatusSets.Open.Contains` trên `IReadOnlySet` ⇒ `InvalidOperationException` ở mọi lượt accept có `out`/`dim`; 6/11 test
  `SurveyPublicationTests` (API) đỏ. Đã viết lại hai nhánh override/không override, tập mở lấy từ `FaultStatusSets.Open.ToArray()`
  (khuôn BE-40). Sau sửa: Api 575/575, Persistence 43, Shared 308, Storage 36.
  ⚠️ Bài học: chạy được test không DB không nói gì về khả năng EF dịch truy vấn.

## Cần sửa

- **R1 (cao) — quan sát `dim` đang được nhận làm member baseline.** `SurveyPublicationRules.Eligible` chỉ đòi `ClassifiedAs != Unknown`,
  nên đèn mờ dần kéo baseline của chính nó xuống, và sau vài phiên được duyệt, đèn mờ lại thành `normal` — phát hiện `dim` tự xoá
  mình. Member chỉ được là quan sát **`normal`** (gồm `normal` chưa đánh giá độ sáng của các phiên đầu — không có chúng thì không bao
  giờ có baseline). Test: chuỗi phiên mô phỏng đèn mờ dần — sau khi đã có baseline, các quan sát `dim` không vào member, phiên kế tiếp
  vẫn `dim`. Phá thử: nhận lại `dim` làm member ⇒ test đỏ.
- **R2 (trung bình) — baseline trộn đỉnh lux của các bóng khác nhau.** Thay bóng (`fixture.removed_date` + bóng mới) thì đèn khác công
  suất/quang thông; member trước ngày lắp bóng đang dùng phải bị loại. Lọc member: `observed_at` ≥ `install_date` của bóng **đang dùng**
  trên cột (nếu cột có bóng và có `install_date`; không có thì không lọc, ghi rõ trong results). Ghi `fixture_id` của bóng đang dùng vào
  baseline nếu bảng có cột đó (§2.5 có `fixture_id`; nếu migration chưa có thì thêm). Lookup phân loại cũng chỉ lấy baseline của bóng
  đang dùng. Test: thay bóng ⇒ baseline cũ không còn được dùng, cần đủ member mới.
- **R3 (thấp) — thumbnail thiếu object trả 500.** `OpenAsync` trên key không tồn tại ném `AmazonS3Exception` ⇒ 500 `INTERNAL_ERROR`.
  Task đòi 503 (storage cần vận hành xử lý) — dịch lỗi not-found thành `LuxMapException` 503 mã `STORAGE_OBJECT_MISSING` ở `Thumbnail`,
  hoặc ở adapter nếu có chỗ chung hợp lý. Test không cần MinIO thật (fake store).
- **R4 (thấp) — Quản lý không xem trước được thứ sẽ công bố.** `GET /sweeps/{id}/results` trả từng lượt quét; cột có hai lượt mâu
  thuẫn ON/OFF sẽ được công bố `unknown` (`on_off_conflict`) mà màn duyệt không thấy. Thêm vào mỗi item `published_as`
  (= `SurveyPublicationRules.Choose` trên mọi quan sát của cột trong run) và `is_representative` (quan sát này có phải quan sát được
  chọn công bố không). Cập nhật spec hợp nhất / tài liệu nếu có schema viết tay.

Sau khi sửa: build 0 warning, test không DB xanh, cập nhật `.ai/results/BE-15-p2c.md` (mục vòng 2). Không chạy test DB, không commit.
