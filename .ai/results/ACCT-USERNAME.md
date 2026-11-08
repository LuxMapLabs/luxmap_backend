# ACCT-USERNAME — phase 2, Codex (08/11/2026)

- Đã viết `UsernameGenerator.BaseName` thuần, kiểm ký tự, NFD/đ và ghép tên; POST từ chối `username` có mặt bằng `OptionalJson.Present` (dùng `JsonElement` giữ undefined/null).
- Cấp hậu tố max số theo regex chính xác, không phân biệt hoa thường. Chọn advisory-xact lock theo `account-username:<gốc>` vì khoá được cả gốc chưa có hàng; transaction bao đọc max → SaveAsync → commit, giữ xử lý unique hiện có. SMTP sau commit. Không migration/seed/password/PATCH change.
- Test mới: `UsernameGeneratorTests`, `AccountUsernameTests` (suffix gap/case/exact regex, body username string/null/rỗng, tên sai không ghi/mail, 5 request thật sự song song với client dựng trước, mail, PATCH). Sửa AccountAdminTests/AccountTestFactory/CommuneAdminTests: bỏ username khỏi create hợp lệ, đọc response, dọn theo email prefix riêng.
- Cập nhật XML, ACCT-2 SELF-SIGNED, auth trap, development/authorization-guide, tracking. Không sửa Contract, OpenAPI hay template mail (đã ghi rõ tên đăng nhập).

## Đã kiểm

`dotnet build LuxMap.slnx --disable-build-servers -m:1 -p:NuGetAudit=false` exit 0 (restore cache local; không sửa dependency/config). Lượt no-restore đầu thiếu project.assets.json; build đa node bị treo, đã yêu cầu huỷ và chuyển sang `-m:1`.
Build lại sau chỉnh test/docs: `dotnet build LuxMap.slnx --disable-build-servers -m:1 --no-restore` exit 0, output thật:

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:04.45
```

Rà `admin/users`, `CreateUserRequest`, username trong tests/scripts/docs/README/commands: create hợp lệ không còn gửi username; chỉ negative POST test và PATCH bất biến cố ý gửi. Contract/spec/lịch sử giữ nguyên theo ticket. AGENTS.md vẫn là symlink. Không chạy test, Swagger, git; không stage/commit/push.

## Claude cần chạy ngoài sandbox

```bash
dotnet test --disable-build-servers --filter 'FullyQualifiedName~UsernameGeneratorTests|FullyQualifiedName~AccountUsernameTests|FullyQualifiedName~AccountAdminTests|FullyQualifiedName~CommuneAdminTests|FullyQualifiedName~AccountMailTemplateTests'
dotnet test --disable-build-servers
```

**Phá thử: chưa chạy, Claude sẽ chạy** (mỗi lần khôi phục bằng cp/touch, build lại):

1. Bỏ dòng `ExecuteSqlInterpolatedAsync` chứa `pg_advisory_xact_lock` ở UserAdminService → `Five_parallel_creates_of_one_name_receive_mynh1_through_mynh5` phải đỏ. Không bảo đảm đỏ ổn định vì scheduler/DB; để tất định, dùng interceptor test giữ mọi request sau SELECT max trước INSERT tới khi đủ N, chỉ bật khi phá bỏ lock (bật barrier cùng lock sẽ deadlock).
2. Bỏ ánh xạ `character is 'đ' or 'Đ' ? 'd'` ở UsernameGenerator → theory `The_last_word_precedes_all_earlier_initials("Đỗ Văn Đức", "ducdv")` đỏ.
3. Thay MAX hậu tố bằng COUNT hàng khớp → `The_largest_exact_positive_suffix_is_used_case_insensitively("mynh7", "mynh8")` đỏ.
4. Bỏ nhánh `OptionalJson.Present(request.Username)` → `Any_supplied_username_is_rejected_without_creating_an_account_or_mail` đỏ (cả string/null/rỗng).

Swagger/Contract chưa hợp nhất: Claude xử lý sau theo ticket và quy trình docs/development.md; Codex không chạy hoặc sửa docs/openapi/*.

## Còn cần xác minh

- Chưa chạy hành vi API/PostGIS, concurrency, teardown, toàn suite hoặc phá thử; build chỉ chứng minh biên dịch.
- Ví dụ ticket `Trần Thị Bích Ngọc → ngoctb` sai với quy tắc chữ đầu mọi từ trước: dùng `ngocttb`; `Lê Văn-Tám → tamlv`.
