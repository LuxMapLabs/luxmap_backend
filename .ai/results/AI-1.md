# AI-1 — model YOLO ON / OFF (PR #118 làm lại) · 08/10/2026

Nhánh `feat/ai-yolo-detector`, mở đầu bằng **commit gốc của người viết #118** (giữ tác giả), rồi sửa theo review của Claude + Codex.
Drift AI-1…AI-3. ⚠️ SELF-SIGNED.

## Review #118 (Codex, đối chiếu code — đều đúng)

| Mức | Vấn đề | Đã sửa |
|---|---|---|
| P1 | `[AllowAnonymous]` cấp class — người lạ chạy decode + inference | `ReviewSurveys` (Quản lý) |
| P1 | Decode cấu hình mặc định, tin `Content-Type` ⇒ BigTIFF giả JPEG tới decoder lỗi (GHSA-wmxv-xphr-5c9g) | Magic bytes + `JpegOnly`; test 415 |
| P2 | Lỗi `{message}` thay envelope | `LuxMapException` |
| P2 | Pipeline khảo sát không dùng model (`DETECTOR_NOT_CONFIGURED`) | `YoloOnOffDetector` + `Detector = yolo` |
| P2 | Không ghi phiên bản model (BE-34) | `artifact_version` băm model + cấu hình |
| P2 | Không giới hạn pixel / đồng thời / huỷ | 40 MP (đọc header trước decode), semaphore 2, `RunOptions.Terminate` |
| P2 | Resize bicubic ≠ Ultralytics bilinear; parser nhận mọi shape | `Triangle`; kiểm shape + tên lớp lúc nạp |
| P3 | Model 10,6 MB trong git | Giữ tạm (Mỹ chọn), ghim SHA-256 |

Giữ nguyên từ #118: đọc output `[1,6,8400]`, tính ngược letterbox, NMS theo lớp — Codex xác nhận đúng; tách thành
`YoloPostprocess` (hàm thuần) để test bằng tensor dựng tay. Bỏ route không phiên bản `/api/ai`.

## Codex review lần 2 (sau sửa) — 0 P1 · 2 P2 · 1 P3, đều đúng, sửa hết

| # | Sửa | Canh bằng |
|---|---|---|
| P2 | Chuẩn hoá bbox tính bằng float ⇒ bbox 128→640 trên ảnh rộng 640 cho x + w > 1, `DetectorValidation` loại cả frame. Nay tính bằng double, kẹp, hạ 1 ulp nếu còn vượt | `A_box_touching_the_edge…` (4 ca; về float → 2 ca đỏ) |
| P2 | Semaphore chỉ bọc inference, decode 40 MP chạy song song không giới hạn | Gate bọc decode + resize + inference (đọc code) |
| P3 | Thiếu ca JPEG hỏng, vượt pixel, huỷ | `A_corrupt_jpeg_is_a_415_too_many_pixels_a_400_and_a_cancelled_call_stops` |

## Bằng chứng

- **1284/1284 xanh** (+14 `AiDetectionTests`); OpenAPI chỉ thêm 1 path + 3 schema.
- Phá thử → đỏ đúng test: decode mặc định + bỏ magic bytes (`JpegOnlyDecodeTests`, test 415); `[AllowAnonymous]`; bỏ padding letterbox;
  bỏ ngưỡng khỏi artifact.

## Còn lại

- Đối chiếu độ chính xác với Ultralytics trên **mẫu chuẩn của WP4** (cùng ảnh → cùng bbox) — chưa có ảnh đèn thật trong repo.
- LFS / MinIO cho model; nâng `Microsoft.ML.OnnxRuntime` 1.20.1 → 1.30 (để quyết riêng); đóng #118 sau khi PR này merge.
