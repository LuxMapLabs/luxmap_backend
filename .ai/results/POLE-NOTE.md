# POLE-NOTE — ghi chú của kỹ sư trên cột (05/10/2026, claude)

Ticket nhỏ, một file kết quả. Mỹ chốt qua câu hỏi trong phiên: Kỹ sư hiện trường + Quản lý ghi; một ghi chú mỗi cột,
ghi đè, kèm ai/lúc nào; hiện ở kiểm kê, chi tiết cột trên bản đồ, danh sách cột của phiếu.

- Lược đồ: migration `AddPoleNote` (đọc trước khi apply: chỉ AddColumn ×3, index FK, 2 CHECK, 1 FK; `Down()` đối xứng).
  luxmap_test: apply → rollback (`0` cột note) → apply (CHECK + FK có mặt).
- API: `PUT /api/v1/assets/poles/{poleId}/note` (`EditPoleNotes`), `PoleNote` ở Assets dùng chung cho 3 nơi đọc.
- Test: `PoleNoteTests` (6), kỳ vọng mới ở `RoleCapabilityMatrixTests`, `CapabilityMatrixTests`, `AssetReadShapeTests`,
  `PoleDetailEndpointTests`, `WorkOrderTests` (WO-12). Toàn bộ: Api 669, Shared 343, Persistence 44, Storage 41 — xanh,
  không "Cleanup Failure".
- Phá thử: `ReplacePole` gán `Note = null` → `Replacing_the_pole_keeps_its_note` đỏ; bỏ kiểm độ dài ở API →
  `A_body_without_the_key_or_with_more_than_1000_characters_is_400` đỏ. Đã khôi phục.
- Còn: báo WP5/WP6; sync offline (BE-43) phải đưa sửa ghi chú vào hàng chờ.

## Lượt 2 — form tạo / sửa (05/10/2026)

Mỹ: FE còn tạo / sửa cột bằng form. Thêm `note?` vào `CreatePoleRequest` và `UpdatePoleRequest` (`JsonElement`: vắng = giữ,
`null`/rỗng = xoá, chữ = ghi đè; gửi lại cùng nội dung không đổi tác giả). Ba đường ghi qua `StampNote`. Bộ lọc schema
`JsonElementFieldSchemaFilter` nay phủ `note` (trước đó spec ra `{}`). Test `PoleNoteTests` 8/8; phá thử "vắng = xoá" và
"cùng nội dung đổi tác giả" đều đỏ ở `Replacing_the_pole_without_the_note_key_keeps_the_note_and_its_author`.
