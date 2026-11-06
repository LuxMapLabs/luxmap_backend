---
ticket: TEST-WO-PARALLEL
title: Test flaky — Parallel_attempts_of_one_operation_end_on_one_row ghi Dictionary song song
status: ready
phase: 2
owner: —
branch: fix/TEST-WO-PARALLEL
contract_refs: []
depends_on: []
---

## Bối cảnh

`WorkOrderTests.Parallel_attempts_of_one_operation_end_on_one_row` (`tests/LuxMap.Api.Tests/WorkOrderPoleVideoUploadTests.cs:142`, POLE-VIDEO)
đỏ ngẫu nhiên khi chạy cả bộ: thấy 06/11/2026 trong lượt full-suite local của nhánh `STAT-citizen-feedback`
(`dotnet test`, exit 1, 1 đỏ / 1396 xanh); chạy riêng 3/3 xanh, lượt full ngay sau xanh, CI #210–#212 xanh. **Chỉ test, không phải lỗi sản phẩm.**

Lỗi thật (log lượt đỏ):

```text
System.InvalidOperationException : Operations that change non-concurrent collections must have exclusive access.
   at Dictionary`2.set_Item
   at WorkOrderTests.PoleClient(String who)          WorkOrderPolePhotoTests.cs:64
   at WorkOrderTests.PutClip(...)
   at WorkOrderTests.Parallel_attempts_of_one_operation_end_on_one_row()   WorkOrderPoleVideoUploadTests.cs:~148
System.NullReferenceException
   at WorkOrderTests.DisposeAsync()                  WorkOrderTests.cs:102
```

## Nguyên nhân (đã đọc code, chưa sửa)

- Test gọi `Task.WhenAll(Enumerable.Range(0, 3).Select(_ => PutClip(...)))` — ba lời gọi `PoleClient("a")` chạy **đồng thời**.
- `PoleClient` (`WorkOrderPolePhotoTests.cs`) dựng host và client **lười**, rồi `polePhotoClients[who] = client` vào một `Dictionary<string, HttpClient>`
  thường (`TryGetValue` rồi ghi, không khoá). Ba lời gọi cùng thấy "chưa có" → cùng ghi → `InvalidOperationException`. Cũng có thể dựng
  `polePhotoHost` nhiều lần (host lười) → host rò, và `DisposeAsync` gặp trạng thái dở (`NullReferenceException` kèm theo).
- Các test khác không lộ vì gọi `PoleClient` tuần tự.

## Yêu cầu

1. Làm `PoleClient` an toàn khi gọi song song **tại gốc**, không né ở riêng test này: một `SemaphoreSlim` bao phần dựng host + ghi dictionary,
   hoặc `ConcurrentDictionary<string, Lazy<Task<HttpClient>>>` — chọn cái ngắn hơn và ghi lý do. Đừng thêm `await PoleClient("a")` làm nóng
   trước `WhenAll` mà bỏ nguyên lỗi (test sau dùng `who` khác vẫn dính).
2. Giữ ý nghĩa test: ba request cùng `client_op_id` vẫn song song thật (đừng tuần tự hoá `PutClip`).
3. **Phá thử**: dựng lại cho thấy lỗi cũ trước khi sửa — chạy test song song nhiều lần (vd. `for i in 1 2 3 4 5 6 7 8`, hoặc tăng `Range(0, 3)` lên
   `Range(0, 16)` tạm thời) và dán số lần đỏ / tổng; sau khi sửa phải 0 đỏ trên cùng số lần. Hoàn tác phần tăng tạm.
4. `DisposeAsync` phải sống sót khi host chưa từng được dựng (kiểm: một test không gọi `PoleClient` không bị `NullReferenceException`).

Tiêu chí xong:

- [ ] test song song đỏ được trước khi sửa (có số liệu), 0 đỏ sau khi sửa trên cùng số lần chạy
- [ ] `dotnet test --disable-build-servers` exit 0
- [ ] `git diff --stat` chỉ gồm file test + `tracking.html` + `.ai/` của ticket

## KHÔNG ĐƯỢC làm

- Không sửa `src/`. Không `Thread.Sleep` / retry để "làm xanh". Không đánh dấu `Skip`.
- Không đổi hành vi idempotent mà test đang kiểm (một dòng cho cùng `client_op_id`).
- Không chạy test trên `luxmap_dev`; không `git push` khi chưa hỏi.
