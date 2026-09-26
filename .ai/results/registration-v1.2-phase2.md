# Registration v1.2 — Phase 2: hiện thực (C1–C7)

| | |
|---|---|
| Ticket | Đồng bộ ROLE (DB + authz) và DOCS theo Phiếu đăng ký FA26SE222 v1.2 |
| Phase | **2 — hiện thực**, theo quyết định D-R1…D-R18 của Mỹ (25/09/2026) |
| Branch | `chore/registration-v1.2-roles-docs` (từ `dev` @ `440cbca`) |
| Khảo sát | `.ai/results/registration-v1.2-phase1.md` |
| DB test | `luxmap_test` (PostGIS 3.5, cùng server dev), trỏ bằng `ConnectionStrings__LuxMap` — **không một test nào chạy vào `luxmap_dev`** |
| Trạng thái | Code + docs xong, đã commit. **Chưa push, chưa mở PR** — chờ Mỹ xác nhận (§8 của CLAUDE.md toàn cục) |

---

## 0. Tóm tắt

- **C1** enum `user_role` đổi song ánh, migration `RenameUserRolesToRegistrationV12` (DROP CHECK → 4 UPDATE → ADD
  CHECK, `Down()` không mất dữ liệu). Chu trình apply → rollback → re-apply chạy 2 lần trên `luxmap_test` có dữ liệu.
  Đã apply lên `luxmap_dev` sau khi mọi bước xanh.
- **C2** ma trận capability 6 dòng ở `LuxMapPolicies.Matrix`, nguồn duy nhất của `AuthorizationSetup`. Mọi endpoint
  nghiệp vụ gắn capability; architecture test `CapabilityPolicyCoverageTests`. Ghi tài sản → Quản lý; `POST
  /lux-readings` → Kỹ sư hiện trường.
- **C3** 4 tài khoản demo, tên hiển thị mới, username / ID / biến `.env` giữ nguyên; không có tài khoản công dân.
- **C4** 24 ca HTTP vai trò × capability với kỳ vọng **literal**; kiểm DB không còn giá trị cũ; sabotage hai chiều.
- **C5** Contract **v1.7**; spec xuất lại, mỗi operation nghiệp vụ có `x-luxmap-capability` / `x-luxmap-roles`,
  `register` mang `deprecated: true`; D-R1…D-R18 đăng ký ở `contract-drift.md`.
- **C6** docs theo bảng kiểm kê đã duyệt. **C7** `tracking.html` + 12 follow-up chưa làm.
- **Phát sinh ngoài kế hoạch** (mỗi cái một commit riêng, nêu ở mục 6):
  1. `dev` không compile `LuxMap.Api.Tests` — di chứng solar;
  2. SQL test phụ thuộc culture `en_VN`;
  3. 🔴 **capability rỗng mở cho mọi vai trò** — bắt được nhờ sabotage;
  4. race khoá tài khoản `crew` giữa các collection.

## 1. Commit

- `9aa43ca` docs: align the project docs with registration form FA26SE222 v1.2
- `752a911` docs(contract)!: Contract v1.7 — registration v1.2 roles and the capability matrix
- `9df9b13` fix(test): lock a throwaway account, not the seeded crew
- `8097f84` test(auth): pin the capability matrix and every role against it
- `63bb385` fix(auth): refuse to start when a capability names no role
- `034431f` feat(seed): name the four demo accounts by their registration v1.2 roles
- `caa20d2` feat(auth)!: authorize by capability, each policy naming its exact roles
- `eff8b0b` feat(identity)!: rename the four user roles to registration form v1.2
- `042c41a` fix(test): format the planted-pole SQL with the invariant culture
- `9671ca8` fix(test): stop MapEndpointTests referencing the removed solar enum values

(Thứ tự cũ → mới đọc từ dưới lên.) Mỗi concern một commit. C4 gồm `63bb385` và `8097f84`, vì bản sửa capability
rỗng là `fix` riêng.

## 2. Verify cuối — output thật

### 2.1 `dotnet build -c Release --no-incremental`

16 project đều build (`grep -c ' -> '` = 16):

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:02.72
```

### 2.2 Migration apply → rollback → re-apply trên `luxmap_test` có dữ liệu seed

Dữ liệu: 4 tài khoản seed + bộ mock FO-26 (103 cột, 28 sự cố) nạp bằng `scripts/seed_mock_set.py --database luxmap_test`.

```
### 0. reset to the pre-migration state
$ dotnet ef database update 20260922002010_DropSolarFixtures
Done.
### BEFORE
 user_id | username |         role         |     full_name     | has_system_wide_scope 
---------+----------+----------------------+-------------------+-----------------------
 USR-001 | admin    | administrator        | Quản trị hệ thống | t
 USR-002 | agency   | management_agency    | Cấp giám sát      | f
 USR-003 | engineer | maintenance_engineer | Quản lý           | f
 USR-004 | crew     | field_crew           | Kỹ sư hiện trường | f
(4 rows)

                                                         ck_app_user_role                                                         
----------------------------------------------------------------------------------------------------------------------------------
 CHECK ((role = ANY (ARRAY['management_agency'::text, 'maintenance_engineer'::text, 'field_crew'::text, 'administrator'::text])))
(1 row)

 poles | faults |               head               
-------+--------+----------------------------------
   103 |     28 | 20260922002010_DropSolarFixtures
(1 row)

### 1. APPLY
$ dotnet ef database update 
Done.
 user_id | username |      role      |     full_name     | has_system_wide_scope 
---------+----------+----------------+-------------------+-----------------------
 USR-001 | admin    | system_admin   | Quản trị hệ thống | t
 USR-002 | agency   | superior       | Cấp giám sát      | f
 USR-003 | engineer | manager        | Quản lý           | f
 USR-004 | crew     | field_engineer | Kỹ sư hiện trường | f
(4 rows)

                                               ck_app_user_role                                                
---------------------------------------------------------------------------------------------------------------
 CHECK ((role = ANY (ARRAY['superior'::text, 'manager'::text, 'field_engineer'::text, 'system_admin'::text])))
(1 row)

 poles | faults |                      head                       
-------+--------+-------------------------------------------------
   103 |     28 | 20260925164118_RenameUserRolesToRegistrationV12
(1 row)

### 2. ROLLBACK
$ dotnet ef database update 20260922002010_DropSolarFixtures
Done.
 user_id | username |         role         |     full_name     | has_system_wide_scope 
---------+----------+----------------------+-------------------+-----------------------
 USR-001 | admin    | administrator        | Quản trị hệ thống | t
 USR-002 | agency   | management_agency    | Cấp giám sát      | f
 USR-003 | engineer | maintenance_engineer | Quản lý           | f
 USR-004 | crew     | field_crew           | Kỹ sư hiện trường | f
(4 rows)

                                                         ck_app_user_role                                                         
----------------------------------------------------------------------------------------------------------------------------------
 CHECK ((role = ANY (ARRAY['management_agency'::text, 'maintenance_engineer'::text, 'field_crew'::text, 'administrator'::text])))
(1 row)

 poles | faults |               head               
-------+--------+----------------------------------
   103 |     28 | 20260922002010_DropSolarFixtures
(1 row)

### 3. RE-APPLY
$ dotnet ef database update 
Done.
 user_id | username |      role      |     full_name     | has_system_wide_scope 
---------+----------+----------------+-------------------+-----------------------
 USR-001 | admin    | system_admin   | Quản trị hệ thống | t
 USR-002 | agency   | superior       | Cấp giám sát      | f
 USR-003 | engineer | manager        | Quản lý           | f
 USR-004 | crew     | field_engineer | Kỹ sư hiện trường | f
(4 rows)

                                               ck_app_user_role                                                
---------------------------------------------------------------------------------------------------------------
 CHECK ((role = ANY (ARRAY['superior'::text, 'manager'::text, 'field_engineer'::text, 'system_admin'::text])))
(1 row)

 poles | faults |                      head                       
-------+--------+-------------------------------------------------
   103 |     28 | 20260925164118_RenameUserRolesToRegistrationV12
(1 row)
```

Lượt đầu ở C1 có thêm một hàng `administrator` **không** system-wide (mô phỏng 14 tài khoản cặn trên dev). Hàng đó
map sang `system_admin` / `f`, rollback về `administrator` / `f`, rồi đã xoá khỏi `luxmap_test`.

⚠️ **Lần chạy chu trình đầu tiên ở C1 KHÔNG chạy gì cả.** zsh không tách từ biến `$EF` chưa quote, `command not
found` bị `grep` lọc mất, nên output trông như "không đổi gì". Đã phát hiện vì CHECK không đổi, rồi làm lại bằng
function. Ghi lại để không ai tin một chu trình mà output không có dòng `Done.` của từng bước.

### 2.3 `dotnet test` (trỏ `luxmap_test`)

```
ConnectionStrings__LuxMap → Host=localhost;Port=5433;Database=luxmap_test;Username=luxmap;Password=<redacted>
Passed!  - Failed:     0, Passed:    20, Skipped:     0, Total:    20, Duration: 104 ms - LuxMap.Persistence.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:   149, Skipped:     0, Total:   149, Duration: 71 ms - LuxMap.Shared.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    18, Skipped:     0, Total:    18, Duration: 1 s - LuxMap.Infrastructure.Storage.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:   402, Skipped:     0, Total:   402, Duration: 7 s - LuxMap.Api.Tests.dll (net10.0)
```

**589/589.** Baseline trên `dev` (sau hai bản sửa test ở mục 6): 550/550. Tăng 39:

- Api 367 → 402 (+35): 24 ca ma trận HTTP, 5 coverage, 1 DB, 2 OpenAPI, 3 do hai Fact của
  `AssetPermissionTests` thành Theory.
- Shared 145 → 149 (+4): `CapabilityMatrixTests`.

### 2.4 Sabotage hai chiều trên ma trận (mỗi lần revert ngay, `git diff --stat` rỗng sau đó)

**A — thêm giá trị role CŨ vào policy:** `RequireClaim(..., roles.Select(ToDbValue).Append("administrator"))`

```
Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 20 ms - LuxMap.Shared.Tests.dll (net10.0)
[xUnit.net 00:00:02.00]     LuxMap.Api.Tests.CapabilityPolicyCoverageTests.No_registered_policy_admits_a_retired_role_value [FAIL]
[xUnit.net 00:00:02.01]     LuxMap.Api.Tests.CapabilityPolicyCoverageTests.Each_registered_policy_admits_exactly_the_roles_the_matrix_names [FAIL]
Failed!  - Failed:     2, Passed:    28, Skipped:     0, Total:    30, Duration: 1 s - LuxMap.Api.Tests.dll (net10.0)
```

**A2 — trả lại quyền cũ của `administrator`:** `[ManageAssets] = [Manager, SystemAdmin]`

```
[xUnit.net 00:00:00.10]     LuxMap.Shared.Tests.CapabilityMatrixTests.Each_capability_admits_exactly_the_roles_of_the_Contract [FAIL]
Failed!  - Failed:     1, Passed:     2, Skipped:     0, Total:     3, Duration: 18 ms - LuxMap.Shared.Tests.dll (net10.0)
[xUnit.net 00:00:01.13]     LuxMap.Api.Tests.AssetReplaceAndDeleteTests.A_system_admin_may_not_replace_or_delete_an_asset(method: "PUT", route: "/api/v1/assets/segments") [FAIL]
[xUnit.net 00:00:01.17]     LuxMap.Api.Tests.AssetReplaceAndDeleteTests.A_system_admin_may_not_replace_or_delete_an_asset(method: "DELETE", route: "/api/v1/assets/feeders") [FAIL]
[xUnit.net 00:00:01.21]     LuxMap.Api.Tests.AssetReplaceAndDeleteTests.A_system_admin_may_not_replace_or_delete_an_asset(method: "DELETE", route: "/api/v1/assets/segments") [FAIL]
[xUnit.net 00:00:01.25]     LuxMap.Api.Tests.AssetReplaceAndDeleteTests.A_system_admin_may_not_replace_or_delete_an_asset(method: "PUT", route: "/api/v1/assets/feeders") [FAIL]
[xUnit.net 00:00:01.28]     LuxMap.Api.Tests.AssetReplaceAndDeleteTests.A_system_admin_may_not_replace_or_delete_an_asset(method: "PUT", route: "/api/v1/assets/poles") [FAIL]
[xUnit.net 00:00:01.48]     LuxMap.Api.Tests.RoleCapabilityMatrixTests.Each_role_is_admitted_or_refused_exactly_as_the_matrix_says(capability: "ManageAssets", role: "system_admin") [FAIL]
[xUnit.net 00:00:02.05]     LuxMap.Api.Tests.AssetPermissionTests.Only_a_manager_may_create_an_asset(username: "admin", passwordVariable: "SEED_ADMIN_PASSWORD") [FAIL]
[xUnit.net 00:00:02.14]     LuxMap.Api.Tests.AssetPermissionTests.A_system_admin_or_a_superior_may_NOT_import(username: "admin", passwordVariable: "SEED_ADMIN_PASSWORD") [FAIL]
Failed!  - Failed:     8, Passed:    56, Skipped:     0, Total:    64, Duration: 1 s - LuxMap.Api.Tests.dll (net10.0)
```

**B1 — bỏ vai trò mới duy nhất khỏi `ManageAssets`** (`[ManageAssets] = []`, sau khi có guard):

```
   System.AggregateException : One or more errors occurred. (Capability 'cap:manage_assets' names no role. An empty RequireClaim admits EVERY role; remove the capability instead of emptying it.) (Capability 'cap:manage_assets' names no role. An empty RequireClaim admits EVERY role; remove the capability instead of emptying it.)
   System.InvalidOperationException : Capability 'cap:manage_assets' names no role. An empty RequireClaim admits EVERY role; remove the capability instead of emptying it.
---- System.InvalidOperationException : Capability 'cap:manage_assets' names no role. An empty RequireClaim admits EVERY role; remove the capability instead of emptying it.
Failed!  - Failed:     2, Passed:     2, Skipped:     0, Total:     4, Duration: 22 ms - LuxMap.Shared.Tests.dll (net10.0)
Failed!  - Failed:    73, Passed:     0, Skipped:     0, Total:    73, Duration: 116 ms - LuxMap.Api.Tests.dll (net10.0)
[xUnit.net 00:00:00.10]     LuxMap.Shared.Tests.CapabilityMatrixTests.Each_capability_admits_exactly_the_roles_of_the_Contract [FAIL]
[xUnit.net 00:00:00.10]     LuxMap.Shared.Tests.CapabilityMatrixTests.No_capability_is_empty [FAIL]
[xUnit.net 00:00:00.25]     LuxMap.Api.Tests.RoleCapabilityMatrixTests.Each_role_is_admitted_or_refused_exactly_as_the_matrix_says(capability: "ControlLighting", role: "field_engineer") [FAIL]
```

Tổng số dòng `[FAIL]` ở lượt này là 75: host từ chối khởi động nên 73/73 test Api đỏ ngay ở fixture.

**B2 — bỏ `field_engineer` khỏi `ReadNetwork`** (bỏ vai trò mới trên policy nhiều vai trò):

```
[xUnit.net 00:00:00.13]     LuxMap.Shared.Tests.CapabilityMatrixTests.Each_capability_admits_exactly_the_roles_of_the_Contract [FAIL]
Failed!  - Failed:     1, Passed:     3, Skipped:     0, Total:     4, Duration: 26 ms - LuxMap.Shared.Tests.dll (net10.0)
[xUnit.net 00:00:01.35]     LuxMap.Api.Tests.RoleCapabilityMatrixTests.Each_role_is_admitted_or_refused_exactly_as_the_matrix_says(capability: "ReadNetwork", role: "field_engineer") [FAIL]
[xUnit.net 00:00:02.37]     LuxMap.Api.Tests.AssetPermissionTests.A_field_engineer_may_read_but_not_write [FAIL]
Failed!  - Failed:     2, Passed:    71, Skipped:     0, Total:    73, Duration: 1 s - LuxMap.Api.Tests.dll (net10.0)
```

🔴 **B lần đầu, TRƯỚC khi có guard, lộ ra lỗi thật.** Với `[ManageAssets] = []`, test vẫn đỏ, nhưng đỏ vì **mọi vai
trò đều lọt**, không phải vì manager bị chặn. `RequireClaim(role)` không kèm giá trị chỉ đòi claim tồn tại. Trích
output lượt đó:

```
[xUnit.net 00:00:00.10]     LuxMap.Shared.Tests.CapabilityMatrixTests.Each_capability_admits_exactly_the_roles_of_the_Contract [FAIL]
Failed!  - Failed:     1, Passed:     2, Skipped:     0, Total:     3, Duration: 24 ms - LuxMap.Shared.Tests.dll (net10.0)
[xUnit.net 00:00:01.01]     LuxMap.Api.Tests.RoleCapabilityMatrixTests.Each_role_is_admitted_or_refused_exactly_as_the_matrix_says(capability: "ManageAssets", role: "superior") [FAIL]
[xUnit.net 00:00:01.09]     LuxMap.Api.Tests.AssetReplaceAndDeleteTests.A_system_admin_may_not_replace_or_delete_an_asset(method: "PUT", route: "/api/v1/assets/segments") [FAIL]
[xUnit.net 00:00:01.13]     LuxMap.Api.Tests.AssetReplaceAndDeleteTests.A_system_admin_may_not_replace_or_delete_an_asset(method: "DELETE", route: "/api/v1/assets/feeders") [FAIL]
[xUnit.net 00:00:01.17]     LuxMap.Api.Tests.AssetReplaceAndDeleteTests.A_system_admin_may_not_replace_or_delete_an_asset(method: "DELETE", route: "/api/v1/assets/segments") [FAIL]
[xUnit.net 00:00:01.21]     LuxMap.Api.Tests.AssetReplaceAndDeleteTests.A_system_admin_may_not_replace_or_delete_an_asset(method: "PUT", route: "/api/v1/assets/feeders") [FAIL]
[xUnit.net 00:00:01.24]     LuxMap.Api.Tests.AssetReplaceAndDeleteTests.A_system_admin_may_not_replace_or_delete_an_asset(method: "PUT", route: "/api/v1/assets/poles") [FAIL]
[xUnit.net 00:00:01.47]     LuxMap.Api.Tests.RoleCapabilityMatrixTests.Each_role_is_admitted_or_refused_exactly_as_the_matrix_says(capability: "ManageAssets", role: "system_admin") [FAIL]
[xUnit.net 00:00:01.65]     LuxMap.Api.Tests.RoleCapabilityMatrixTests.Each_role_is_admitted_or_refused_exactly_as_the_matrix_says(capability: "ManageAssets", role: "field_engineer") [FAIL]
[xUnit.net 00:00:01.99]     LuxMap.Api.Tests.AssetPermissionTests.Only_a_manager_may_create_an_asset(username: "agency", passwordVariable: "SEED_AGENCY_PASSWORD") [FAIL]
[xUnit.net 00:00:02.03]     LuxMap.Api.Tests.AssetPermissionTests.Only_a_manager_may_create_an_asset(username: "admin", passwordVariable: "SEED_ADMIN_PASSWORD") [FAIL]
String:    "{"error":{"code":"COMMUNE_FORBIDDEN","mes"···
[xUnit.net 00:00:02.06]     LuxMap.Api.Tests.AssetPermissionTests.Only_a_manager_may_create_an_asset(username: "crew", passwordVariable: "SEED_CREW_PASSWORD") [FAIL]
[xUnit.net 00:00:02.11]     LuxMap.Api.Tests.AssetPermissionTests.A_system_admin_or_a_superior_may_NOT_import(username: "admin", passwordVariable: "SEED_ADMIN_PASSWORD") [FAIL]
[xUnit.net 00:00:02.15]     LuxMap.Api.Tests.AssetPermissionTests.A_system_admin_or_a_superior_may_NOT_import(username: "agency", passwordVariable: "SEED_AGENCY_PASSWORD") [FAIL]
String:    "{"error":{"code":"COMMUNE_FORBIDDEN","mes"···
Failed!  - Failed:    13, Passed:    51, Skipped:     0, Total:    64, Duration: 1 s - LuxMap.Api.Tests.dll (net10.0)
```

`COMMUNE_FORBIDDEN` nghĩa là request đã **qua** policy và dừng ở kiểm tra địa bàn. Bản sửa là commit `63bb385`.

### 2.5 Ma trận vai trò × capability qua HTTP (`RoleCapabilityMatrixTests`)

```
DISTINCT role: field_engineer, manager, superior, system_admin
ck_app_user_role: CHECK ((role = ANY (ARRAY['superior'::text, 'manager'::text, 'field_engineer'::text, 'system_admin'::text])))
field_engineer  ControlLighting   GET /api/v1/_scope/manager-only → HTTP 403
field_engineer  ManageAssets      POST /api/v1/assets/segments → HTTP 403
field_engineer  ManageUsers       GET /api/v1/_scope/admin-only → HTTP 403
field_engineer  ReadLuxReadings   GET /api/v1/lux-readings → HTTP 200
field_engineer  ReadNetwork       GET /api/v1/assets/segments → HTTP 200
field_engineer  RecordLuxReading  POST /api/v1/lux-readings → HTTP 400
manager         ControlLighting   GET /api/v1/_scope/manager-only → HTTP 200
manager         ManageAssets      POST /api/v1/assets/segments → HTTP 400
manager         ManageUsers       GET /api/v1/_scope/admin-only → HTTP 403
manager         ReadLuxReadings   GET /api/v1/lux-readings → HTTP 200
manager         ReadNetwork       GET /api/v1/assets/segments → HTTP 200
manager         RecordLuxReading  POST /api/v1/lux-readings → HTTP 403
superior        ControlLighting   GET /api/v1/_scope/manager-only → HTTP 403
superior        ManageAssets      POST /api/v1/assets/segments → HTTP 403
superior        ManageUsers       GET /api/v1/_scope/admin-only → HTTP 403
superior        ReadLuxReadings   GET /api/v1/lux-readings → HTTP 200
superior        ReadNetwork       GET /api/v1/assets/segments → HTTP 200
superior        RecordLuxReading  POST /api/v1/lux-readings → HTTP 403
system_admin    ControlLighting   GET /api/v1/_scope/manager-only → HTTP 403
system_admin    ManageAssets      POST /api/v1/assets/segments → HTTP 403
system_admin    ManageUsers       GET /api/v1/_scope/admin-only → HTTP 200
system_admin    ReadLuxReadings   GET /api/v1/lux-readings → HTTP 200
system_admin    ReadNetwork       GET /api/v1/assets/segments → HTTP 200
system_admin    RecordLuxReading  POST /api/v1/lux-readings → HTTP 403
```

Probe ghi gửi body rỗng: vai trò được vào dừng ở 400 (validation), vai trò bị chặn dừng ở 403 `ROLE_FORBIDDEN`,
không ghi hàng nào.

### 2.6 `luxmap_dev` — trước / sau khi apply (sau khi 2.1–2.4 xanh)

```
### luxmap_dev BEFORE
 current_database 
------------------
 luxmap_dev
(1 row)

         role         | has_system_wide_scope | count 
----------------------+-----------------------+-------
 administrator        | f                     |    14
 administrator        | t                     |     1
 field_crew           | f                     |     1
 maintenance_engineer | f                     |     1
 management_agency    | f                     |     1
(5 rows)

           migration_id           
----------------------------------
 20260922002010_DropSolarFixtures
(1 row)

### APPLY
$ dotnet ef database update
Done.
### luxmap_dev AFTER
 current_database 
------------------
 luxmap_dev
(1 row)

      role      | has_system_wide_scope | count 
----------------+-----------------------+-------
 field_engineer | f                     |     1
 manager        | f                     |     1
 superior       | f                     |     1
 system_admin   | f                     |    14
 system_admin   | t                     |     1
(5 rows)

                  migration_id                   
-------------------------------------------------
 20260925164118_RenameUserRolesToRegistrationV12
(1 row)

 user_id | username |      role      |        full_name        | has_system_wide_scope 
---------+----------+----------------+-------------------------+-----------------------
 USR-001 | admin    | system_admin   | Quản trị hệ thống       | t
 USR-002 | agency   | superior       | Cán bộ cơ quan quản lý  | f
 USR-003 | engineer | manager        | Kỹ sư bảo trì           | f
 USR-004 | crew     | field_engineer | Tổ khảo sát và sửa chữa | f
(4 rows)

 user_id  |       username       |     role     
----------+----------------------+--------------
 USR-1802 | be12a-3a7a9094a1eb4f | system_admin
 USR-1803 | be12b-4113b79e504c47 | system_admin
 USR-1808 | be12a-61ade0e86dee4d | system_admin
 USR-1809 | be12b-499d2f0d178842 | system_admin
 USR-1810 | be12a-b4dca5767bdb4a | system_admin
 USR-1811 | be12b-d03592275ea74b | system_admin
 USR-1812 | be12a-e692c9ef6e6847 | system_admin
 USR-1813 | be12b-b64174ff1d0645 | system_admin
 USR-1814 | be12a-3623ce944d974e | system_admin
 USR-1815 | be12b-7577048eeeaf4e | system_admin
 USR-1816 | be12a-3e69aa753ada42 | system_admin
 USR-1817 | be12b-ea1201513f7240 | system_admin
 USR-1840 | be12a-74cef3178d7c46 | system_admin
 USR-1842 | be12b-685fdf3ac22044 | system_admin
(14 rows)
```

**14 tài khoản cặn** (theo D-R6.2, dọn ở BE-36): danh sách ở cuối output trên, `USR-1802 … USR-1842`, nay đều là
`system_admin` với `has_system_wide_scope = f`.

⚠️ `full_name` của `USR-002..004` trên dev là tên đã sửa tay trước đây ("Cán bộ cơ quan quản lý", "Kỹ sư bảo trì",
"Tổ khảo sát và sửa chữa"). Migration không đụng `full_name`; chạy `--seed` trên dev sẽ đồng bộ, nhưng **chưa chạy**
vì lệnh cho dev chỉ nói apply migration.

## 3. Trả lời hai câu Mỹ hỏi

**D-R10 — `data_source` cho testbed.** Enum hiện có `field | public_imagery | calibration_rig | simulated`
(CHECK trên `road_segment`, `pole`, `fixture`, `lux_reading`, `fault`).
- `calibration_rig` dùng được cho ảnh và số đo của **Controlled Reference Capture Set** trên testbed, đúng nghĩa
  "bộ hiệu chuẩn tự dựng" cũ.
- `simulated` là telemetry **mô phỏng**.
- Telemetry **thật** từ thiết bị IoT trên testbed không khớp gọn cái nào: nó không mô phỏng, cũng không phải hiện
  trường. Đã đưa thành **Contract O-9** + follow-up, **không** thêm giá trị enum.

**D-R16 — cột bảo hành.** Nằm trên **`fixture`**, không trên `pole`:

```
 table_name |   column_name   | data_type | is_nullable
------------+-----------------+-----------+-------------
 fixture    | install_date    | date      | NO
 fixture    | removed_date    | date      | YES
 fixture    | warranty_expiry | date      | YES
```

Hệ quả cho BE-31: cảnh báo của một cột lấy từ **bóng đang dùng**, vốn là duy nhất nhờ `ux_fixture_pole_id_active`.
`warranty_expiry` nullable, nên "không biết hạn" phải hiện khác "còn bảo hành".

## 4. Ma trận đã hiện thực

| Capability | Vai trò | Endpoint |
|---|---|---|
| `ReadNetwork` | superior, manager, field_engineer, system_admin | 6 `GET /assets/*` + `GET /poles` + `GET /segments` (8) |
| `ReadLuxReadings` | cả bốn | 2 `GET /lux-readings*` |
| `ManageAssets` | manager | 12 ghi trong `AssetsController` + `POST /assets/import/{kind}` (13) |
| `RecordLuxReading` | field_engineer | `POST /lux-readings` |
| `ControlLighting` | manager | — (probe test `/_scope/manager-only`) |
| `ManageUsers` | system_admin | — (probe test `/_scope/admin-only`) |

`GET /auth/me` giữ `[Authorize]` trần, là ngoại lệ duy nhất, có tên trong architecture test. `CommuneScopeConsistencyRequirement`
nằm trên mọi policy (có test đọc lại policy từ host).

⚠️ **Đính chính Phase 1:** báo cáo khảo sát ghi "15 chỗ gắn `Administrator`"; con số đúng là **13 attribute** (dòng
thứ 14 của grep là XML doc). Đã ghi ở `contract-drift.md`.

## 5. Docs đã sửa (C5–C7)

- `api-contract-v1.1.md` → **v1.7**: §1.4, §1.6, §2 (viết lại), §3.1, §3.3, §4.1 (DEPRECATED), §4.5, §4.7, §5.3–§5.7,
  §7, §9 (O-2 viết lại, O-9 mới), §10. Dòng đếm operation cũ ở đầu file (37 = 22 + 15) sửa thành
  **45 = 32 + 13**, đếm từ spec.
- `docs/openapi/luxmap-v1.json` xuất lại từ code. `luxmap-v1.5.json` sinh lại (`info.version = 1.7`); Redocly:
  *valid*, **3 cảnh báo, giống hệt bản trên `HEAD`** (`info-license`, 2 × `servers` localhost — O-8).
- `contract-drift.md`: khối *Registration v1.2* (bốn trường FW-00, D-R1…D-R18, FO-01 `SUPERSEDED`, việc nợ WP5/WP6)
  + banner lịch sử cho phần trước 25/09.
- `CLAUDE.md` — chỉ các mục trong bảng kiểm kê:
  - tên đề tài;
  - phạm vi thay Nhánh C;
  - retention;
  - BE-11 (video planned);
  - BE-42 (lux tương đối, policy);
  - BE-12a quy tắc 4;
  - BE-18 (audit bắt buộc);
  - `administrative_unit`;
  - Vai trò;
  - Quy tắc dễ sai;
  - `POLE-0047`;
  - `external_ref` mở lại;
  - anti-pattern;
  - version.

  **Không đụng** timeline, lịch W0–W21, tên member (D-R8, D-R9), khối v1.6.
- `README.md`, `authorization-guide.md`, `code-walkthrough.md`, `templates/README.md`, `.ai/README.md`,
  `.ai/context/sources.md`.
- Banner D-R18: `backend-report.md`, `docs/review/*` (3 file), `docs/archive/contract-drift-v1.md`.
- `tasks-backend.csv` — đổi **đúng 3 ô**, kiểm bằng so từng ô với `HEAD` (độ rộng hàng vẫn 36):
  - BE-07 `Công việc`;
  - BE-41 `Kết quả cần đạt`;
  - BE-08 `Ghi chú` (notes, trong bảng kiểm kê).

  Cột `Trạng thái` / `% hoàn thành` không đụng. ⚠️ Lần sửa đầu làm lệch cột: văn bản BE-07 có dấu phẩy mà không bọc
  nháy kép, lộ ra khi kiểm độ rộng hàng. Đã sửa trước khi commit.

## 6. Phát sinh ngoài kế hoạch

| # | Chuyện gì | Bằng chứng | Xử lý |
|---|---|---|---|
| 1 | `dev` **không compile** `LuxMap.Api.Tests`: PR #46 (BE-14) và #47 (bỏ solar) merge độc lập, test BE-14 vẫn dùng `PowerSource.Solar` | `error CS0117: 'PowerSource' does not contain a definition for 'Solar'` | `9671ca8` — phân biệt hai bóng bằng `lamp_watt` |
| 2 | Hai test bbox đỏ **chỉ trên máy culture dấu phẩy** (`AppleLocale = en_VN`): nội suy `108.5` ra `108,5` nên `ST_MakePoint` nhận 4 đối số | `22023: Geometry has Z dimension`; chạy lại với `LC_ALL=en_US.UTF-8` thì xanh | `042c41a` — `string.Create(CultureInfo.InvariantCulture, …)` |
| 3 | 🔴 Capability rỗng mở cho mọi vai trò | mục 2.4, B lần đầu | `63bb385` — từ chối khởi động |
| 4 | Race: `AuthEndpointTests` khoá tài khoản seed `crew` vài ms, collection khác đăng nhập `crew` đúng lúc đó thì nhận 403 | 1/3 lượt đỏ ở `RoleCapabilityMatrixTests` với 403 từ `LoginAsync` | `9df9b13` — khoá tài khoản dùng một lần; 19 lượt liên tiếp xanh |
| 5 | ⚠️ `PrefixedIdOrderingTests.A_listing_orders_ids_past_the_padding_width_numerically_not_as_text` đỏ **đúng một lần**, trước bản sửa #4 | Test không đăng nhập nên #4 không giải thích được; không bắt lại được thông điệp lỗi qua 19 lượt | **Chưa giải thích.** Ghi ở "Vấn đề đang mở" của tracking |

Mục 3 và 4 đề xuất ghi vào `CLAUDE.md`. Mục 3 **đã ghi** vì nằm trong mục BE-12a quy tắc 4 được duyệt. Hai bẫy #2
(culture) và #4 (đừng khoá / sửa tài khoản seed dùng chung trong test) là ràng buộc kỹ thuật nội bộ, đúng tầng
`CLAUDE.md`, **nhưng nằm ngoài các mục được duyệt**, nên **chưa ghi**. Chờ Mỹ.

## 7. Còn lại / chưa xác minh

- ⚠️ **Chưa push, chưa mở PR** — cần Mỹ xác nhận.
- ⚠️ `docs/registration/FA26SE222_v1.2.md` vẫn **untracked**, trong khi `CLAUDE.md` và `contract-drift.md` nay trỏ
  vào nó. File có họ tên + mã sinh viên của 5 thành viên (không có SĐT / email). Commit vào hay không là quyết định
  của Mỹ.
- ⚠️ `full_name` trên dev chưa đồng bộ (mục 2.6).
- ⚠️ Lỗi chập chờn #5 chưa giải thích.
- 📌 Access token phát trước migration mang giá trị role cũ tới 60 phút và bị 403 ở mọi endpoint nghiệp vụ cho tới lần
  refresh kế tiếp (refresh đọc role từ DB). Suy ra từ code (`AuthService.cs:231`), **chưa đo** trên một token thật.
- 📌 Tag `Poles` / `Segments` trong `gen_consolidated_spec.py` vẫn ghi "CHƯA HIỆN THỰC (BE-14)" — lệch có sẵn, ngoài
  phạm vi, đã ghi vào tracking.
- Mô tả PR (khi mở) phải có mục **"Breaking cho FE/Mobile"**: 4 giá trị role mới + bảng thay thế; `/assets/*` ghi
  chuyển sang Quản lý; `POST /lux-readings` chỉ Kỹ sư hiện trường; `/auth/register` DEPRECATED; và 3 ô CSV đã đổi.
