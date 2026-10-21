# CLAUDE-MD-CLEANUP — Phase 2 (Claude)

Theo D-1…D-6 (`.ai/tasks/CLAUDE-MD-CLEANUP.md`). Phần Codex: `CLAUDE-MD-CLEANUP-p2-by-codex.md`.

## Làm

1. Script tách các mục bẫy theo ticket **nguyên văn** sang 12 file `docs/agents/traps/*.md` (commit `68589fb`).
2. Viết lại `CLAUDE.md` lõi (338 dòng), áp các mục S/C của audit Codex thuộc phần lõi: v1.4 / D-R, Contract v1.14, consumer máy,
   luật ưu tiên D-4, sort `-severity`, `out` do CV + `out_threshold_ratio = null`, nguồn `lamp_*` (C-3), `LPAD` giữ chữ số đầu (C-1), phạm vi
   theo JWT (C-2), trạng thái cột từ sweep **hoặc** ảnh (C-6), cascade ngoại lệ (C-7), máy là ngoại lệ capability (C-9), cấu hình quay cấp
   phiên (S-15), stack không Hangfire (S-16); tách D-6. Bẫy chung bị chôn trong mục ticket đưa lên lõi (µs clock, một audit / save, khoá hàng,
   port, body `sealed class`, Swagger, `Response.Clear()`, `Notifier`, `PLAN`), cộng bẫy MSBuild node reuse khi Codex build song song.
3. Drift CLAUDE-MD-1 (Contract `:285` ↔ `:703`) và CLAUDE-MD-2 (câu tóm tắt D-R28 lỗi thời — đã sửa).
4. `luxmap_format_id` (từ mục "Thứ tự hiện tại" đã bỏ) chuyển sang `migrations.md`.

## Review phần Codex

- `.cs` / `.py`: chỉ comment / docstring đổi (lọc dòng không phải comment: 0, trừ một dòng trong docstring `seed_mock_set.py`).
- 9 luật LOST phục hồi đúng chỗ; LOST-3 (firmware) và LOST-4 (không lọc khoảng trên ID chuỗi) đưa lên lõi vì áp mọi ticket.
- Đọc diff `commune-scope`, `testing`, `assets`, `faults-workorders`, `media-storage`: luật + lý do + tên test giữ, nhật ký đo bỏ. Bổ sung lại lý do
  `priority_score` là cột lưu (commit `0d06358`). Tên bucket / trần clip đối chiếu code: đúng.

## Bằng chứng

```
$ wc -lc CLAUDE.md            # trước: 2293 dòng, 204184 byte
     338   32491 CLAUDE.md
$ (mọi đường dẫn `docs|src|tests|scripts|.ai|deploy/…` và tên test / method được nhắc tới đều tồn tại; ngoại lệ `docs/adr/` — lõi nói nó KHÔNG có)
$ dotnet build --disable-build-servers
    0 Warning(s)
    0 Error(s)
```

## Còn

- ✅ Rebase lên `dev` sau PR #164: đoạn PHOTO-REVIEW (6 dòng, nguyên văn) nằm cuối mục POLE-PHOTO-AI của `docs/agents/traps/survey.md`.
