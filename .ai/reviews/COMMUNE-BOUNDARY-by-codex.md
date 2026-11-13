---
ticket: COMMUNE-BOUNDARY
reviewer: codex (gpt-6.1-sol, effort high, sandbox read-only)
reviewed: bf76bd9 (feat/COMMUNE-BOUNDARY)
date: 2026-11-13
verdict: changes_requested
---

## Phạm vi đã review

Commit `bf76bd9`: cột `administrative_unit.boundary`, migration `CommuneBoundary`, `CommuneBoundaryReader`,
`PUT /admin/communes/{id}/boundary`, `GET /map/communes` (`CommuneLayerService`), `Geometry.MultiPolygon`, test và script nạp.
Codex chỉ đọc diff và các file nó gọi trực tiếp; không build, không chạy test (suite đã xanh trước đó). ~87k token.

Sáu điểm được giao kiểm: lộ phạm vi xã, lỗ kiểm tra polygon dẫn tới 500, phát hiện thay đổi của EF, đối xứng migration,
luật trong `AGENTS.md`, khoảng trống test.

## Phát hiện

| # | Mức Codex | File:dòng | Vấn đề | Xử lý (Claude kiểm lại) |
|---|---|---|---|---|
| 1 | P2 | `CommuneBoundaryReader.cs:100` | Vị trí có hơn hai giá trị bị bỏ thừa âm thầm, kể cả `[0,0,null]` | **Đúng một phần.** RFC 7946 §3.1.1 cho phép độ cao là số thứ ba, nên không chặn hẳn: nhận 2 hoặc 3 số (độ cao bị bỏ, cột 2D), từ chối `null` và giá trị thứ tư. Test `null altitude`, `four values`, `A_numeric_altitude_is_accepted_and_dropped` |
| 2 | P2 | `CommuneBoundaryTests.cs:76` | Không test phạm vi rỗng: đổi điều kiện thành "bỏ lọc khi claim rỗng" sẽ lộ mọi ranh giới mà test vẫn xanh | **Đúng.** Thêm `An_empty_scope_gets_no_outlines_not_all_of_them` (token ký đúng, vai trò manager, không claim xã) và `Asking_for_own_and_foreign_communes_together_is_403` |
| 3 | P2 | `CommuneBoundaryTests.cs:98` | Thiếu ca âm: MultiPolygon rỗng, phần tử rỗng, lồng sai cấp, object / null trong mảng, số quá lớn, lỗ nằm ngoài vỏ, vượt số đỉnh | **Đúng.** Thêm 9 ca vào `BadOutlines` (gồm vòng 10 001 đỉnh hợp lệ). Code hiện tại đã trả 400 cho tất cả — chỉ thiếu test |
| 4 | P3 | `CommuneBoundaryTests.cs:125` | Không có test chứng minh thay đổi thật làm `updated_at` tăng | **Đúng.** Thêm `Replacing_or_clearing_the_outline_moves_updated_at` |
| 5 | P3 | `CommuneContracts.cs:32` | Body JSON là `sealed record`, luật repo là `sealed class` + `init` | **Đúng.** Đổi sang `sealed class` (record không positional nên không dính lỗi 500 mà luật chặn; sửa cho đúng quy ước) |

Codex ghi **không có phát hiện** ở: lộ phạm vi (claim được lọc độc lập với `CommuneFilter.Narrow`, scope rỗng ra rỗng),
đường 500 trong reader, phát hiện thay đổi EF (so sánh hình học theo cấu trúc), đối xứng migration, luật GeoJSON / thứ tự ID.

## Đã kiểm chứng

- Mỗi phát hiện được đọc lại trên code trước khi sửa.
- Phá thử bốn chốt mà phát hiện 1–4 nói "có thể hỏng mà test vẫn xanh" — mỗi lần đúng một test đỏ:
  bỏ lọc khi claim rỗng → `An_empty_scope_gets_no_outlines_not_all_of_them`; bỏ chặn MultiPolygon rỗng → ca `empty multipolygon`;
  bỏ gán `updated_at` → `Replacing_or_clearing_the_outline_moves_updated_at`; bỏ kiểm độ cao → ca `null altitude`. Đã khôi phục.
- `CommuneBoundaryTests` 26/26 xanh.
