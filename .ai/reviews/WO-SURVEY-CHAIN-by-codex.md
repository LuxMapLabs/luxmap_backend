Không phát hiện lỗi production **P1/P2** qua đọc tĩnh. Có **2 finding P3 về test**:

- **P3 — Chưa canh việc lấy nhầm kết quả từ phiếu khảo sát khác.** [SurveyPublicationTests.cs:726](/Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/SurveyPublicationTests.cs:726): các test follow-up chỉ có sweep thuộc một phiếu. Nếu bỏ điều kiện `sweep.WorkOrderId == workOrderId`, dữ liệu hiện tại vẫn thỏa mọi assert. Kịch bản lỗi: phiếu A thấy `out`, phiếu B khảo sát cùng cột sau đó thấy `normal`; follow-up A trả sai `NOTHING_TO_FOLLOW_UP`. Cần thêm phiếu B cùng cột để kiểm tính độc lập.

- **P3 — Chưa canh điều kiện cùng `data_source`.** [SurveyPublicationTests.cs:707](/Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/SurveyPublicationTests.cs:707): toàn bộ history/fault trong các test mới đều dùng `Simulated`. Nếu bỏ kiểm nguồn trong `Keep`, các assert vẫn được đáp ứng. Kịch bản lỗi: history `simulated/out` mang sang fault `field/lamp_out` cùng cột. Cần thêm fault khác nguồn và assert bị loại.

Không thấy lỗi cụ thể về race, scope/xã, latest-per-pole hoặc EF translation trong implementation hiện tại. Teardown đáp ứng purge dữ liệu survey bất biến và thứ tự FK `Restrict`; docs/drift khớp code. Không có cơ sở cụ thể để phản đối quyết định bỏ khóa cha hay không tách `InsertCore`.

✅ Đã đọc diff và các tài liệu được chỉ định.  
⚠️ Không build, chạy test, sửa file; các nhận xét về test là suy luận tĩnh.