# ASSET-PHOTOS — Phase 2-A

Ngày: 12/11/2026 · Codex · branch `feat/ASSET-PHOTOS-a`.
Mốc khảo sát: `402bc1ed45a09e3aac7a8562d8f7b886be6dfcf1`.

**Dừng trước triển khai để chờ chốt D-13 / D-14**, theo yêu cầu cuối ticket:
“Chỗ mơ hồ hoặc thấy phải lệch D-01…D-06 thì dừng ở đó và ghi D-item, đừng tự chọn.”
Chưa thực hiện các mục 1–8; chưa sửa code, schema, migration, Contract, spec hoặc cấu hình.
Không chạy restore/build/test, phá thử, Docker, API hay kết nối database; không có kết luận test xanh
và không có bằng chứng sandbox chặn Docker/mạng. Không sửa tracking.html theo phạm vi riêng của ticket.

Đã đọc CLAUDE.md, .ai/README.md, .ai/context/sources.md, ticket, báo cáo Phase 1
(D-01…D-06 và bảng chốt), các tài liệu trap được ticket liệt kê và commands.
Các phát hiện dưới đây là xung đột giữa yêu cầu ticket và nền hiện tại, không phải quyết định mới.
Deviation bốn/năm bucket đã được đăng ký ở `docs/contract-drift.md`, ACCT-5;
không đăng ký một deviation trùng lặp trong lượt này.

## D-13 — D-02 / mục 7: “vẫn bốn bucket” và ACCT-5

Ticket mục 7 yêu cầu “§4.10 vẫn bốn bucket”; D-02 chốt dùng Evidence để không đổi
“đủ 4 bucket”. Tuy nhiên ACCT-5 đã chốt 09/11/2026, có bucket Avatar trong code,
provision dev/prod, backup và đăng ký monitoring. Trap media-storage hiện cũng nói năm bucket.
Theo thứ tự nguồn sự thật, quyết định drift đã chốt thắng chữ Contract cũ.

Cần Mỹ chốt cách áp dụng câu “vẫn bốn bucket” khi hoàn tất Phase 2-A:

- Đề xuất: giữ cả năm bucket hiện tại, dùng Evidence cho PAP/CAP, không tạo bucket mới;
  cho phép đồng bộ §4.10 với ACCT-5 (năm bucket), ghi rõ thay đổi đó khi bàn giao.
- Hoặc giữ nguyên §4.10 bốn bucket trong phạm vi ticket, dẫn ACCT-5 làm ngoại lệ hiện hành;
  không khẳng định runtime chỉ có bốn bucket.

Không chọn phương án hoặc sửa Contract trong lượt này; không đề xuất bỏ Avatar.

Bằng chứng terminal thật:

```text
$ rg -n 'bucket|Avatar|avatar' src/LuxMap.Shared/Storage* src/LuxMap.Modules.Identity/IdentityModule.cs docs/api-contract-v1.1.md docs/contract-drift.md deploy/compose.yml docker-compose.yml deploy/backup.sh
[trích output; exit 0]
docs/api-contract-v1.1.md:575:| `storage.buckets[]` | Đủ 4 bucket `survey` / `evidence` / `video` / `citizen`. `object_count` / `bytes` **theo dòng trong DB** (ảnh gốc + thumbnail) |
docs/contract-drift.md:256:- Bucket riêng **`luxmap-avatar`**, hai key `original/{user_id}.jpg`, `thumb/{user_id}.jpg` ghi đè; original đã sanitize, phục vụ thumbnail.
deploy/backup.sh:27:  for bucket in luxmap-survey luxmap-evidence luxmap-video luxmap-citizen luxmap-avatar; do
src/LuxMap.Modules.Identity/IdentityModule.cs:55:        services.AddSingleton(new StoredObjectColumn(StorageBucket.Avatar, "app_user", "avatar_object_key", "avatar_byte_count"));
src/LuxMap.Modules.Identity/IdentityModule.cs:56:        services.AddSingleton(new StoredObjectColumn(StorageBucket.Avatar, "app_user", "avatar_thumbnail_key", "avatar_thumbnail_bytes"));
docker-compose.yml:186:        mc mb --ignore-existing luxmap/luxmap-avatar
deploy/compose.yml:90:        mc mb --ignore-existing luxmap/luxmap-avatar
src/LuxMap.Shared/Storage/ObjectStorage.cs:26:    Avatar,

$ sed -n '233,264p' docs/contract-drift.md
[trích output; exit 0]
### ACCT-5 — avatar tài khoản (09/11/2026)

**Mỹ chốt 09/11/2026 · SELF-SIGNED**, nền tạm tới FW kế tiếp; chưa hợp nhất Contract.
```

## D-14 — D-11 / mục 7: baseline operation hiện tại là 166

Ticket yêu cầu 141 → 151 operation. Cả spec xuất từ code và spec hợp nhất hiện có
166 operation. Thêm đúng 10 endpoint ảnh, giữ các endpoint đã có, sẽ cho **176**
(đây là phép tính dự kiến, chưa phải kết quả sinh spec).

Đề xuất Mỹ xác nhận thay tiêu chí tuyệt đối 141 → 151 bằng **166 → 176, tăng đúng 10**;
giữ các operation ngoài ASSET-PHOTOS. Không xoá route hoặc sửa JSON sinh để ép số 151.
Contract/spec hiện đã ở v1.22; khi tiếp tục đề xuất giữ version hiện tại và cập nhật entry
v1.21 phần A theo ticket, không hạ version.

Bằng chứng terminal thật:

```text
$ python3 - <<'PY'
import json
from pathlib import Path
verbs = {'get','post','put','patch','delete','head','options','trace'}
for name in ['luxmap-v1.json','luxmap-v1.5.json']:
    path = Path('docs/openapi') / name
    spec = json.loads(path.read_text())
    print(f'{path}: version={spec["info"]["version"]}, operations={sum(k in verbs for item in spec["paths"].values() for k in item)}')
    for route, item in spec['paths'].items():
        if 'avatar' in route:
            print(' ', ','.join(k.upper() for k in item if k in verbs), route)
PY
docs/openapi/luxmap-v1.json: version=v1, operations=166
  POST,GET,DELETE /api/v1/auth/me/avatar
  GET /api/v1/admin/users/{id}/avatar
docs/openapi/luxmap-v1.5.json: version=1.22, operations=166
  POST,GET,DELETE /api/v1/auth/me/avatar
  GET /api/v1/admin/users/{id}/avatar
[exit 0]

$ git status --short --branch
## feat/ASSET-PHOTOS-a
[exit 0; trước khi tạo results]

$ git rev-parse HEAD
402bc1ed45a09e3aac7a8562d8f7b886be6dfcf1
[exit 0]
```

## Xác minh và bàn giao

✅ Đã xác minh xung đột bằng nội dung repo và JSON parser, không suy số operation từ trí nhớ.
⚠️ Chưa triển khai hoặc kiểm thử; migration, SQL đếm, lint và mọi phá thử còn chưa chạy.
📌 Chờ quyết định D-13 / D-14 trước khi tiếp tục Phase 2-A. Chỉ định stage/commit báo cáo này,
nhưng sandbox đã chặn stage như output dưới đây. Báo cáo còn untracked, **chưa có commit**.
Không push, mở PR, deploy hoặc đổi branch.

```text
$ git diff --check
[không output; exit 0]

$ git add .ai/results/ASSET-PHOTOS-p2a.md
fatal: Unable to create '/Users/nhm809/Documents/LuxMap/luxmap_backend/.git/worktrees/luxmap_backend-asset-photos/index.lock': Operation not permitted
[exit 128]

$ git diff --cached --check
[không output; exit 0; index rỗng vì stage bị chặn]

$ git diff --cached --stat
[không output; exit 0]

$ git commit -m "docs(ASSET-PHOTOS): record phase 2a baseline decision blockers"
On branch feat/ASSET-PHOTOS-a
Untracked files:
  (use "git add <file>..." to include in what will be committed)

	.ai/results/ASSET-PHOTOS-p2a.md

nothing added to commit but untracked files present (use "git add" to track)
[exit 1]

$ git status --short --branch
## feat/ASSET-PHOTOS-a
?? .ai/results/ASSET-PHOTOS-p2a.md
[exit 0]
```
