---
ticket: TOPO-INFER
reviewer: codex (gpt-6.1-sol, reasoning high)
date: 2026-10-07
scope: đặc tả Phase 1 (.ai/results/TOPO-INFER-p1.md bản đầu); đọc tĩnh
tokens: 89633
---

> Nguyên văn. Claude đối chiếu code: cả 8 phát hiện đúng, đã áp vào đặc tả. Số dòng TOPO-INFER-p1.md trỏ vào bản đầu.

**Chưa nên sang Phase 2:** bảng 3.2 đúng cho thao tác tuần tự, nhưng còn thiếu bảo vệ khi ghi đồng thời và khi thay đổi quan hệ qua feeder/cabinet.

Các vị trí dưới đây thuộc spec chưa triển khai.

- **P1 — Có thể gán `verified` cho sai feeder khi ghi đồng thời.** [TOPO-INFER-p1.md:54](.ai/results/TOPO-INFER-p1.md). Ví dụ: ban đầu `(F1,inferred)`; A đọc rồi xác minh F1; B cũng đọc rồi chuyển sang F2, mặc định `inferred`. A lưu trước; B có thể chỉ cập nhật `feeder_id` vì nhãn bằng giá trị ban đầu → DB thành `(F2,verified)`. EF phát hiện thay đổi theo snapshot (`AssetStamp.cs:20`); import lập kế hoạch **trước** transaction (`AssetImportService.cs:116,139`). **Sửa:** đọc–quyết định–ghi cặp ID/nhãn dưới cùng khóa hàng/transaction, hoặc dùng kiểm soát phiên bản và ghi nguyên tử cả cặp; áp dụng cả import.

- **P1 — D-7 kiểm lúc gắn cột chưa đủ giữ invariant.** [TOPO-INFER-p1.md:107](.ai/results/TOPO-INFER-p1.md). Có thể gắn cột `field` vào feeder chưa có tủ, rồi gắn feeder vào tủ không-`field`; hoặc đổi nguồn của tủ sau đó. Các đường này hiện hợp lệ: `AssetCrudService.cs:203,397`; `AssetImportService.cs:228,321`. **Sửa:** kiểm trạng thái cuối tại mọi đường ghi pole, gắn/chuyển feeder–cabinet và đổi `cabinet.data_source`, gồm CRUD + import. Kiểm lại trong transaction để tránh thay đổi đồng thời. Đây là luật mới về **pole–cabinet**, không được khôi phục lệnh cấm tủ `field` mang IoT đã gỡ ở CAB-5 (`docs/contract-drift.md:1120`).

- **P2 — Chưa phân biệt xác minh cột–mạch với xác minh cả sơ đồ.** [TOPO-INFER-p1.md:96](.ai/results/TOPO-INFER-p1.md). `verified` của cột đích không chứng minh cạnh cột trước→cột đích, cũng không chứng minh feeder được cấp từ tủ hiện tại. Feeder không có rơ-le vẫn chuyển/tháo tủ được qua PUT và import (`AssetCrudService.cs:393`; `AssetImportService.cs:301`). **Sửa:** chốt rõ nhãn chỉ xác minh **cặp pole–feeder**; đổi tủ có thể giữ nhãn cặp này nhưng không được biểu diễn thành “đường nối đã xác minh”. Nếu muốn nhãn chứng minh cả chuỗi tới tủ, phải bổ sung nguồn xác minh feeder–cabinet hoặc hạ nhãn khi đổi/gắn/tháo tủ.

- **P2 — Bảng ghi thiếu trường hợp `feeder_source: null` và kiểu JSON sai.** [TOPO-INFER-p1.md:56](.ai/results/TOPO-INFER-p1.md). “Vắng” và “có giá trị” chưa bao phủ null. Endpoint hẹp hiện bắt buộc có khóa `feeder_id` (`AssetsController.cs:519`); full PUT và import thiếu feeder lại **tháo** quan hệ (`AssetContracts.cs:296`; `AssetImportService.cs:397`). GeoJSON còn biến object/array thành null (`ImportRow.cs:53`). **Sửa:** quy định riêng missing/null/chuỗi/kiểu sai cho nhãn trên mọi request; giữ quy tắc bắt buộc `feeder_id` của endpoint hẹp, tháo feeder phải xóa nhãn, JSON sai phải báo lỗi thay vì coi là vắng.

- **P2 — Thứ tự feature/đánh số branch chưa xác định hoàn toàn.** [TOPO-INFER-p1.md:94](.ai/results/TOPO-INFER-p1.md). Chưa nói `branch` đánh số toàn cục hay trong từng nhóm, có phụ thuộc thứ tự `GroupBy` hay không; `segment_id` ở bước 5 chưa có khóa sắp theo độ rộng ID. **Sửa:** quy định phạm vi branch và thứ tự hai phía cố định; mọi tie dùng comparer ordinal. Chốt khóa segment theo quy ước ID, không sắp text trần; WO-12 đã dùng `length,id` tại `WorkOrderPoles.cs:132`.

- **P2 — “Cột gần trụ nhất” không đúng nghĩa khoảng cách thực.** [TOPO-INFER-p1.md:95](.ai/results/TOPO-INFER-p1.md). Trụ xa tuyến vẫn bị chiếu lên tuyến; thứ tự theo chỉ số chiếu không bảo đảm cột đầu gần trụ nhất. `LengthIndexedLine` trên 4326 dùng khoảng cách phẳng theo độ; WO-12 chỉ cam kết **thứ tự** (`WorkOrderPoles.cs:70,123`). **Sửa:** đổi mô tả thành “cột đầu tiên theo thứ tự từ điểm chiếu của trụ”; công khai cạnh là sơ đồ logic. Nếu cần chọn cột gần nhất hoặc ngưỡng xa tuyến, dùng phép tính metric nội bộ và đặc tả riêng.

- **P2 — D-4 nhận sai ý nghĩa audit hiện có.** [TOPO-INFER-p1.md:131](.ai/results/TOPO-INFER-p1.md). `updated_by/updated_at` là người/lúc sửa **bất kỳ thuộc tính nào**, không phải người xác minh (`AssetStamp.cs:33`; sửa note: `AssetCrudService.cs:631`). **Sửa:** bỏ tuyên bố “ai/lúc nào xác minh”; nếu cần truy xuất người xác minh, lưu dấu xác minh riêng.

- **P3 — Section 4 thiếu cập nhật fixture và kiểm tra hồi quy cụ thể.** [TOPO-INFER-p1.md:112](.ai/results/TOPO-INFER-p1.md). Fixture hiện gắn `FeederId` trực tiếp (`tests/LuxMap.Api.Tests/CabinetTests.cs:670`) sẽ vi phạm CHECK mới; test đọc khóa chính xác cũng cần cập nhật (`AssetReadShapeTests.cs:48`). **Sửa:** bổ sung fixture/raw writer, schema enum cho cả endpoint hẹp, tài liệu GeoJSON import, test CHECK hai chiều, null/kiểu sai, concurrency, D-7 qua feeder/cabinet, chuyển feeder cùng/khác tủ, ties và đánh số branch. Ghi rõ cập nhật Contract sau duyệt, `tracking.html` và bàn giao review.

**Những ca đã đối chiếu và không phải lỗ hổng:**

- Chuyển cột sang feeder khác **cùng hay khác tủ**: mặc định phải về `inferred`; cùng tủ không phải lý do giữ `verified`.
- Xóa feeder còn cột bị FK `Restrict` chặn (`AssetConfigurations.cs:240`); service trả 409 (`AssetCrudService.cs:552`).
- Tuyến `inter_commune` bị filter che: spec đã xử lý đúng theo WO-12. Chỉ bỏ filter khi lấy geometry của các segment được cột đã scope tham chiếu.
- Cột của feeder thuộc xã ngoài scope trong khi tủ vẫn thấy được **không xảy ra với dữ liệu hợp lệ**: FK ghép buộc pole–feeder–cabinet cùng xã (`AssetConfigurations.cs:145,242`). Vẫn giữ filter trên cả ba loại tài sản.

| D-item | Verdict |
|---|---|
| D-1 | Đồng ý: nhãn thuộc cặp pole–feeder. |
| D-2 | Đồng ý: hai giá trị + null khi chưa gắn. |
| D-3 | Cần sửa: null/kiểu sai và tính nguyên tử. |
| D-4 | Quyền phù hợp; tuyên bố audit chưa đúng. |
| D-5 | Hình dạng phù hợp; làm rõ nhãn trên cạnh logic. |
| D-6 | Có điều kiện: chốt branch, determinism và giới hạn phép chiếu. |
| D-7 | Tương thích CAB nếu giới hạn pole–cabinet; chưa đủ đường kiểm. `non-field` không đồng nghĩa toàn bộ đều là testbed. |
| D-8 | Đồng ý: script chỉ đề xuất, người duyệt quyết định. |
| D-9 | Đồng ý: backfill **mọi hàng có feeder**, không cố định 103 hàng. |

✅ Đã đối chiếu mã nguồn. ⚠️ Không build/test, không truy cập DB; số lượng dữ liệu trong spec chưa xác minh. Không sửa file.
