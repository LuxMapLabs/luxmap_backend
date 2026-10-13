# CITIZEN-QR — P2b: form công khai, không token (Claude, 13/10/2026)

Nhánh `feat/CITIZEN-QR-p2b` (từ `origin/dev` @ `cc6d514`). Quyết định: D-1…D-19 + phần "Sau review Codex" của Phase 1 (Mỹ chốt 12/10).
Drift **PB-1…PB-8** (SELF-SIGNED). Duyệt / tạo sự cố / AI là P2c.

## Đã làm

| Phần | File |
|---|---|
| **Forwarded headers** (D-8): `ForwardedHeaders:KnownProxies` / `KnownNetworks`; **không cấu hình ⇒ middleware tắt** (danh sách rỗng mà bật thì ASP.NET Core tin mọi peer — review P1); có cấu hình ⇒ đúng các proxy đó, đứng đầu pipeline | `Api/Http/ForwardedHeadersSetup.cs`, `Program.cs` |
| Hai policy giới hạn theo địa chỉ client: `public-lookup` 30/10 phút, `public-report` 10/giờ (cấu hình được) | `RateLimitSetup.cs`, `LuxMapRateLimits.cs` |
| Bảng `citizen_report` (mục hàng chờ: cột + đêm, `pending`, cột quyết định cho P2c, CHECK trạng thái ↔ cột, FK ghép `(fault_id, commune_id)`), `citizen_submission` (mỗi lượt gửi, `client_op_id` + `request_hash`, vị trí cùng có / cùng vắng, `far_from_pole`, `photos_dropped`), `citizen_photo` (≤ 3 / lượt). Unique partial `(pole_id, night_of) WHERE status='pending'` + index đầy đủ `pole_id`. Mọi FK `Restrict` | `Entities/CitizenReport.cs`, `Configurations/CitizenReportConfigurations.cs`, migration `AddCitizenReports` |
| Audit actor **`public`** (D-15), entity `citizen_report`; thông báo `citizen_report_received` | `AuditEvent.cs`, `Notification.cs`, `CitizenNotices.cs` |
| `CitizenScopeFactory`: tra mã / gửi lại **không lọc**, mọi thứ khác trong context **đúng một xã** (review P1 #1) | `CitizenScopeFactory.cs` |
| `IImageSanitizer` / `JpegSanitizer`: chỗ trong cổng (2 xử lý + 8 chờ, quá → 503 `SERVER_BUSY`) **trước** khi đọc ảnh vào RAM → đọc giới hạn byte → magic bytes → `Identify` + giới hạn pixel → decode → `AutoOrient` → thu nhỏ về 2560 px → bỏ EXIF/XMP/IPTC/ICC → JPEG q90 (review P1 #3, D-10; review P2b #2) | `Shared/Storage/ImageSanitizing.cs`, `Infrastructure.Storage/JpegSanitizer.cs` |
| `PublicReportService` + `PublicController`: `POST /public/qr/lookup`, `POST /public/reports`; mã trong **body**; ảnh băm dạng luồng; hạn mức ảnh kiểm trước decode và lại dưới khoá `pole` (cộng byte thật); dưới khoá đọc lại mã còn hiệu lực và gửi lại; object ghi dưới khoá nên lượt thua không để ảnh mồ côi; quá hạn mức thì nhận chữ, bỏ ảnh (review P1 #5); gửi lại gắn mã + băm nội dung (P1 #4); mỗi lượt một hàng (P1 #4–#5, D-6). FK ghép giữ lượt gửi cùng cột + xã với báo cáo và mã (review P2b #8) | `PublicReportService.cs`, `PublicController.cs`, `CitizenOptions.cs` |
| Bucket `luxmap-citizen` (sidecar compose) | `ObjectStorage.cs`, `docker-compose.yml` |
| Test: `CitizenReportTests` (20), `AnonymousEndpointTests` (nêu đích danh 2 route ở cả hai phép kiểm), `OpenApiSpecTests` (ngoại lệ `/public/`), bảng tiền tố (23) | tests |
| Spec xuất lại + generator (`security: []`, tag `Public`); `PLAN` của `copy_dev_to_supabase.py` (3 bảng); teardown fixture | `docs/openapi/`, scripts, tests |

## Bằng chứng

```
dotnet build LuxMap.slnx -warnaserror      Build succeeded
Api 920 ✓ · 2 skip (MQTT E2E) · Storage 46 ✓ · Persistence 45 ✓ · Shared 383 ✓   (container BE-36, sau review)
Redocly: valid, 8 warnings (như trước)
```

Migration (DB dùng một lần, sinh lại sau review): apply → 3 bảng, 17 index, `ak_qr_code_qr_id_pole_id_commune_id`, `ck_audit_event_actor_kind` có `public`; rollback → `AddQrCodes`: 0 bảng, CHECK
không còn `public`, không còn khoá `ak_qr_code_…`; apply lại khớp lần đầu. Đọc migration trước khi apply: chỉ tạo sequence / bảng / index /
một unique constraint trên `qr_code` và dựng lại 4 CHECK của audit + thông báo; không `DropIndex` / `DropColumn`; `Down()` đối xứng.

Phá thử (khôi phục bằng `cp` + `touch`):

| Phá | Test đỏ |
|---|---|
| Bỏ giới hạn pixel trong sanitizer | `A_photo_over_the_pixel_cap_is_refused_from_its_header_and_nothing_is_written` |
| Giữ EXIF | `Stored_photos_carry_no_exif_and_no_gps` |
| Nhận mã đã thu hồi | `Malformed_unknown_and_revoked_codes_get_the_same_404` |
| Tin mọi `X-Forwarded-For` (`0.0.0.0/0`) | `Forwarded_addresses_count_only_from_a_trusted_proxy` |
| Bật middleware khi không khai proxy | `With_no_proxy_configured_a_forwarded_address_is_ignored_from_every_peer` |
| Bỏ lượt đọc lại mã dưới khoá | `A_code_revoked_while_the_photos_are_being_cleaned_files_nothing` |
| Ghi đè `last_submitted_at` vô điều kiện | `A_slow_send_that_lands_after_a_fast_one_joins_the_item` |
| Ngân sách không cộng byte mới | `Past_the_nights_byte_budget_the_text_is_kept_and_no_object_is_written` |
| Bỏ kiểm hạn mức dưới khoá | `Racing_sends_for_the_last_photo_slot_leave_no_orphaned_object` (đỏ 3/3) |

## Review

Codex (`.ai/reviews/CITIZEN-QR-p2b-by-codex.md`, ~101k token): 1 P1 / 9 P2 — **sửa hết**, mỗi bản sửa có test và phá thử đỏ (bảng ở
cuối file review). Đáng kể nhất: forwarded headers với danh sách rỗng tin mọi peer; mã thu hồi giữa chừng vẫn được nhận; object mồ côi.

## Giới hạn đã biết

- Cổng làm sạch giới hạn 2 ảnh đang xử lý + 8 chờ; chưa có test tải đo bộ nhớ thật.
- Ngân sách byte toàn hệ thống không tuần tự hoá giữa các xã — có thể vượt bằng ảnh của các lượt đang chạy cùng lúc ở hai xã.
- Object chỉ mồ côi khi ghi dòng thất bại sau khi đã ghi object (BE-11 quy tắc 3); chưa có job đối chiếu (BE-35).
- Tự xoá ảnh / báo cáo cũ: nợ, chờ thao tác delete + job nền BE-26.
- Không CAPTCHA (D-13).
- WP5 chưa có trang `/r/:code`; chưa báo.
