# Registration v1.2 — Phase 1: khảo sát role + docs (READ-ONLY)

| | |
|---|---|
| Ticket | Đồng bộ ROLE (DB + authz) và DOCS theo Phiếu đăng ký FA26SE222 v1.2 |
| Phase | **1 — khảo sát. Không sửa code, DB hay docs nào.** File này là file duy nhất được ghi |
| Branch | `chore/registration-v1.2-roles-docs` (tạo từ `dev` @ `440cbca`) |
| Ngày | 25/09/2026 |
| Nguồn chuẩn | `docs/registration/FA26SE222_v1.2.md` (untracked, 324 dòng — đã đọc hết) |
| Trạng thái | **DỪNG — chờ Mỹ chốt D-R1…D-R6, D-R8, D-R9 và D-R10…D-R18** |

---

## 0. Tóm tắt

1. **Role lưu bằng `text` + CHECK** (`ck_app_user_role`), sinh tự động từ enum C# `UserRole` qua
   `HasContractEnum`. Không có PG enum, không có bảng role. 4 giá trị cũ:
   `management_agency | maintenance_engineer | field_crew | administrator`. **Không có "Surveyor"
   hay "Leader/Officer" nào** trong code hay DB — Surveyor đã gộp sẵn vào `field_crew` ("Tổ khảo sát /
   sửa chữa").
2. **Chỉ MỘT policy được dùng ở dòng sản xuất: `Administrator`** (15 chỗ gắn, toàn bộ ở `/assets/*`).
   Ba policy còn lại khai báo nhưng **0 call site** sản xuất. Mọi GET và `POST /lux-readings` **không gắn
   policy** → mọi role đã đăng nhập đều vào được.
3. **Chỗ lệch nặng nhất với phiếu không nằm ở tên role** mà ở:
   - **Ghi tài sản đang là `administrator`**, phiếu giao cho **Manager** (D-R12).
   - **`POST /auth/register` — tự đăng ký mở cho mọi người** đang chạy thật; phiếu: System Admin tạo
     account (D-R11).
   - **Nhánh C (FO-01) — "không có thử nghiệm hiện trường"** mâu thuẫn TRỰC TIẾP với phiếu (field trial
     với xã đối tác, deliverable *Field Trial and Evaluation Report*). Không chỉ là chữ: nó là lý do của
     ít nhất 4 quyết định kỹ thuật đã chốt (D-R10).
   - **NFR audit trail "every engineer decision"** vs BE-18 **chỉ giữ quyết định mới nhất** (D-R13).
   - **Phiếu nói VIDEO** (survey + evidence), BE-11 **chỉ nhận JPEG** (D-R14).
   - **`POST /lux-readings` không gắn policy** → Superior (read-only) sẽ ghi được nếu thêm role mà
     không xử lý (D-R5).
4. **"Drift 31" không nằm trong `docs/contract-drift.md`** mà trong
   `docs/archive/contract-drift-v1.md` (log cũ, đã gộp vào Contract v1.4 §2 dưới tên D-14).
5. **Không có cấp huyện trong DB.** `administrative_unit` chỉ có xã; "nhiều xã" đi qua bảng nối
   `app_user_commune`. Scope đọc hiện tại là **tập xã**, không phải district.
6. Kế hoạch nội bộ "13 tuần 07/09 → 03/12/2026" **không có trong repo** — repo ghi W1–W21,
   07/09/2026 → 31/01/2027 (D-R9).

---

## 1.1 Role hiện tại trong code & DB

### 1.1.a Cơ chế lưu role (bằng chứng)

`src/LuxMap.Shared/Contracts/Enums/UserRole.cs` — enum 4 thành viên, XML doc ghi rõ
*"There is no Citizen role"*. `IdentityConfigurations.cs` gọi `builder.HasContractEnum(user => user.Role)`
→ cột `text` + CHECK sinh từ enum.

```
$ docker compose exec -T postgres psql -U luxmap -d luxmap_dev -c '\dT+'
                                          List of data types
 Schema |     Name      | Internal name | Size  | Elements | Owner  | Access privileges | Description 
--------+---------------+---------------+-------+----------+--------+-------------------+-------------
 public | box2d         | box2d         | 65    |          | luxmap |                   | 
 public | box2df        | box2df        | 16    |          | luxmap |                   | 
 public | box3d         | box3d         | 52    |          | luxmap |                   | 
 public | geography     | geography     | var   |          | luxmap |                   | 
 public | geometry      | geometry      | var   |          | luxmap |                   | 
 public | geometry_dump | geometry_dump | tuple |          | luxmap |                   | 
 public | gidx          | gidx          | var   |          | luxmap |                   | 
 public | spheroid      | spheroid      | 65    |          | luxmap |                   | 
 public | valid_detail  | valid_detail  | tuple |          | luxmap |                   | 
(9 rows)
```

→ **Không có PG enum nào** (chỉ type của PostGIS).

```
                                                           Table "public.app_user"
        Column         |           Type           | Collation | Nullable |                              Default                               
-----------------------+--------------------------+-----------+----------+--------------------------------------------------------------------
 user_id               | text                     |           | not null | luxmap_format_id('USR'::text, nextval('user_id_seq'::regclass), 3)
 username              | text                     |           | not null | 
 email                 | text                     |           | not null | 
 full_name             | text                     |           | not null | 
 password_hash         | text                     |           | not null | 
 password_algorithm    | text                     |           | not null | 
 role                  | text                     |           | not null | 
 is_locked             | boolean                  |           | not null | false
 has_system_wide_scope | boolean                  |           | not null | false
 created_at            | timestamp with time zone |           | not null | now()
 updated_at            | timestamp with time zone |           | not null | now()
Indexes:
    "pk_app_user" PRIMARY KEY, btree (user_id)
    "ix_app_user_email" UNIQUE, btree (email)
    "ix_app_user_email_lower" UNIQUE, btree (lower(email))
    "ix_app_user_username" UNIQUE, btree (username)
    "ix_app_user_username_lower" UNIQUE, btree (lower(username))
Check constraints:
    "ck_app_user_role" CHECK (role = ANY (ARRAY['management_agency'::text, 'maintenance_engineer'::text, 'field_crew'::text, 'administrator'::text]))
Referenced by:
    TABLE "app_user_commune" CONSTRAINT "fk_app_user_commune_app_user_user_id" FOREIGN KEY (user_id) REFERENCES app_user(user_id) ON DELETE CASCADE
    TABLE "fault" CONSTRAINT "fk_fault_app_user_confirmed_by" FOREIGN KEY (confirmed_by) REFERENCES app_user(user_id) ON DELETE RESTRICT
    TABLE "fault" CONSTRAINT "fk_fault_app_user_reported_by" FOREIGN KEY (reported_by) REFERENCES app_user(user_id) ON DELETE RESTRICT
    TABLE "fault" CONSTRAINT "fk_fault_app_user_resolved_by" FOREIGN KEY (resolved_by) REFERENCES app_user(user_id) ON DELETE RESTRICT
    TABLE "lux_reading" CONSTRAINT "fk_lux_reading_app_user_measured_by" FOREIGN KEY (measured_by) REFERENCES app_user(user_id) ON DELETE RESTRICT
    TABLE "refresh_token" CONSTRAINT "fk_refresh_token_app_user_user_id" FOREIGN KEY (user_id) REFERENCES app_user(user_id) ON DELETE CASCADE

                     Table "public.app_user_commune"
   Column    |           Type           | Collation | Nullable | Default 
-------------+--------------------------+-----------+----------+---------
 user_id     | text                     |           | not null | 
 commune_id  | text                     |           | not null | 
 assigned_at | timestamp with time zone |           | not null | now()
Indexes:
    "pk_app_user_commune" PRIMARY KEY, btree (user_id, commune_id)
    "ix_app_user_commune_commune_id" btree (commune_id)
Foreign-key constraints:
    "fk_app_user_commune_administrative_unit_commune_id" FOREIGN KEY (commune_id) REFERENCES administrative_unit(commune_id) ON DELETE RESTRICT
    "fk_app_user_commune_app_user_user_id" FOREIGN KEY (user_id) REFERENCES app_user(user_id) ON DELETE CASCADE

                List of relations
   Schema   |        Name        | Type  | Owner  
------------+--------------------+-------+--------
 pg_catalog | pg_db_role_setting | table | luxmap
(1 row)
```

→ `\dt *role*` chỉ khớp bảng hệ thống: **không có bảng `role` / `user_role`**. Mỗi user đúng MỘT role
(cột scalar). Ghi chú: **không có ràng buộc DB nào nối `has_system_wide_scope` với `role`** — được
canh ở tầng authz (`CommuneScopeConsistencyHandler`), không ở DB.

### 1.1.b Dữ liệu role trong DB dev

```
$ ... -c 'SELECT role, has_system_wide_scope, count(*) FROM app_user GROUP BY role, has_system_wide_scope ORDER BY role;'
         role         | has_system_wide_scope | count 
----------------------+-----------------------+-------
 administrator        | f                     |    14
 administrator        | t                     |     1
 field_crew           | f                     |     1
 maintenance_engineer | f                     |     1
 management_agency    | f                     |     1
(5 rows)

 user_id  |       username       |         role         | has_system_wide_scope | is_locked |     communes      
----------+----------------------+----------------------+-----------------------+-----------+-------------------
 USR-001  | admin                | administrator        | t                     | f         | 
 USR-002  | agency               | management_agency    | f                     | f         | COM-070
 USR-003  | engineer             | maintenance_engineer | f                     | f         | COM-070
 USR-004  | crew                 | field_crew           | f                     | f         | COM-070
 USR-1802 | be12a-3a7a9094a1eb4f | administrator        | f                     | f         | COM-1604
 USR-1803 | be12b-4113b79e504c47 | administrator        | f                     | f         | COM-1603,COM-1604
 USR-1808 | be12a-61ade0e86dee4d | administrator        | f                     | f         | COM-1613
 USR-1809 | be12b-499d2f0d178842 | administrator        | f                     | f         | COM-1612,COM-1613
 USR-1810 | be12a-b4dca5767bdb4a | administrator        | f                     | f         | COM-1615
 USR-1811 | be12b-d03592275ea74b | administrator        | f                     | f         | COM-1615,COM-1616
 USR-1812 | be12a-e692c9ef6e6847 | administrator        | f                     | f         | COM-1618
 USR-1813 | be12b-b64174ff1d0645 | administrator        | f                     | f         | COM-1618,COM-1619
 USR-1814 | be12a-3623ce944d974e | administrator        | f                     | f         | COM-1622
 USR-1815 | be12b-7577048eeeaf4e | administrator        | f                     | f         | COM-1621,COM-1622
 USR-1816 | be12a-3e69aa753ada42 | administrator        | f                     | f         | COM-1625
 USR-1817 | be12b-ea1201513f7240 | administrator        | f                     | f         | COM-1624,COM-1625
 USR-1840 | be12a-74cef3178d7c46 | administrator        | f                     | f         | COM-1649
 USR-1842 | be12b-685fdf3ac22044 | administrator        | f                     | f         | COM-1649,COM-1650
(18 rows)

 total_users 
-------------
          18
(1 row)
```

- 4 tài khoản seed (`USR-001..004`), mỗi role một cái.
- **14 tài khoản cặn test** (`be12a-*`, `be12b-*`): role `administrator` **không** system-wide, gán
  vào xã test. Đây là cặn của fixture BE-12a/BE-12b (nợ N-5 / BE-36 đã ghi ở `CLAUDE.md`). Chúng là
  "admin có phạm vi xã" — đúng cái mà mô hình mới gọi là **Manager**. Xem D-R6.
- `mocks/mock-work-orders.json` dòng 21 và 37: `"assigned_to": "USR-004"` → mock phụ thuộc `USR-004`
  **là field crew**. Migration phải đổi role **tại chỗ**, không tạo lại user.

### 1.1.c Migration đụng tới role

```
$ ... -c 'SELECT migration_id FROM __ef_migrations_history ORDER BY 1;'
                   migration_id                   
--------------------------------------------------
 20260829083507_InitialIdentity
 20260829093106_AddRefreshTokenChainTracking
 20260831061451_AddCaseInsensitiveIdentityIndexes
 20260901082917_AddAssetEntities
 20260901084931_FixPrefixedIdOverflow
 20260901091555_AddCommuneForeignKeys
 20260905030514_AddLuxReading
 20260905034539_RejectNonFiniteLuxValues
 20260905085759_AddFaultEntities
 20260905095851_AddAssetExternalRef
 20260911160551_AddRefreshTokenSessionKind
 20260918154738_OneActiveFixturePerPole
 20260918155017_FixtureRemovedAfterInstall
 20260918155145_PoleCurrentStatusConfidenceRange
 20260921142601_FeederCommuneCompositeFk
 20260922002010_DropSolarFixtures
(16 rows)

$ grep -rn -i 'role' src/LuxMap.Persistence/Migrations/*.cs | grep -v Designer | grep -v ModelSnapshot
src/LuxMap.Persistence/Migrations/20260829083507_InitialIdentity.cs:48:                    role = table.Column<string>(type: "text", nullable: false),
src/LuxMap.Persistence/Migrations/20260829083507_InitialIdentity.cs:57:                    table.CheckConstraint("ck_app_user_role", "\"role\" IN ('management_agency', 'maintenance_engineer', 'field_crew', 'administrator')");
```

→ **Chỉ `InitialIdentity`** tạo và từng chạm role. 16/16 migration trong source đã apply trên DB dev.
Khuôn tiền lệ cho migration đổi giá trị enum: `DropSolarFixtures` (đổi dữ liệu **rồi mới** siết CHECK).

### 1.1.d Role trong code (grep)

```
$ grep -rn -E 'RequireRole|AddPolicy|ClaimTypes\.Role|IsInRole|RequireClaim|\[Authorize|\[AllowAnonymous' src/ | grep -v '/Migrations/'
src/LuxMap.Modules.Assets/Import/AssetImportController.cs:24:[Authorize(Policy = LuxMapPolicies.Administrator)]
src/LuxMap.Modules.Assets/Crud/AssetsController.cs:66:    [Authorize(Policy = LuxMapPolicies.Administrator)]
src/LuxMap.Modules.Assets/Crud/AssetsController.cs:76:    [Authorize(Policy = LuxMapPolicies.Administrator)]
src/LuxMap.Modules.Assets/Crud/AssetsController.cs:85:    [Authorize(Policy = LuxMapPolicies.Administrator)]
src/LuxMap.Modules.Assets/Crud/AssetsController.cs:96:    [Authorize(Policy = LuxMapPolicies.Administrator)]
src/LuxMap.Modules.Assets/Crud/AssetsController.cs:117:    [Authorize(Policy = LuxMapPolicies.Administrator)]
src/LuxMap.Modules.Assets/Crud/AssetsController.cs:133:    [Authorize(Policy = LuxMapPolicies.Administrator)]
src/LuxMap.Modules.Assets/Crud/AssetsController.cs:155:    [Authorize(Policy = LuxMapPolicies.Administrator)]
src/LuxMap.Modules.Assets/Crud/AssetsController.cs:177:    [Authorize(Policy = LuxMapPolicies.Administrator)]
src/LuxMap.Modules.Assets/Crud/AssetsController.cs:197:    [Authorize(Policy = LuxMapPolicies.Administrator)]
src/LuxMap.Modules.Assets/Crud/AssetsController.cs:221:    [Authorize(Policy = LuxMapPolicies.Administrator)]
src/LuxMap.Modules.Assets/Crud/AssetsController.cs:245:    [Authorize(Policy = LuxMapPolicies.Administrator)]
src/LuxMap.Modules.Assets/Crud/AssetsController.cs:343:    [Authorize(Policy = LuxMapPolicies.Administrator)]
src/LuxMap.Shared/Authorization/LuxMapPolicies.cs:12:/// <c>[Authorize(Policy = LuxMapPolicies.Administrator)]</c> could not see a name defined in the host.
src/LuxMap.Modules.Identity/Auth/Web/WebAuthController.cs:29:[AllowAnonymous]
src/LuxMap.Modules.Identity/Auth/AuthController.cs:23:// ⚠️ It used to sit on the class, and moving it was not tidying up. [AllowAnonymous] declared farther
src/LuxMap.Modules.Identity/Auth/AuthController.cs:24:// away BEATS an [Authorize] on a method, so the first endpoint here that needed a token (`me`) would
src/LuxMap.Modules.Identity/Auth/AuthController.cs:30:    [AllowAnonymous]
src/LuxMap.Modules.Identity/Auth/AuthController.cs:62:    [AllowAnonymous]
src/LuxMap.Modules.Identity/Auth/AuthController.cs:94:    [AllowAnonymous]
src/LuxMap.Modules.Identity/Auth/AuthController.cs:126:    [Authorize]
src/LuxMap.Modules.Identity/Auth/AuthController.cs:150:    [AllowAnonymous]
src/LuxMap.Api/Authorization/ForbiddenCodeResultHandler.cs:21:/// A role policy is one <see cref="ClaimsAuthorizationRequirement"/> (<c>RequireClaim(role, …)</c>).
src/LuxMap.Api/Authorization/AuthorizationSetup.cs:39:            // endpoint requires an explicit [AllowAnonymous].
src/LuxMap.Api/Authorization/AuthorizationSetup.cs:48:            .AddPolicy(LuxMapPolicies.ManagementAgency, RolePolicy(UserRole.ManagementAgency))
src/LuxMap.Api/Authorization/AuthorizationSetup.cs:49:            .AddPolicy(LuxMapPolicies.MaintenanceEngineer, RolePolicy(UserRole.MaintenanceEngineer))
src/LuxMap.Api/Authorization/AuthorizationSetup.cs:50:            .AddPolicy(LuxMapPolicies.FieldCrew, RolePolicy(UserRole.FieldCrew))
src/LuxMap.Api/Authorization/AuthorizationSetup.cs:51:            .AddPolicy(LuxMapPolicies.Administrator, RolePolicy(UserRole.Administrator));
src/LuxMap.Api/Authorization/AuthorizationSetup.cs:60:            .RequireClaim(AuthClaims.Role, ContractEnum.ToDbValue(role));
```

Không có `RequireRole`, `IsInRole`, `ClaimTypes.Role` nào. Claim role là claim tên `role`
(`AuthClaims.Role`), `MapInboundClaims = false`, `RoleClaimType = AuthClaims.Role`.

Quét theo từ khoá (danh sách file khớp, `src tests docs mocks scripts .ai/context .ai/README.md CLAUDE.md README.md tracking.html docker docker-compose.yml`, đã bỏ `*.Designer.cs`):

```
== management_agency
src/LuxMap.Shared/Authorization/LuxMapPolicies.cs src/LuxMap.Persistence/Migrations/20260829083507_InitialIdentity.cs src/LuxMap.Persistence/Migrations/LuxMapDbContextModelSnapshot.cs tests/LuxMap.Shared.Tests/UserRoleTests.cs docs/backend-report.md docs/archive/contract-drift-v1.md docs/api-contract-v1.1.md .ai/context/tracking.html tracking.html 
== maintenance_engineer
src/LuxMap.Shared/Authorization/LuxMapPolicies.cs src/LuxMap.Persistence/Migrations/20260829083507_InitialIdentity.cs src/LuxMap.Persistence/Migrations/LuxMapDbContextModelSnapshot.cs src/LuxMap.Modules.Identity/Auth/AuthContracts.cs tests/LuxMap.Api.Tests/AuthEndpointTests.cs tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs tests/LuxMap.Api.Tests/TopologyQueryTests.cs tests/LuxMap.Api.Tests/CurrentUserTests.cs tests/LuxMap.Api.Tests/AssetPermissionTests.cs tests/LuxMap.Api.Tests/AuthenticationTests.cs tests/LuxMap.Shared.Tests/UserRoleTests.cs tests/LuxMap.Api.Tests/PoleWriteTests.cs docs/authorization-guide.md docs/api-contract-v1.1.md docs/archive/contract-drift-v1.md docs/backend-report.md docs/review/BE-REVIEW-02.md .ai/context/tracking.html CLAUDE.md README.md tracking.html 
== field_crew
src/LuxMap.Shared/Authorization/LuxMapPolicies.cs src/LuxMap.Persistence/Migrations/LuxMapDbContextModelSnapshot.cs src/LuxMap.Persistence/Migrations/20260829083507_InitialIdentity.cs src/LuxMap.Modules.Identity/Auth/AuthClaims.cs src/LuxMap.Api/logs/luxmap-20260831.log tests/LuxMap.Api.Tests/RegistrationTests.cs tests/LuxMap.Api.Tests/CommuneScopeTests.cs tests/LuxMap.Api.Tests/AuthenticationTests.cs tests/LuxMap.Shared.Tests/UserRoleTests.cs tests/LuxMap.Api.Tests/AssetPermissionTests.cs tests/LuxMap.Api.Tests/PoleWriteTests.cs docs/api-contract-v1.1.md docs/authorization-guide.md docs/backend-report.md docs/archive/contract-drift-v1.md .ai/context/tracking.html tracking.html 
== administrator
src/LuxMap.Modules.Assets/Crud/AssetContracts.cs src/LuxMap.Modules.Assets/Crud/AssetsController.cs src/LuxMap.Modules.Assets/Import/AssetImportService.cs src/LuxMap.Shared/Authorization/LuxMapPolicies.cs src/LuxMap.Shared/Authorization/CommuneScope.cs src/LuxMap.Modules.Map/Features/MapController.cs src/LuxMap.Modules.Map/MapModule.cs src/LuxMap.Persistence/Migrations/20260829083507_InitialIdentity.cs src/LuxMap.Persistence/Migrations/LuxMapDbContextModelSnapshot.cs src/LuxMap.Modules.Identity/Auth/AuthController.cs src/LuxMap.Modules.Identity/Auth/AuthFailureErrors.cs src/LuxMap.Modules.Identity/Auth/AuthContracts.cs src/LuxMap.Modules.Identity/Seeding/IdentitySeeder.cs src/LuxMap.Modules.Identity/Entities/AppUser.cs src/LuxMap.Modules.Identity/Auth/AuthClaims.cs src/LuxMap.Modules.Identity/Auth/AuthService.cs src/LuxMap.Api/Http/ExceptionHandlingMiddleware.cs src/LuxMap.Api/Authorization/CommuneScopeConsistency.cs tests/LuxMap.Api.Tests/RegistrationTests.cs tests/LuxMap.Api.Tests/CommuneScopeTests.cs tests/LuxMap.Api.Tests/AuthEndpointTests.cs tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs tests/LuxMap.Api.Tests/AssetImportTests.cs tests/LuxMap.Api.Tests/LuxReadingTests.cs tests/LuxMap.Api.Tests/AssetPermissionTests.cs tests/LuxMap.Api.Tests/AssetImportFixture.cs tests/LuxMap.Api.Tests/CurrentUserTests.cs tests/LuxMap.Api.Tests/PoleWriteTests.cs tests/LuxMap.Api.Tests/CommuneWriteScopeTests.cs docs/authorization-guide.md docs/api-contract-v1.1.md docs/archive/contract-drift-v1.md docs/backend-report.md tests/LuxMap.Shared.Tests/UserRoleTests.cs .ai/context/tracking.html tracking.html 
== ManagementAgency
src/LuxMap.Shared/Contracts/Enums/UserRole.cs src/LuxMap.Shared/Authorization/LuxMapPolicies.cs src/LuxMap.Modules.Identity/Seeding/SeedCredentials.cs src/LuxMap.Api/Authorization/AuthorizationSetup.cs src/LuxMap.Modules.Identity/Seeding/IdentitySeeder.cs tests/LuxMap.Shared.Tests/UserRoleTests.cs docs/authorization-guide.md 
== MaintenanceEngineer
src/LuxMap.Modules.Assets/Crud/AssetsController.cs src/LuxMap.Shared/Contracts/Enums/UserRole.cs src/LuxMap.Shared/Authorization/LuxMapPolicies.cs src/LuxMap.Modules.Identity/Seeding/SeedCredentials.cs src/LuxMap.Modules.Identity/Seeding/IdentitySeeder.cs tests/LuxMap.Api.Tests/ScopeTestController.cs tests/LuxMap.Api.Tests/FaultSchemaTests.cs tests/LuxMap.Api.Tests/AssetPermissionTests.cs src/LuxMap.Api/Authorization/AuthorizationSetup.cs tests/LuxMap.Shared.Tests/UserRoleTests.cs docs/authorization-guide.md tests/LuxMap.Api.Tests/CommuneWriteScopeTests.cs docs/code-walkthrough.md docs/review/BE-13-topology-shape.md docs/review/BE-12b-read-shape.md CLAUDE.md 
== FieldCrew
src/LuxMap.Shared/Contracts/Enums/UserRole.cs src/LuxMap.Shared/Authorization/LuxMapPolicies.cs src/LuxMap.Modules.Identity/Auth/AuthService.cs src/LuxMap.Modules.Identity/Seeding/SeedCredentials.cs src/LuxMap.Modules.Identity/Seeding/IdentitySeeder.cs src/LuxMap.Api/Authorization/AuthorizationSetup.cs tests/LuxMap.Shared.Tests/UserRoleTests.cs docs/authorization-guide.md 
== Surveyor

== surveyor

== Leader
docs/registration/FA26SE222_v1.2.md 
== Officer
src/LuxMap.Modules.Identity/Seeding/IdentitySeeder.cs 
== Manager
src/LuxMap.Api/logs/luxmap-20260920.log docs/registration/FA26SE222_v1.2.md 
== FieldEngineer

== field_engineer

== Superior
docs/registration/FA26SE222_v1.2.md 
== superior

== Citizen
src/LuxMap.Shared/Contracts/Enums/UserRole.cs src/LuxMap.Modules.Identity/Entities/AppUser.cs tests/LuxMap.Api.Tests/AnonymousEndpointTests.cs tests/LuxMap.Shared.Tests/UserRoleTests.cs docs/registration/FA26SE222_v1.2.md 
== citizen
tests/LuxMap.Shared.Tests/UserRoleTests.cs 
== system_admin

```

Ghi chú đọc kết quả:
- `Officer` duy nhất là chuỗi hiển thị `"Managing Authority Officer"` (full_name của seed `agency`,
  `IdentitySeeder.cs`), không phải role.
- `Manager` trong `src/` chỉ khớp một file log (`src/LuxMap.Api/logs/…`), không phải code.
- **`administrator` khớp `MapController.cs`, `CommuneScope.cs`… vì XML doc nói về scope `*`**, không
  phải vì gắn policy.
- **Không có hit nào trong `mocks/`, `scripts/`, `docker/`, `docker-compose.yml`** với các giá trị role.
  Biến môi trường seed nằm ở `.env.example:23-26` (`SEED_ADMIN_PASSWORD`, `SEED_AGENCY_PASSWORD`,
  `SEED_ENGINEER_PASSWORD`, `SEED_CREW_PASSWORD`).
- `docs/openapi/luxmap-v1.json` và `luxmap-v1.5.json`: **0 lần** nhắc tới bất kỳ giá trị role nào,
  **0 lần** `ROLE_FORBIDDEN` (lệnh ở mục 1.4.b).

### 1.1.e Policy → Role

`AuthorizationSetup.cs:48-60` — mỗi policy là `RequireAuthenticatedUser()` +
`CommuneScopeConsistencyRequirement` + `RequireClaim("role", <đúng một giá trị>)`.

| Policy (hằng `LuxMapPolicies`) | Chuỗi policy | Role nhận | Call site sản xuất |
|---|---|---|---|
| `ManagementAgency` | `role:management_agency` | `management_agency` | **0** |
| `MaintenanceEngineer` | `role:maintenance_engineer` | `maintenance_engineer` | **0** (chỉ `tests/…/ScopeTestController.cs:74`) |
| `FieldCrew` | `role:field_crew` | `field_crew` | **0** |
| `Administrator` | `role:administrator` | `administrator` | **15** (14 ở `AssetsController`, 1 cấp class ở `AssetImportController`) |
| *Default policy* (`[Authorize]` trần) | — | mọi role đã đăng nhập + kiểm `*`↔role | 1 (`GET /auth/me`) |
| *Fallback policy* (không gắn gì) | — | mọi role đã đăng nhập + kiểm `*`↔role | mọi endpoint còn lại |

### 1.1.f Endpoint → Policy (đủ mọi controller sản xuất)

Nguồn: `grep -rn -E '\[Http(Get|Post|Put|Patch|Delete)|\[Route\(|\[Authorize|\[AllowAnonymous\]|class [A-Za-z]+Controller' src`
(output đầy đủ đã chạy trong phiên; `Program.cs:111` chỉ có `app.MapControllers()`, không có minimal API).

| # | Endpoint | Policy | Role vào được hôm nay |
|---|---|---|---|
| 1 | `POST /api/v1/auth/login` | `[AllowAnonymous]` | ẩn danh |
| 2 | `POST /api/v1/auth/register` | `[AllowAnonymous]` | **ẩn danh — tự đăng ký**, server gán `field_crew` (`AuthService.LowestRole`, dòng 176) |
| 3 | `POST /api/v1/auth/refresh` | `[AllowAnonymous]` | ẩn danh |
| 4 | `GET /api/v1/auth/me` | `[Authorize]` (default) | cả 4 |
| 5 | `POST /api/v1/auth/logout` | `[AllowAnonymous]` | ẩn danh |
| 6–8 | `POST /api/v1/auth/web/{login,refresh,logout}` | `[AllowAnonymous]` cấp class | ẩn danh |
| 9–11 | `GET /api/v1/assets/{segments,feeders,poles}` | fallback | cả 4 |
| 12–15 | `POST /api/v1/assets/{segments,feeders,poles,fixtures}` | `Administrator` | `administrator` |
| 16–18 | `PUT /api/v1/assets/{segments,feeders,poles}/{id}` | `Administrator` | `administrator` |
| 19–21 | `DELETE /api/v1/assets/{segments,feeders,poles}/{id}` | `Administrator` | `administrator` |
| 22 | `PUT /api/v1/assets/poles/{id}/feeder` | `Administrator` | `administrator` |
| 23 | `PUT /api/v1/assets/fixtures/{id}/removal` | `Administrator` | `administrator` |
| 24–26 | `GET /api/v1/assets/feeders/{id}/poles`, `/segments/{id}/poles`, `/feeders/poles` | fallback | cả 4 |
| 27 | `POST /api/v1/assets/import/{kind}` | `Administrator` cấp class | `administrator` |
| 28 | `GET /api/v1/poles` (bbox) | fallback | cả 4 |
| 29 | `GET /api/v1/segments` (bbox) | fallback | cả 4 |
| 30 | `POST /api/v1/lux-readings` | **fallback — endpoint GHI không gắn policy** | cả 4 |
| 31 | `GET /api/v1/lux-readings/poles/{id}` | fallback | cả 4 |
| 32 | `GET /api/v1/lux-readings` | fallback | cả 4 |

Chỉ trong test: `/_scope/*` (`ScopeTestController`: `engineer-only` = `MaintenanceEngineer`, `admin-only`
= `Administrator`, `open` = anonymous) và `TestEndpointsController`.

### 1.1.g Drift 31 — trích NGUYÊN VĂN

⚠️ **Drift 31 không nằm trong `docs/contract-drift.md`.** Log mới chỉ nhắc nó ở một mệnh đề đầu file
(*"(2) policy là một vai trò chính xác"*). Toàn văn nằm ở log cũ đã archive
`docs/archive/contract-drift-v1.md`, và đã được gộp vào **Contract v1.4 §2** dưới tên **D-14**.

`docs/archive/contract-drift-v1.md:36-58` (FW-00, nguyên tắc 2):

> ### 2. Drift 31 nâng thành Contract rule
>
> > 🟡 **Mục này CHỜ FW-00 chốt. CHƯA vào Contract.** Đang là đề xuất, và tới khi được duyệt thì
> > `api-contract-v1.1.md` không thay đổi một chữ nào.
>
> **Policy là MỘT vai trò chính xác, không phải một bậc.** `RequireClaim(role, "<một giá trị>")` khớp
> đúng một vai trò; nó **không** có nghĩa "từ cấp này trở lên". Gắn `maintenance_engineer` lên một
> endpoint ĐỌC sẽ **chặn luôn Quản trị và Cơ quan quản lý** — trông như siết bảo mật, thực chất là chặn
> hai vai trò khỏi dữ liệu của chính họ.
>
> Bảng vai trò được GHI, áp cho **BE-12a / BE-15 / BE-17 / BE-18 / BE-21 / BE-24**:
>
> | Nhóm endpoint | Ticket | Vai trò được GHI | Đọc |
> |---|---|---|---|
> | `/assets/*` — CRUD tài sản, import | BE-12a | Quản trị | mọi vai trò đã đăng nhập |
> | Sweep, frame, luminance | BE-15, BE-17 | *chờ chốt* | mọi vai trò đã đăng nhập |
> | Fault — chuyển trạng thái | BE-18, BE-19 | *chờ chốt* | mọi vai trò đã đăng nhập |
> | Work order, evidence | BE-21, BE-24 | *chờ chốt* | mọi vai trò đã đăng nhập |
>
> **Đọc KHÔNG gắn policy nào** — `SetFallbackPolicy` đã bắt buộc đăng nhập, và nêu tên một vai trò ở đó
> là loại trừ ba vai trò kia chứ không phải đặt sàn.
>
> Chốt **một lần cho cả nhóm**: BE-12a đã hiện thực nên tiền lệ đã tạo, sáu ticket tự chọn riêng sẽ ra
> sáu ma trận quyền khác nhau mà không ai giải thích được. Chi phí đảo hướng đo ở mục 31b.

`docs/archive/contract-drift-v1.md:367` (dòng bảng):

> | 31 | **Vai trò nào được GHI tài sản** — mục 7 chỉ nói phạm vi địa bàn | 🔴 Cao | WP5, WP6, BE-33, **BE-15/18/21/24** | **FW-00 — chốt MỘT LẦN cho cả nhóm ticket ghi**, không để mỗi ticket tự chọn |

Trạng thái hiện hành: `docs/api-contract-v1.1.md` §2 *"Quy tắc vai trò (v1.4, D-14 — EXACT-ROLE)"* —
*"Policy là **một vai trò chính xác, không phải một bậc**"*, bảng GHI: `/assets/*` = **Quản trị**;
`/lux-readings` (POST) = *"Mọi vai trò đã đăng nhập"*; sweep/fault/work order = `[OPEN → O-2]`.

**Code hiện xử lý endpoint nhiều role cần vào như thế nào:** bằng cách **không gắn policy** — fallback
chỉ đòi đăng nhập. Hệ quả: "nhiều role" hôm nay luôn có nghĩa là **cả bốn** role. **Không có endpoint
nào đang cho đúng 2 hoặc 3 role**, và không có cơ chế nào để diễn đạt điều đó mà vẫn khớp chữ "một vai
trò chính xác". Chuyện này vô hại tới hôm nay vì cả bốn role cũ đều được phép đọc mọi thứ và BE chưa
có endpoint ghi nào cần 2 role. **Phiếu v1.2 phá giả định đó ở hai chỗ:** Superior chỉ-đọc (nên
`POST /lux-readings` không thể để trần), và nhiều thao tác ghi chia cho hai role (Manager tạo task /
FE cập nhật trạng thái cùng task). → **D-R5**.

### 1.1.h `CommuneWriteGuard` và phạm vi đọc

- **Guard không nhìn role.** `CommuneWriteGuard.Enforce(changeTracker, scope)` chỉ nhìn `CommuneScope`:
  `scope.IsSystemWide` (claim `["*"]`) thì bỏ qua (dòng 45); ngược lại mọi `ICommuneScoped` ghi phải nằm
  trong `CommuneIds`. Nên **mọi role không-system-wide đều bị giới hạn theo xã khi ghi** — hôm nay là
  `management_agency`, `maintenance_engineer`, `field_crew`, và cả 14 `administrator` cặn test. Chỉ
  tài khoản có `has_system_wide_scope = true` (hiện chỉ `USR-001`) thoát.
- **`*` bị buộc với role Administrator** ở hai chỗ: `CommuneScopeConsistencyHandler` (403 + log Error nếu
  claim `*` mà role ≠ administrator) và `CommuneScopeAccessor.FromPrincipal` (fail-closed).
- **Scope ĐỌC là tập XÃ, không phải huyện.** `HasQueryFilter` đưa `commune_id IN (claim)` vào `WHERE`.
  DB không có cấp huyện:

```
$ ... -c '\d administrative_unit'
                                                  Table "public.administrative_unit"
   Column   |           Type           | Collation | Nullable |                                Default                                
------------+--------------------------+-----------+----------+-----------------------------------------------------------------------
 commune_id | text                     |           | not null | luxmap_format_id('COM'::text, nextval('commune_id_seq'::regclass), 3)
 name       | text                     |           | not null | 
 created_at | timestamp with time zone |           | not null | now()
 updated_at | timestamp with time zone |           | not null | now()
 seed_key   | text                     |           |          | 
```

  → không có `parent_id`, `district_id`, `level`. "Nhiều xã" (Contract §2: *"Cơ quan quản lý — Có thể
  gồm nhiều xã"*) đi qua nhiều dòng `app_user_commune`, đã chạy được hôm nay (xem `USR-1803` có
  `COM-1603,COM-1604`).
- **Read-only không được guard thực thi.** Guard chặn ghi *ngoài xã*, không chặn *role không được ghi*.
  Việc một role chỉ-đọc không ghi được là việc của policy trên từng endpoint ghi → xem `POST /lux-readings`.

---

## 1.2 Mapping role cũ → role mới (ĐỀ XUẤT, chưa áp dụng)

Tên canonical đề xuất: `superior`, `manager`, `field_engineer`, `system_admin`.

| Role cũ | Nơi xuất hiện | Số user/seed đang dùng | Role mới đề xuất | Ghi chú |
|---|---|---|---|---|
| `management_agency` | `UserRole.ManagementAgency`; `ck_app_user_role`; `LuxMapPolicies.ManagementAgency` (0 call site); seed `agency`/`SEED_AGENCY_PASSWORD`; Contract §2, §3.1; `backend-report.md`; `tracking.html` | **1** (`USR-002`, `COM-070`) | `superior` | Contract §2 mô tả *"Có thể gồm nhiều xã"* — đúng vai giám sát nhiều xã của Superior. Mô hình mới: **chỉ đọc** (hôm nay nó ghi được `POST /lux-readings`). |
| `maintenance_engineer` | `UserRole.MaintenanceEngineer`; CHECK; policy (chỉ test); seed `engineer`; Contract §2, §4.5, §4.7 (ví dụ `/auth/me`); ~40 call site test dùng `engineer`/`SEED_ENGINEER_PASSWORD` | **1** (`USR-003`, `COM-070`) | `manager` | Mô tả cũ: *"reviews faults, limited to the communes in the claim"* = Manager "Review the faults reported by FE". **Phương án khác ở D-R6.** |
| `field_crew` | `UserRole.FieldCrew`; CHECK; `AuthService.LowestRole` (vai trò tự đăng ký); seed `crew`; Contract §2, §4.1 (ví dụ `register`); `mock-work-orders.json` `assigned_to: USR-004` | **1** (`USR-004`, `COM-070`) | `field_engineer` | "Tổ khảo sát / sửa chữa" — **Surveyor đã gộp sẵn ở đây**, không có role Surveyor riêng nào để xoá. |
| `administrator` | `UserRole.Administrator`; CHECK; **policy duy nhất có call site sản xuất** (15 chỗ, `/assets/*`); seed `admin` (`*`); `CommuneScopeConsistencyHandler`; Contract §2 | **15** = 1 system-wide (`USR-001`) + **14 cặn test** scoped | `system_admin` | ⚠️ **Tên đổi được 1-1, nhưng QUYỀN thì không**: phiếu giao "Manage lighting poles and assets" cho **Manager**, System Admin không có. Nên `/assets/*` phải chuyển policy → D-R12. 14 cặn test → D-R6. |
| *(không có)* | — | 0 | `citizen`? | Phiếu: actor ẩn danh qua QR. Chờ **D-R1** — khuyến nghị **không** thành role. |

Mapping đề xuất là **song ánh** (4 ↔ 4) → `Down()` của migration khôi phục được **không mất dữ liệu**,
khác `DropSolarFixtures` (đổi solar→grid là mất thông tin).

---

## 1.3 Ma trận quyền theo phiếu v1.2 (mục 3.2.c) ↔ endpoint hiện có

Ký hiệu: **CÓ** (endpoint tồn tại, role đúng) · **CÓ\*** (tồn tại nhưng hôm nay mở cho mọi role —
đúng cho role này, xem ghi chú) · **THIẾU** · **THỪA/SAI ROLE**. "Contract" = đã đặc tả, chưa code.

### Superior

| Capability | Trạng thái | Endpoint / ghi chú |
|---|---|---|
| Xem bản đồ mạng chiếu sáng | **CÓ\*** | `GET /poles`, `GET /segments` (fallback) |
| Xem thống kê sự cố | **THIẾU** | BE-28 (chưa đặc tả, Contract không có) |
| Xem báo cáo phân tích | **THIẾU** | BE-28…BE-31 (chưa đặc tả) |
| Export báo cáo | **THIẾU** | Chỉ BE-31 nhắc "xuất được báo cáo" (warranty). Không có đặc tả |
| **Chỉ đọc** | **SAI** | `POST /lux-readings` không gắn policy → Superior **ghi được**. Mọi endpoint ghi tương lai để trần cũng vậy |

### Manager

| Capability | Trạng thái | Endpoint / ghi chú |
|---|---|---|
| Xem bản đồ GIS | **CÓ\*** | `GET /poles`, `GET /segments` |
| Quản lý cột / tài sản | **THỪA/SAI ROLE** | 15 endpoint `/assets/*` + import đang gắn `Administrator`; phiếu giao Manager (D-R12) |
| Xem lịch sử bảo trì tài sản | **THIẾU** | `GET /poles/{id}` (Contract §5.1, BE-20) chưa code; lịch sử work order chưa có bảng |
| Tạo + giao survey route cho FE | **THIẾU** | Không entity, không Contract |
| Review chất lượng survey session, accept/reject | **THIẾU** | `GET /sweeps` (Contract §5.6) chỉ có `processing_status`, không có accept/reject |
| Review fault do FE báo | **THIẾU** (Contract) | `PATCH /faults/{id}` §5.4, BE-19 |
| Tạo + giao task inspection/repair | **THIẾU** (Contract) | `POST /work-orders` §5.5, BE-21 |
| Quản lý lịch làm việc + trạng thái task của FE | **THIẾU** | Không có khái niệm lịch làm việc ở đâu |
| Điều khiển đèn ON/OFF/AUTO | **THIẾU** | D-R7 đã chốt: chỉ khai policy. Chưa có bảng thiết bị để mang `supports_remote_control` |
| Xem trạng thái vận hành thiết bị được hỗ trợ | **THIẾU** (Contract) | `GET /iot-nodes` §5.6 |

### Field Engineer

| Capability | Trạng thái | Endpoint / ghi chú |
|---|---|---|
| Xem survey route được giao (thứ tự đoạn, chiều đi) | **THIẾU** | Không entity |
| Quay **video** đêm, khoá phơi sáng | **THIẾU** + **D-R14** | Client. Upload sweep BE-15 chưa code; BE-11 chỉ nhận JPEG |
| Ghi GPS / hướng / timestamp đồng bộ video | **THIẾU** | BE-16 |
| Buffer offline + sync | **THIẾU** (Contract) | `GET /sync/bundle`, `POST /sync/push` §5.8, BE-43 |
| Xem trạng thái xử lý session | **THIẾU** (Contract) | `GET /sweeps` §5.6 |
| Re-survey khi session bị reject | **THIẾU** | Phụ thuộc survey session |
| Xem task inspection/repair được giao | **THIẾU** (Contract) | `GET /work-orders?assigned_to=` §5.5 |
| Dẫn đường tới tài sản | **CÓ\*** | Client + `GET /poles` |
| Kiểm tra hiện trường | — | Thao tác ngoài hệ thống |
| Quay + upload **video** thiết bị | **THIẾU** + **D-R14** | `POST /work-orders/{id}/evidence` (§5.5) chỉ ảnh, BE-11 JPEG-only |
| Báo fault mới | **THIẾU** (Contract) | `POST /faults` §5.4, BE-41 |
| Cập nhật trạng thái task | **THIẾU** (Contract) | `PATCH /work-orders/{id}` §5.5 |
| Nộp báo cáo inspection/repair | **THIẾU** | Không có đặc tả ngoài evidence |
| (Đo lux — phương pháp mục 3.3.c) | **CÓ\*** | `POST /lux-readings` — hiện mở cho cả 4 role |

### System Admin

| Capability | Trạng thái | Endpoint / ghi chú |
|---|---|---|
| Quản lý account (không self-registration) | **THIẾU** + **THỪA** | Không có API tạo/sửa account (hướng dẫn hiện tại: `UPDATE` bằng SQL, `authorization-guide.md:155-186`). Trong khi `POST /auth/register` **mở tự đăng ký** (D-R11) |
| Quản lý role/permission | **THIẾU** | BE-33 (chưa đặc tả). D-R4 |
| Cấu hình hệ thống | **THIẾU** | BE-33 (ngưỡng dim/out, trọng số ưu tiên) |
| Giám sát vận hành | **THIẾU** | BE-35 |
| Xem system log | **THIẾU** | Serilog ghi file (`src/LuxMap.Api/logs/`), không có endpoint |
| Ghi tài sản `/assets/*` | **THỪA/SAI ROLE** | Không có trong phiếu cho System Admin (D-R12) |

### Citizen

| Capability | Trạng thái | Endpoint / ghi chú |
|---|---|---|
| Gửi báo sự cố qua QR | **THIẾU** | Mâu thuẫn trực tiếp với 3 thứ đang canh trong repo: `CLAUDE.md:8,1219,1313`; `UserRoleTests.There_are_exactly_four_roles_and_none_of_them_is_a_citizen`; `AnonymousEndpointTests` (whitelist đúng 7 endpoint ẩn danh, ghi quyết định nhóm *"bỏ actor guest"* 20/09/2026). D-R1 |

---

## 1.4 Kiểm kê chỗ lệch trong docs

### 1.4.a Bằng chứng quét

```
$ python3 (quét 16 file docs + docs/review/*.md, đếm hit theo từ khoá)
solar                    CLAUDE.md:8 tracking.html:5 api-contract-v1.1.md:10 contract-drift.md:1 README.md:4 README.md:6 tracking.html:5 BE-13-topology-shape.md:4
Nhánh C                  CLAUDE.md:4 tracking.html:3 api-contract-v1.1.md:2 backend-report.md:1 contract-drift.md:2 tracking.html:3 BE-12b-read-shape.md:2 BE-13-topology-shape.md:1
nhánh C                  CLAUDE.md:2 README.md:2
Branch C                 
/register                README.md:1 api-contract-v1.1.md:1 authorization-guide.md:1 gen_consolidated_spec.py:1 BE-REVIEW-02.md:1
Người dân                CLAUDE.md:1 backend-report.md:1 tasks-backend.csv:1
citizen                  
QR                       CLAUDE.md:3 tracking.html:2 tracking.html:2
Superior                 
Field Engineer           
System Admin             
2027                     CLAUDE.md:1 tracking.html:1 tasks-backend.csv:8 tracking.html:1 BE-12b-read-shape.md:1
W21                      CLAUDE.md:2 tracking.html:1 tasks-backend.csv:3 tracking.html:1
field trial              
Field Trial              
ON/OFF                   
điều khiển               
Controlled Reference     
Night Survey Procedure   
Annotated                
lịch làm việc            
re-survey                
Tổ khảo sát              CLAUDE.md:1 api-contract-v1.1.md:2 backend-report.md:1 tasks-backend.csv:2 gen_consolidated_spec.py:1
Cơ quan quản lý          CLAUDE.md:2 tracking.html:1 api-contract-v1.1.md:1 tasks-backend.csv:1 tracking.html:1 BE-12b-read-shape.md:1 BE-13-topology-shape.md:1
Kỹ sư bảo trì            CLAUDE.md:2 api-contract-v1.1.md:2 tasks-backend.csv:2
field_crew               tracking.html:1 api-contract-v1.1.md:3 authorization-guide.md:1 backend-report.md:2 tracking.html:1
management_agency        tracking.html:1 api-contract-v1.1.md:2 backend-report.md:2 tracking.html:1
maintenance_engineer     CLAUDE.md:1 README.md:1 tracking.html:1 api-contract-v1.1.md:5 authorization-guide.md:1 backend-report.md:3 tracking.html:1 BE-REVIEW-02.md:1
administrator            CLAUDE.md:1 tracking.html:2 api-contract-v1.1.md:3 authorization-guide.md:2 backend-report.md:3 contract-drift.md:1 tracking.html:2 BE-REVIEW-02.md:1
```

(Hai `README.md`/`tracking.html` trong một dòng = root + `docs/templates/README.md` hoặc `mocks/README.md` /
`.ai/context/tracking.html`. "QR" khớp chuỗi con trong từ khác, không phải mã QR — kiểm tay: 0 chỗ nói
về QR code.) `diff -q tracking.html .ai/context/tracking.html` → **giống hệt** (bản sao untracked).
`docs/openapi/*.json`: `solar` 0, role 0, `ROLE_FORBIDDEN` 0; `luxmap-v1.5.json` `info.version = 1.5`.

**Chưa có tài liệu nào trong repo nói về:** Superior, Field Engineer, System Admin, Citizen/QR, điều
khiển ON/OFF/AUTO, survey route, accept/reject session, re-survey, lịch làm việc, 4 deliverable phiếu
(Controlled Reference Capture Set, Night Survey Procedure Package, Annotated Night Lighting Dataset,
Field Trial and Evaluation Report), precision/recall theo lớp, feeder topology coverage.

**Điều khiển lưới thật (D-R7):** quét `remote|điều khiển|force on|force off|AUTO|bật/tắt|supports_remote|
command|lệnh` trên docs + `src/` → **không có chỗ nào ngầm hiểu điều khiển lưới thật**. Hai điểm dễ
đọc nhầm, nên ghi rõ khi sửa docs:
1. Phiếu mục 3.2.c ghi *"Control streetlights (ON, OFF, AUTO)"* **không kèm** chữ "supported" (dòng
   ngay dưới thì có). Chính phiếu là chỗ duy nhất ngầm hiểu lưới thật.
2. `node_role = segment_controller` (Contract §3.1, `CLAUDE.md:162`, mock 3 node) nghe như "bộ điều
   khiển đoạn" nhưng là **node cảm biến đặt ở tủ điều khiển đoạn** (phiếu 3.2.b: *"IoT nodes at a
   segment controller … reports power state, current draw"*) — không phát lệnh.

### 1.4.b Bảng kiểm kê

| File | Vị trí | Hiện tại | Theo v1.2 | Loại |
|---|---|---|---|---|
| `CLAUDE.md` | L3 | "Nền tảng GIS + IoT + Computer Vision quản lý tài sản và sự cố chiếu sáng đường nông thôn." | Tên đề tài EN/VI chính thức (phiếu 3.1.1/3.1.2) | thêm |
| `CLAUDE.md` | L4 | "W1–W21: 07/09/2026 – 31/01/2027" | 09/2026 – 03/2027 | **D-R9** |
| `CLAUDE.md` | L8 | "Không có API công khai cho người dân." | Citizen báo qua QR | sửa — sau **D-R1** |
| `CLAUDE.md` | L13 | "bản hợp nhất **v1.5**" | (không do phiếu) Contract đang là **v1.6** | sửa |
| `CLAUDE.md` | L79–103 "Phạm vi đã chốt — Nhánh C" | "**Không có thử nghiệm hiện trường.** Ba nguồn dữ liệu" | Field trial trên mạng thí điểm với xã đối tác; ground truth kiểm tra đêm thủ công | **D-R10** |
| `CLAUDE.md` | L86 | "Bộ hiệu chuẩn tự dựng (FO-07)" | Deliverable **Controlled Reference Capture Set** | thêm (đối chiếu tên) |
| `CLAUDE.md` | L150–181 khối enum + ghi chú v1.6 | Giải thích việc xoá solar | Phiếu không có solar | **D-R18** (giữ làm lịch sử lý do hay xoá) |
| `CLAUDE.md` | L253 | "Còn để mở: … chính sách lưu ảnh dài hạn" | NFR: giữ ảnh/telemetry/fault **qua thời hạn bảo hành** | sửa — sau **D-R16** |
| `CLAUDE.md` | L487 BE-11 quy tắc 5 | "Chỉ nhận JPEG, quyết bằng MAGIC BYTES" | FE quay **video** (survey + thiết bị) | **D-R14** |
| `CLAUDE.md` | L557 BE-42 | "`lux_value` — **tuyệt đối, đơn vị lux**" | Đo bằng smartphone, **thang tương đối**, không tuyên bố giá trị tuyệt đối | **D-R15** |
| `CLAUDE.md` | L593–697 BE-12a quy tắc 4 | "POST / PUT / import: `LuxMapPolicies.Administrator`"; L682 ví dụ `maintenance_engineer` | Manager quản lý tài sản | sửa — sau **D-R12/D-R6** |
| `CLAUDE.md` | L946–955, L1232 BE-18 | "chỉ giữ quyết định MỚI NHẤT, không giữ chuỗi"; `FaultHistory` "chỉ dựng nếu BE-19 cần" | NFR: audit trail **mọi** quyết định của engineer | **D-R13** |
| `CLAUDE.md` | L968–969 | "`administrative_unit` không có cột geometry — cố ý, nhánh C không có nguồn ranh giới thật" | (nhãn Nhánh C) | sửa — sau **D-R10** |
| `CLAUDE.md` | L1151 O-7 bẫy 1 | Giải thích lý do cũ của `MATCH SIMPLE` qua `solar_all_in_one` | Phiếu không có solar | **D-R18** |
| `CLAUDE.md` | L1214–1219 "### Vai trò" | 4 vai trò cũ; "**Không có vai trò Người dân.**" | 4 role đăng nhập + Citizen ẩn danh (QR) | sửa — sau **D-R1/D-R6** |
| `CLAUDE.md` | L1227–1236 "Quy tắc dễ sai âm thầm" | Không nói precision/recall theo lớp, không nói feeder coverage | NFR: precision/recall **riêng** cho out và dim; báo tỉ lệ cột có feeder xác nhận | thêm |
| `CLAUDE.md` | L1279 | "`POLE-0047` là cột solar … pin yếu làm đèn mờ" | **Cũ từ v1.6** (nay `grid`, `mocks/README.md:26` đã sửa) | sửa |
| `CLAUDE.md` | L1288–1293 | "Dưới Nhánh C … danh tính ngoài VĨNH VIỄN … di trú mã ngoài phạm vi" | Field trial có thể mang mã kiểm kê thật về → lập trường "đã đóng" mất nền | **D-R10** |
| `CLAUDE.md` | L1310, L1313 | "ba nguồn dữ liệu của nhánh C"; "**Đừng thêm luồng người dân gửi phản ánh.**" | Giữ luật không gộp nguồn, bỏ nhãn; Citizen QR có trong phiếu | sửa — sau **D-R10/D-R1** |
| `CLAUDE.md` | L1333 | Lộ trình tới W21 | Timeline phiếu | **D-R9** |
| `CLAUDE.md` | (mới) | — | Ràng buộc D-R7: chỉ Manager, chỉ thiết bị `supports_remote_control = true` (testbed), mọi lệnh ghi audit | thêm |
| `README.md` | L5 | "Contract **v1.5**" | (không do phiếu) v1.6 | sửa |
| `README.md` | L203–205 | "bốn tài khoản … `admin`, `agency`, `engineer`, `crew`" | 4 account demo theo role mới | sửa — sau **D-R6** |
| `README.md` | L310–324 | `POST /api/v1/auth/register` trong danh sách 7 endpoint không cần token | Không self-registration | sửa — sau **D-R11** |
| `README.md` | L340–341 | ví dụ `role: maintenance_engineer`, "Quản trị: `["*"]`" | Giá trị role mới | sửa — sau **D-R6** |
| `docs/api-contract-v1.1.md` | L1–8 tiêu đề/changelog | v1.6 | Cần v1.7 (đổi enum `user_role` là **BREAKING**) | sửa — sau **D-R17** |
| `docs/api-contract-v1.1.md` | L158 §1.6 | "`field` giữ cho tương lai; Nhánh C không sinh bản ghi nào mang nó." | Field trial sinh `data_source = field` | sửa — sau **D-R10** (qua drift) |
| `docs/api-contract-v1.1.md` | L162–196 §2 | Bảng 4 vai trò cũ; "Tài khoản mới đăng ký…"; bảng GHI `/assets/*` = Quản trị, `/lux-readings` = mọi vai trò; O-2 | 4 role mới + Citizen; ma trận quyền mới | sửa — sau D-R1…D-R6, D-R11, D-R12 (qua drift) |
| `docs/api-contract-v1.1.md` | L217 §3.1 | `user_role : management_agency \| maintenance_engineer \| field_crew \| administrator` | `superior \| manager \| field_engineer \| system_admin` (đề xuất) | sửa — sau **D-R6** |
| `docs/api-contract-v1.1.md` | L287–292 §4.1 | `POST /auth/register` → `"role": "field_crew"` | Không self-registration | sửa — sau **D-R11** |
| `docs/api-contract-v1.1.md` | L346, L365 §4.5/§4.7 | ví dụ `maintenance_engineer`, `"full_name": "Kỹ sư bảo trì"` | Giá trị mới | sửa |
| `docs/api-contract-v1.1.md` | L438 §5.3 | "Ghi = **Quản trị**" | Manager | sửa — sau **D-R12** |
| `docs/api-contract-v1.1.md` | L481 §5.4 | "Tổ khảo sát báo tại chỗ (FM-19)" | Field Engineer | sửa |
| `docs/api-contract-v1.1.md` | L510–518 §5.6 | Frame/thumbnail là ảnh JPEG | Video | **D-R14** |
| `docs/api-contract-v1.1.md` | L536–537 §5.7 | `lux_value` "trên 200 lux chỉ log cảnh báo"; "`data_source` — Nhánh C hầu hết là `calibration_rig`" | Thang tương đối; field trial | **D-R15**, **D-R10** |
| `docs/api-contract-v1.1.md` | L578 §7 | "lưu ảnh dài hạn" chưa chốt | Qua thời hạn bảo hành | sửa — sau **D-R16** |
| `docs/api-contract-v1.1.md` | L594 §9 O-2 | Ma trận ghi sweep/fault/WO còn mở | Phiếu cho đủ dữ kiện để đóng | sửa — sau **D-R5** |
| `docs/openapi/luxmap-v1.json` | `components.securitySchemes.Bearer` | Không mô tả role claim, không 403 `ROLE_FORBIDDEN` | Mô tả role + 401/403 | sửa (file SINH — sửa nguồn code rồi xuất lại) |
| `docs/openapi/tools/gen_consolidated_spec.py` | L54 | tag Assets: "Ghi = Quản trị." | Manager | sửa — sau **D-R12** |
| `docs/openapi/tools/gen_consolidated_spec.py` | L574 | "Tổ khảo sát báo sự cố tại hiện trường" | Field Engineer | sửa |
| `docs/openapi/luxmap-v1.5.json` | toàn file | `info.version = 1.5` trong khi Contract v1.6; nội dung sinh từ hai nguồn trên | Sinh lại, **không sửa tay** (BE-REVIEW-02 ràng buộc 8) | sửa (sinh lại) |
| `docs/authorization-guide.md` | L4 | "Đặc tả gốc: … mục 7" | Contract đã đánh số lại → **§2** | sửa |
| `docs/authorization-guide.md` | L93–99 | ví dụ `MaintenanceEngineer`; "Bốn policy: `ManagementAgency`, `MaintenanceEngineer`, `FieldCrew`, `Administrator`" | Policy mới | sửa — sau **D-R6/D-R5** |
| `docs/authorization-guide.md` | L130–136 | "`*` mà vai trò không phải Quản trị" | `system_admin` | sửa |
| `docs/authorization-guide.md` | L155–186 | "Tài khoản mới đăng ký: quản trị phải làm gì — `POST /api/v1/auth/register` mở cho mọi người … `field_crew`"; `UPDATE app_user SET role = 'maintenance_engineer'` | Admin tạo account; không self-registration | sửa — sau **D-R11** |
| `docs/code-walkthrough.md` | L287 | "`LuxMapPolicies.MaintenanceEngineer` v.v." | Policy mới | sửa |
| `docs/backend-report.md` | L16, L42, L197, L303, L323–324, L345, L478–492, L562–564, L587 | Báo cáo **chốt ngày 30/08/2026** (BE-00→BE-08): 4 vai trò cũ, "không có vai trò Người dân", "Không có endpoint đăng ký", "Nhánh C" | — | **D-R18** (tài liệu lịch sử — khuyến nghị gắn banner, không viết lại) |
| `docs/tasks-backend.csv` | BE-07 (dòng 10) | "API đăng ký / đăng nhập / refresh token" (cột Công việc) | Không self-registration | **D-R11** (cột Công việc, không phải notes — xem 🔴 dưới) |
| `docs/tasks-backend.csv` | BE-08 notes (dòng 11) | "4 vai trò: Cơ quan quản lý, Kỹ sư bảo trì, Tổ khảo sát/sửa chữa, Quản trị. KHÔNG còn vai trò Người dân" | 4 role mới + Citizen QR | sửa (notes) — sau D-R1/D-R6 |
| `docs/tasks-backend.csv` | BE-41 (dòng 44) cột Kết quả | "Tổ khảo sát báo sự cố phát sinh tại chỗ" | Field Engineer | sửa? — cột Kết quả, không phải notes (xem 🔴) |
| `docs/tasks-backend.csv` | Tuần/Bắt đầu/Kết thúc, W0–W21 | 07/09/2026 → 31/01/2027 | 09/2026 → 03/2027 | **D-R9** |
| `tracking.html` | L44 | "W1–W21: 07/09/2026 – 31/01/2027" | Timeline phiếu | **D-R9** |
| `tracking.html` | L289 | "external_ref … ĐÃ ĐÓNG … Dưới Nhánh C không có thử nghiệm hiện trường" | Field trial | sửa — sau **D-R10** |
| `tracking.html` | L292 | "Vai trò được GHI … ghi `/assets/*` = Quản trị … Còn nợ ma trận ghi (O-2)" | Ma trận mới | sửa — C7 |
| `tracking.html` | L337, L344 | "Bốn giá trị vai trò KHÔNG có trong Contract…"; "Tài khoản vừa đăng ký không thấy gì…" | Lỗi thời (đã vào Contract v1.4) + self-registration | sửa — C7 |
| `tracking.html` | L73, L110, L238, L244 | Mục đã xong (solar, `/auth/me`, commit BE-18, BE-12a "gate writes on the administrator role") | — | giữ (lịch sử commit) |
| `tracking.html` | (mới) | — | Task cho mọi capability THIẾU ở mục 1.3 | thêm — C7 |
| `docs/templates/README.md` | L13 | "Chỉ **Quản trị** được nạp." | Manager | sửa — sau **D-R12** |
| `docs/templates/README.md` | L212, L349 | "nhánh C không khảo sát tuyến cáp"; "`public_imagery` — đúng bản chất … của nhánh C" | Nhãn Nhánh C | sửa — sau **D-R10** |
| `docs/templates/README.md` | L337–338 | "Cột `solar_all_in_one` thì **đúng là không có feeder** … Đừng gán feeder cho cột solar" | **Sót từ trước v1.6**, văn hiện tại, không đánh dấu lịch sử | xóa |
| `docs/review/BE-12b-read-shape.md`, `BE-13-topology-shape.md`, `BE-REVIEW-02.md` | nhiều chỗ | Nhánh C, solar, "Quản trị", `MaintenanceEngineer`, `/auth/register` | — | **D-R18** (bản đề xuất đã nộp, là lịch sử) |
| `docs/contract-drift.md` | L45, L106–125, L225 | D-14 "ghi `/assets/*` = Quản trị"; "provenance của Nhánh C"; "45 cột solar" | — | giữ — log là lịch sử; thêm mục mới (C5) |
| `docs/contract-drift.md` | (mới) | — | Quyết định D-R7 (Mỹ, 25/09/2026) + mọi D-item chốt ở phiếu này | thêm — C5 |
| `.ai/README.md` | L8 | "Contract, bản hợp nhất **v1.4**" | (không do phiếu) v1.6 | sửa |
| `.ai/context/sources.md` | L31–32 | "Contract hợp nhất là **v1.5**" | v1.6 | sửa |
| `.ai/context/tracking.html` | toàn file | Bản sao **untracked**, giống hệt `tracking.html` | — | **D-R18** (khuyến nghị: không sửa bản sao; xoá hay giữ là việc của Mỹ) |
| `mocks/README.md` | L26, L69–73 | Đã đánh dấu "Trước 22/09/2026 đây là cột solar" | — | giữ (đã là ghi chú lịch sử) |
| `src/…/UserRole.cs`, `AppUser.cs` (XML doc) | L1–13 | "There is no Citizen role — both the Contract and CLAUDE.md state this explicitly." | — | sửa ở C2 (code, không phải docs) — sau D-R1 |

🔴 **Mâu thuẫn trong chính đề bài, cần Mỹ nói rõ:** đề bài cho sửa *"phần notes của
`docs/tasks-backend.csv`"* và cấm *"sửa cột status/completion"*. Hai chỗ lệch ở CSV (BE-07 "API đăng ký",
BE-41 "Tổ khảo sát") nằm ở cột **Công việc / Kết quả cần đạt** — không phải notes, cũng không phải
status. Tôi coi là **chưa được phép** cho tới khi Mỹ nói. Gộp vào D-R11.

---

## 1.5 D-items

> Không item nào dưới đây đã được chọn. "Khuyến nghị" là ý kiến, không phải quyết định.
> ⚠️ Nhắc FW-00 mục 3: mọi item chạm bề mặt API (enum `user_role`, policy, `/auth/register`) mà im lặng
> quá 3 ngày làm việc thì **ESCALATE**, không phải approve; nếu ký `SELF-SIGNED` thì nền là **tạm** cho
> tới FW kế tiếp.

### D-R1 — Citizen

**Evidence:** phiếu 3.2.c *"The Citizen can directly report lighting issues via the QR code on each light
pole"*. Trong repo, ba lớp đang **khẳng định ngược lại**: `CLAUDE.md:8,1219,1313`;
`UserRoleTests.There_are_exactly_four_roles_and_none_of_them_is_a_citizen` (đếm đúng 4, cấm chuỗi
`citizen|public|resident|nguoidan`); `AnonymousEndpointTests` (whitelist đúng 7 endpoint ẩn danh, trích
quyết định *"bỏ actor guest — team's decision of 20/09/2026"* — **không tìm thấy quyết định này ghi ở
docs nào**, chỉ trong comment test). Thêm: `source_channel` khoá cứng `cv | iot | field_report` — báo
cáo của dân **không có giá trị kênh** nếu đi thẳng vào `fault`.

| Phương án | Hệ quả |
|---|---|
| (a) Không account; `POST /public/pole-reports` với pole public code + token ký trong QR, rate-limit | Không đụng `user_role`, `UserRoleTests` giữ nguyên ý. Khi làm feature: sửa whitelist `AnonymousEndpointTests` (7→8), cần chỗ chứa riêng (không phải `fault`) để Manager duyệt rồi mới thành fault — giữ nguyên tắc "kỹ sư duyệt, không tạo". Không có rate limiter nào trong repo (BE-REVIEW-02 M-5 còn nợ) |
| (b) Role `citizen` | Mâu thuẫn "Citizen không phải account" của đề bài; phải có đăng ký/đăng nhập cho dân → ngược D-R11 |

**Khuyến nghị: (a).** Trong task này chỉ sửa docs + đảo test `UserRoleTests` về "4 role, không citizen"
(vẫn đúng), **không** thêm endpoint. Quyết định 20/09 "bỏ actor guest" cần được ghi lại là **bị phiếu
v1.2 thay thế một phần**.

### D-R2 — Cách lưu role

**Hiện tại:** `text` + CHECK `ck_app_user_role`, sinh từ enum C# qua `HasContractEnum` — **cùng cơ chế
với mọi enum khác của repo**. Không PG enum, không bảng lookup (bằng chứng 1.1.a).

| Phương án | Chi phí migrate |
|---|---|
| (a) Giữ `text` + CHECK | Migration ~3 lệnh: `DROP CONSTRAINT ck_app_user_role` → 4 `UPDATE … WHERE role = …` → `ADD CONSTRAINT` với giá trị mới. `Down()` đối xứng, **không mất dữ liệu** vì mapping song ánh. Đổi enum C# → snapshot EF tự sinh CHECK mới. Khuôn có sẵn: `DropSolarFixtures`. |
| (b) Chuyển sang PG enum | Tạo type, `ALTER COLUMN … USING`, `MapEnum` trong Npgsql data source, **tách role khỏi `HasContractEnum`** — ngoại lệ duy nhất trong repo. PG không xoá được giá trị enum → mỗi lần đổi role sau này phải dựng lại type. |
| (c) Bảng lookup `role` + FK | Bảng mới + seed 4 dòng + FK + entity; chỉ đáng nếu D-R4 = (b). Vẫn phải giữ enum C# cho policy. |

**Khuyến nghị: (a).**

Hệ quả vận hành chung cho mọi phương án: access token đã phát (sống 60 phút) còn mang giá trị cũ → sẽ bị
policy mới từ chối tới khi hết hạn. Refresh **đọc role từ DB** (`AuthService.cs:231` `.Include(token =>
token.User)` → `BuildTokensAsync`), nên lần refresh kế tiếp tự sửa. Chấp nhận được với môi trường dev.

### D-R3 — Phạm vi đọc của Superior

**Evidence:** không có cấp huyện trong DB (1.1.h). Nhiều xã đã chạy qua `app_user_commune`.
`CommuneScopeConsistencyHandler` cấm `*` cho role ≠ admin.

| Phương án | Hệ quả |
|---|---|
| (a) Tập xã qua `app_user_commune` (một hoặc nhiều) | **0 thay đổi schema, 0 thay đổi guard/filter.** "Huyện" = danh sách xã được gán. Thêm xã mới cho Superior là một `INSERT` |
| (b) Thực thể huyện (`administrative_unit.parent_id` hoặc bảng `district`) | Migration schema + đổi `CommuneScopeAccessor` để nở huyện thành xã + đổi claim. Đụng bảng neo phạm vi mà `CLAUDE.md` mục 1b cấm "sửa cho nhất quán" |
| (c) Superior mang `*` | Phải nới `CommuneScopeConsistencyHandler` (đang là lớp canh bug phía phát token) |

Guard ghi không liên quan: Superior chỉ-đọc được thực thi bằng policy trên endpoint ghi (D-R5), không
bằng `CommuneWriteGuard`. **Khuyến nghị: (a).** Manager và Field Engineer giữ "đúng các xã trong claim".

### D-R4 — "Manage roles & permissions" của Admin

| Phương án | Hệ quả |
|---|---|
| (a) Role cố định + ma trận permission trong code; Admin chỉ gán role (và xã) cho user | Khớp EXACT-ROLE, khớp `text`+CHECK (D-R2 a), test tĩnh được (sabotage test ở C4 khả thi). API quản trị user là BE-33 |
| (b) Bảng permission động | Policy phải đọc DB mỗi request hoặc cache; không test tĩnh được ma trận; kéo theo D-R2 (c) |

**Khuyến nghị: (a).**

### D-R5 — Endpoint nhiều role vs Drift 31

**Evidence:** 1.1.g — hôm nay "nhiều role" = "không gắn policy" = cả 4. Phiếu cần: Superior chỉ-đọc
(`POST /lux-readings` đang trần); System Admin không có quyền nghiệp vụ; các thao tác chia đôi Manager/FE.

| Phương án | Hệ quả |
|---|---|
| (a) Giữ luật: ĐỌC không gắn policy (mọi role), GHI một role chính xác; thao tác cần 2 role thì **tách endpoint** (vd. Manager `POST /work-orders`, FE `PATCH /work-orders/{id}/status`) | Đúng chữ Drift 31. Nhưng không loại được ai khỏi một GET (vd. System Admin vẫn đọc mọi dữ liệu nghiệp vụ — xem D-R12) |
| (b) Policy theo **capability**, mỗi policy khai **danh sách role tường minh** (`RequireClaim(role, "manager", "field_engineer")`), không thứ bậc | Diễn đạt đúng phiếu; vẫn "chính xác" (không có "từ bậc X trở lên") nhưng **trái chữ "MỘT vai trò"** của Contract §2 → phải lên Contract + drift. Ma trận nằm ở một file, test sabotage 2 chiều viết được |
| (c) (a) + ngoại lệ có tên cho vài endpoint | Rẻ nhất trước mắt, nhưng đúng loại "mỗi ticket tự chọn" Drift 31 sinh ra để tránh |

Riêng `POST /lux-readings`: dù chọn gì cũng **phải gắn policy** (tối thiểu loại Superior). Phương pháp
phiếu 3.3.c cho engineer đo → `field_engineer`; Manager có đo không là câu hỏi con.
**Khuyến nghị: (b)**, vì nó giữ tinh thần Drift 31 (không thứ bậc, không ngầm định) và là cách duy nhất
loại được role khỏi một GET khi cần. Đổi luật này là đổi Contract §2 → D-R17.

### D-R6 — Mapping user/seed cũ

Ba câu con:

1. **Mapping giá trị** — đề xuất ở 1.2: `management_agency→superior`, `maintenance_engineer→manager`,
   `field_crew→field_engineer`, `administrator→system_admin`. Phương án khác đáng cân nhắc:
   `management_agency→manager` ("cơ quan quản lý" quản tài sản) và `maintenance_engineer→field_engineer`
   — nhưng khi đó `field_crew` và `maintenance_engineer` **cùng dồn vào một giá trị** → `Down()` mất
   thông tin, và Superior không có tài khoản cũ nào tương ứng. **Khuyến nghị: mapping 1.2.**
2. **14 `administrator` cặn test** (`be12a-*`, `be12b-*`, không system-wide): (a) map máy móc →
   `system_admin` (nhất quán, nhưng là "system_admin có phạm vi xã" — trạng thái mô hình mới không có
   nghĩa); (b) map → `manager` (đúng nghĩa: chúng tồn tại để ghi tài sản trong một xã); (c) xoá kèm
   xã test (cần `DELETE … WHERE`, phải xin phép theo §8, và `fault`/`lux_reading` `Restrict` có thể
   chặn). **Khuyến nghị: (a) trong migration** (migration không nên biết username test), rồi dọn riêng
   nếu Mỹ muốn. Gốc là BE-36.
3. **Username + biến môi trường seed**: giữ `admin/agency/engineer/crew` + `SEED_*_PASSWORD` (0 test
   phải sửa call site) hay đổi `superior/manager/field_engineer/sysadmin` (~60 call site test +
   `.env` của mọi thành viên + `README.md`). ID `USR-001..004` **phải giữ** (mock trỏ `USR-004`).
   **Khuyến nghị: đổi `role` và `full_name` tại chỗ, giữ username/ID/biến môi trường**; tên hiển thị
   tiếng Việt cho 4 role cũng cần chốt ở đây (đề xuất: Cấp giám sát / Quản lý / Kỹ sư hiện trường /
   Quản trị hệ thống).

### D-R7 — Remote control — **ĐÃ CHỐT (Mỹ, 25/09/2026)**, chỉ áp dụng

Kết quả khảo sát: không có docs/code nào ngầm hiểu điều khiển lưới thật (1.4.a). Hai lưu ý khi áp:
- **Chưa có bảng thiết bị nào** (không có `iot_node`, không có bảng device) → `supports_remote_control`
  chưa có chỗ để nằm. Phase 2 chỉ khai policy `manager`; cột thuộc ticket feature.
- Phiếu 3.2.c dòng *"Control streetlights (ON, OFF, AUTO)"* thiếu chữ "supported" — docs nên viết theo
  dòng *"supported lighting devices (testbed demo)"*.

### D-R8 — Member 1–5 → tên

**Evidence:** phiếu 3.2.g không ghi tên; bảng Team có 5 người, Leader là **Nguyen Huu My**. Repo:
`CLAUDE.md:4` "BE1 – Mỹ"; `contract-drift.md` nhắc "Thịnh/Ngọc" là WP5 và CODEOWNERS Contract.
Member 1 (PM/Backend, auth, user & role, fault, work order) **khớp mô tả** với BE1 – Mỹ, nhưng khớp mô
tả không phải là ánh xạ. Member 3 (Web/GIS) có thể là Thịnh hoặc Ngọc — repo không cho biết.
**Không tự gán.** Phase 2 dùng `Member 1…5` cho tới khi Mỹ đưa bảng. Cần: bảng Member n → tên, và
xác nhận có muốn ghi tên vào docs không.

### D-R9 — Timeline

**Evidence:** phiếu `Duration: 09/2026 → 03/2027`. Repo: `CLAUDE.md:4`, `tracking.html:44`,
`tasks-backend.csv` (W0–W21, 07/09/2026 → 31/01/2027). **Kế hoạch 13 tuần 07/09 → 03/12/2026 không có
trong repo** — tìm `13 tuần|13 weeks|03/12/2026` không ra dòng nào (chỉ khớp nhầm "W13").

| Phương án | Ghi ở đâu |
|---|---|
| (a) Ghi cả hai, tách nghĩa: "Thời hạn đề tài (phiếu): 09/2026–03/2027" + "Kế hoạch dev nội bộ: 13 tuần, 07/09–03/12/2026" | `CLAUDE.md` L4, `tracking.html` header; CSV giữ nguyên lịch tuần (không sửa được mà không đụng lịch) |
| (b) Chỉ ghi mốc phiếu | Mất thông tin kế hoạch thật |
| (c) Dời toàn bộ lịch CSV sang 13 tuần | Viết lại cột Tuần/Bắt đầu/Kết thúc của 40+ task — ngoài phạm vi task này |

**Khuyến nghị: (a)**, và nếu kế hoạch 13 tuần là thật thì lịch W14–W21 trong CSV đang sai — nêu thành
việc riêng, không sửa ở đây.

### D-R10 (mới) — Nhánh C vs field trial của phiếu 🔴

**Evidence:** `CLAUDE.md:79-81` *"Chốt 24/08/2026 (FO-01). **Không có thử nghiệm hiện trường.**"*. Phiếu:
3.2.e *"validating the platform on real rural roads with a partner commune or district transport
office"*, *"Run a field trial across a pilot network"*; 3.3.c ground truth là *"an engineer inspecting
each sampled fixture at night"*; deliverable *Field Trial and Evaluation Report*; mục 4 cho phép bắt đầu
bằng ảnh công khai + controlled reference set **trong lúc chờ** quyền vào hiện trường.

Đề bài liệt "Branch C / Nhánh C" vào nội dung phải xoá. Nhưng **Nhánh C không chỉ là một cái nhãn** —
nó là lý do đã ghi của ít nhất bốn quyết định:
1. `external_ref` của mock là "danh tính ngoài VĨNH VIỄN", di trú mã "ngoài phạm vi" (`CLAUDE.md:1288`).
2. `administrative_unit` không có geometry (`CLAUDE.md:968`).
3. `data_source = field` "giữ cho tương lai, không sinh bản ghi nào" (Contract §1.6 L158).
4. Bộ hiệu chuẩn là "ground truth photometric **duy nhất**" (`CLAUDE.md:86`) — phiếu thêm lux thực địa
   + kiểm tra thủ công làm ground truth.

| Phương án | Hệ quả |
|---|---|
| (a) Phiếu thay FO-01: có field trial; xoá nhãn Nhánh C, **giữ luật tách `data_source`** (vẫn đúng, còn quan trọng hơn), ghi lại 4 quyết định trên là "nền đã đổi, cần xét lại" | Mở lại D-6/external_ref (đường "xoá sạch nạp lại" đóng từ W5 — FO-14 đo lux ở W5) |
| (b) Nhánh C vẫn là kế hoạch thực thi, phiếu là cam kết tối đa; docs ghi "field trial phụ thuộc quyền vào hiện trường, dự phòng = Nhánh C" | Khớp mục 4 của phiếu (rủi ro tiếp cận hiện trường), giữ nguyên 4 quyết định |
| (c) Chỉ xoá chữ "Nhánh C", giữ nguyên nội dung "không có thử nghiệm hiện trường" | Docs vẫn mâu thuẫn phiếu, chỉ khó tìm hơn |

**Khuyến nghị: (b)** — phiếu tự nói rủi ro nằm ở quyền vào hiện trường, nên "field trial là mục tiêu,
Nhánh C là đường dự phòng đã chốt" là câu đúng cho cả hai. Nhưng đây là quyết định phạm vi đề tài,
**không phải của backend** — cần người ký FO-01.

### D-R11 (mới) — Self-registration `POST /auth/register`

**Evidence:** endpoint **đang chạy** (`AuthController.cs:61-91`, commit `1eed598` 31/08/2026), Contract
§4.1, `RegistrationTests`, whitelist `AnonymousEndpointTests`, `authorization-guide.md:155`. Server gán
`LowestRole = UserRole.FieldCrew` (`AuthService.cs:176`) và 0 xã → đăng nhập được, không thấy gì.
Mâu thuẫn nội bộ sẵn có: `backend-report.md:303,564` (30/08) ghi "Không có endpoint đăng ký".
Phiếu: System Admin "Manage accounts"; đề bài: "không self-registration".

| Phương án | Hệ quả |
|---|---|
| (a) Xoá endpoint | **BREAKING** (Contract §4.1, 7→6 endpoint ẩn danh, FM có thể đã dựng màn đăng ký); xoá `RegistrationTests`; cần API tạo account cho Admin (BE-33) — nếu chưa có thì tạm tạo bằng seed/SQL |
| (b) Tắt bằng cấu hình (mặc định tắt, 404/403) | Không xoá code; vẫn đổi hành vi publish → vẫn phải lên Contract |
| (c) Giữ, chỉ đổi role mặc định → `field_engineer` | Trái phiếu |

**Khuyến nghị: (a)** nhưng **không trong task này** — task này chỉ role/policy + docs; xoá endpoint
là thay đổi bề mặt API cần drift + báo WP6. Nếu giữ lại tạm thì Phase 2 phải map `LowestRole` →
`field_engineer`. Kèm câu hỏi CSV ở cuối 1.4.b.

### D-R12 (mới) — Quyền của System Admin với dữ liệu nghiệp vụ, và ai ghi tài sản

**Evidence:** 15 endpoint ghi `/assets/*` gắn `Administrator`; phiếu giao "Manage lighting poles and
lighting assets" cho Manager, System Admin chỉ account/role/config/monitor/log. `*` gắn với role admin ở
`CommuneScopeConsistencyHandler`.

Câu con: (1) `/assets/*` + import chuyển sang `manager`? (2) System Admin còn đọc bản đồ/tài sản/sự cố
(hôm nay đọc được toàn hệ thống qua `*`)? (3) System Admin còn ghi tài sản (vd. nạp dữ liệu đầu kỳ cho
xã chưa có Manager)?

**Khuyến nghị:** (1) có — đúng phiếu, và WP5 phải biết (màn quản lý tài sản đổi người dùng); (2) giữ
đọc qua `*` cho vận hành/giám sát, trừ khi D-R5 = (b) và Mỹ muốn loại hẳn; (3) không — dùng
`EnterUnscopedSystemWriteBackdoor`/seeder cho nạp đầu kỳ như BE-39. Hệ quả test: toàn bộ test ghi tài
sản (`AssetImportFixture`, `PoleWriteTests`, `AssetReplaceAndDeleteTests`, `AssetPermissionTests`…) đang
đăng nhập bằng `admin` → phải đổi sang tài khoản Manager có xã.

### D-R13 (mới) — Audit trail mọi quyết định của engineer

**Evidence:** phiếu NFR *"an audit trail of every automated finding and engineer decision"*; deliverable
backend *"… with role-based access and an audit trail"*. `CLAUDE.md:946-955`: BE-18 **chỉ giữ quyết định
mới nhất**, `FaultHistory` là việc của BE-19 nếu cần. D-R7 cũng đòi audit mọi lệnh điều khiển.
Phương án: (a) chốt `FaultHistory` (hoặc bảng audit chung) là **bắt buộc** ở BE-19; (b) giữ nguyên.
**Khuyến nghị: (a)** — phiếu biến "nếu cần" thành "cần". Chỉ ghi quyết định + task tracking ở đây,
**không** tạo bảng trong task này.

### D-R14 (mới) — Video vs ảnh JPEG

**Evidence:** phiếu: FE *"Capture continuous night video"*, *"Record and upload videos of lighting
equipment"*; deliverable Mobile *"before-and-after repair evidence"*. Repo: BE-11 quy tắc 5 **chỉ JPEG,
magic bytes `FF D8 FF`, ImageSharp chỉ đăng ký JPEG**; Contract §5.6 frame/thumbnail JPEG; §5.5 evidence
multipart ảnh. Câu hỏi: video là nguồn upload (server tách frame) hay FE tách frame trên máy rồi upload
JPEG như Contract? Evidence sửa chữa: video, ảnh, hay cả hai?
**Khuyến nghị:** hỏi WP4/WP6 trước; đây là thay đổi lớn nhất về lưu trữ (dung lượng, BE-11 quy tắc 4/5/6,
MinIO). Ngoài phạm vi task này — chỉ ghi drift + task.

### D-R15 (mới) — Lux: tuyệt đối hay tương đối

**Evidence:** `CLAUDE.md:557` *"`lux_value` — tuyệt đối, đơn vị lux"*; Contract §5.7 ngưỡng cảnh báo 200
lux. Phiếu 3.3.c + mục 4: smartphone, *"no absolute illuminance value is claimed; readings are treated as
a relative scale"*. Không cần đổi schema (`meter_model` đã có, khớp "cùng thiết bị"). Chỉ đổi cách diễn
đạt ở docs + Contract (qua drift). **Khuyến nghị:** sửa diễn đạt; giữ tên cột.

### D-R16 (mới) — Chính sách lưu trữ

**Evidence:** Contract §7 L578 và `CLAUDE.md:253` để mở "lưu ảnh dài hạn"; phiếu NFR: *"Imagery,
telemetry and fault records retained through the warranty period"*. **Khuyến nghị:** đóng mục mở bằng
câu của phiếu (qua drift), thêm task hiện thực (thuộc BE-35).

### D-R17 (mới) — Version Contract và quy trình ký

Đổi giá trị `user_role` là **BREAKING** (FE/mobile hardcode giá trị claim `role`). Cần: số version
(đề xuất **v1.7**), tên file spec (Contract đã chốt giữ `luxmap-v1.5.json`), người ký. Theo FW-00: chạm
API → im lặng là ESCALATE; nếu `SELF-SIGNED` thì ticket sau xây trên nền tạm. Cần Thịnh/Ngọc (CODEOWNERS
của `api-contract-v1.1.md`, `luxmap-v1.json`) review PR.

### D-R18 (mới) — Tài liệu lịch sử có nội dung solar / Nhánh C / role cũ

`docs/backend-report.md` (chốt 30/08/2026), `docs/review/*.md` (bản đề xuất đã nộp), `docs/archive/`,
log `contract-drift.md`, các mục đã xong trong `tracking.html`, khối giải thích v1.6 trong `CLAUDE.md`
(L150–181, L1151), bản sao untracked `.ai/context/tracking.html`.
Phương án: (a) giữ nguyên, gắn banner "tài liệu lịch sử — vai trò/phạm vi đã đổi theo phiếu v1.2"
ở đầu `backend-report.md`; (b) viết lại cho khớp; (c) xoá.
**Khuyến nghị: (a).** Viết lại lịch sử làm mất lý do của quyết định; `CLAUDE.md` khối v1.6 thì giữ vì nó
giải thích vì sao enum chỉ còn một giá trị (đề bài cấm "đưa lại" solar — giữ lời giải thích việc xoá
không phải đưa lại). Nhưng đề bài ghi "phải xóa" solar → cần Mỹ xác nhận.

---

## 1.6 Chưa làm / không làm trong Phase 1

- **Không chạy `dotnet test`**: nhiều fixture ghi vào DB dev dùng chung (chính là nguồn 14 user cặn) —
  vi phạm "không sửa DB" của Phase 1. Số endpoint ở 1.1.f đếm từ grep, không từ
  `AnonymousEndpointTests` chạy thật.
- `.ai/results/review1/**` (untracked) **không nằm trong phạm vi quét** (đề bài chỉ gồm `.ai/context/**`);
  nó có nhắc `SEED_ENGINEER_PASSWORD`, `admin/engineer` — sẽ lệch nếu D-R6 đổi username.
- `.ai/README.md` quy ước tên `BE-xx-p1.md`; file này theo tên đề bài chỉ định.
- Không đụng `pole_current_status`, không tạo migration, không sửa file nào ngoài file này.

```
$ git status --short
?? ".ai/context/FA26SE222 v1.1.docx"
?? .ai/context/tracking.html
?? .ai/results/registration-v1.2-phase1.md
?? .ai/results/review1/
?? docs/registration/
```

**DỪNG.** Chờ Mỹ chốt D-R1…D-R6, D-R8, D-R9, D-R10…D-R18 và câu hỏi cột CSV (cuối 1.4.b) trước Phase 2.
