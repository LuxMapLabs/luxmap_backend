# TEST-WO-PARALLEL — phase 2

- Chọn **SemaphoreSlim(1, 1)**: một khóa bảo vệ cả host lười và Dictionary, ngắn hơn ghép ConcurrentDictionary<Lazy<Task<HttpClient>>> với cơ chế khởi tạo host riêng. Khóa bao gồm login; chỉ cache client đã đăng nhập xong, dispose client nếu login lỗi, luôn nhả khóa trong finally.
- Không đổi PutClip / Task.WhenAll / Range(0, 3): gửi request ngoài khóa, giữ kiểm tra idempotent song song.
- DisposeAsync dùng cùng khóa khi duyệt cache, giữ `if (polePhotoHost is not null)`; khi chưa gọi PoleClient thì cache rỗng, host null, không dựng host trong teardown. Đây là kiểm tra bằng đọc code, chưa kiểm runtime.

Các chỗ khác dùng chung host/cache (đã rà toàn bộ `tests/`):

- WorkOrderTests.DisposeAsync: duyệt polePhotoClients, dispose polePhotoHost; đã đặt dưới khóa và dispose semaphore.
- WorkOrderPolePhotoReviewTests.Review_checks_parent_membership_and_the_photos_own_commune: xóa client `a` để lấy claim mới; đã dùng cùng khóa, bỏ `old!`.
- WorkOrderPolePhotoTests.Pole_photo_missing_model_still_returns_201_with_unavailable_and_one_user_audit: gán host `broken`, reset null; chạy tuần tự trên instance riêng của xUnit, host có await using. Giữ nguyên.
- WorkOrderPoleVideoUploadTests.The_request_is_checked_before_and_while_the_clip_is_taken: đọc polePhotoHost.Services sau await PoleClient, tuần tự. Giữ nguyên.
- Mọi caller còn lại dùng PoleClient qua các helper và lời gọi trực tiếp trong WorkOrderPolePhotoTests, WorkOrderPolePhotoReviewTests, WorkOrderPoleVideoTests (GetVideo), WorkOrderPoleVideoUploadTests, WorkOrderPoleVideoDecisionTests và WorkOrderUnassignedPoleTests; đều đi qua khóa tại gốc. photoHost/photoClients cho repair evidence là cache khác, không đổi.

✅ Build toàn solution, lệnh thực tế:

```text
dotnet build --disable-build-servers -m:1 -p:NuGetAudit=false
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:27.27
exit 0
```

Lượt đầu `--no-restore` báo NETSDK1004 vì worktree chưa có project.assets.json; hai lượt build mặc định nhiều node đã yêu cầu hủy sau khi không tiến triển. Lượt `-m:1` ở trên restore và build thành công; NuGetAudit=false chỉ áp dụng cho lệnh này, không đổi cấu hình/dependency.

⚠️ Không chạy test, không phá thử, không có số lần đỏ trước/sau theo chỉ thị người dùng; Claude thực hiện phần đó và kiểm runtime teardown khi không gọi PoleClient. Không chạy Git, không stage / commit / push, không sửa source trong src/. Dashboard đã cập nhật; còn chờ kiểm chứng test.
