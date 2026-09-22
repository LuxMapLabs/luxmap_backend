# BE-12b — hình dạng response khi ĐỌC tài sản (đề xuất, chờ duyệt)

**Trạng thái:** đề xuất. **Chưa có dòng code nào theo hình dạng này**, và `GET /api/v1/assets/…`
vẫn trả `PagedResult<string>` như Contract mục 5.3 đang ghi.

| | |
|---|---|
| **Người cần duyệt** | **Thịnh / Ngọc** (CODEOWNERS của `api-contract-v1.1.md` và `luxmap-v1.json`) |
| **Duyệt cùng lúc với** | **drift 44** — năm endpoint `PUT`/`DELETE` của BE-12 |
| **Nếu duyệt** | Contract lên **v1.6**: mục 5.3 viết lại phần đọc, thêm mục 5.3.1 cho năm endpoint ghi |
| **Nếu không duyệt** | Không có gì phải hoàn tác — `PagedResult<string>` đứng nguyên |

**Vì sao gộp một lần duyệt.** Năm endpoint ghi và hình dạng đọc là **cùng một màn hình của WP5**:
danh sách kiểm kê tài sản, sửa một dòng, xoá một dòng. Tách ra hai lần duyệt là bắt Thịnh/Ngọc dựng
lại cùng một ngữ cảnh hai lần, và lần thứ hai sẽ thiếu mất lý do vì sao lần thứ nhất quyết như vậy.

---

## 0. Ba câu hỏi thật sự cần chữ ký

Phần còn lại của tài liệu này là chi tiết. **Ba câu dưới đây mới là thứ không tự quyết được**, vì cả
ba đều đụng đúng một dòng Contract đang cấm:

> Mục 5.1: *"`data_source`, `external_ref`, `feeder_id` **không** emit."*

Dòng đó viết cho **endpoint bản đồ**. Câu hỏi là nó có áp cho **endpoint kiểm kê** không.

| # | Câu hỏi | Đề xuất | Hệ quả nếu trả lời ngược |
|---|---|---|---|
| **Q1** | `/assets/…` có emit `external_ref` không? | **CÓ** | Màn kiểm kê không hiển thị được mã kiểm kê của đơn vị — xem §1 |
| **Q2** | `/assets/…` có emit `data_source` không? | **CÓ** | Không phân biệt được cột hiệu chuẩn với cột thật trên màn quản trị — xem §2 |
| **Q3** | `/assets/poles/{id}` có emit `feeder_id` không? | **CÓ** | BE-13 và màn sửa mạch điện không đọc được giá trị nó sắp ghi — xem §3 |

Ba câu độc lập nhau: duyệt được câu nào áp câu đó.

---

## 1. Q1 — `external_ref`

**Contract đang ghi (mục 5.3, cuối):** *"`external_ref` **không** emit ra API."*

**Vì sao lúc đó đúng.** Luật này ra đời cùng BE-12a, khi bề mặt đọc duy nhất là **bản đồ**. Bộ mock
FO-26 là nguồn chuẩn cho hình dạng `properties` và nó không có trường này, nên emit ra là làm code
FE lệch khỏi bộ mock họ đang dựng theo.

**Vì sao giờ thành vấn đề.** `external_ref` **là** mã kiểm kê của đơn vị quản lý — khoá tự nhiên duy
nhất của lược đồ, và là thứ người nhập liệu dùng để đối chiếu file với hệ thống. Một màn *quản lý
kiểm kê* không hiện được mã kiểm kê thì người dùng không có cách nào biết dòng `POLE-0047` trên màn
hình ứng với dòng nào trong file Excel của họ. Đó cũng chính là cột duy nhất họ **gõ vào** lúc import.

**Đề xuất:** emit trên `/assets/…`, **giữ nguyên lệnh cấm** trên `/poles` và `/segments` (mục 5.1,
5.2). Hai bề mặt, hai việc khác nhau — đúng lý do nhóm `/assets/` được tách ra ngay từ đầu.

⚠️ Nếu **không** duyệt: màn kiểm kê vẫn chạy được nhưng người dùng phải tra ngược qua file import.
Tôi cho là đủ tệ để đáng hỏi, không đủ tệ để chặn.

---

## 2. Q2 — `data_source`

**Contract đang ghi (mục 5.1):** không emit ra `properties` của bản đồ.
**`CLAUDE.md`** nói rõ hơn: ba cột này *"LƯU và LỌC được, nhưng KHÔNG emit ra `properties`"*.

**Vì sao giờ thành vấn đề.** Nhánh C có ba nguồn dữ liệu và `CLAUDE.md` gọi việc gộp chúng là *"lỗi
nghiêm trọng, không phải chi tiết trình bày"*. Màn quản trị là chỗ **người ta sửa dữ liệu**. Sửa một
cột mà không nhìn thấy nó thuộc nguồn nào là đúng điều kiện để vô tình trộn bộ hiệu chuẩn FO-07 vào
số liệu thật — và drift 44 vừa cho `data_source` **sửa được** qua `PUT`, nên nó đang ở trạng thái tệ
nhất có thể: ghi được mà không đọc được.

**Đề xuất:** emit trên `/assets/…`. Và nếu Q2 bị từ chối thì **phải bỏ `data_source` khỏi ba request
`PUT`** của drift 44 — ghi được một trường mà không đọc lại được là thiết kế không bảo vệ được.

> 🔴 **Hai câu này buộc phải trả lời cùng nhau.** Q2 = không, drift 44 điểm 3 = không. Đừng duyệt
> lệch nhau.

---

## 3. Q3 — `feeder_id`

**Contract đang ghi (mục 5.1):** không emit trên bản đồ.

**Vì sao đúng ở bản đồ:** mạch điện không phải thứ vẽ lên bản đồ cột đèn.

**Vì sao sai ở kiểm kê:** đã có `PUT /assets/poles/{id}/feeder` để **ghi** giá trị này, và BE-13 sẽ
dùng nó để sửa topology hàng loạt. Một endpoint ghi mà không có endpoint đọc tương ứng nghĩa là
client không bao giờ biết được nó đang ghi đè lên cái gì.

**Đề xuất:** emit trên `/assets/poles/{id}` và trong dòng danh sách.

---

## 4. Hình dạng đề xuất

### 4.1 Nguyên tắc: KHÔNG lặp lại mục 5.1

`GET /poles/{id}` (mục 5.1, BE-14) là màn **vận hành**: tình trạng, chuỗi độ sáng, runtime, sự cố
đang mở, ảnh gần đây. `GET /assets/poles/{id}` là màn **kiểm kê**: danh tính, vị trí, phân loại,
nguồn gốc, thiết bị đang lắp, dấu thời gian.

**Không trường nào của mục 5.1 xuất hiện ở đây trừ khi nó là thuộc tính kiểm kê.** Cụ thể là
**không** có `fixture_status`, `status_confidence`, `open_fault_count`, `last_seen_at`,
`last_sweep_id`, `luminance_*`, `runtime_*`, `recent_frames`. Ai cần những thứ đó gọi mục 5.1.

Lý do không phải tiết kiệm byte: hai endpoint cùng trả `fixture_status` là **hai nguồn sự thật cho
một câu hỏi**, và tới lúc chúng lệch nhau thì không có gì phát hiện — đúng lớp lỗi mà `external_ref`
mồ côi và `LPAD` cắt ID đã gây ra.

### 4.2 Dòng trong danh sách

`GET /api/v1/assets/{segments|feeders|poles}` → `PagedResult<T>`, phong bì phân trang giữ nguyên
mục 1.3.

```jsonc
// poles
{ "pole_id": "POLE-0047", "external_ref": "TB-2024-047", "segment_id": "SEG-003",
  "feeder_id": null, "commune_id": "COM-001", "data_source": "public_imagery",
  "near_sensitive_poi": true, "location": { "lat": 10.972447, "lng": 106.502058 },
  "has_active_fixture": true, "updated_at": "2026-09-18T04:12:07Z" }

// segments
{ "segment_id": "SEG-003", "external_ref": "DX-03", "segment_name": "Đường liên thôn 3",
  "road_class": "inter_village", "length_m": 1420, "commune_id": "COM-001",
  "data_source": "public_imagery", "pole_count": 31, "updated_at": "..." }

// feeders
{ "feeder_id": "FDR-001", "external_ref": "TĐ-01", "feeder_name": "Tủ điện chợ",
  "commune_id": "COM-001", "has_geometry": false, "pole_count": 12, "updated_at": "..." }
```

**`location` chứ không phải GeoJSON.** Đây là danh sách phân trang, không phải lớp bản đồ — cùng lý
do `GET /faults` là JSON phân trang chứ không phải `FeatureCollection` (mục 5.4). Khuôn `{lat,lng}`
đã publish ở đó rồi, dùng lại chứ không phát minh thêm.

**`has_geometry` chứ không phải `geom_wkt`.** Nhánh C không khảo sát tuyến cáp nên đa số feeder
không có hình học; trả cả WKT vào mỗi dòng danh sách là tốn băng thông cho thứ gần như luôn rỗng.

⚠️ **Đây là breaking change so với `PagedResult<string>` đang chạy.** Hiện **chưa ai bị ảnh hưởng** —
WP5 chưa dựng màn quản trị tài sản, và trường này sinh ra chính là chỗ giữ chỗ chờ quyết định. Nhưng
sau khi WP5 bắt đầu thì không còn đổi miễn phí được nữa.

### 4.3 Một tài sản

`GET /api/v1/assets/poles/{id}` — thêm so với dòng danh sách:

```jsonc
{ "...": "mọi trường của dòng danh sách",
  "geom_wkt": "POINT(106.502058 10.972447)",
  "segment_name": "Đường liên thôn 3",
  "active_fixture": {
    "fixture_id": "FIX-0047", "fixture_type": "led_road_lamp", "power_source": "grid",
    "lamp_watt": 60, "install_date": "2024-03-18", "warranty_expiry": "2027-03-18"
  },
  "created_at": "...", "updated_at": "..." }
```

`active_fixture` là `null` khi cột chưa lắp bóng. **Đúng một bóng** — BE-REVIEW-02 ràng buộc 3 đã
làm nó thành duy nhất, nên không cần quy tắc tổng hợp.

**`fixture_history[]` — đề xuất KHÔNG đưa vào v1.6.** Lịch sử thiết bị là thật và có giá trị, nhưng
nó là màn hình riêng, và nhét một mảng không giới hạn vào response chi tiết là thứ sau này phải phân
trang ngược. Để lại thành open item.

---

## 5. Phân quyền — không đổi

Đọc **không gắn policy nào**; `SetFallbackPolicy` đã bắt buộc đăng nhập. Gắn `MaintenanceEngineer`
vào GET sẽ chặn luôn Quản trị và Cơ quan quản lý — policy là **một vai trò chính xác**, không phải
một bậc (BE-12a điểm 4, đã canh bằng test). Phạm vi địa bàn do query filter lo, `?commune_id=` thu
hẹp trong phạm vi qua `CommuneFilter.Narrow`.

---

## 6. Việc phải làm nếu duyệt

1. Contract lên **v1.6**: viết lại phần đọc của mục 5.3, thêm 5.3.1 cho drift 44, ghi changelog mục 10.
2. Bỏ dòng cấm emit ở mục 5.3 → nêu rõ phạm vi: cấm ở 5.1/5.2, cho phép ở 5.3.
3. Hiện thực: DTO + `AssetCrudService`, giữ `PagedResult`, không đụng đường ghi.
4. Xuất lại `luxmap-v1.json` → sinh lại `luxmap-v1.5.json` **đổi tên theo version** → lint.
   ⚠️ Đổi tên file thì phải sửa **cùng lúc**: `README.md`, `CLAUDE.md` (mục Nguồn sự thật và ràng
   buộc BE-REVIEW-02 số 8), `.ai/context/commands.md`. Đã vấp đúng chỗ này ở lần v1.4 → v1.5.
5. Đóng mục drift 44 và bỏ mọi ghi chú *"chỗ giữ chỗ tới BE-12b"* trong code.

## 7. Câu hỏi để lại

- **Q4.** `fixture_history[]` — endpoint riêng hay mảng lồng? (đề xuất: để sau v1.6)
- **Q5.** Danh sách có cần lọc theo `data_source` và `external_ref` không, hay chỉ `commune_id`?
  Phụ thuộc Q1/Q2 — lọc theo trường không emit được là hợp lệ nhưng khó giải thích trên UI.
