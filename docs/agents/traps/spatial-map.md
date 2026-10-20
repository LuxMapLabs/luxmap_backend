# Không gian và bản đồ

Tra cứu tách từ `CLAUDE.md` (20/10/2026). Đọc trước khi truy vấn khoảng cách / bbox / SRID, endpoint `/map/…`, chi tiết cột, độ phủ sweep. Luật chung cho mọi ticket nằm ở `CLAUDE.md`.

## Bốn quy tắc chốt ở BE-10 — khoảng cách và SRID

**1. `SpatialFunctions.DistanceMeters` là đường tính khoảng cách hợp lệ DUY NHẤT.**

Nó dịch sang `ST_Distance(ST_Transform(a,3405), ST_Transform(b,3405))` bằng
`HasDbFunction().HasTranslation()` — **không có function nào trong DB, không có migration nào**.
Bốn API còn lại bị cấm ở **compile-time** qua `BannedSymbols.txt` (RS0030 = error trong
`.editorconfig`): `Geometry.Distance`, `EF.Functions.Distance`, `EF.Functions.IsWithinDistance`,
`EF.Functions.DistanceKnn`. Trên cột 4326 cả bốn trả **ĐỘ**: cặp cột cách 34.973 m ra `0.00032`,
sai **109.290 lần**, mà kết quả vẫn là `double` dương trông hợp lý.

⚠️ **Lệnh cấm có lỗ.** BannedApiAnalyzers chỉ khớp khi **mọi tham số được truyền tường minh**.
`EF.Functions.Distance(a, b, false)` bị bắt; `EF.Functions.Distance(a, b)` — bỏ trống `useSpheroid` —
**lọt hoàn toàn**, kể cả khi cấm nguyên type bằng `T:`. Đúng dạng người ta hay gõ nhất. Đã bịt bằng
`BannedDistanceApiTests` quét văn bản mã nguồn; đừng xoá test đó vì tưởng analyzer đã lo.

**2. Quy ước HAI TẦNG cho BE-13, BE-14, BE-29 — bắt buộc.**

`ST_Transform` trên cột đã đánh index **VÔ HIỆU HOÁ index đó**: index dựng trên `geom` (4326), còn
predicate lại là một hàm của `geom`. Đo thật trên 2500 cột đã `ANALYZE`:

| Truy vấn | Plan | Cost | Buffers | Execution |
|---|---|---|---|---|
| Chỉ `ST_Distance(ST_Transform(...)) < 500` | `Seq Scan on pole` | 62608.25 | 77 | 1.082 ms |
| Thêm `ST_Intersects(geom, envelope)` phía trước | `Bitmap Index Scan on ix_pole_geom` | 86.34 | 5 | 0.029 ms |

**Lọc thô bằng bbox `ST_Intersects` trên 4326 trước** (đi qua GIST index), **rồi mới tinh chỉnh bằng
khoảng cách 3405** trên tập nhỏ còn lại. Đảo thứ tự là mất index — và ở BE-14 thì đó là ngưỡng 500 ms
của Contract mục 5.4.

⚠️ **Predicate khoảng cách 3405 KHÔNG BAO GIỜ được là điều kiện dẫn dắt của một join.**
Plan A ở trên ước lượng `rows=833` — đúng bằng 2500/3, tức PostgreSQL đã rơi về hằng số selectivity
mặc định cho bất đẳng thức: `ST_Distance(ST_Transform(...))` là hàm của cột, nên **không có thống kê
nào áp được**. Thực tế trả về **1** dòng — sai **833 lần**.

Ở một truy vấn đơn lẻ thì vô hại: cost quá cao khiến planner vẫn ưu tiên index nếu có. Nhưng khi
BE-13/BE-14/BE-29 đặt predicate này vào một `JOIN` hoặc subquery, ước lượng sai 833 lần sẽ chọn nhầm
join strategy (nested loop thay vì hash, hoặc ngược lại) — và **không có gì cảnh báo**: truy vấn vẫn
ra đúng kết quả, chỉ chậm dần theo dữ liệu cho tới lúc không ai nhớ vì sao. Luôn thu hẹp bằng bbox
trước để planner có một `rows` thật để làm việc.

**3. Không có API .NET nào trả về `Geometry` đã transform.**

`ST_Transform` chỉ tồn tại **bên trong cây SQL**. Giá trị duy nhất đi ra tầng .NET là `double` mét.
Không phải chuyện phong cách: toạ độ 3405 rò ra API lệch **226 m** trên bản đồ FE — đủ để đặt cột
sang tuyến khác, vẫn đủ nhỏ để trông "gần đúng" và không ai nghi ngờ. Chi tiết số liệu ở XML doc của
`SpatialConstants.SridVn2000`.

**4. `RoadSegment.LengthM` là giá trị KHAI BÁO, không phải giá trị dẫn xuất.**

Đừng "sửa cho đúng" bằng `ST_Length`. Nó là property đã publish ở Contract mục 2.3, và khoảng cách
3405 là khoảng cách trên **mặt phẳng chiếu** — ngắn hơn trên ellipsoid khoảng **73 ppm** do hệ số tỉ
lệ lưới UTM. Lấy `ST_Length` ghi đè sẽ làm số liệu FE nhảy mà không ai giải thích được vì sao.

## Bốn bẫy chốt ở BE-14 — bản đồ bbox (22/09/2026)

**1. 🔴 `Enum.TryParse` KHÔNG đọc được giá trị enum trên query string.**

Giá trị trên dây là snake_case thường (`calibration_rig`, `field_report`, `node_offline`), còn tên
thành viên .NET là `CalibrationRig`. `Enum.TryParse(ignoreCase: true)` **không biết dấu gạch dưới**,
nên nó **nhận giá trị một từ** (`normal`, `dim`, `out`) và **từ chối mọi giá trị nhiều từ**.

Đây là dạng lỗi ẩn kỹ nhất trong ticket này: bộ lọc thường dùng chạy tốt, chỉ vài giá trị trả 400, và
không ai nghĩ tới việc bộ phân tích enum mới là thủ phạm. Đã gặp thật ở `data_source=calibration_rig`.

Khuôn đúng: so với **tên trên dây**, sinh bằng `JsonNamingPolicy.SnakeCaseLower.ConvertName` — đúng
policy đang serialize chúng ra, nên giá trị client đọc được chính là giá trị nó gửi lại được. Dùng
**`WireEnum.ParseCsv` / `WireEnum.Name`** ở `LuxMap.Shared/Http` (chuyển từ `MapController` ở BE-40,
cùng `BoundingBox`, vì Faults không tham chiếu được Map). Đừng chép bản thứ ba.

**2. 🔴 Test THỨ TỰ không thay được test KẾ HOẠCH, và ngược lại.**

`ST_Intersects(geom, envelope)` trên cột 4326 thô là dạng **duy nhất** đi được `ix_pole_geom`. Bọc bất
kỳ hàm nào quanh cột — `ST_Transform`, `ST_Buffer` — thì **hàng trả về vẫn đúng y hệt**, chỉ mất index.
Kiểm bằng phá hoại: thêm `.Buffer(0)` làm plan rơi về `ix_pole_commune_id`, **không một assert dữ liệu
nào đỏ**.

Vì vậy `MapQueryPlanTests` chạy `EXPLAIN` trên **SQL mà EF thật sự sinh** (`ToQueryString()`), không
phải SQL viết tay. `SpatialIndexTests` của BE-09 vẫn EXPLAIN một câu viết tay kèm chú thích *"what
BE-14 will issue"* — đó là **dự đoán**, và giờ đã có bản đối chứng.

**3. 🔴 Teardown của fixture PHẢI xoá mọi bảng `Restrict` trỏ vào `pole`.**

`fault.pole_id` và `lux_reading.pole_id` đều `Restrict`. `AssetImportFixture` xoá
`fixture → pole → feeder → road_segment` nhưng **thiếu cả hai** — nên một test tạo fault làm lượt xoá
pole gãy, **cả teardown gãy theo**, và cột ở lại mang ID mà sequence sẽ phát lại. Lượt chạy sau chết
vì `pk_pole`, trông như flaky.

Đo được: 217 cột + 20 fault mồ côi tích lại qua vài lượt. Mọi test có ghi fault đều đang rò cột theo
đường này mà không ai thấy.

**4. 🔴 Lùi sequence dùng chung thì PHẢI trả nó về chỗ cũ.**

`PrefixedIdOverflowTests` cố ý `setval` lùi để chạm ngưỡng độ rộng, rồi **để nguyên**. Các lượt chèn
sau trong cùng run leo dần từ chỗ thấp đó và đụng bất cứ hàng nào đang nằm trên đường —
đúng "nhiễm độc ~355 lượt chèn" đã ghi ở mục bộ test tài sản.

Chọn dải trống **không đủ**: `FreeFourDigitDecadeAsync` kiểm thập niên nó ghi vào, **không kiểm cả
đoạn phía trên** mà sequence sẽ đi qua. Nay mỗi lượt lùi nằm trong `try/finally` trả sequence về giá
trị đọc được trước đó. Sau khi bịt, bộ test **xanh 3 lượt liên tiếp ngay cả khi 217 cột mồ côi vẫn
còn** — tức đây mới là nguyên nhân, không phải rác dữ liệu.

⚠️ **Đừng thêm `setval` vào fixture để "sàn" sequence theo ID lớn nhất.** Tôi đã thử: nó đẩy khối
2500 cột của `AssetSchemaFixture` vào đúng dải `9999/10000` mà `PrefixedIdOverflowTests` giữ, và ngưỡng
đó **không dời đi đâu được**. Trả sequence về chỗ cũ là đường đúng.

## BE-20 — chi tiết cột (04/10/2026)

**Một schemaId trong Swagger chỉ ứng một kiểu .NET — qua MỌI module.** `PoleDetail` đã là kiểu của BE-12b
(`Assets.Crud`), nên kiểu cùng tên ở `Map` làm `swagger tofile` ném `Can't use schemaId` — mà `dotnet build` và toàn bộ test
vẫn xanh, chỉ spec không xuất được. Kiểu của `/map/poles/{id}` mang tiền tố `PoleMap*`. Đặt tên response mới thì grep
tên đó trong cả `src/` trước.

**Cái gì được LIỆT KÊ phải mở được.** Danh sách frame của chi tiết cột và `GET /frames/{id}/thumbnail` dùng **cùng một**
`SurveyMediaAccess` (xã của cả run + người được giao của phiếu). Viết lại điều kiện ở chỗ thứ hai thì hai nơi sẽ lệch nhau và
client nhận URL mà mở ra 404 — hoặc tệ hơn, thấy frame của xã khác.

## BE-17 — độ phủ sweep (13/10/2026, drift SC-1…SC-6)

- **Độ phủ đọc từ ẢNH CHỤP GIS của run** (`survey_processing_run.gis_snapshot`: `segments`, `communes`, `poles` kèm `segment_id`), không từ tài
  sản hôm nay — câu trả lời phải là cái run đã thấy, như `results` / `UnobservedPoles`. Chỉ `length_m` (khai báo) và hình học lấy từ
  `road_segment` hiện tại.
- 🔴 **Phân số của `survey_pass` đo trên 3405** (`ST_LineLocatePoint` × `ST_Length` của tuyến chiếu). Cắt khoảng trống phải cắt **trên cùng
  phép chiếu đó** rồi chuyển về 4326 **trong SQL**: `ST_Transform(ST_LineSubstring(ST_Transform(geom,3405), f, t), 4326)`. Cắt thẳng trên
  4326 bằng phân số 3405 lệch trên tuyến cong (hai trục lệch tỉ lệ ~cos(vĩ độ)); để toạ độ 3405 rời SQL là phạm BE-10 quy tắc 3.
- **`SqlQuery<T>` đi qua quy ước snake_case**: alias cột phải là `geo_json`, `ordinal` — alias `"GeoJson"` ném `required column 'geo_json'
  was not present` lúc chạy, build vẫn xanh.
- **Quyền độ phủ = luật ảnh khảo sát cho một run:** `CoversWholeRun` (xã của cột) **và** mọi xã trong `gis_snapshot.communes` (xã của
  phiếu). Độ phủ vẽ tuyến; tuyến liên xã có thể thuộc xã mà run không có cột nào.
- `SweepResponse` không còn `sealed` — chỉ để `SweepDetailResponse` thêm `latest_run`; danh sách vẫn trả kiểu gốc, không mang run.
- ⚠️ **Object nullable xuất ra spec là `$ref` TRẦN, không `nullable`** (`latest_run`, `CoverageGap.geometry`, và sẵn có từ trước:
  `PoleMapDetail.fixture`). Đó là hạn chế chung của tầng sinh Swagger, ghi ở "Vấn đề đang mở" của tracking — sửa một lần cho mọi schema,
  đừng vá riêng từng endpoint.
- Khoảng trống **đúng 1 m được giữ**, chỉ khoảng **ngắn hơn** bị bỏ (SC-4); so sánh có dung sai `1e-9` vì phân số × chiều dài hiếm khi rơi
  đúng 1 m.
- Test ghi run / quan sát tay: `classified_as = unknown` là cách duy nhất không cần frame (`ck_pole_observation_classification`).
- 🔴 **Dữ liệu khảo sát dựng trong test KHÔNG xoá được** (run / lượt / quan sát bất biến), nên phiếu và sweep của nó **ở lại** — đừng gắn
  chúng vào tài khoản của fixture: `work_order.assigned_to` / `created_by` giữ tài khoản bằng `Restrict`, teardown của
  `AssetDatabaseCollection` gãy (`23503 fk_work_order_app_user_assigned_to`) dù mọi test xanh. Tạo tài khoản riêng **một lần cho cả class**
  (`InitializeAsync` của `IAsyncLifetime` chạy **mỗi test** — khuôn seed tĩnh có khoá ở `SweepCoverageTests`), tên xã kèm GUID
  (`ix_administrative_unit_name` unique).
- 🔴 **Đọc mã thoát của `dotnet test`, không chỉ dòng `Passed!`.** Lỗi teardown collection in `[Test Collection Cleanup Failure …]` và trả
  mã 1, trong khi dòng tổng vẫn ghi `Failed: 0` — CI đỏ ở #140 vì đúng chỗ này, lượt chạy local bỏ sót vì chỉ lọc dòng tổng.
