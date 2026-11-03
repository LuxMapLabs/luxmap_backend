# POLE-SEGMENT-NULL — kết quả PR 2e · 03/11/2026

Người viết: Claude. Review độc lập: Codex `-p river` (DeepSeek-V4.1-Flash, chỉ đọc, ~930k token) → lần đầu **REQUEST CHANGES** (1 P2 + 8 P3), đã xử lý.

## 1. Thay đổi

- `CreatePoleRequest.SegmentId` tuỳ chọn; `UpdatePoleRequest.SegmentId` là `JsonElement` (khoá bắt buộc, `null` hợp lệ) đọc qua `ReadSegmentId()` ở ĐẦU service (400 trước mọi tra cứu).
- Import poles: `segment_external_ref` bỏ khỏi `RequiredColumns`; ô trống/vắng ⇒ cột mới chưa tuyến, cột đã có giữ tuyến; mã điền mà không khớp, hoặc ô GeoJSON là object/array, là lỗi dòng.
- `GET /assets/poles?unassigned_segment=true`: `ListAsync` nhận `Expression<Func<TEntity,bool>>? filter`, áp trước `CountAsync` và phân trang.
- OpenAPI sinh lại bằng lệnh chuẩn; tài liệu: README template, `poles.example.csv`, `photos_to_poles.py`, drift, trap.

## 2. Bằng chứng

- Build 0 Warning / 0 Error. OpenAPI (`dotnet swagger tofile` + `gen_consolidated_spec.py`) trùng bản đã commit.
- Test mới (`PoleWithoutSegmentWriteTests` 8 test + 1 test spec); phá thử, mỗi lần test ĐỎ đúng:

| # | Đột biến | Test đỏ |
|---|---|---|
| S1 | PUT thiếu khoá bị đọc như "tháo tuyến" | `Update_requires_the_segment_key_…` |
| S2 | Ô trống tháo tuyến của cột ĐÃ CÓ | `Import_leaves_a_blank_cell_unassigned_…` |
| S3 | Header lại đòi `segment_external_ref` | `A_poles_file_without_the_segment_column_…` |
| S4 | Bộ lọc giữ cột ĐÃ có tuyến | `The_unassigned_segment_filter_counts_before_paging_…` |
| S5 | POST: chuỗi trắng đọc như "không tuyến" | `Create_without_a_segment_key_or_with_null_…` |
| S6 | Bỏ bộ lọc | `The_unassigned_segment_filter_counts_before_paging_…` |
| P2 | Ô GeoJSON object/array đọc như ô trống | `A_geojson_segment_reference_that_is_an_object_an_array_or_a_typo_…` |
| P3a | POST tra cứu xã/ref TRƯỚC khi đọc tuyến | `Create_reports_a_malformed_segment_before_it_looks_anything_else_up` |
| P3b | OpenAPI mất `maxLength` của `segment_id` ở PUT | `Pole_segment_is_optional_on_create_…` |

- Toàn bộ test: xem PR (CI) — Api 1263 pass / 7 skip ở lượt trước khi vá review.
- ⚠️ **Test chập chờn, không liên quan cột:** `SurveyIngestTests.Concurrent_create_returns_one_id_and_one_audit_and_conflicting_body_is_409` thất bại 2/5 lượt chạy đầy đủ trên nhánh này (~4 ms), pass 8/8 khi chạy riêng và không thất bại ở 2c / 2d. Chưa bắt được thông báo lỗi.

## 3. Review của Codex (river) và xử lý

| Mức | Phát hiện | Xử lý |
|---|---|---|
| **P2** | GeoJSON `segment_external_ref: {}` / `[]` bị đọc im lặng thành "chưa tuyến" (regression: trước đây `required: true` chặn) | Sửa ở `Resolve` (`IsNonScalar` → lỗi dòng, áp luôn cho feeder / cabinet cùng lớp lỗi) + test + phá thử |
| P3 | `CreatePoleAsync` đọc `segment_id` sau tra cứu (409 che 400) | `ReadSegmentId()` lên đầu + test + phá thử |
| P3 | OpenAPI: PUT mất `maxLength`; create không mô tả; summary không nhắc | Sửa filter + `gen_consolidated_spec.py` + assert trong `OpenApiSpecTests` |
| P3 | Class doc import, docstring `photos_to_poles.py`, ví dụ CSV thiếu cột, `D-20` không tra được, unused `using`, `tracking.html` cũ | Đã sửa |
| P3 | Test thiếu: object/array, > 32 ký tự, GeoJSON typo, thứ tự create | Đã thêm |
| — | Không có P1; audit / `CommuneWriteGuard` / downstream đã xử lý ở 2b–2d | `Pole` là `IUpdateStamped` (không `IAudited`): đổi tuyến ghi `updated_by` / `updated_at`, không có `AuditEvent` — nhất quán hiện trạng |

## 4. Mở

- **D-20 (WP6):** quy tắc thay cache của gói offline, từ 2d.
- Gán / tháo tuyến không có `AuditEvent` riêng (chỉ `updated_by` / `updated_at`). Nếu cần truy vết "ai tháo tuyến cột X" thì phải thêm audit — thay đổi riêng, cần duyệt.
