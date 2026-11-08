---
ticket: ACCT-USERNAME
title: POST /admin/users — server tự sinh username từ họ tên (tên + chữ đầu họ/đệm + số thứ tự)
status: ready
phase: 2
owner: codex
branch: feat/ACCT-username-generated
contract_refs:
  - "Chưa lên Contract — SELF-SIGNED. Mã drift: ACCT-2 (kiểm docs/contract-drift.md trước, dùng số trống kế tiếp nếu ACCT-2 đã có)"
depends_on: []
---

## Bối cảnh

FE (WP5) xin bỏ ô nhập username khi Quản trị tạo tài khoản. Mỹ chốt (08/11/2026): **server tự sinh username từ `full_name`**
và gửi trong mail mời. Tên đăng nhập hiện do client gửi (`CreateUserRequest.Username`, bắt buộc); `UpdateUserRequest`
không có username (D-11, username không bao giờ đổi) — giữ nguyên.

## Quyết định đã chốt (Mỹ, 08/11/2026) — không phải D-item

- **Quy tắc sinh**, ví dụ `Nguyễn Hữu Mỹ` → `mynh1`:
  1. Chuẩn hoá `full_name`: NFD, bỏ dấu kết hợp, `đ/Đ` → `d`, hạ chữ thường; tách theo khoảng trắng và dấu gạch ngang.
  2. **Tên** = từ cuối. **Chữ đầu họ + đệm** = chữ cái đầu của các từ còn lại, theo thứ tự.
  3. **Gốc** = tên + các chữ đầu (`my` + `n` + `h` = `mynh`). Tên một từ → gốc chính là từ đó (`my`).
  4. **Hậu tố số** = (số lớn nhất đang có của username khớp đúng `^<gốc>[1-9][0-9]*$`, so không phân biệt hoa thường) + 1; chưa có ai → `1`.
     `Đỗ Văn Đức` → `ducdv1`; người thứ hai cùng gốc → `ducdv2`.
- **Chỉ áp cho tên Việt** (Mỹ chốt): không cố nhận diện tên nước ngoài. Sau bước chuẩn hoá, nếu `full_name` còn ký tự ngoài `a-z`/khoảng trắng/gạch ngang (chữ Hán, Kirin, chữ số, ký hiệu…) hoặc gốc rỗng → 400 `VALIDATION_FAILED`, `details.field = "full_name"`, thông điệp nói rõ vì sao. Tên Latin nước ngoài vẫn đi qua cùng quy tắc (không phân biệt được) — ghi vào XML-doc/drift, không xử lý riêng.
- **Client gửi `username`** (khoá có mặt, kể cả `null`/chuỗi rỗng → dùng `OptionalJson.Present`) → 400 `SERVER_OWNED_FIELD`, như `WorkOrderService.Create`. Vì thế **giữ** thuộc tính `Username` trên `CreateUserRequest` (nullable, bỏ `[Required]`/`[MinLength]`, XML-doc ghi "server-owned") — nếu bỏ hẳn, field lạ bị bỏ qua im lặng (`LuxMapJsonOptions` không bật `UnmappedMemberHandling`). Đọc `src/LuxMap.Shared/Http/OptionalJson.cs` xem cách phân biệt vắng mặt / `null`.
- **Mail mời giữ nguyên luồng** (link một lần, người dùng tự đặt mật khẩu — D-R11 / D-1). Mail đã in username (`AccountMailer.Compose` → `AccountMailTemplate.Render`); chỉ chỉnh câu chữ nếu bản hiện tại không nói rõ "tên đăng nhập của bạn là …". KHÔNG gửi mật khẩu qua mail, KHÔNG thêm mật khẩu mặc định.
- Response `CreateUserResponse.User.Username` trả username đã sinh (đã có sẵn).
- Tài khoản cũ giữ nguyên username; seed `admin/agency/engineer/crew` không khớp mẫu `<gốc><số>` nên không ảnh hưởng. Không migration, không đụng schema.

## Phải đọc trước

- `CLAUDE.md` (lõi) + `docs/agents/traps/auth-accounts.md` + `docs/agents/traps/testing.md` + `docs/agents/traps/migrations.md` (mục khoá / ghi đồng thời)
- `src/LuxMap.Modules.Identity/Accounts/{AccountContracts,UserAdminService,AdminUsersController,AccountMailTemplate,AccountMailer}.cs`
- `src/LuxMap.Modules.Identity/Entities/AppUser.cs`, `Configurations/IdentityConfigurations.cs` (unique index username)
- `src/LuxMap.Shared/Http/OptionalJson.cs`, `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs` (`Create`, mẫu `SERVER_OWNED_FIELD`)
- `tests/LuxMap.Api.Tests/AccountAdminTests.cs`, `AccountTestFactory.cs`, `tests/LuxMap.Shared.Tests/AccountMailTemplateTests.cs`
- `docs/contract-drift.md` mục ACCT-1 và D-11 của tài khoản (chỗ nói username không đổi)

## Yêu cầu

1. Hàm sinh username là **một chỗ duy nhất**, thuần (chuẩn hoá + tách + ghép gốc) tách khỏi truy vấn số thứ tự; đặt trong module Identity (vd `UsernameGenerator`), test đơn vị không cần DB cho phần thuần.
2. `UserAdminService.CreateAsync`: bỏ `request.Username!.Trim()`; từ chối `username` có mặt (400 `SERVER_OWNED_FIELD`); sinh username; `RequireFreeAsync` vẫn kiểm email và (như lưới an toàn) username.
3. **Đồng thời:** hai admin tạo hai người cùng gốc cùng lúc phải ra hai username khác nhau, không 500, không trùng. Unique index (`IdentityConfigurations.cs`) là chốt cuối; lấy số kế tiếp phải được khoá (ví dụ `pg_advisory_xact_lock` theo gốc, hoặc khoá hàng theo mẫu `ExecuteSql … FOR UPDATE` trong CLAUDE.md — chọn một, ghi lý do trong results). Chú ý `CreateAsync` hiện không mở transaction tường minh: khoá advisory-xact cần transaction bao từ lúc tính số tới `SaveChanges`. Nếu phải thêm transaction, giữ `SaveAsync` hiện có (đọc nó trước: có thể đã bắt lỗi unique).
4. Controller: `[ProducesResponseType]` 400 đã đủ; cập nhật XML-doc/remarks của `POST /admin/users` (không còn nhận username; quy tắc sinh; tên Việt).
5. Mọi nơi khác tạo tài khoản qua API và đang gửi `username` phải sửa: grep `"username"` trong `tests/`, `scripts/`, `docs/`, `README.md`, `.ai/context/commands.md`, seed/demo (đừng sửa `IdentitySeeder` — nó chèn thẳng, không qua API). `AccountTestFactory` và các test đang tạo tài khoản qua `POST /admin/users` phải chuyển sang không gửi username và lấy từ response.
6. Test (assert giá trị cụ thể):
   - Thuần: `Nguyễn Hữu Mỹ` → gốc `mynh`; `Đỗ Văn Đức` → `ducdv`; `Trần Thị Bích Ngọc` → `ngoctb`; `Lê  Văn-Tám` (khoảng trắng kép, gạch ngang) → `tamlv` (hoặc kết quả đúng quy tắc — nêu rõ); tên một từ `Mỹ` → `my`; có dấu thanh tổ hợp sẵn lẫn dạng NFD cho cùng một tên → cùng gốc.
   - API: tạo `Nguyễn Hữu Mỹ` lần đầu → `mynh1`; lần hai (email khác) → `mynh2`; một tài khoản có sẵn `mynh7` (chèn trực tiếp hoặc tạo trước) thì người kế tiếp → `mynh8`; `MYNH3` chữ hoa có sẵn vẫn được tính (so không phân biệt hoa thường). Tiền tố tài khoản / xã riêng của class test để không đụng test khác (CLAUDE.md mục Test).
   - `username` có mặt trong body (chuỗi, `null`, rỗng) → 400 `SERVER_OWNED_FIELD`, **không tạo hàng nào**.
   - `full_name` chứa chữ Hán / chữ số → 400 `VALIDATION_FAILED` `details.field = full_name`; không tạo hàng, không gửi mail.
   - Đồng thời: N (≥ 5) request `POST /admin/users` song song cùng một họ tên (email khác nhau) → tất cả 201, username là `mynh1..mynhN` không trùng. Test này phải thực sự song song (`Task.WhenAll`), dựng client không dùng chung từ điển lười như lỗi ở `.ai/tasks/TEST-WO-PARALLEL.md` — đọc ticket đó trước.
   - Mail mời: nội dung vẫn có username đã sinh (mở rộng `AccountMailTemplateTests` nếu câu chữ đổi).
   - `PATCH` không đổi username (đã có test thì giữ; chưa có thì thêm một).
7. **Phá thử** (viết vào results là "chưa chạy, Claude sẽ chạy", kèm đúng dòng): (a) bỏ khoá lấy số → test đồng thời đỏ (có thể không đỏ ổn định — nói thật nếu vậy và đề xuất cách làm nó tất định); (b) bỏ chuẩn hoá `đ` → test `Đỗ Văn Đức` đỏ; (c) đổi so sánh số lớn nhất sang đếm số hàng → test `mynh7 → mynh8` đỏ; (d) bỏ kiểm `username` có mặt → test `SERVER_OWNED_FIELD` đỏ.
8. Tài liệu: `docs/contract-drift.md` thêm dòng ACCT-2 (SELF-SIGNED 08/11/2026): quy tắc sinh, tên Việt, `SERVER_OWNED_FIELD`, hệ quả với FE (bỏ ô nhập; hiển thị username trả về). Cập nhật `docs/agents/traps/auth-accounts.md` nếu có bẫy mới (khoá lấy số). `tracking.html` thêm thẻ. **KHÔNG** sửa `docs/api-contract-v1.1.md`, **KHÔNG** sửa `docs/openapi/*` (Claude sinh lại sau).
9. Ghi `.ai/results/ACCT-USERNAME.md` ngắn: đã làm gì, lệnh Claude cần chạy (test, phá thử), điều chưa chắc.

Tiêu chí xong:

- [ ] `dotnet build LuxMap.slnx` không cảnh báo mới
- [ ] Toàn bộ test mới ở mục 6 có mặt; Claude chạy `dotnet test` xanh (Codex không chạy được test)
- [ ] Không còn nơi nào trong repo gửi `username` khi `POST /admin/users` (grep sạch)
- [ ] `docs/contract-drift.md` có dòng mới

## KHÔNG ĐƯỢC làm

- Không tạo migration, không đụng schema (`app_user` giữ nguyên). Thêm `phone` là ticket riêng, KHÔNG làm ở đây.
- Không gửi / sinh mật khẩu; không đổi luồng mời.
- Không cho `PATCH` đổi username; không đổi tên đăng nhập của tài khoản có sẵn.
- Không sửa `docs/api-contract-v1.1.md` / `docs/openapi/*.json`.
- Không nhận diện "tên Việt" bằng từ điển hay danh sách họ — chỉ kiểm ký tự sau chuẩn hoá.
- Không `git add` / commit / push (sandbox chặn git; Claude làm).
- Không tự chọn phương án kiến trúc ngoài mục 3 — thấy chỗ mơ hồ thì ghi vào results như câu hỏi.
