---
ticket: BE-23 (lát BE-23a — audit_event)
reviewer: claude
reviewed: feat/BE-23a-audit-event @ 1673d80 (base f07f974)
date: 2026-09-28
verdict: approved            # vòng 2, 28/09/2026 — xem mục "Review lại"
---

## Phạm vi đã review

`git diff f07f974..HEAD`, 17 file: `src/LuxMap.Persistence/Audit/*` (4 file mới), `LuxMapDbContext.cs`,
`PersistenceServiceCollectionExtensions.cs`, `IdentityConfigurations.cs`, migration
`20260927161055_AddAuditEvent` (+ Designer, snapshot), `AuditGuardTests.cs`, `AuditTrailTests.cs`,
`BannedBulkWriteApiTests.cs`, `CLAUDE.md`, `docs/contract-drift.md`, `tracking.html`.

Đối chiếu với: `.ai/tasks/BE-23.md`; phần CHỐT + CHỐT BỔ SUNG của `.ai/results/BE-23-p1.md`;
`.ai/results/BE-23-claude-decisions.md` §D1, §3.1, §7; `.ai/results/BE-23-p2.md`; Contract §2, §3.3;
`CLAUDE.md` mục 1c, "ĐỌC migration", "PARTIAL INDEX", "`ExecuteUpdate` bị CẤM", BE-14 bẫy 3–4, và
mục mới "BE-23a".

**Đã chạy:** đọc diff; `\d audit_event` + `\du luxmap` trên `luxmap_test`; build; toàn bộ
`dotnet test` trên `luxmap_test`; ba sabotage (hai của tôi, một lặp lại của Codex). Mọi sabotage đã
khôi phục, `git status --short src` rỗng sau mỗi lượt. Không sửa `src/`.

⚠️ **Nhánh này chỉ là BE-23a** (theo D9, chốt bổ sung của Mỹ 27/09). Trong diff **chưa có** work
order, endpoint, capability, listing hay mock. Điểm 1, 3, 4, 7, 8 của đề review vì vậy **không có gì
để soi**; ghi rõ từng điểm ở bảng cuối, để lượt review BE-23 không tưởng chúng đã qua.

## Phát hiện

| # | Mức | File:dòng | Vấn đề | Đề xuất |
|---|---|---|---|---|
| 1 | major | `tests/LuxMap.Persistence.Tests/AuditGuardTests.cs:80-85` | Test canh công tắc purge **có lỗ**, đúng họ lỗi với `BannedDistanceApiTests`/`BannedBulkWriteApiTests`. Nó chỉ quét `*.cs` và bỏ qua **cả thư mục** `Migrations/`. Hai đường bật `luxmap.audit_purge` cho **mọi** session đều lọt: một migration sau này viết `ALTER DATABASE … SET luxmap.audit_purge = 'on'`, và một connection string có `Options=-c luxmap.audit_purge=on` trong `appsettings*.json`. Sabotage bên dưới: cài cả hai, test vẫn **xanh**; psql cho thấy `SET` cấp session làm trigger cho `DELETE` đi qua. Vi phạm hai câu `CLAUDE.md` (mục "BE-23a"): *"`AuditGuardTests.Production_source_never_enables_the_test_purge_switch` quét `src/` … để chặn đường bật GUC trong ứng dụng"* và *"Không đặt ở mức session/connection pool"*. | Quét mọi file văn bản dưới `src/` (`*.cs`, `*.json`, `*.sql`, …, trừ `bin`/`obj`). Miễn trừ **đúng một file theo tên**, `20260927161055_AddAuditEvent.cs`, không miễn cả thư mục. Nên quét thêm `docker-compose.yml`, `.env.example` và `.github/workflows/*`, vì connection string cũng sống ở đó. |
| 2 | major | `tests/LuxMap.Api.Tests/AssetSchemaFixture.cs:93-96` | Teardown xoá `administrative_unit` của fixture nhưng **không dọn `audit_event` trước**. Mà `fk_audit_event_administrative_unit_commune_id` là `ON DELETE RESTRICT` (xem `\d audit_event`). Hôm nay may mắn qua được, vì `AuditTrailTests` tự xoá theo `correlation_id` của nó. Ở BE-23, test work order ghi audit qua HTTP bằng correlation id do middleware sinh, nên test không biết trước giá trị. Một dòng audit sót lại làm dòng 96 gãy, **cả teardown gãy theo**, và lượt trả `pole_id_seq` ở dòng 98+ không chạy. Vi phạm `CLAUDE.md` BE-14 bẫy 3 (*"Teardown của fixture PHẢI xoá mọi bảng `Restrict` trỏ vào…"*) và bẫy 4 (*"Lùi sequence dùng chung thì PHẢI trả nó về chỗ cũ"*). Chính mục BE-23a mới cũng tự ghi *"muốn dọn tài khoản/xã test phải dọn audit của test trước"*, nhưng fixture chưa làm. **Chỉ đọc code, chưa tái hiện** (xem "Đã kiểm chứng"). | Trong `DisposeAsync`, trước dòng 93, mở transaction riêng: `SET LOCAL luxmap.audit_purge = 'on'` rồi `DELETE FROM audit_event WHERE commune_id = @commune`, đúng khuôn đã duyệt ở D1 mục 8. `AssetImportFixture`/`ScopeTestFixture`/`AuthTestFactory` cũng xoá commune/user: áp cùng lúc, hoặc ghi nợ có tên cho BE-23. |
| 3 | minor | `tests/LuxMap.Api.Tests/AuditTrailTests.cs:41-72`, `src/LuxMap.Persistence/Audit/AuditTrail.cs:35-48` | Phần ánh xạ `AuditChange → AuditEvent` gần như **chưa được ghim**. Sabotage: `Action` hằng `Created`, `EntityId` hằng `"WO-0001"`, `BeforeState = null`, `Note = null`. Kết quả 15/15 `AuditTrailTests` và 15/15 test Persistence liên quan vẫn **xanh**. Cũng không test nào lưu thành công một actor `user` (có `actor_user_id` + `actor_role`); ca `user` duy nhất là ca bị CHECK từ chối. Neo: Contract §3.3 (*"mọi quyết định của người … ghi vào một bảng audit"*). `CLAUDE.md` BE-23a nói guard *"không chứng minh nội dung event khớp thao tác"*, nên nội dung chỉ còn test ghim được, mà `AuditTrail` là chỗ duy nhất ánh xạ nội dung. | Thêm một test round-trip: actor `user` thật (tài khoản throwaway, theo "Hai bẫy test REG-v1.2"), `Action`/`EntityId` **khác** giá trị mặc định của helper, `BeforeState` + `Note` khác null. Assert từng cột bằng literal. |

## Đã kiểm chứng

**Schema thật (luxmap_test):** migration khớp code, không có `DropIndex`, `ix_audit_event_commune_id`
đơn cột có mặt, FK `RESTRICT` cả hai, CHECK đủ cho 4 enum nội bộ, hai trigger.

```
$ docker compose exec -T postgres psql -U luxmap -d luxmap_test -c "\d audit_event" -c "\du luxmap"
Indexes:
    "pk_audit_event" PRIMARY KEY, btree (audit_id)
    "ix_audit_event_actor_user_id" btree (actor_user_id)
    "ix_audit_event_commune_id" btree (commune_id)
    "ix_audit_event_entity" btree (entity_type, entity_id, audit_id)
Check constraints:
    "ck_audit_event_action" CHECK (action = ANY (ARRAY['created'::text, 'assigned'::text, ... 'details_changed'::text]))
    "ck_audit_event_actor" CHECK ((actor_kind = 'user'::text) = (actor_user_id IS NOT NULL) AND (actor_user_id IS NULL) = (actor_role IS NULL))
    "ck_audit_event_actor_kind" CHECK (actor_kind = ANY (ARRAY['user'::text, 'cv'::text, 'iot'::text]))
    "ck_audit_event_actor_role" CHECK (actor_role IS NULL OR (actor_role = ANY (ARRAY['superior'::text, 'manager'::text, 'field_engineer'::text, 'system_admin'::text])))
    "ck_audit_event_after_state_object" CHECK (after_state IS NULL OR jsonb_typeof(after_state) = 'object'::text)
    "ck_audit_event_before_state_object" CHECK (before_state IS NULL OR jsonb_typeof(before_state) = 'object'::text)
    "ck_audit_event_entity_type" CHECK (entity_type = 'work_order'::text)
    "ck_audit_event_has_state" CHECK (before_state IS NOT NULL OR after_state IS NOT NULL)
Foreign-key constraints:
    "fk_audit_event_administrative_unit_commune_id" FOREIGN KEY (commune_id) REFERENCES administrative_unit(commune_id) ON DELETE RESTRICT
    "fk_audit_event_app_user_actor_user_id" FOREIGN KEY (actor_user_id) REFERENCES app_user(user_id) ON DELETE RESTRICT
Triggers:
    audit_event_append_only_rows BEFORE DELETE OR UPDATE ON audit_event FOR EACH ROW EXECUTE FUNCTION luxmap_audit_event_append_only()
    audit_event_append_only_truncate BEFORE TRUNCATE ON audit_event FOR EACH STATEMENT EXECUTE FUNCTION luxmap_audit_event_append_only()
 luxmap    | Superuser, Create role, Create DB, Replication, Bypass RLS
```

**Build + toàn bộ test** (`ConnectionStrings__LuxMap` trỏ `luxmap_test`):

```
$ dotnet build
    0 Warning(s)
    0 Error(s)
$ dotnet test --no-build
Passed!  - Failed:     0, Passed:   149, Skipped:     0, Total:   149 - LuxMap.Shared.Tests.dll
Passed!  - Failed:     0, Passed:    35, Skipped:     0, Total:    35 - LuxMap.Persistence.Tests.dll
Passed!  - Failed:     0, Passed:    18, Skipped:     0, Total:    18 - LuxMap.Infrastructure.Storage.Tests.dll
Passed!  - Failed:     0, Passed:   434, Skipped:     0, Total:   434 - LuxMap.Api.Tests.dll
```

636 test, khớp con số của `tracking.html`.

**Sabotage R1 (của tôi), cho phát hiện 1.** Tôi thêm `src/LuxMap.Persistence/Migrations/ZzReviewSabotage.cs`,
chứa hằng `"ALTER DATABASE luxmap_dev SET luxmap.audit_purge = 'on'"`, và
`src/LuxMap.Api/appsettings.ReviewSabotage.json`, chứa `Options=-c luxmap.audit_purge=on`. Test canh
vẫn xanh:

```
$ dotnet test tests/LuxMap.Persistence.Tests --filter "FullyQualifiedName~Production_source_never_enables_the_test_purge_switch"
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1
```

Công tắc cấp session vô hiệu trigger thật (psql, trong transaction đã rollback, không để lại gì):

```
INSERT 0 1
SAVEPOINT
ERROR:  audit_event is append-only                      ← không có GUC: bị chặn
ROLLBACK
SET                                                     ← SET cấp session, như Options=-c
DELETE 1                                                ← trigger cho qua
ROLLBACK
 left_behind
-------------
           0
```

**Sabotage R2 (của tôi), cho phát hiện 3.** Tôi sửa `AuditTrail.cs`: `Action = AuditAction.Created`,
`EntityId = "WO-0001"`, `BeforeState = null`, `Note = null`.

```
$ dotnet test tests/LuxMap.Api.Tests --filter "FullyQualifiedName~AuditTrailTests"
Passed!  - Failed:     0, Passed:    15, Skipped:     0, Total:    15
$ dotnet test tests/LuxMap.Persistence.Tests --filter "FullyQualifiedName~Audit"
Passed!  - Failed:     0, Passed:    15, Skipped:     0, Total:    15
```

**Sabotage C1 (lặp lại của Codex): đỏ đúng chỗ.** Tôi nới guard `!= 1` thành `< 1`, tức cho phép
nhiều event:

```
  Failed ...An_audited_write_requires_exactly_one_event(state: Deleted, events: 2)
  Failed ...An_audited_write_requires_exactly_one_event(state: Modified, events: 2)
  Failed ...An_audited_write_requires_exactly_one_event(state: Added, events: 2)
Failed!  - Failed:     3, Passed:    12, Skipped:     0, Total:    15
```

Đỏ đúng ba ca `events: 2` và không ca nào khác, nên test phân biệt được "đúng một" với "ít nhất một".

**Phát hiện 2 chưa tái hiện, có chủ đích.** Muốn tái hiện phải để dòng audit sót trên `luxmap_test`
và làm gãy teardown. Khi đó commune + 2500 cột của fixture sẽ mồ côi, và `pole_id_seq` không được trả
lại (bẫy 4). Tôi không làm điều đó trên DB dùng chung khi đang chỉ-đọc. Bằng chứng là thứ tự lệnh ở
`AssetSchemaFixture.cs:93-96` cộng FK `RESTRICT` ở trên.

## Mười điểm của đề review: kết quả từng điểm

| Điểm | Kết quả |
|---|---|
| 1. Fallback / capability rỗng | **Không áp dụng.** Diff không thêm controller nào. `LuxMapPolicies.cs` không đổi (`git diff --stat f07f974..HEAD -- src/LuxMap.Shared/Authorization` rỗng) |
| 2. Ghi né `CommuneWriteGuard` | Qua EF thì không né được. `AuditEvent : ICommuneScoped`, và `Audit_read_and_write_are_commune_scoped` cho ra `COMMUNE_FORBIDDEN` khi ghi ngoài xã. Guard audit chạy trước guard xã, và backdoor chỉ bỏ yêu cầu "có event", **không** bỏ lệnh cấm sửa/xoá (`AuditWriteGuard.cs:10-14`). Đường né duy nhất là SQL thô `INSERT`, và `src/` hiện không có đường đó (xem Ý kiến) |
| 3. FE-A thấy WO của FE-B | **Không áp dụng**, chưa có WO |
| 4. Điều kiện `task_kind` | **Không áp dụng**, chưa có WO |
| 5. Append-only / transaction / mồ côi / nhạy cảm | Trigger ở tầng DB là thật (SQLSTATE `55000`, `Raw_sql_cannot_mutate_audit` phủ UPDATE/DELETE/TRUNCATE). Audit ghi cùng transaction: `Business_write_and_audit_rollback_together` và `Invalid_audit_rolls_back_business_write_in_the_same_save` cho thấy rollback không để lại dòng mồ côi ở chiều nào. Giới hạn: công tắc GUC và vai trò superuser, đã ghi ở `CLAUDE.md`; lỗ quét thì xem phát hiện 1. Trường nhạy cảm: hiện **chưa có caller nào** trong `src/`, nên chưa có gì lộ (xem Ý kiến) |
| 6. Migration | Đạt. Không có `DropIndex`. `ix_audit_event_commune_id` khai tường minh, và không index ghép nào dẫn đầu bằng `commune_id`. `CREATE FUNCTION` đứng trước `CREATE TRIGGER`. `Down()` drop 2 trigger, rồi hàm, rồi bảng, đối xứng với `Up()`. CHECK có cho 4 enum. FK `Restrict`. Snapshot chỉ thêm phần audit |
| 7. Thứ tự ID | **Không áp dụng.** Audit không có ID hiển thị (`bigint` identity) và chưa có listing |
| 8. Hình dạng response / drift | **Không áp dụng** cho response. Drift "BE-23 — audit trước và loại việc của mock" đã được đăng ký (`docs/contract-drift.md`), và việc báo WP5/WP6 được ghi đúng là *chưa gửi*. Trường WO mới sẽ đăng ký ở BE-23 |
| 9. Test literal / sabotage | CHECK và SQLSTATE assert bằng literal. Sabotage C1 của Codex đỏ đúng chỗ. Hai lỗ ở phát hiện 1 và 3 |
| Toàn bộ test + sabotage riêng | 636/636 xanh; R1 và R2 ở trên |

## Ý kiến, không phải phát hiện

- **`correlation_id` rỗng ngoài HTTP.** `CorrelationIdHolder.CorrelationId` mặc định là `string.Empty`
  (`src/LuxMap.Api/Http/CorrelationIdMiddleware.cs:67`), và cột chỉ có `NOT NULL`, không có
  `<> ''`. Audit do engine sinh (`actor_kind = cv | iot`) sẽ chạy trong job nền (BE-26), tức mọi dòng
  mang `''`. Nên quyết trước BE-19/BE-26: accessor sinh id riêng cho mỗi job, hoặc thêm CHECK.
- **`AuditChange.BeforeState`/`AfterState` là `object?`.** Không có gì chặn caller truyền nguyên
  entity. Truyền `AppUser` là ghi `password_hash` vào bảng không xoá được. Truyền entity EF đang track
  thì có thể vỡ vì chu trình navigation. Đề xuất cho BE-23: chỉ truyền DTO snapshot khai riêng (record
  nhỏ), và có một test chặn kiểu entity.
- **SQL thô `INSERT INTO audit_event`** né cả guard xã lẫn guard "đúng một event". Hiện `src/` không
  có đường đó (chỉ test dùng, và đó là chủ ý). Chỉ đáng ghi vì `authorization-guide.md` đã liệt kê
  `FromSqlRaw` trong bảng "chỗ dễ lách", còn `ExecuteSql*` thì chưa.
- `AuditWriteGuard` ném `InvalidOperationException`, ra 500. Hợp lý vì đó là lỗi lập trình, không phải
  lỗi của người dùng. Nhưng BE-23 nên có test HTTP khẳng định không endpoint nào chạm tới ca đó.
- `AuditActorKind` chưa có giá trị cho seeder/hệ thống. Hôm nay đúng, vì backdoor miễn yêu cầu
  event. Nếu BE-39 muốn ghi vết lúc seed thì phải thêm giá trị, tức thêm một CHECK enum.

## Review lại — vòng 2 (28/09/2026)

Codex đã sửa trong working tree, trên nền `1673d80`. Codex không commit được vì sandbox chặn
`.git/index.lock`; reviewer đã commit hộ, xem git log. Phần giải thích từng fixture nằm ở
`.ai/results/BE-23-p2.md`, mục "Sửa theo review Claude".

| # | Kết quả | Bằng chứng |
|---|---|---|
| 1 | **Đã sửa.** Test giờ quét mọi file dưới `src/` (trừ `bin`/`obj`). Chỉ miễn trừ đúng file `20260927161055_AddAuditEvent.cs`. Quét thêm `docker-compose.yml`, `.env.example` và `.github/workflows/*` | Sabotage R1 chạy lại: `Failed: 1` (vòng 1 là `Passed: 1`) |
| 2 | **Đã sửa.** `AssetSchemaFixture` và `AssetImportFixture` purge audit theo xã (Import còn purge theo actor) trong transaction riêng, trước khi xoá tài sản. Các fixture còn lại được giải thích là hiện không sinh audit | Sau toàn bộ suite: `SELECT count(*) FROM audit_event` = `0` trên `luxmap_test` |
| 3 | **Đã sửa.** Thêm `User_event_round_trips_every_column`: actor là tài khoản throwaway, 13 cột đều assert bằng literal, dọn trong `finally` | Sabotage R2 chạy lại: `Failed User_event_round_trips_every_column` (vòng 1 xanh 15/15) |

```
$ dotnet build            →  0 Warning(s) · 0 Error(s)
$ dotnet test --no-build  →  149 + 35 + 18 + 435 = 637 passed, 0 failed
```

**Minor mới, không chặn merge.** `AuditGuardTests.cs:81` giờ đọc **mọi** file dưới `src/`, kể cả
`src/LuxMap.Api/logs/` (gitignore, sinh lúc chạy). EF log một lệnh SQL **thất bại** ở mức Error kèm
toàn văn lệnh, dù `Database.Command` đang đặt ngưỡng Warning. Nên nếu một lượt teardown purge nổ và
test host ghi vào thư mục đó, lượt chạy sau sẽ đỏ trên máy local, cho tới khi xoá log. CI không bị
vì CI không có log cũ. Đề xuất: loại thêm thư mục `logs`.

**Kết luận: approved.** Chưa push; theo `CLAUDE.md` §5, push chờ Mỹ.
