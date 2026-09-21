# BE-13 — hình dạng endpoint topology (đề xuất, chờ duyệt)

**Trạng thái:** đề xuất. **Chưa có dòng code nào.** Contract không có endpoint topology nào, và
`GET /api/v1/assets/poles` vẫn chỉ nhận `commune_id` + phân trang, vẫn trả `PagedResult<string>`.

| | |
|---|---|
| **Người cần duyệt** | **Thịnh / Ngọc** (CODEOWNERS của `api-contract-v1.1.md` và `luxmap-v1.json`) |
| **Duyệt cùng lúc với** | **BE-12b** và **drift 44** — xem §0.1, có một câu hỏi dùng chung |
| **Người dùng** | **CV-15** (gom cụm), **CV-05** (gán cột vào tuyến) — WP4, engine nội bộ. **Không phải WP5** |
| **Hạn** | BE-13 là W4 (28/09 – 04/10). CV-05 và CV-15 chặn sau nó |
| **Nếu duyệt** | Contract lên **v1.6**, thêm mục 5.4 |
| **Nếu không duyệt** | Không có gì phải hoàn tác |

**Tiêu chí nghiệm thu của ticket** (`tasks-backend.csv` dòng 16): *"Truy vấn được 'tất cả cột trên
feeder X' phục vụ clustering"*. Tài liệu này đề xuất hình dạng của đúng câu truy vấn đó.

---

## 0. Ba câu cần chữ ký

| # | Câu hỏi | Đề xuất | Hệ quả nếu trả lời ngược |
|---|---|---|---|
| **Q1** | Endpoint riêng, hay thêm filter vào `GET /assets/poles`? | **Endpoint riêng** | Xem §2 — CV-15 phải gọi thêm N lần để lấy toạ độ |
| **Q2** | Response có kèm **toạ độ** không? | **CÓ** | CV-15 không gom cụm được bằng một request; đụng anti-pattern *"đừng bắt gọi nhiều lần"* |
| **Q3** | Cột `solar` (không mạch) truy vấn thế nào? | **`?unassigned=true`** trên endpoint feeder | Không có cách liệt kê cột ngoài mạch; CV-05 mất đầu vào |

### 0.1 Một câu dùng chung với BE-12b

**Q3 của BE-12b** hỏi `GET /assets/poles/{id}` có emit `feeder_id` không. Tài liệu đó đã ghi lý do
*"BE-13 và màn sửa mạch điện không đọc được giá trị nó sắp ghi"*. Nay BE-13 có mặt thật, nên câu đó
không còn là giả định.

> 🔴 **Nếu BE-12b Q3 trả lời KHÔNG emit `feeder_id`, thì Q2 ở đây phải trả lời lại.** Một hệ thống
> mà `feeder_id` không đọc được ở đâu cả nhưng endpoint topology lại **nhóm theo nó** là mâu thuẫn
> — người dùng nhìn thấy kết quả gom nhóm mà không tra được cột nào thuộc nhóm nào. Hai câu này
> phải duyệt cùng một lượt.

---

## 1. Contract đang ghi gì

**Không gì cả.** Đã tra toàn văn: không có "topology", không có endpoint nào nhóm cột theo mạch điện
hay theo tuyến. Mục 5.3 phủ CRUD `/assets/…`; mục 2.1 phủ `GET /poles` theo `bbox` (là BE-14, bản đồ).

Thứ gần nhất đang có:

| Có sẵn | Vì sao không đủ |
|---|---|
| `GET /assets/poles?commune_id=` | Không lọc được theo mạch hay theo tuyến |
| `PUT /assets/poles/{id}/feeder` | Đường **ghi**. BE-13 cần đường **đọc** |
| `GET /poles?bbox=` (BE-14) | Lọc theo **không gian**. Mạch điện không phải khái niệm không gian — hai cột cạnh nhau có thể khác mạch |

**Phần GÁN của BE-13 đã xong từ BE-12a/BE-12.** `PUT /assets/poles/{id}/feeder` gán mạch,
`PUT /assets/poles/{id}` gán tuyến, import gán cả hai theo lô. Phần còn thiếu đúng là phần **truy
vấn** — và đó cũng đúng là câu tiêu chí nghiệm thu viết ra.

---

## 2. Q1 — endpoint riêng, không phải filter

**Đề xuất: endpoint riêng.**

```
GET /api/v1/assets/feeders/{feederId}/poles
GET /api/v1/assets/segments/{segmentId}/poles
```

**Vì sao không thêm `?feeder_id=` vào `GET /assets/poles`.** Endpoint đó đang trả
`PagedResult<string>` — một chỗ giữ chỗ mà **BE-12b còn chưa duyệt**. Nhét filter của BE-13 lên nó
buộc hai thứ vào nhau: BE-13 sẽ **thừa kế bất cứ hình dạng nào BE-12b chốt**, mà BE-12b đang thiết
kế cho một **màn hình kiểm kê của WP5** — một bảng cho người đọc, phân trang, có `external_ref`, có
`install_date`, có `warranty_expiry`.

CV-15 không phải người đọc. Nó là engine gom cụm; nó cần **ID và toạ độ**, không cần ngày bảo hành.
Ép chung một hình dạng thì một trong hai bên phải nhận thứ mình không dùng — và khi BE-12b đổi hình
dạng (nó sẽ đổi, vì đang chờ duyệt) thì CV-15 gãy theo vì một lý do không liên quan gì đến nó.

**Hai bề mặt, hai người dùng, hai nhịp thay đổi.** Cùng lý lẽ đã tách `/assets/poles` khỏi `/poles`
ở BE-12a.

---

## 3. Q2 — response kèm toạ độ

**Đề xuất: có.**

```json
{
  "page": 1,
  "page_size": 50,
  "total": 46,
  "items": [
    {
      "pole_id": "POLE-0001",
      "segment_id": "SEG-001",
      "feeder_id": "FDR-001",
      "lat": 10.969973,
      "lng": 106.49
    }
  ]
}
```

**Vì sao kèm toạ độ.** CV-15 gom cụm **theo không gian dọc mạch điện** — nó cần vị trí của từng cột.
Nếu endpoint chỉ trả ID thì CV-15 phải gọi thêm **một request cho mỗi cột**: 46 lần cho `FDR-001`.
`CLAUDE.md` liệt kê đúng việc đó vào anti-pattern — *"Đừng bắt FE gọi nhiều lần để dựng màn chi tiết
cột"* — và lý do không đổi khi người gọi là engine thay vì trình duyệt.

**Vì sao `{lat, lng}` chứ không phải GeoJSON.** Đây **không phải endpoint bản đồ**. Contract mục 2.4
đã có tiền lệ đúng cho ca này: `GET /faults` là *"phân trang JSON, KHÔNG phải GeoJSON"*, mỗi item
mang `location{lat,lng}`. BE-13 cùng loại — danh sách phân trang cho một engine, không phải lớp bản
đồ cho MapLibre.

**EPSG:4326**, như mọi endpoint. Khoảng cách tính ở tầng DB bằng `SpatialFunctions.DistanceMeters`
(3405) và **không bao giờ đi ra API** — xem bốn quy tắc BE-10.

**Năm trường, không hơn.** Cố ý **không** mang `fixture_status`, `open_fault_count`,
`status_confidence` hay bất cứ thứ gì của mục 5.1: hai endpoint trả lời cùng một câu hỏi bằng hai
giá trị là cách drift bắt đầu, và không có gì phát hiện ngày chúng lệch nhau. Cùng nguyên tắc
BE-12b tự đặt ra cho chính nó.

`feeder_id` lặp lại trong từng item **dù đã có trên đường dẫn** — để `/segments/{id}/poles` và
`/feeders/{id}/poles` dùng chung đúng một kiểu item, và để một tập kết quả gộp từ nhiều lần gọi vẫn
tự mô tả được.

---

## 4. Q3 — cột không có mạch

**Đề xuất:** `GET /api/v1/assets/feeders/poles?unassigned=true`

**Vì sao cần.** **45 trên 103 cột của bộ mock là `solar_all_in_one`** — chúng không đấu vào mạch
nào, và `feeder_id = NULL` là **câu trả lời đúng**, không phải dữ liệu thiếu. Không có endpoint nào
liệt kê được chúng thì:

- **CV-05** không biết cột nào còn chờ gán;
- không ai phân biệt được *"cột này solar nên không có mạch"* với *"cột này chưa ai gán"* — hai thứ
  trông giống hệt nhau trong DB, và chỉ khác nhau ở `power_source`.

Vì vậy item của endpoint này (và **chỉ** endpoint này) mang thêm **`power_source`**: nó là thứ duy
nhất phân biệt được hai ca trên.

> ⚠️ **Đây là chỗ đề xuất này yếu nhất, và tôi nêu ra thay vì giấu đi.** Đường dẫn
> `/assets/feeders/poles` đọc không xuôi — nó không phải cột *của* feeder nào cả. Hai lựa chọn khác
> đều có nhược điểm riêng: `/assets/poles?feeder_id=none` đụng lại Q1 (gắn vào endpoint BE-12b chưa
> chốt), còn `/assets/poles/unassigned` thì lấn vào không gian tên mà BE-12b đang giữ. **Đây là câu
> tôi mong người duyệt chọn giúp**, không phải câu tôi muốn bảo vệ.

---

## 5. Phân quyền

Theo đúng luật BE-12a, **không phát minh gì mới**:

| | |
|---|---|
| Ba endpoint này (đều là ĐỌC) | **KHÔNG gắn policy nào** |

`SetFallbackPolicy` đã bắt buộc đăng nhập. Gắn `MaintenanceEngineer` lên một GET sẽ **chặn luôn Quản
trị và Cơ quan quản lý** — policy là một vai trò chính xác, không phải một bậc. Phạm vi địa bàn vẫn
do query filter BE-08 canh, và `CommuneFilter.Narrow` trả 403 nêu đúng xã nếu có tham số
`?commune_id=`.

Cột ngoài phạm vi xã thì **không tồn tại** với người gọi, nên `GET /assets/feeders/{id}/poles` với
một feeder ngoài phạm vi là **404**, không phải 403 — đúng Contract mục 7, và đúng tiền lệ
`GET /faults?pole_id=` đã chốt (quyết định A): không biến endpoint thành kênh dò sự tồn tại của tài
sản ở xã khác.

---

## 6. 🔴 Duyệt xong vẫn CHƯA chạy được — O-6 chặn dữ liệu

Đây là phần cần nói thẳng, vì nó quyết định kỳ vọng chứ không quyết định thiết kế.

**Bộ mock không có một tủ điện nào, và cả 103 cột đều `feeder_id = NULL`.** Bảng `feeder` trên DB dev
đang rỗng. `scripts/seed_mock_set.py` ghi cứng `feeder_id = NULL` và tự ghi rõ lý do là O-6.

Nghĩa là dù ba endpoint này được duyệt và hiện thực xong hôm nay:

| Truy vấn | Trả về |
|---|---|
| `GET /assets/feeders/{id}/poles` | **404** — không có feeder nào tồn tại |
| `GET /assets/segments/{id}/poles` | Đúng số cột của tuyến đó (46 / 31 / 26), tất cả `feeder_id: null` |
| `GET /assets/feeders/poles?unassigned=true` | **cả 103 cột** |

**CV-15 vẫn bị chặn sau khi BE-13 xong.** BE-13 giao *khả năng truy vấn*; O-6 giao *dữ liệu để truy
vấn*. Nhập hai thứ làm một sẽ dẫn tới chuyện tuần W4 báo BE-13 xong rồi tuần W5 CV-15 phát hiện
không có gì để gom.

**Đã dựng sẵn khuôn để gỡ:** `mocks/mock-feeders.csv` và `mocks/mock-pole-feeders.csv` (103 dòng,
phần tra cứu điền sẵn, cột `feeder_external_ref` để trống), hướng dẫn ở `mocks/README.md`.
**Chỉ 58 dòng cần điền** — 45 dòng solar để trống là đúng.

> ⚠️ **O-6 như Contract mục 9 đang ghi là thiếu một nửa:** nó chỉ nhắc `mock-pole-feeders.csv`,
> nhưng không gán cột vào tủ điện chưa tồn tại được. Cần `mock-feeders.csv` **trước**. Đề nghị sửa
> câu chữ của O-6 ở v1.6.

---

## 7. Việc phải làm nếu duyệt

1. Ba endpoint ở `AssetsController` + `AssetCrudService`, theo hình dạng §3 và §4.
2. Item type mới — **không** dùng lại DTO của BE-12b, xem §2.
3. Contract mục 5.4 + `luxmap-v1.json` xuất lại + `gen_consolidated_spec.py` chạy lại +
   `npx @redocly/cli lint` (BE-REVIEW-02 ràng buộc 8).
4. Test: phạm vi địa bàn (404 chứ không 403), phân trang, cột solar không lọt vào kết quả của
   feeder, và **cột ở xã khác trên cùng tuyến vẫn ra** — `inter_commune` là hợp lệ.
5. Đóng mục drift đăng ký cho ticket này.

**Chỉ số 3 chạm bề mặt đã publish.** Các mục còn lại nằm trong backend.

---

## 8. Câu hỏi để lại

- **Phân trang mặc định bao nhiêu?** Đề xuất theo mục 0: `page_size` mặc định 50, tối đa 200. Một
  mạch điện thật hiếm khi quá 200 cột, nhưng `GET /assets/segments/{id}/poles` thì có thể —
  `SEG-001` đã 46 cột trên một bộ mock cố tình nhỏ.
- **CV-15 có cần lọc theo `data_source` không?** Mục 5.1 mặc định loại `calibration_rig` khỏi
  `GET /poles`. Ở đây chưa rõ engine muốn gì; **hỏi WP4 trước khi hiện thực** thay vì đoán, vì gộp
  số liệu giữa ba nguồn của Nhánh C là lỗi nghiêm trọng chứ không phải chi tiết trình bày.
- **Có cần endpoint ngược — "mạch nào phủ tuyến X" không?** Chưa ai yêu cầu. Không làm trước khi có
  người hỏi.
