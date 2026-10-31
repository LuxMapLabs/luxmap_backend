---
ticket: ACCOUNT-ONE-COMMUNE
title: Mỗi tài khoản thuộc đúng một xã (trừ quản trị hệ thống)
status: ready
phase: 1
owner: codex
branch: feat/ACCOUNT-ONE-COMMUNE
contract_refs:
  - "Contract mục 2 (vai trò, phạm vi địa bàn), mục 7 (claim commune_ids), D-R3 (cấp giám sát nhiều xã) — bị thay"
depends_on:
  - "Quyết định của Mỹ 29/10/2026: \"mỗi account thuộc về đúng 1 commune\" — ép cả superior; migration giữ xã được gán sớm nhất"
---

## Bối cảnh

Mỹ chốt 29/10/2026: **mỗi tài khoản thuộc đúng một xã**. `system_admin` không thuộc xã nào (`HasSystemWideScope`, claim `["*"]`, không có dòng gán xã) — giữ nguyên.
Hai điểm đã hỏi và đã trả lời:

1. **`superior` cũng bị ép một xã** (thay D-R3 "cấp giám sát xem nhiều xã"). Muốn giám sát nhiều xã thì dùng nhiều tài khoản.
2. **Tài khoản đang gán nhiều xã:** migration **tự giữ xã có `assigned_at` sớm nhất** (hoà → `commune_id` nhỏ nhất theo thứ tự ordinal), xoá các xã còn lại. Mỹ đã chấp nhận `Down()` không khôi phục được các dòng bị xoá.

### Hiện trạng (Claude đã đọc trên `origin/dev` 5217c9d — Codex tự xác minh lại)

- Bảng `app_user_commune` (PK `(user_id, commune_id)`, nhiều-nhiều) — `AppUserCommuneConfiguration` ở `IdentityConfigurations.cs`.
- Dùng bảng này: `UserAdminService` (tạo / sửa / liệt kê; `ValidCommunesAsync` chỉ đòi ≥ 1 xã, `ReplaceCommunes` so diff), `AuthService.CommuneIdsForAsync`, `IdentitySeeder` (mỗi tài khoản seed 1 xã), `Notifier` (thông báo tới người phụ trách xã), `SurveyProcessor` (~dòng 192), `WorkOrderAgenda` (~268), `WorkOrderService` (~36).
- Bề mặt API: `POST/PATCH /admin/users` nhận `commune_ids[]`; `GET /admin/users` + `/auth/me` trả `commune_ids[]`; JWT claim `commune_ids` là mảng. **Hình dạng này GIỮ NGUYÊN** (xem D-1).
- Test: ~25 file trong `tests/LuxMap.Api.Tests` dựng người dùng nhiều xã trực tiếp bằng `AppUserCommune` (liệt kê bằng `git grep AppUserCommune tests`).

## Quyết định (Claude quyết theo ủy quyền của Mỹ)

- **D-1 — Giữ bảng `app_user_commune`, thêm UNIQUE `(user_id)`.** Không chuyển sang cột `app_user.commune_id`: đổi bảng sẽ động `Notifier`, `SurveyProcessor`, `WorkOrderAgenda`, `WorkOrderService` mà không thêm bảo đảm nào (UNIQUE đã đủ). Tên index khai bằng `HasDatabaseName` (≤ 63 ký tự), khai cả trong EF model lẫn migration.
- **D-2 — Wire giữ nguyên, chỉ siết giá trị.** `commune_ids[]` vẫn là mảng ở request, response và JWT. Với mọi vai trò khác `system_admin`: request phải có **đúng 1** phần tử (0 hoặc ≥ 2 → `400 VALIDATION_ERROR`, `details` chỉ `commune_ids`); `system_admin` vẫn không được gửi `commune_ids`. Không thêm trường `commune_id` số ít → FE (WP5/WP6) không phải đổi gì. Đây là siết kiểm tra, không đổi field đã publish.
- **D-3 — PATCH.** Đổi `commune_ids` = **thay** xã duy nhất (không còn "diff"): `ReplaceCommunes` thành "đặt xã". Đổi vai trò giữa các vai trò không phải admin mà không gửi `commune_ids` → giữ xã hiện tại. Rời `system_admin` mà không gửi `commune_ids` → vẫn 400 như cũ.
- **D-4 — Migration `AccountsSingleCommune`.** `Up()`: (1) đọc `Up()`/`Down()` theo `docs/agents/traps/migrations.md`; (2) xoá các dòng thừa theo luật ở trên bằng SQL thuần trong migration (không `ExecuteDelete` ở code app — cấm compile-time); (3) `RAISE NOTICE` liệt kê từng cặp `(user_id, commune_id)` bị xoá để người deploy thấy; (4) tạo UNIQUE index. `Down()`: chỉ `DROP INDEX` — **ghi rõ trong comment** rằng các dòng đã xoá không khôi phục. Không đụng ràng buộc / index khác.
- **D-5 — Phần còn lại của code giữ nguyên.** Mọi chỗ đọc `CommuneScope.CommuneIds` là danh sách chung — không "đơn giản hoá" về một giá trị. Chúng vẫn đúng cho `system_admin`, và việc đổi sẽ nới phạm vi diff vô ích. Chỉ ghi nhận hệ quả ở mục dưới.
- **D-6 — Test.** Test dựng người dùng **không phải admin** với ≥ 2 xã sẽ vi phạm UNIQUE. Với mỗi test: (a) nếu mục đích là kiểm tra "phạm vi nhiều xã" chung → viết lại dùng hai tài khoản, hoặc `system_admin`, hoặc dựng claim trực tiếp ở mức unit nếu test không cần DB; (b) nếu mục đích là tính năng giờ không còn tới được (xem hệ quả) → **KHÔNG xoá âm thầm**: liệt kê trong báo cáo (tên test, vì sao, đề xuất) để Mỹ quyết. Test "tạo / sửa tài khoản" mới: 0 xã, 2 xã, xã không tồn tại, admin gửi xã, đổi xã, đổi vai trò, và **ràng buộc DB** (chèn trực tiếp dòng thứ hai → vi phạm UNIQUE). Test migration: dữ liệu trước có tài khoản 3 xã → sau còn xã sớm nhất, hoà thì `commune_id` nhỏ nhất.

## Hệ quả cần ghi rõ cho Mỹ (ghi vào drift, không tự sửa tính năng)

Người dùng không phải admin chỉ có một xã, nên:
- **Tuyến liên xã (ERD Q7, drift S-Q7, SC-5):** phiếu khảo sát của Quản lý xã A không còn chứa được cột / tuyến xã B (ngoài phạm vi → "không xử lý, báo rõ trong độ phủ"). Dữ liệu thật 28/09 (Nguyễn Xiển & Phước Thiện chạy qua Long Phước và Long Bình) sẽ chỉ phủ phần xã của người giao.
- **Cấp giám sát** xem thống kê / bảo hành theo **một** xã mỗi tài khoản.
- **`POST /faults` không có cột** (R-6): nhánh "nhiều xã → phải gửi `commune_id`" không còn xảy ra với người thường.
- Thông báo, giao việc theo xã: không đổi, chỉ ít người nhận hơn.

## Phải đọc trước

`.ai/context/sources.md` · `docs/agents/traps/auth-accounts.md` · `docs/agents/traps/commune-scope.md` · `docs/agents/traps/migrations.md` · `docs/agents/traps/testing.md` · Contract mục 2 và 7 · `docs/contract-drift.md` (D-R3, S-Q7, SC-5, R-6) · `docs/database/erd.md` (Q7).

## Yêu cầu

1. **Code:** D-1…D-4 (entity configuration, migration, `UserAdminService` — `ValidCommunesAsync`, `ReplaceCommunes`, thông báo lỗi). Không đổi chữ ký DTO. Cập nhật XML-doc / comment nói "nhiều xã" cho đúng.
2. **Seeder:** kiểm lại `IdentitySeeder` và mọi dữ liệu demo / script (`scripts/`, `docs/`) có gán nhiều xã cho một tài khoản không; sửa nếu có.
3. **Test:** D-6, kèm **phá thử**: bỏ UNIQUE → test ràng buộc đỏ; bỏ kiểm "đúng 1" ở service → test API đỏ; khôi phục bằng `cp`. Đọc mã thoát `dotnet test`.
4. **Tài liệu:** Contract bump **v1.22** (mục 2: cột "Phạm vi địa bàn" — `superior`, `manager`, `field_engineer` mỗi tài khoản đúng một xã; mục 7; changelog; đúng thứ tự header như các bản trước); `docs/contract-drift.md` thêm mục mới (đặt tên `ACCT-1`) ghi quyết định, D-1…D-4 và toàn bộ mục "Hệ quả"; chỉnh `docs/agents/traps/auth-accounts.md` dòng "Cấp giám sát xem nhiều xã" và `commune-scope.md` nếu nhắc nhiều xã cho người thường; chú thích ở `docs/database/erd.md` Q7; `CLAUDE.md` (số version Contract, dòng "Cấp giám sát"); `docs/deploy/update.md` — bước **sao lưu DB trước khi deploy** vì migration xoá dòng. Sinh lại spec nếu mô tả `commune_ids` ở swagger đổi (`.ai/context/commands.md`).
5. **`tracking.html`:** thẻ cho ticket.

## KHÔNG ĐƯỢC làm

- Không đổi tên / kiểu `commune_ids`, không thêm `commune_id` số ít, không đổi claim JWT.
- Không chuyển sang cột `app_user.commune_id`, không xoá bảng `app_user_commune`.
- Không "đơn giản hoá" `CommuneScope`, `CommuneWriteGuard`, query filter, `Notifier`, `SurveyProcessor`, `WorkOrder*` (D-5).
- Không xoá test của tính năng liên xã mà không báo cáo (D-6b).
- Không `git commit`, `git push`, `git add -A`. Không chạm cây chính `luxmap_backend` — chỉ worktree này. Không chạy test trên `luxmap_dev`.
- Không đổi `Directory.Build.props`, NuGet, hay tập tin ngoài phạm vi trên.

## Xong khi

Build sạch (`--disable-build-servers`), toàn bộ test xanh (đọc mã thoát), phá thử đã chạy và khôi phục, spec lint không thêm cảnh báo so với baseline (9), tài liệu mục 4 đủ, báo cáo ở `.ai/results/ACCOUNT-ONE-COMMUNE.md` gồm: danh sách test đã viết lại / cần Mỹ quyết, kết quả phá thử, và lệnh đã chạy.
