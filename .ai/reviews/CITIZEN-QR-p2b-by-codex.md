# CITIZEN-QR P2b — review Codex (13/10/2026)

**Cần sửa trước khi bật công khai: 1 P1, 9 P2.** Nền `cc6d514..a1777b7`; đã đọc task, quyết định/P1 sau review, review P1, PB-1…PB-8 và AGENTS. Hai trục Standards/Spec được kiểm độc lập bằng subagent, rồi đối chiếu code. Chỉ ghi file này; không Docker/dotnet, không commit/dashboard.

## Standards — 1 P1, 2 P2

1. **P1 — Cấu hình proxy rỗng lại tin mọi peer.** `src/LuxMap.Api/Http/ForwardedHeadersSetup.cs:38–43,53`: bật forwarding rồi clear cả hai danh sách; ASP.NET Core coi danh sách rỗng là **bỏ kiểm IP**, không phải từ chối tất cả. Client đổi `X-Forwarded-For` để né limiter, giả được cả scheme. Trái PB-7/AGENTS. [Nguồn Microsoft, dòng 196–219](https://github.com/dotnet/aspnetcore/blob/v10.0.11/src/Middleware/HttpOverrides/src/ForwardedHeadersMiddleware.cs#L196). **Sửa:** không áp forwarding khi chưa khai proxy/network; test cấu hình trống với peer loopback và ngoài loopback. Test hiện tại (`CitizenReportTests.cs:257`) luôn khai một proxy nên bỏ sót.

2. **P2 — Cổng decode chưa bao toàn pipeline, hàng chờ giữ ảnh trong RAM.** `src/LuxMap.Modules.Citizen/PublicReportService.cs:80,99–103`; `src/LuxMap.Infrastructure.Storage/JpegSanitizer.cs:29,34,81`: giữ tới 24 MiB ảnh/request và thêm bản buffer trước khi chờ semaphore không giới hạn hàng chờ. Sau nhả cổng, `StoreImageAsync` lại decode toàn ảnh để tạo thumbnail (`S3ObjectStore.cs:33` → `ThumbnailFactory.cs:63`), ngoài cổng; giới hạn hai decode không còn đúng. **Sửa:** admission có hàng chờ hữu hạn trước buffer lớn, và cổng chung cho sanitize/thumbnail hoặc tạo thumbnail từ lần decode đầu. `PhotoStore` có chạy pipeline thật, nhưng test chưa đo concurrency/bộ nhớ.

3. **P2 — Cấu hình được chấp nhận nhưng schema từ chối.** `src/LuxMap.Modules.Citizen/CitizenOptions.cs:29` cho `MaxPhotos` tới 10; `Configurations/CitizenReportConfigurations.cs:113` chỉ cho vị trí 1…3 (PB-2). Cấu hình 4, gửi bốn JPEG nhỏ: qua validate/body cap, ghi object rồi CHECK thất bại → 500. **Sửa:** validate tối đa 3 và kiểm giá trị 4 bị từ chối lúc khởi động.

## Spec/race — 7 P2

4. **P2 — Thu hồi sau lookup vẫn nhận báo cáo.** `src/LuxMap.Modules.Citizen/PublicReportService.cs:79,107–116`: chỉ kiểm `RevokedAt` trước buffer/upload; sau khoá pole không đọc lại QR. Giữ request tại `PhotoStore.DuringWrite`, thu hồi/xoay mã rồi thả: request dùng mã cũ vẫn commit 202. Không đáp ứng việc kiểm lại sau upload đã chốt ở P1. **Sửa:** dưới cùng khoá pole, đọc lại QR active + đúng cột/xã trước ghi; thêm test race. Test revoked hiện chỉ gửi sau khi thu hồi xong.

5. **P2 — Request chậm gây CHECK lỗi thay vì gom/replay.** `src/LuxMap.Modules.Citizen/PublicReportService.cs:85,125,131`: lấy `now` trước upload/khoá. A có ảnh chậm; B đến sau, text-only mở report trước; A ghi `LastSubmittedAt = now` cũ, nhỏ hơn `CreatedAt`, vi phạm CHECK (`CitizenReportConfigurations.cs:28`) → 500. Nhánh bắt unique client-op không bắt lỗi CHECK này. **Sửa:** cập nhật thời điểm cuối bằng max của giá trị đang lưu và thời điểm nhận; ép thứ tự A/B bằng barrier trong test.

6. **P2 — Không tính byte sắp ghi vào ngân sách.** `src/LuxMap.Modules.Citizen/PublicReportService.cs:239–252`: cả hai lượt kiểm chỉ so `bytes >= budget`, bỏ qua byte ảnh mới. Một lượt tuần tự vẫn có thể vượt 512 MiB, ngoài biên đua giữa các xã được AGENTS chấp nhận. **Sửa:** kiểm headroom trước xử lý, cộng byte thực của ảnh sạch + thumbnail dưới khoá trước lưu dòng; vượt thì nhận chữ. Test ngân sách 1 byte + JPEG nhỏ hiện sẽ nhận ảnh thay vì bỏ.

7. **P2 — Ảnh mồ côi không bị quota/budget tính.** `src/LuxMap.Modules.Citizen/PublicReportService.cs:97–103,110–113,172–177`: ảnh đầu đã ghi object/rút CPH sequence khi ảnh sau mới bị từ chối; hoặc nhiều request cùng qua kiểm sớm, ghi object rồi bị bỏ dưới khoá/rollback replay. Quota chỉ đếm dòng đã commit nên có thể lặp làm đầy kho mà số byte DB không tăng. Đây là rủi ro mới của đường ẩn danh; nợ dọn object chung chưa chặn được. **Sửa:** xác minh toàn bộ ảnh trước ghi object, đặt chỗ tài nguyên dùng chung trước upload; hoàn trả/dọn có retry cho lượt thua. Khoá quyết định/replay cần kiểm lại trước rút ID; khoảng trống sequence tự nó không phải lỗi.

8. **P2 — Thiếu FK ghép giữ cùng cột/xã đã chốt.** `src/LuxMap.Modules.Citizen/Configurations/CitizenReportConfigurations.cs:50,98–100`: report→pole và submission→report/QR/pole chỉ dùng ID đơn. Ghi trong scope A vẫn có thể gắn submission xã A tới cha xã B; guard chỉ kiểm `CommuneId` của hàng đang ghi, `HasCommuneReference` chỉ chứng minh xã tồn tại. Trái P1 sau review mục 10. **Sửa:** FK ghép cùng xã/cột cho chuỗi này, test ghi sai cha bị DB từ chối. Endpoint hiện tự lấy ID từ target nên chưa thấy cách client khai thác trực tiếp.

9. **P2 — PLAN lọc con khác tập cha được chép.** `scripts/copy_dev_to_supabase.py:117–121`: submission thiếu điều kiện `revoked_by` của PLAN QR (`:84–85`); photo chỉ lọc xã, không theo toàn bộ điều kiện giữ submission/report. QR bị loại vì người thu hồi không được chép nhưng submission vẫn vào, hoặc submission bị loại nhưng ảnh vẫn vào → FK lỗi, rollback lần copy. **Sửa:** dùng chung tập QR/report/submission được giữ; ảnh chỉ theo tập submission ấy. Thứ tự PLAN hiện đúng.

10. **P2 — Test xanh chưa bảo vệ race/scope và các chốt tài nguyên.** `tests/LuxMap.Api.Tests/CitizenReportTests.cs:74,134,202,296`: gom/replay/quota đều tuần tự, mọi cột cùng xã; không test ngân sách byte, replay đổi ảnh hay token xã A kèm QR xã B. Bỏ khoá pole, catch unique+rollback, lần kiểm quota dưới khoá, byte-budget hoặc ảnh khỏi hash **có thể vẫn xanh các test mới** (nhận định tĩnh, chưa sabotage). **Sửa:** barrier ép cùng qua replay, tranh khoá, spy sanitizer/store; test retry cùng/khác payload có ảnh, hai xã và token kèm theo. Bốn sabotage đã báo không bao phủ các nhánh này.

✅ **Đã kiểm tĩnh:** ghi nghiệp vụ/audit/notification trên context một xã, không backdoor; replay so QR + hash trước trả receipt, chưa thấy trả dữ liệu mã khác; code trong body, audit/object key không chứa code; unknown/revoked cùng truy vấn active và 404; limiter trước model binding, body cap/byte/magic/Identify/pixel trước decode; unique partial pending và rollback client-op có hiện diện. Spec hiện đúng hai route public không capability; teardown đúng photo → submission → report → QR → pole.

⚠️ **Chưa xác minh runtime:** không chạy build/test/migration/sabotage hay đo thời gian/RAM. Các số 912/46/45/383, 2 skip MQTT và migration sạch là kết quả Claude do người dùng cung cấp. 📌 **Nền tạm:** PB-1…PB-8 SELF-SIGNED chờ FW; màn duyệt/AI/retention thuộc P2c/BE-26, không coi thiếu chúng là lỗi P2b.

## Đã xử lý (Claude, 13/10/2026)

Cả 10 phát hiện đã kiểm lại bằng đọc code / nguồn ASP.NET Core và **sửa hết**:

| # | Xử lý | Canh bằng (phá thử → đỏ) |
|---|---|---|
| P1 forwarded rỗng tin mọi peer | Không cấu hình proxy ⇒ middleware không vào pipeline (`TrustedProxies.Any`) | `With_no_proxy_configured_a_forwarded_address_is_ignored_from_every_peer` — bật lại middleware vô điều kiện → đỏ |
| #2 RAM / cổng / thumbnail | Lấy chỗ trong cổng TRƯỚC khi đọc ảnh, hàng chờ có hạn (8) → 503 `SERVER_BUSY`; ảnh sạch thu nhỏ về 2560 px nên decode thumbnail chỉ gặp ảnh nhỏ; ảnh băm dạng luồng, không buffer trước | (bounded queue chưa có test tải — ghi ở kết quả) |
| #3 `MaxPhotos` 10 vs CHECK 3 | `Validate` giới hạn 3 | `More_than_three_photos_is_refused_at_startup_…` |
| #4 thu hồi giữa chừng | Dưới khoá `pole` đọc lại mã còn hiệu lực (thu hồi cùng khoá đó) | `A_code_revoked_while_the_photos_are_being_cleaned_files_nothing` — bỏ lượt đọc lại → đỏ |
| #5 lượt chậm ghi lùi thời gian | `last_submitted_at` = max | `A_slow_send_that_lands_after_a_fast_one_joins_the_item` — ghi đè vô điều kiện → đỏ |
| #6 ngân sách bỏ qua byte mới | Cộng byte sắp ghi (khai báo ở lượt sớm, byte sạch thật dưới khoá) | `Past_the_nights_byte_budget_…` — bỏ phần cộng → đỏ |
| #7 object mồ côi | Làm sạch hết trước, kiểm lại gửi trùng + hạn mức dưới khoá, rồi mới ghi object | `Racing_sends_for_the_last_photo_slot_leave_no_orphaned_object` — bỏ kiểm dưới khoá → đỏ 3/3 |
| #8 FK ghép | `submission → report` và `submission → qr_code` ghép `(id, pole_id, commune_id)` | `A_sends_report_and_code_are_held_…_by_composite_keys` |
| #9 PLAN | Một định nghĩa tập giữ cho QR / báo cáo / lượt gửi; ảnh theo đúng tập lượt gửi | `python3 -m unittest` scripts |
| #10 test | Thêm 8 test trên; gửi lại khác ảnh → 409 | `A_resend_with_a_different_photo_is_409` |

Sau khi sửa: 920 / 46 / 45 / 383 xanh, 2 skip MQTT; migration sinh lại, apply → rollback → apply sạch (3 bảng, 17 index, `ak_qr_code_…`).
Phát hiện thêm khi sửa: Postgres cắt tên định danh ở 63 ký tự — tên FK ghép của EF bị cắt (ghi ở AGENTS.md).
