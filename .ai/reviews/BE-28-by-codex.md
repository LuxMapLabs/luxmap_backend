# BE-28 review — Codex (gpt-6.1-sol, high, chỉ đọc), 07/10/2026, 58 013 token

Phạm vi: commit `252440a`. Nguyên văn phát hiện bên dưới; phần **Xử lý** do Claude kiểm và sửa.

Không phát hiện P1; có **3 P2**:

1. **P2 — [StatisticsService.cs:105](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Statistics/StatisticsService.cs:105): ngày ISO hợp lệ gây 500.**  
   `repair-timeliness?to=9999-12-31` làm `to.AddDays(1)` tràn. Tương tự, `to=0001-01-01` khi thiếu `from` tràn tại dòng 95. Binder chấp nhận các ngày này; exception được trả thành 500.  
   **Sửa:** kiểm giới hạn trước khi tính cửa sổ, trả 400 `VALIDATION_FAILED`.

2. **P2 — [StatisticsTests.cs:223](/Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/StatisticsTests.cs:223): test chưa chứng minh ranh giới `open_overdue`.**  
   Chỉ trồng hạn rất xa trong quá khứ/tương lai. Đổi điều kiện thành `due_date <= tonight` vẫn xanh, nhưng phiếu hạn đêm nay bị đếm quá hạn, trái ST-6.  
   **Sửa:** cố định `TimeProvider`, trồng hạn hôm trước/đêm nay/đêm sau và kiểm hai phía 12:00 giờ Việt Nam.

3. **P2 — [StatisticsTests.cs:204](/Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/StatisticsTests.cs:204): test cửa sổ chưa chứng minh phần sau nửa đêm của đêm `to`.**  
   Với `to=2026-09-30`, không có phiếu hoàn tất sáng 01/10 trước 12:00 phải được tính. Nếu cận cuối sai thành 00:00 ngày 01/10, test vẫn xanh dù bỏ mất phần cuối đêm 30/09.  
   **Sửa:** thêm phiếu tại đầu đêm `from`, ngay trước cận cuối và đúng cận cuối; assert hai trường hợp đầu được tính, trường hợp cuối bị loại.

✅ Đã đối chiếu ST-1…ST-10, policy vai trò, query filter xã, tách `data_source`, `unknown`/`never_surveyed`, thứ tự ID/enum và binder ngày ISO.  
⚠️ Chỉ kiểm tra tĩnh; không sửa file, không chạy build/test hay truy vấn DB.

## Xử lý (Claude)

| # | Kiểm | Sửa | Phá thử |
|---|---|---|---|
| 1 | Tái hiện trên API local: `to=9999-12-31` và `to=0001-01-01` → **HTTP 500** | `from`/`to` phải trong 2000-01-01…2099-12-31, ngoài dải → 400 `VALIDATION_FAILED`; 2 ca thêm vào `A_malformed_query_is_a_400` | Gỡ kiểm dải → 2 ca đỏ |
| 2 | Đúng: chỉ có hạn rất xa | `A_repair_due_tonight_is_not_overdue_and_one_due_last_night_is` (đêm nay tính độc lập theo luật 12:00; bỏ qua nếu request vắt qua 12:00) | `<` → `<=` → đỏ |
| 3 | Đúng: không có phiếu trong phần sáng của đêm `to` | Thêm phiếu 12:00 đêm `from` và 11:00 sáng sau `to` (đều được tính) | Cận cuối = 00:00 → đỏ |

Sau sửa: 1178/1178 xanh trên `luxmap_test`.
