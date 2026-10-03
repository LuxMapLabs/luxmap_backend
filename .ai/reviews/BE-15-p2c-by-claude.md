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

## Vòng 2 (Claude) và review độc lập (Codex `exec review`)

- R1–R4 đã sửa; Claude phá thử trên PostGIS: cho `dim` làm member ⇒ `Accepted_dim_sweeps_do_not_move_the_baseline` +
  `Gradual_dimming_does_not_lower_its_own_baseline` đỏ; lookup bỏ lọc bóng ⇒ `Replacing_fixture_invalidates_old_baseline_until_three_new_members`
  + `Replacement_lookup_uses_only_the_active_fixture` đỏ. Đã khôi phục. Api 577, Persistence 43, Shared 312 (môi trường sạch), Storage 39.
- OpenAPI xuất lại; `gen_consolidated_spec.py` thiếu SUMMARY cho 3 endpoint P2c (KeyError) — đã thêm, lint hợp lệ.
- **R5 (review độc lập, xác nhận đúng):** kiểm tương thích cột (`SURVEY_SCOPE_CHANGED`) chạy cả khi **trả lại** — sửa `data_source`
  của một cột sau khi xử lý làm phiên kẹt `awaiting_review`. Claude sửa: khoá cột + kiểm chỉ khi `accept`. Test
  `A_corrected_pole_blocks_accept_but_not_return`; phá thử (kiểm luôn chạy) ⇒ đỏ. Api 578/578.

## Vòng 3 — R6 (review độc lập lần 2, Claude xác nhận đúng; Codex sửa)

**R6 (cao) — cột dự kiến mà phiên không quan sát được không được công bố `unknown`.** Contract mục 1: `unknown` = *sweep gần nhất
không phủ được cột đó*; Phase 1 §2.5 đã ghi "một sweep vẫn ghi kết quả unknown cho tập dự kiến hợp lệ", `observation_id` NULL
**chỉ** cho trường hợp này. Code hiện chỉ dựng lựa chọn công bố từ `pole_observation`, nên cột trong `run.GisSnapshot.poles`
mà xe không đi qua (phiên dừng giữa đường) giữ nguyên `normal/dim/out` cũ trên bản đồ sau khi duyệt.

Sửa:
1. `luminance_history.observation_id` → nullable (sửa chính migration `AddSurveyPublication` chưa merge, sinh lại bằng
   `ConnectionStrings__LuxMap` giả, giữ nguyên SQL trigger viết tay, đọc lại Up/Down). CHECK mới: `observation_id IS NOT NULL OR
   (classified_as = 'unknown' AND peak_lux IS NULL AND cv_state IS NULL AND baseline_id IS NULL AND baseline_ratio IS NULL
   AND status_confidence IS NULL)`. FK ghép tới `pole_observation` vẫn `MATCH SIMPLE` (null bỏ qua) — đừng siết `MATCH FULL`.
2. Khi accept: tập công bố = **mọi cột trong `run.GisSnapshot.poles`** ∪ cột có observation. Cột dự kiến không có observation ⇒
   history `unknown`, `observation_id` null, lý do `["not_observed"]`, `evaluated_at` = thời điểm kết thúc phiên (cùng cách tính
   `ended_at` của `GET /sweeps`, không phải giờ duyệt); `commune_id` lấy từ bảng `pole` (đã khoá). Cập nhật `pole_current_status`
   theo đúng luật D-08 (chỉ khi mới hơn). Không sinh sự cố, không dựng baseline cho các cột này.
3. Phạm vi người duyệt (`RequireWholeRun`) và lượt khoá/kiểm tương thích cột (chỉ khi accept) phải phủ **cả** cột dự kiến không
   quan sát, không chỉ cột có observation — nếu không, người duyệt thiếu xã của một cột không quan sát vẫn công bố được `unknown`
   lên cột đó. Cột dự kiến đã bị xoá khỏi `pole` ⇒ `SURVEY_SCOPE_CHANGED` như hiện tại.
4. `GET /sweeps/{id}/results` hiện chỉ liệt kê observation; thêm các cột dự kiến không quan sát (một item mỗi cột, các trường đo
   null, `published_as = unknown`, `reason_codes = ["not_observed"]`) để Quản lý thấy trước — giữ phân trang ổn định
   (thứ tự: theo thời điểm, cột không quan sát xếp cuối theo `length(pole_id), pole_id`). Nếu record `SurveyResultItem` cần
   `observation_id`/`pass_id` nullable thì đổi, cập nhật spec (`gen_consolidated_spec.py` không có schema viết tay cho nó).
5. Test (DB, Claude chạy): phiên phủ 1/2 cột dự kiến ⇒ cột còn lại có history `unknown`/`not_observed` và trạng thái hiện tại
   đổi từ trạng thái cũ sang `unknown`; phiên muộn hơn (D-08) không đè; người duyệt thiếu xã của cột không quan sát ⇒ từ chối;
   results liệt kê cột không quan sát; CHECK từ chối hàng `observation_id` null mà `classified_as` khác `unknown`.
   Test không DB cho phần dựng tập công bố. Phá thử từng luật.
