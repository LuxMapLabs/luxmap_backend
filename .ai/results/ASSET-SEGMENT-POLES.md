# ASSET-SEGMENT-POLES — phase 2 (09/11/2026)

Đã implement trong worktree này; chưa xác minh runtime. Không chạy git / dotnet test / Swagger, không commit theo chỉ dẫn phiên.

## Đã làm

- `AssetContracts.cs`: hai request là sealed class + init; `JsonElement PoleIds`, reader chung phân biệt vắng/null/mảng,
  tối đa 2000 ID, regex ASCII neo tuyệt đối, trùng so ordinal; 400 `details.field = pole_ids` trước DB.
- `AssetCrudService.cs`: helper chung thay tập cột thấy được, kiểm đủ cột trước kiểm xung đột, chỉ Touch cột đổi.
  Giữ membership khi PUT vắng; [] bỏ cột thấy được. Chặn tuyến khác / khác xã khi gán mới; giữ cột vốn thuộc tuyến.
- Transaction tường minh khi có field, bao khóa và đúng **một SaveChangesAsync** cho tuyến + cột.
  PUT: kiểm tuyến qua filter → khóa tuyến (serialize thay thế cùng tuyến) → đọc tuyến → helper.
  Helper: đọc ID cột qua filter, lấy ID membership hiện tại qua filter → khóa hợp tập bằng
  `SELECT 1 FROM pole WHERE pole_id = ANY({0}) ORDER BY pole_id FOR UPDATE` → đọc LINQ lại.
  Lượt đầu chỉ projection ID để không có entity Pole track cũ khi đọc sau khóa.
  POST dùng navigation `Pole.Segment` đã có để EF truyền ID tuyến do DB sinh trong cùng SaveChanges; không insert tuyến riêng.
  Exception thoát qua await-using để rollback/dispose, không catch rồi query trong transaction đã lỗi.
- `AssetsController.cs`: XML-doc hai action, bổ sung metadata POST 404; 400/404/409 đủ ở hai action.
  `JsonElementFieldSchemaFilter.cs`: schema source cho mảng string, maxItems 2000, uniqueItems, pattern, không nullable;
  chưa sinh file OpenAPI.
- `SegmentPoleMembershipTests.cs`: POST tập/vắng/[], PUT A,B → B,C và timestamp, vắng/[], giữ cột sẵn có;
  validation từng loại trên POST/PUT và 400 thắng lookup; 404 thiếu/ẩn và atomic;
  409 occupied có details và atomic; luật liên xã trên cả POST/PUT, đổi road_class giữ membership cũ,
  bỏ cột ẩn vẫn giữ; đua POST/POST và PUT/PUT mỗi loại ba vòng.
  Test đua giữ `FOR NO KEY UPDATE`, đợi cả hai SELECT khóa qua `pg_stat_activity` (deadline 20 giây, không sleep),
  rồi nhả khóa và assert đúng một thành công / một 409 + tuyến cuối đúng. Monitor dùng context riêng ngoài transaction giữ khóa.
  Tài khoản hai xã dùng `LegacyTwoCommuneClientAsync` sẵn có, không sửa seed.
  Collection `AssetDatabaseCollection`; tiền tố `segp-`; teardown có sẵn của AssetImportFixture dọn cột → tuyến → tài khoản.
  Quyền dùng coverage hiện có `AssetPermissionTests.Only_a_manager_may_create_an_asset` (admin, agency, crew đều 403).
- Drift ASSET-SEG-1 SELF-SIGNED, trap assets, thẻ tracking đã cập nhật. Không đổi Contract, file OpenAPI, schema/migration,
  hai đường POST/PUT pole cũ hay Skip D-6b.

## Chỗ lệch / giới hạn đã ghi

- Ticket gọi 404 `NOT_FOUND`, nhưng Contract bảng lỗi / assets và `RequireAsync` dùng **ASSET_NOT_FOUND**;
  `NOT_FOUND` dành cho route không tồn tại. Giữ Contract và mã đã publish, test assert ASSET_NOT_FOUND.
  Đã ghi rõ ở `docs/contract-drift.md`, ASSET-SEG-1.
- Đường POST/PUT pole cũ vẫn cho gán segment thấy được mà chưa kiểm khác xã / chiếm tuyến hay dùng khóa helper mới.
  Giữ nguyên đúng phạm vi; đã ghi vào drift và trap, không chỉ results.
- Pole chưa là IAudited; không tự mở rộng cơ chế audit của tài sản trong ticket này.
- FE chưa được thông báo trực tiếp; drift ghi đầy đủ việc WP5 cần làm, nền tạm tới FW.

## Verification

✅ Kiểm tra tĩnh nguồn và HTML (Python + rg, exit 0), output thật:

```text
CreateSegmentAsync: ReadPoleIds first; exactly one SaveChangesAsync
UpdateSegmentAsync: ReadPoleIds first; exactly one SaveChangesAsync
New tests: no scope bypass, bulk writes or sleep
tracking.html: parsed with HTMLParser
Existing D-6b Skip: present
```

⚠️ Build thử:

```text
$ dotnet build LuxMap.slnx --disable-build-servers --no-restore
/usr/local/share/dotnet/sdk/10.0.302/Sdks/Microsoft.NET.Sdk/targets/Microsoft.PackageDependencyResolution.targets(266,5): error NETSDK1004: Assets file '/Users/nhm809/Documents/LuxMap/wt-segment-poles/src/LuxMap.Api/obj/project.assets.json' not found. Run a NuGet package restore to generate this file. [/Users/nhm809/Documents/LuxMap/wt-segment-poles/src/LuxMap.Api/LuxMap.Api.csproj]
```

Worktree chưa có restore assets. Đã yêu cầu hủy build (terminal trả `Attempting to cancel the build...`),
chưa nhận mã thoát cuối từ phiên build. Không restore / không kết luận build thành công hoặc không cảnh báo mới.
Chưa chạy HTTP/DB, tests, phá thử hay Swagger theo yêu cầu; Claude cần xác minh cả biên dịch và hành vi thực tế.

## Claude cần chạy

```bash
dotnet build LuxMap.slnx --disable-build-servers
dotnet test --disable-build-servers --filter 'FullyQualifiedName~SegmentPoleMembershipTests|FullyQualifiedName~AssetPermissionTests|FullyQualifiedName~AssetReplaceAndDeleteTests|FullyQualifiedName~AssetUpdatedByTests'
dotnet test --disable-build-servers
```

Sau kiểm chứng: xuất Swagger → generator hợp nhất → lint đúng `.ai/context/commands.md` / `docs/development.md`.
Chú ý generator có schema ghi tay; kiểm `pole_ids` là array string optional, non-null, uniqueItems/maxItems/pattern trên cả hai request.
Không sửa Contract nếu chưa duyệt. Cập nhật tracking theo kết quả thật rồi review/commit ở phiên được phép chạy git.

## Phá thử — chưa chạy, Claude sẽ chạy

Các dòng bên dưới thuộc `src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs` hiện tại:

- (a) Dòng 813: bỏ `FOR UPDATE` trong SELECT pole ⇒ `Concurrent_assignments_have_one_winner_and_one_conflict` phải đỏ.
- (b) Dòng 820–824: bỏ nhánh `ASSET_IN_USE` ⇒ `Occupied_pole_is_409_with_details_and_every_write_is_atomic` phải đỏ.
- (c) Dòng 844–849: bỏ nhánh bỏ-cột-vắng ⇒ `Put_replaces_visible_membership_but_keeps_unchanged_poles_and_absent_membership` phải đỏ.
- (d) Dòng 825: bỏ kiểm `InterCommune` (luôn cho) ⇒ `Cross_commune_requires_inter_commune_only_for_new_assignments_and_hidden_removals_are_kept` phải đỏ.
- (e) Dòng 852: `Touch` cả cột không đổi ⇒ test `updated_at` của B phải đỏ **nếu làm thay đổi timestamp**.
  Lưu ý code hiện có: AssetStamp.Touch tự no-op khi entity Unchanged, nên chỉ gọi Touch thêm sẽ KHÔNG làm test đỏ;
  phá luật thực sự bằng ép UpdatedAt của B đổi rồi kiểm test trên. Không đổi AssetStamp để phục vụ phá thử.
- (f) Dòng 263 / 427: tách hai `SaveChanges` ⇒ test atomic phải đỏ **khi ghi/commit tuyến trước rồi thất bại kiểm cột**.
  Nếu hai SaveChanges vẫn cùng transaction tường minh thì atomic vẫn đúng, test hành vi không thể chứng minh số lần gọi;
  luật một SaveChanges được kiểm tĩnh ở trên. Không khẳng định mutation đó chắc chắn đỏ khi vẫn giữ transaction.

Khôi phục sau phá bằng cp/touch; đọc exit code từng lượt. Hai lưu ý (e)/(f) là giới hạn logic của yêu cầu phá thử,
không phải đã chạy rồi thấy đỏ/xanh.

## Claude đã xác minh (09/11/2026)

- `dotnet build LuxMap.slnx`: exit 0, 0 cảnh báo. Worktree cần `.env` (chép từ cây chính, không vào git).
- `dotnet test LuxMap.slnx`: exit 0 — 1477 passed / 7 skipped (Api.Tests), test D-6b cũ vẫn Skip.
- Phá thử (build **project test** rồi `--no-build`; build riêng `LuxMap.Api` chạy DLL cũ và cho xanh giả):
  (a) bỏ `FOR UPDATE` ⇒ 2 test đua đỏ · (b) bỏ nhánh `ASSET_IN_USE` ⇒ test 409 + 2 test đua đỏ · (c) bỏ nhánh bỏ-cột-vắng ⇒ test thay thế đỏ ·
  (d) bỏ kiểm `InterCommune` ⇒ test luật liên xã đỏ · (e) ép `Touch` cả cột không đổi (phải ép `Modified` trước vì `Touch` bỏ qua Unchanged) ⇒ 2 test đỏ ·
  (f) bỏ transaction tường minh ⇒ test đua POST đỏ (atomic vẫn nhờ một `SaveChanges`; transaction là để giữ khoá). Khôi phục bằng `cp`.
- OpenAPI: `pole_ids` là mảng chuỗi tuỳ chọn, `maxItems 2000`, `uniqueItems`, pattern, ở cả hai request; lint hợp lệ, 10 cảnh báo nền.
