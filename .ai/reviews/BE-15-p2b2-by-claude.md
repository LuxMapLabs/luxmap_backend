# Review BE-15 P2b-2 — Claude, vòng 1 (03/10/2026)

Đã kiểm: build 0 warning; migration `AddSurveyFrames` đọc xong — chỉ thêm (13 cột, 2 bảng, index, CHECK), gỡ/dựng lại
`ck_artifact_version_component`, `Down()` đối xứng, không cột `xmin` vật lý; áp lên `luxmap_test` được, model khớp. Guard
công tắc dọn chỉ miễn thêm đúng file migration mới — đúng. Không DB: Shared 281, Persistence 40, Storage 30 xanh. Tích hợp:
**3/564 đỏ** (F2, F5). Teardown sạch.

## F1 — P1. Mỗi frame một lần ffmpeg, giải mã lại từ đầu clip

`FfmpegFrameExtractor.ExtractAsync` chạy một tiến trình ffmpeg **cho từng frame** với `select=eq(n,index)`, nên mỗi lần giải
mã từ đầu clip tới frame đó. **Đo trên video thật** (iPhone 16 Pro Max, HEVC 1080p, 45 s): một frame ở chỉ số 300 mất
**3,9 s**; cắt **68 frame trong một lượt** (`select='between(n,a,b)+…'`, `-fps_mode passthrough`, mẫu `frame_%04d.jpg`) mất
**3,4 s**. Clip 1 phút với ~340 frame theo cách hiện tại ≈ **một giờ**. Sửa: **một lượt ffmpeg cho mỗi clip**, xuất chuỗi ảnh
rồi ánh xạ lại theo thứ tự chỉ số đã chọn (kiểm số ảnh ra đúng bằng số chỉ số, thiếu ⇒ lỗi rõ). Test: đếm số tiến trình
ffmpeg được khởi chạy cho một clip có nhiều cửa sổ — phải là **1** (qua một seam đếm, không đo giờ).

## F2 — P1. Cờ `ambiguous_association` của P2b-1 chặn luôn ON/OFF

Ở P2b-1 cờ này nói **đỉnh lux không quy chắc được cho cột** (ví dụ phiên có < `MinimumOffsetAnchors` đỉnh nên không ước lượng
được độ trễ GPS). Thời điểm ngang cột theo GPS vẫn dùng được để cắt frame. Hiện pipeline gán `unknown`
(`association_ambiguous`) ⇒ ngoài thực địa, đoạn ngắn ít đèn sáng thành **toàn unknown** dù camera thấy rõ. Sửa: lux mơ hồ chỉ
làm **không đánh giá được độ sáng** (peak null ⇒ `normal`, không đủ điều kiện xét `dim` — `FrameClassification` đã xử lý đúng);
**vẫn ghép và xét ON/OFF**. Chỉ cờ làm **thời điểm ngang cột** không dùng được (ví dụ `route_ambiguous`) mới được chặn CV —
ghi rõ danh sách và lý do. Test tích hợp `Frames_detections_classification_and_three_coverages…` (đang đỏ: độ phủ (b) 50 thay
vì 100) phải xanh **mà không đổi kỳ vọng**.

## F3 — P2. Theo dõi bằng IoU giữa hai frame liên tiếp sẽ vỡ trên video thật

Ở 3–5 frame/giây, xe máy 6–7 m/s, đầu đèn **to dần và trượt nhanh** khi tới gần, nên IoU giữa hai frame liên tiếp thường
≈ 0 ⇒ mỗi frame một track mới ⇒ `ambiguous_tracks` ⇒ `unknown`. Tiêu chí mơ hồ nên là: **cùng một frame có ≥ 2 phát hiện đủ
chất lượng ở phía của cột** mà không tách được; còn nếu mỗi frame có tối đa một phát hiện phía cột, cùng nhãn ⇒ một bóng.
Giữ `on_off_conflict` và `shared_cv_evidence`. Test: hộp đi nhanh, IoU giữa các frame = 0, mỗi frame một phát hiện ⇒ `associated`;
hai đèn cùng lúc trong một frame ⇒ mơ hồ.

## F4 — P2. Cửa sổ mặc định quá hẹp và lệch

`BeforeSeconds = 1`, `AfterSeconds = 1`, 3 fps. Camera chéo lên phía trước thấy đèn rõ nhất **khi cột còn phía trước**, và thời
điểm GPS chưa hiệu chỉnh có thể lệch ~0,8 s. Đặt mặc định theo thiết kế Phase 1: **trước 3 s, sau 0,5 s, 5 frame/giây**; vẫn là
tham số, chốt sau quay thử (Q6). Cập nhật README.

## F5 — test. Hai test chainage đỏ vì dữ liệu fixture đổi

Cột được dời ngang (16.00001 → 16.00004 / 15.99998) để có cột hai bên đường ⇒ tỉ lệ chainage của cột đầu ra 0,2513, ngoài khoảng
ghim `(.249, .251)` ở `Chainage_is_projected…` và `Projection_snapshot…`. Sửa kỳ vọng theo hình học mới (tính từ toạ độ, ghi
chú lý do) hoặc tách dữ liệu riêng cho test chainage — không nới khoảng mà không giải thích.

## Ghi nợ (P3, không sửa vòng này)

- Video iPhone là HEVC 10-bit HDR; JPEG xuất ra chưa tone-map. Máy Android của WP6 có thể khác — xét khi có mẫu D-06.
- `ffprobe -show_frames` giải mã cả clip để lấy PTS; chấp nhận với clip < 1 phút.

# Vòng 2 — Claude (03/10/2026)

Đã commit mốc `0b59a51` (code vòng 1 + sửa của Claude bên dưới). **Làm tiếp trên HEAD.**

## Claude đã sửa trong vòng này — đừng đảo lại

- **P1 lỗi câm:** `classified_as` có `HasDefaultValue(Unknown)`, mà `FixtureStatus.Normal` là giá trị 0 (mặc định CLR) ⇒ EF
  bỏ cột khỏi INSERT ⇒ DB điền `unknown` ⇒ **mọi đèn `normal` bị lưu thành `unknown`**. Đã gỡ default khỏi model; migration
  sinh lại thành `20261003071658_AddSurveyFrames` (đã khôi phục hai trigger bất biến và khối chặn rollback viết tay; cột mới
  điền `unknown` cho hàng cũ rồi `DROP DEFAULT`). Test canh toàn model `ModelDefaultValueTests`; phá thử ⇒ đỏ đúng cột.
  ⚠️ Migration có **SQL viết tay** — nếu phải sinh lại, giữ nguyên đoạn đó.
- Tích hợp: Api 564/564, Persistence 41/41 trên `luxmap_test`; migration áp → gỡ → áp lại được.

## R2-1 — P1. Biểu thức `select` một lượt vỡ ở số frame thực tế

Chạy đúng lệnh hiện tại trên video thật (iPhone HEVC 45 s) với **324 chỉ số** (cỡ một clip 1 phút, ~18 cột × 18 frame):
ffmpeg báo `Error while parsing expression … Cannot allocate memory`, exit 244, **không frame nào**. Test hiện chỉ chọn vài frame
nên không lộ.

**Đã thử thành công trên video thật** (nên làm theo): **mỗi cửa sổ cột một lượt ffmpeg**, `-ss <đầu cửa sổ − 1 s> -copyts -i clip`
và chọn bằng **PTS gốc** `select='eq(pts\,p1)+eq(pts\,p2)+…'` (đơn vị time_base của stream, lấy từ ffprobe), `-fps_mode passthrough`,
xuất `image2`. Kết quả: 18 lượt, **292/292 frame đúng**, 0 cửa sổ hụt, ~34 s cho clip 45 s. Mỗi lượt chỉ ~18 vế và chỉ giải mã vài
giây quanh cửa sổ, nên không phụ thuộc độ dài clip. Gộp các cửa sổ chồng nhau thành một lượt nếu muốn bớt tiến trình, nhưng
**giới hạn số vế mỗi lượt** (ví dụ ≤ 64) và nêu hằng số đó.

Yêu cầu kèm:
- Cập nhật test "một clip một tiến trình" thành: số tiến trình = số nhóm cửa sổ (không phụ thuộc số frame), và mỗi lượt ≤ giới
  hạn vế.
- **Test thực tế:** clip tự sinh **≥ 45 s, 30 fps (có VFR)**, **≥ 300 frame được chọn** trên ~18 cửa sổ ⇒ đủ frame, đúng PTS, đúng
  thứ tự. Test này phải đỏ với cách một-lượt-một-biểu-thức hiện tại (Claude sẽ phá thử để kiểm).
- Giữ: kiểm số ảnh ra đúng bằng số frame chọn, quota đĩa tạm, timeout, dọn file tạm, `-copyts` để PTS khớp ánh xạ thời gian.
