# Tài sản: cột, bóng, tuyến, feeder, tủ điện

Tra cứu tách từ `CLAUDE.md` (20/10/2026). Đọc trước khi CRUD / import tài sản, topology, tủ điện, khung xã, dời cột, bảo hành, bộ mock. Luật chung cho mọi ticket nằm ở `CLAUDE.md`.

## Sáu quy tắc chốt ở BE-12a — nhập và sửa tài sản

**1. Nhóm endpoint là `/api/v1/assets/…`, KHÔNG phải `/poles`.**

Bề mặt bản đồ là `/api/v1/map/poles` (MAP-1); xem `CLAUDE.md`, mục API đã đặc tả. Danh sách kiểm kê dùng `/assets/…`.

```
GET/POST      /api/v1/assets/{segments|cabinets|feeders|poles}    POST /api/v1/assets/fixtures
PUT/DELETE    /api/v1/assets/{segments|cabinets|feeders|poles}/{id}
PUT           /api/v1/assets/poles/{id}/feeder           PUT  /api/v1/assets/fixtures/{id}/removal
POST          /api/v1/assets/import/{segments|cabinets|feeders|poles|fixtures}
```

**Fixture KHÔNG có DELETE, và cũng không có PUT thay thế** — ngừng dùng thiết bị là việc của
`fixture.removed_date`, vì đó là sự kiện có thật, còn một dòng gõ nhầm thì không. Bốn loại còn lại xoá
được và **khoá ngoại quyết định**, không kiểm trong code: `fault` / `lux_reading` giữ pole,
`pole` / `fault` / `fault_cluster` giữ segment, `pole.feeder_id` giữ feeder — tất cả `Restrict`, vi
phạm là 409 `ASSET_IN_USE` kèm tên constraint trong `details` (kể cả khi ràng buộc vấp ở bóng của
cột). Drift 43 (18/09/2026) mở đường cho pole; **drift 44 (20/09/2026)** cho năm endpoint còn lại.

> ⚠️ **`PUT` ở đây là THAY THẾ TOÀN PHẦN, không phải patch — và nó cắn.** Body thiếu `feeder_id` sẽ
> **XOÁ mạch điện của cột**, âm thầm. Ai chỉ muốn đổi mạch thì dùng `PUT /assets/poles/{id}/feeder`,
> endpoint đó phân biệt được "không gửi" với "gửi null" (`SetPoleFeederRequest`). Đã ghim bằng
> `Replacing_a_pole_without_a_feeder_id_clears_its_circuit` — test đó **ghi lại một quyết định**,
> đừng "sửa" cho field dính lại.
>
> **`commune_id` KHÔNG nằm trong request update tài sản.** Chuyển tài sản sang xã khác không phải sửa mà
> là chuyển giao: đổi luôn ai nhìn thấy dòng đó, và phải kiểm scope ở **cả hai** phía. Riêng với
> `feeder` thì còn nặng hơn — `RequireFeederInCommuneAsync` chỉ chạy khi ghi **POLE**, không bao giờ
> chạy khi feeder đổi xã, nên cho sửa sẽ kéo tủ điện ra khỏi các cột đang đấu vào nó và **mọi cặp đó
> lặng lẽ thành liên-xã mà không còn lượt ghi nào bắt được**.

**2. `external_ref` — khoá tự nhiên DUY NHẤT của lược đồ, trên BỐN bảng.**

`road_segment`, `electrical_cabinet`, `feeder`, `pole`. `text NULL` trong DB, **BẮT BUỘC trong file import**, unique là
**partial index** `(commune_id, external_ref) WHERE external_ref IS NOT NULL`. Inventory API emit `external_ref`;
**không emit ra properties bản đồ** (`AssetContracts`, `AssetConfigurations`).

`fixture` **không có** — một cột mang nhiều bóng qua thời gian nên không mã nào chỉ đúng một lần lắp
đặt. Vì vậy **nhập bóng là INSERT-ONLY**: cột đã có bóng → lỗi theo dòng. Upsert bừa ở đây sẽ nhân
đôi lịch sử thiết bị mà không ai phát hiện.

Template tham chiếu bằng `segment_external_ref` / `cabinet_external_ref` / `feeder_external_ref` / `pole_external_ref`, **không
phải `SEG-001` / `FDR-001`** — mã đó do DB sinh lúc INSERT, người soạn file không biết trước.

Quy tắc partial index: xem `docs/agents/traps/migrations.md`, mục PARTIAL INDEX.

**3. Ngữ nghĩa nhập: kiểm TOÀN BỘ trước, ghi tập hợp lệ trong MỘT transaction, trả 200.**

Dòng vi phạm FK / CHECK / enum / phạm vi xã **phải bị bắt ở bước kiểm**, không được lọt xuống bước
ghi. Lỗi ở bước ghi **rollback cả mẻ, trả 500**, không đổ lỗi cho dòng nào.

> 🔴 **HẠN CHẾ ĐÃ BIẾT — không phải "thiết kế". Upsert KHÔNG nguyên tử.**
>
> Bước đọc-trước và bước ghi là hai câu lệnh riêng. **Hai request nạp cùng một file đồng thời**: cả
> hai đọc không thấy gì, cả hai `Add`, và cái thua đụng `ux_*_commune_external_ref` ngay trong
> `SaveChanges`. Đó **đúng là** một lỗi mức-dòng lọt xuống bước ghi, và nó nổi lên dưới dạng
> `DbUpdateException` thô → 500, **không phải** kết quả validate có số dòng.
>
> **Chưa sửa ở BE-12a** — một transaction, thua thì không ghi gì, dữ liệu không hỏng, và hai người
> quản lý nạp cùng file trong cùng một giây chưa đáng thiết kế riêng. Ghi lại vì **BE-12b kế thừa đúng
> đường ghi này**, và vì bản sửa đúng là **upsert thật (`ON CONFLICT DO UPDATE`)**, không phải thêm
> một lượt kiểm nữa — thêm kiểm chỉ thu hẹp cửa sổ chứ không đóng được.

Kết quả: `{inserted, updated, unchanged, failed, total_errors, truncated, rows[], total_warnings, warnings[]}` (`ImportResult`).

- **200 chứ không phải 4xx**, vì các dòng hợp lệ đã ghi thật; bọc trong `{error:…}` là nói sai. Tiền
  lệ: `POST /lux-readings` trùng `client_op_id` trả 200 (Contract mục 5.8). **Không dùng 207** — nó
  không có trong Contract và không có trong repo.
- **`rows[]` là MẢNG, không phải dictionary khoá-là-số-dòng.** Dictionary không cam kết thứ tự, và
  khoá số dạng chuỗi thì `"10"` đứng trước `"9"` — danh sách người ta đọc sẽ nhảy lung tung.
- Cắt ở **100** phần tử, `total_errors` vẫn là số thật. Một file sai delimiter làm hỏng mọi dòng;
  không cắt thì body trả về lớn hơn cả file gửi lên.

**Mỗi request nạp ĐÚNG MỘT loại file.** Đó là thứ làm cho tham chiếu an toàn: tuyến của một cột đã
được commit ở request trước nên đã có `SEG-001` thật. Thứ tự: **segments → cabinets → feeders → poles → fixtures** (`ImportKindExtensions.LoadOrder`). Nạp cả năm loại trong một transaction sẽ phải
phân giải tham chiếu tới hàng chưa có ID. Thứ tự tự thực thi: nạp cột trước tuyến thì **mọi** dòng
báo `segment_external_ref` không khớp gì cả.

**4. Phân quyền — capability, mỗi capability là một DANH SÁCH vai trò chính xác, KHÔNG phải một bậc.**

> Từ Contract v1.7 (D-R5, 25/09/2026). Tới v1.6 mỗi policy là **một** vai trò và GET để trần; ghi
> tài sản là của Quản trị. Nay:

| | |
|---|---|
| POST / PUT / DELETE / import | `[Authorize(Policy = LuxMapPolicies.ManageAssets)]` — **chỉ Quản lý** |
| GET | `[Authorize(Policy = LuxMapPolicies.ReadNetwork)]` — cả bốn vai trò |

Ma trận ở **`LuxMapPolicies.Matrix`**, nguồn DUY NHẤT; `AuthorizationSetup` đăng ký policy bằng vòng lặp
trên nó, `RequireClaim(role, <danh sách>)` — OR giữa các giá trị, **không** thứ bậc. Vai trò được vào vì
được **nêu tên**. Quản trị hệ thống **không** ghi tài sản nữa (D-R12): nó đọc qua `*`, nhưng `*` làm
guard `SaveChanges` cho nó qua hết — nên **chỉ policy** đứng giữa nó và bảng tài sản.

Luật capability, fallback, capability rỗng và test literal: xem `CLAUDE.md`, mục Vai trò và phân quyền.
`CapabilityPolicyCoverageTests` có ngoại lệ máy đặt tên trong `MachineEndpoints`; `ControlLighting` và `ManageUsers`
đã có endpoint thật. Giữ `RoleCapabilityMatrixTests` và `CapabilityMatrixTests`, không cố định số capability trong tài liệu.

`LuxMapPolicies` nằm ở **`LuxMap.Shared`** từ BE-12a — chuyển từ
`LuxMap.Api` sang **`LuxMap.Shared`**: host tham chiếu module chứ không ngược lại, nên controller
trong module không thấy được hằng khai ở host.

> ⚠️ **Đây là quyết định KIẾN TRÚC, chưa được chốt ở cấp nhóm — đang chờ FW-00.**
> Hệ quả: khái niệm authorization rò vào `LuxMap.Shared`, mà `LuxMap.Persistence` và các assembly
> không-API cũng tham chiếu. Chấp nhận được **nếu** coi `Shared` là nơi chứa hằng liên-tầng — đúng
> vai trò nó đang giữ cho `ErrorCodes` và `PrefixedIds`. Nếu không chấp nhận thì đường đúng là **mỗi
> module tự khai tên policy của mình, host đăng ký khớp** — đổi được sau, chỉ là đổi ở nhiều chỗ
> hơn. Chốt ở FW-00 rồi hãy sửa; đừng để một ticket import quyết thay.

Ma trận vai trò nay nằm ở Contract **§2** (v1.7); drift 31 và D-14 là lịch sử.

**5. `CommuneFilter.Narrow` giờ có call site thật.**

Trước BE-12a nó không được gọi ở đâu trong `src/`. Nay gọi ở hai chỗ: tham số `?commune_id=` của
danh sách, và `commune_id` trong body khi tạo tài sản. Nó trả **403 nêu đúng xã bị từ chối**; query
filter một mình chỉ cho ra 200 rỗng, người dùng không hiểu vì sao.

⚠️ **Guard `SaveChanges` KHÔNG bao giờ nổ trên đường HTTP của BE-12a**, vì mọi lối ghi đã kiểm phạm
vi ở entry point trước. Đó là phân tầng đúng — nhưng nghĩa là **không thể** viết test HTTP cho guard
ở ticket này; test guard đi thẳng qua `DbContext`
(`The_write_guard_still_refuses_a_pole_for_a_foreign_commune_even_with_no_entry_point_check`).

**Trong import thì xã ngoài phạm vi là lỗi THEO DÒNG, không phải 403 cả request** — file là một mẻ,
một dòng sai không được vứt 499 dòng còn lại.

**6. `commune_id` lấy từ đâu — khác nhau theo bảng, và có lý do.**

| Bảng | Nguồn |
|---|---|
| `road_segment`, `electrical_cabinet`, `feeder`, `pole` | **BODY / file**, kiểm bằng `Narrow` |
| `fixture` | **Chép từ pole.** Client gửi cũng không đọc |

`fixture` chép xã từ pole ở server; luật feeder cùng xã và ngoại lệ tuyến liên xã: xem mục O-7.

**Parser CSV tự viết**, ở `LuxMap.Shared/Csv/` (144 dòng code, không thêm package). Xử lý BOM UTF-8,
CRLF, ô bọc nháy kép chứa dấu phẩy, và tự dò delimiter `,` / `;` bằng cách **chỉ đếm ký tự ngoài
nháy** — hai dấu phẩy trong `LINESTRING(...)` không được phép thắng phiếu. Đặt ở Shared vì nó không
kéo theo dependency nào, nên test chạy trong assembly **không cần DB và không cần Docker**.

**GeoJSON parse tay bằng `System.Text.Json`** cho `Point` và `LineString`: NTS lõi **không có
GeoJSON reader** (namespace `IO` chỉ có GML2, GML3, KML), và thêm `NetTopologySuite.IO.GeoJSON4STJ`
là thêm package.

> ⚠️ **`ImportGeoJsonAsync` phải là `async`, không được trả thẳng inner Task.** `JsonDocument` giữ
> bộ nhớ pooled mà mọi `JsonElement` trỏ vào; trả Task từ hàm không-async sẽ dispose document **trước
> khi** import đọc xong hàng. Đã gặp thật: 500 `INTERNAL_ERROR` khi nạp bộ mock.

> ⚠️ **`WKTReader` trả SRID 0, không phải 4326.** WKT không mang hệ toạ độ nên reader không có gì để
> đọc. Phải gán SRID **tường minh** trên mọi geometry rời `AssetGeometry`.

**Giới hạn upload 10 MB**, nhận qua `IFormFile`. Mặc định của framework không phải thứ ai cũng nhớ:
Kestrel `MaxRequestBodySize` = **30.000.000 byte (~28,6 MB)**, **thấp hơn** con số 128 MB của
`FormOptions.MultipartBodyLengthLimit` mà người ta hay trích; và form **value** bị chặn ở 4 MB, nên
gửi GeoJSON dưới dạng field sẽ vỡ ở một ngưỡng chẳng liên quan. Repo trước đó chưa cấu hình cái nào.

## Tám ràng buộc chốt ở BE-REVIEW-02 (18/09/2026)

**1. Mạch điện cùng xã, tuyến liên xã được phép: xem mục O-7 (FK ghép + lớp app trả 409 `CROSS_COMMUNE_REFERENCE`).**

**2. `DateTime` trên query string KHÔNG đi qua `UtcDateTimeConverter`.** Converter đó chỉ áp cho JSON.
Query binder trả `Kind=Unspecified` khi thiếu `Z`, và `.ToUniversalTime()` trên nó dịch theo múi giờ
máy chủ (đo được 7 giờ trên máy Asia/Saigon). Chuẩn hoá bằng `UtcNormalization.ToUtc` — Unspecified
= UTC, đúng Contract. Test `A_from_or_to_bound_without_a_Z_suffix…` chỉ đỏ trên máy không phải UTC,
đừng xoá vì thấy nó "luôn xanh" trên CI.

   ⚠️ **Hình dạng cũng phải chặn, không chỉ múi giờ.** Binder của framework đọc ngày theo văn hoá bất biến (kiểu Mỹ), nên
   `06/10/2026` thành **10/06**, trả 200, không lỗi (gặp ở BE-25 `night_of`; có sẵn ở `scheduled_from/to`, lux `from/to`).
   `IsoDateQueryModelBinderProvider` (`LuxMap.Api/Http`) chặn mọi `DateOnly`/`DateTime` trên query string không đúng ISO → 400,
   rồi giao lại binder cũ nên nghĩa không đổi. Tham số ngày mới **không cần làm gì** — chỉ đừng nhận ngày dưới dạng `string`
   rồi tự `DateTime.Parse`, như vậy là đi vòng qua chốt chặn. Canh bằng `A_bound_that_is_not_iso_8601_is_a_400_never_a_guess`.

**3. Một cột có tối đa MỘT bóng đang dùng.** `ux_fixture_pole_id_active` là UNIQUE partial
(`pole_id WHERE removed_date IS NULL`). Thay bóng = ngừng dùng bóng cũ rồi ghi bóng mới; bóng có
`removed_date` là lịch sử, không chặn và không bị chặn. CRUD trả 409 `POLE_HAS_ACTIVE_FIXTURE`, import
báo theo dòng. BE-14 flatten từ **bóng đang dùng** — nay là duy nhất, không cần quy tắc tổng hợp.

**4. `removed_date >= install_date` và ngừng dùng đúng một lần.** CHECK
`ck_fixture_removed_after_install` ở DB; API trả 400 `VALIDATION_FAILED` nêu cả hai ngày; PUT lần hai
là 400 — `removed_date` là lịch sử thiết bị, không ghi đè.

**5. `pole_current_status.status_confidence` đã có CHECK hữu hạn 0..1** (`ck_pole_current_status_confidence_range`,
đóng drift 24). BE-15/BE-17 ghi vào bảng này phải qua nó; NaN/Infinity/42.5 giờ là 500 chứ không âm
thầm vào — hãy validate ở API trước cho ra 400.

**6. 403 có HAI mã.** `ROLE_FORBIDDEN` khi policy vai trò từ chối (`ForbiddenCodeResultHandler` ghi lý do
lên `HttpContext.Items`, trang status-code đọc lại); `COMMUNE_FORBIDDEN` chỉ cho địa bàn và cho claim
`["*"]` lệch vai trò. Đừng ném `COMMUNE_FORBIDDEN` cho việc không phải địa bàn.

**7b. Nạp bộ mock: dùng `scripts/seed_mock_set.py`, KHÔNG dùng endpoint import.**
Endpoint import nạp đúng dữ liệu nhưng **không giữ được ID của mock**: EF Core không bảo toàn thứ tự
`Add` khi database sinh khoá. FE tham chiếu ID mock nên script phải INSERT ID tường minh rồi `setval`
sequence trên vùng đã dùng (D-6, Contract §1.2). Canh kỳ vọng thứ tự bằng `PrefixedIdOrderingTests`, không dựa `AddRange`.

**7. Sequence dùng chung trong một lượt test:** xem `docs/agents/traps/testing.md`, mục bộ test tài sản; không chạy trên DB dev.

**8. Spec hợp nhất `docs/openapi/luxmap-v1.5.json` là file SINH.** Sinh bằng
`python3 docs/openapi/tools/gen_consolidated_spec.py` từ `luxmap-v1.json` (xuất từ code) — chạy lại
**sau mỗi lần** xuất `luxmap-v1.json`, rồi `npx @redocly/cli lint`. Không sửa tay cả hai.

## Ba bẫy chốt ở O-7 — FK ghép `(feeder_id, commune_id)` (21/09/2026)

`pole` trỏ tới `feeder` bằng **cả hai** cột, nên "mạch điện của cột nằm trong xã của cột" là **luật
của lược đồ**, không còn là một lượt gọi hàm phải nhớ. Migration `FeederCommuneCompositeFk`. FK đơn
cột `fk_pole_feeder_feeder_id` **bị thay thế**, không phải thêm chồng — hai FK chồng nhau sẽ làm
`details.constraint` của `409 ASSET_IN_USE` thành không đoán được.

**1. 🔴 `MATCH SIMPLE` là thứ giữ cho cột-không-có-mạch hợp lệ. ĐỪNG "siết" sang `MATCH FULL`.**

Luật FK ghép nullable: xem `docs/agents/traps/survey.md`, mục BE-15 P2c. Ở đây `feeder_id` null, `commune_id` không null vẫn hợp lệ.

⚠️ **Lý do ban đầu của quy tắc này đã đổi, quy tắc thì không.** Trước v1.6 nó tồn tại vì cột
`solar_all_in_one` không đấu vào mạch nào; đèn solar nay hết, nên **mọi cột rồi sẽ có feeder**.
Nhưng `feeder_id` vẫn nullable và vẫn phải nullable: cột mới tạo chưa gán mạch, và `PUT` thay thế
toàn phần vẫn xoá mạch được. `MATCH FULL` vẫn sẽ từ chối đúng những hàng đó. `MATCH FULL` đòi các cột phải null **cùng nhau**; vì
`commune_id` không bao giờ null, nó sẽ từ chối **mọi** cột không có mạch trong bảng, tức đa số.

Đây là loại sửa trông *đúng hơn* lúc review. Canh bằng
`A_pole_with_no_feeder_is_still_legal_even_though_its_commune_is_not_null`.

**2. 🔴 `ak_feeder_feeder_id_commune_id` TRÔNG thừa và KHÔNG thừa.**

`feeder_id` đã là khoá chính, nên unique trên `(feeder_id, commune_id)` tự nó chẳng thêm gì — và đó
đúng là lý do sẽ có người xoá nó đi như rác. Nó là **đích** của FK ghép: Postgres chỉ cho khoá ngoại
trỏ vào cột có ràng buộc unique. Xoá nó là kéo theo cả FK ghép.

**3. `segment_id` KHÔNG có khoá tương tự, và đó là cố ý.** `road_class = inter_commune` nghĩa là
đường chạy **giữa** các xã (BE-REVIEW-02 ràng buộc 1). Thêm "cho đối xứng" sẽ làm tuyến liên xã hợp
lệ trở thành không ghi được. Canh bằng `A_pole_may_still_sit_on_a_segment_owned_by_another_commune`.

**Hai lớp, và cần cả hai — đừng bỏ lớp app vì "DB lo rồi".** `RequireFeederInCommuneAsync` vẫn chạy
trước và vẫn là thứ trả lời, vì FK một mình nổ ra `DbUpdateException` trần → middleware BE-04 trả
**500**. Cùng hình dạng `CommuneFilter.Narrow` chồng lên `CommuneWriteGuard`: thông điệp đọc được ở
trước, thứ không thể quên ở sau.

⚠️ **FK bắt được một ca mà tầng app CHƯA BAO GIỜ phủ:** đổi `commune_id` của **chính cột** trong khi
nó đang đấu vào một tủ điện. Lượt kiểm đọc xã của cột làm vế cố định nên không có gì kích hoạt. Hôm
nay không request nào hỏi được điều đó, nhưng đó là tính chất của controller tuần này chứ không phải
của dữ liệu.

⚠️ **Tác dụng phụ của alternate key: KHÔNG sửa được `commune_id` của `Feeder` đang track nữa.**
`Feeder.CommuneId` nay là **key property**, và EF Core cấm sửa key trên entity đang track: ném
`InvalidOperationException` từ `DetectChanges`, **trước** `CommuneWriteGuard`, nên ra **500** thay vì
403 `COMMUNE_FORBIDDEN`. **Dữ liệu không gặp rủi ro** — cả hai lớp đều từ chối. Cố ý **không** vá:
chuyển feeder sang xã khác vốn đã bị BE-12a cấm (nó kéo tủ điện ra khỏi các cột đang đấu vào, và không
còn lượt ghi nào bắt được), nên EF đang thực thi một luật nhóm đã chốt. Dịch lại lỗi thì phải bắt
`InvalidOperationException` quanh `SaveChanges` và khớp theo thông điệp có thể đổi khi nâng phiên bản
— bắt quá rộng một kiểu lỗi quá phổ biến, để làm đẹp thông điệp cho một thao tác không bao giờ được
phép thành công. Canh bằng `Changing_a_tracked_feeders_own_commune_is_refused_by_EF_before_the_guard_sees_it`
(`docs/agents/traps/commune-scope.md`, mục 1c).

⚠️ **Index đổi hình:** `ix_pole_feeder_id` → `ix_pole_feeder_id_commune_id`. Vẫn dẫn đầu bằng
`feeder_id` nên phục vụ được mọi truy vấn lọc theo feeder, và vì query filter BE-08 luôn nhét
`commune_id` vào `WHERE`, index ghép còn phủ tốt hơn. **`ix_pole_commune_id` KHÔNG bị drop** — đã soi
riêng lúc đọc migration, vì đây đúng là cái bẫy đã cắn ở BE-12a (xem `docs/agents/traps/migrations.md`, mục PARTIAL INDEX).

## Testbed dùng cùng mô hình tài sản

Bộ testbed FO-07 đăng ký như `RoadSegment` thật, không tạo thực thể riêng; cùng pipeline với dữ liệu khác,
phân biệt bằng `data_source` (`calibration_rig`, Contract §1.6). Controlled Reference Capture Set rời deliverable v1.4;
không dùng luật này để seed thêm khi D-R25 còn chờ.

## Bộ mock FO-26 — đã bàn giao

`mock-poles.geojson`, `mock-pole-detail.json`, `mock-faults.json`, `mock-work-orders.json`, `mock-iot-nodes.geojson`.

Bộ mock có 103 cột và **3 IoT node ở tủ điện** (`mocks/mock-iot-nodes.geojson`); seed bằng
`scripts/seed_mock_set.py`, giữ ID để demo khớp FE. Detail/runtime của `POLE-0047` còn theo mô hình NODE-047 cũ:
xem deviation tại `docs/contract-drift.md` (I-18 và BE-20), không dùng làm ngoại lệ cho IoT ở tủ.

> ✅ **`external_ref` — CHỐT 03/10/2026 (Mỹ, D-R10 đóng): mã VĨNH VIỄN do nhóm quản lý; mã kiểm kê của xã đi cột RIÊNG.**
>
> - Cột thực địa giữ mã hiện có `KS-<ngày>-<giờ chụp>` (114 cột Long Phước / Long Bình), tuyến giữ `osm-…`; bộ mock giữ
>   `pole_id` / `segment_id` của mock. **Không đổi tên, không ghi đè** `external_ref` sau khi đã nạp.
> - Mã kiểm kê của xã (nếu xã giao, ví dụ số sơn trên cột) vào cột **`inventory_code`** riêng — nullable, unique trong mỗi xã —
>   thêm bằng migration **khi xã thật sự giao**, ánh xạ bằng vị trí + người kiểm. **Không** dùng nó làm khoá import.
> - Khảo sát ảnh lần sau **phải dùng `scripts/photos_to_poles.py --existing`**: vị trí khớp thì dùng lại
>   `group.matched.external_ref`, không import lại cột cũ; ca mơ hồ đưa vào review. Sinh lại `KS-…` theo giờ ảnh mới sẽ tạo cột trùng.
> - Đường "xoá sạch nạp lại" cho cột thực địa còn mở tới khi **phiên khảo sát thực địa đầu tiên được xử lý** (`pole_observation`
>   trỏ vào `pole` bằng `Restrict`); quyết định này làm cho đường đó không còn cần thiết.

## POLE-NOTE — ghi chú cột (05/10/2026)

**Ghi chú đi qua bốn đường:** `PUT /assets/poles/{id}/note` (`EditPoleNotes`, kỹ sư + quản lý), form tạo
(`POST /assets/poles`, `note?`), form sửa (`PUT /assets/poles/{id}`) và import cột (cột `note` tuỳ chọn). Ở form sửa, `note`
là **trường duy nhất GIỮ NGUYÊN khi vắng** trong một `PUT` thay thế toàn phần (đọc bằng `PoleNoteInput.Read`, `JsonElement`):
đừng "sửa cho nhất quán" thành vắng = xoá — form cũ chưa có ô ghi chú sẽ xoá sạch ghi chú (cùng họ bẫy `feeder_id`). Ở import,
**ô trống cũng là giữ** — import không bao giờ xoá ghi chú; thay ghi chú khác nội dung thì `warnings[]` **trích nguyên văn cũ**,
vì không có lịch sử ghi chú nào khác để khôi phục. Canh bằng
`Replacing_the_pole_without_the_note_key_keeps_the_note` và
`The_import_overwrites_a_different_note_warning_with_the_old_text_and_never_clears_one`. Trường `JsonElement` mới trong request
phải thêm vào `JsonElementFieldSchemaFilter`, nếu không spec ra `{}` và FM-04 sinh `Any`.

**Ghi chú KHÔNG có tác giả riêng từ N-4 (06/10/2026).** Ai sửa — ghi chú hay bất cứ gì — là `updated_by` của cột.

## `updated_by` của tài sản — chỉ đóng dấu khi giá trị THẬT SỰ đổi (06/10/2026, drift N-4)

`pole`, `road_segment`, `feeder`, `fixture` implement `IUpdateStamped`; **mọi** đường ghi gọi `AssetStamp.Touch` **sau khi**
gán giá trị (sau `Add` với hàng mới). `Touch` đọc `Entry().State` — `Entry()` chạy dò thay đổi cho đúng entity đó — nên gán
lại giá trị bằng giá trị đang lưu thì hàng vẫn `Unchanged` và **không** đóng dấu. Hệ quả cho code mới:

- 🔴 **Không gán `UpdatedAt` trực tiếp** trên bốn bảng này. Chính phép gán đó làm hàng thành `Modified`, phá luật "nạp lại file
  y hệt không đổi gì" — và test so `updated_at` mới bắt được, test so dữ liệu thì không.
- Geometry đọc lại từ WKT là object mới mỗi lần, nhưng comparer của NTS so theo giá trị nên vẫn `Unchanged` — đã kiểm bằng
  test (`Re_importing_an_identical_file_changes_nothing_and_a_changed_row_names_the_importer`), đừng thêm so sánh tay.
- Import đếm `updated` / `unchanged` theo **giá trị trả về** của `Touch`, không đếm theo "khớp `external_ref`".
- `updated_by` là FK `Restrict` tới `app_user` trên **cả bốn bảng**: fixture test tạo tài khoản rồi để tài khoản đó ghi tài
  sản phải xoá **tài sản trước, tài khoản sau** (bài học PR #93). Script `copy_dev_to_supabase.py` chỉ chép tài khoản seed, nên
  hàng có `updated_by` là tài khoản khác sẽ vấp FK — cùng giới hạn đã có với `reported_by` / `created_by`.
- Chỉ người **gần nhất**. Cần lịch sử lượt nạp thì là bảng riêng (`asset_import`), không phải nới cột này.

## BE-31 — bảo hành và xuất CSV (12/10/2026)

**`warranty_status` có ĐÚNG MỘT định nghĩa: `WarrantyWindow` (`LuxMap.Shared/Contracts/Warranty`).** Mọi bề mặt đọc tính
bằng `WarrantyWindow.Classify` **sau** truy vấn (`AssetCrudService.WithWarranty`, `MapQueryService`, `PoleDetailService`),
không viết lại luật trong projection SQL. Chỗ duy nhất luật phải nằm trong SQL là **lọc** của `/statistics/warranty`: nó lọc
trên `window.Today` / `window.LastExpiringDay`, không tự tính ngày, và `WarrantyTests.The_status_filter_agrees_with_the_one_definition_on_every_edge`
đối chiếu từng dòng trả về với `Classify` trên cả năm mép. Lấy "hôm nay" qua `WarrantyCalendar` — múi giờ đọc từ chính
`WorkOrders:Agenda:TimeZone`, đừng khai khoá múi giờ thứ hai.

`ActiveFixture.WarrantyStatus` và `PoleMapFixture.WarrantyStatus` **mặc định `unknown`**, vì chúng được gán sau truy vấn: đường
đọc mới nào quên gán thì ra `unknown` — sai nhưng an toàn — chứ không ra `active`. Đừng đổi mặc định.

🔴 **Hai `ProducesResponseType` cùng một mã trạng thái: cái SAU THAY cái trước, không gộp.** Khai
`[ProducesResponseType<T>(200, "application/json")]` rồi `[ProducesResponseType<string>(200, "text/csv")]` làm schema `T` **biến
khỏi spec** (gặp thật: `FixtureStatusStatistics` và ba schema nữa mất), mà build, test và lint đều xanh. Action trả thêm CSV
gắn `[CsvExport]`; `CsvExportOperationFilter` thêm `text/csv` bên cạnh JSON. So spec theo **ngữ nghĩa** (path/schema đổi gì),
đừng chỉ đọc số dòng diff.

**CSV đi qua đúng một đường: `CsvExport.Write`**, tuần tự hoá từng dòng bằng chính `LuxMapJsonOptions` rồi đọc ô ra — nên tên
cột, enum, ngày, `null` viết y như JSON. Dòng phải **phẳng** (ô object/array → ném lỗi). Báo cáo mới cần CSV thì trả record
phẳng và gọi nó, đừng tự format. `GetTypeInfo` cần options đã có resolver: `Write` khoá options trước (`MakeReadOnly`) — gặp
thật ở test đơn vị, đỏ hay xanh tuỳ test nào chạy trước.

Trường mới trong properties bản đồ phải cập nhật schema viết tay: xem `docs/agents/traps/iot-lighting.md`, mục BE-14b (kể cả `warranty_status`).

## CABINET — trụ điện tổng là tài sản, IoT là thiết bị gắn lên trụ (07/10/2026, drift CAB-1…CAB-8)

**Toạ độ thuộc `electrical_cabinet`, không thuộc `iot_node` nữa** — `iot_node.geom` đã bỏ. Mọi chỗ cần vị trí thiết bị
(bản đồ, IOT-11 sinh `node_offline` / `runtime_decline` với `location` = toạ độ tủ theo I-9) phải join trụ qua
`iot_node.cabinet_id`. Lọc bbox thì `ST_Intersects` trên **`electrical_cabinet.geom` trần** (`MapQueryService.IotNodeQuery`),
canh bằng `MapQueryPlanTests.The_device_and_cabinet_bbox_queries_can_ride_the_cabinet_gist_index`.

🔴 **Bốn ràng buộc chỉ có ở DB, EF không biết** (`CabinetConstraints`, raw SQL trong `AddElectricalCabinet`): hai unique đích
`(feeder_id, cabinet_id)`, `(node_id, cabinet_id)` và hai FK rơ-le-cùng-trụ của CAB-4. Lý do: mỗi đích
chứa một cột người ta sửa được; khai `HasAlternateKey` là biến cột đó thành key property và EF ném ngay trong
`DetectChanges` (cùng bẫy `Feeder.CommuneId` của O-7), còn alternate key trên cột nullable thì EF không cho. Hệ quả:
**snapshot không thấy chúng, migration sau không tự dựng lại, và không ai biết khi một cái mất** — chỉ
`CabinetTests.Every_database_only_cabinet_constraint_exists` biết. Gộp/squash migration thì phải chép khối SQL đó theo.

- **Viết `feeder_control` thì `cabinet_id` phải là trụ của CẢ thiết bị lẫn feeder** — gắn feeder vào trụ trước. Hệ quả ngược:
  feeder đang có rơ-le **không đổi trụ / tháo trụ được**, thiết bị đang có rơ-le không chuyển trụ được (FK `NO ACTION`).
  Service trả 409 `ASSET_IN_USE`; import báo lỗi theo dòng — **phải** bắt ở bước kiểm, FK ở bước ghi làm hỏng cả mẻ (500).
- **Assets không thấy `iot_node` / `feeder_control`** (Telemetry tham chiếu Assets, không ngược lại): hỏi qua port
  **`ICabinetDeviceLookup`** (Telemetry hiện thực), khuôn `IActiveWorkOrderLookup`. Đừng thêm project reference, đừng
  `SqlQuery` thẳng bảng của Telemetry từ Assets.
- **`PUT /assets/feeders/{id}`: `cabinet_id` vắng = GIỮ** — ngoại lệ thứ hai của thay thế toàn phần, cùng khuôn `note` của cột.
  Import feeder: ô trống / thiếu cột = giữ, import không bao giờ tháo trụ. Đừng "sửa cho nhất quán" với `feeder_id` của cột.
- **Teardown xoá `feeder_control` → `iot_node` → `feeder` → `electrical_cabinet`** (tất cả `Restrict`). Fixture tài sản đã làm;
  fixture mới tạo thiết bị phải theo.
- Trụ có thể `field`, **kể cả khi đang mang thiết bị** — luật CAB-5 đã **gỡ** (Mỹ, 07/10/2026, migration
  `DropFieldCabinetRule`), canh bằng `A_cabinet_carrying_a_device_may_become_field_data`. Thiết bị thì không bao giờ `field`
  (`ck_iot_node_data_source_not_field` giữ nguyên); mở rộng nó là quyết định phạm vi (lắp IoT ngoài xã), không phải sửa lỗi.

## TOPO-INFER — nhãn nguồn gốc topology (07/10/2026, drift TI-1…TI-6)

Topology ngoài thực địa là **suy luận công khai**: `pole.feeder_source` và `feeder.cabinet_source` (`verified | inferred`), mỗi
nhãn **null đúng khi quan hệ null** (`ck_pole_feeder_source_matches_feeder`, `ck_feeder_cabinet_source_matches_cabinet`).

- 🔴 **Mọi đường ghi `pole.feeder_id` / `feeder.cabinet_id` đi qua `TopologyLink`** (`Resolve` rồi `ApplyPoleFeeder` /
  `ApplyFeederCabinet`). Đó là chỗ DUY NHẤT có luật TI-2 (vắng + quan hệ không đổi → giữ; đổi → `inferred`; `null` / sai kiểu /
  nhãn không quan hệ → 400) và chỗ **ghi theo cặp**: EF chỉ ghi cột đổi so với snapshot, nên hai lượt xen nhau (A xác minh F1,
  B chuyển sang F2) sẽ để lại `(F2, verified)` — cặp không ai khẳng định. `Apply*` đánh dấu cả hai cột khi một trong hai đổi,
  và **không** đánh dấu gì khi cả hai giữ nguyên (giữ luật N-4: nạp lại file y hệt không đóng dấu). Canh bằng
  `Interleaved_writers_never_leave_a_pair_nobody_asserted`. Gán `FeederId` / `CabinetId` trực tiếp trong code mới là mở lại lỗ đó.
- **Không đường nào tự nhận `verified`.** Backfill, seed, mặc định đều `inferred`. Fixture test gán quan hệ phải kèm nhãn (CHECK).
- **Import: ô trống = vắng.** GeoJSON object / mảng ở ô nhãn phải là lỗi dòng — `GeoJsonImportRow` đọc chúng thành `null`, tức
  "trống", nên cột có nghĩa "trống = giữ" phải hỏi `IImportRow.IsNonScalar` trước (`ImportRowReader.Label`).
- **`GET /map/cabinets/{id}/topology`** (`CabinetTopologyService`): sơ đồ **logic**, thứ tự dọc đường bằng `LengthIndexedLine`
  trên 4326 — **chỉ thứ tự, không bao giờ khoảng cách**. Hình học tuyến đọc **bỏ query filter** (tuyến `inter_commune` có thể
  thuộc xã khác, cùng lý lẽ WO-12); bỏ `IgnoreQueryFilters` thì tra cứu trượt và ra 500. Cạnh đầu lấy nhãn **thấp hơn** của hai
  quan hệ. `verified` của cột nghĩa là "cột thuộc mạch này", **không** phải cạnh đó là dây thật.
- Chưa làm (ticket riêng): chặn trộn cột `field` với trụ không-`field` (phải kiểm ở ba đường), script đề xuất quan hệ suy luận.

## COMMUNE-BBOX — khung bản đồ của xã (14/10/2026, drift CB-1…CB-6)

- **bbox là KHUNG NHÌN, không phải ranh giới.** Đừng dùng nó để lọc, ẩn hay từ chối tài sản, và đừng suy `commune_id` từ nó: phân quyền
  vẫn một nguồn là cột `commune_id` (`CLAUDE.md`, mục Phạm vi xã). Ranh giới thật (polygon) vẫn chưa có nguồn.
- Bốn cột `bbox_*` trên `administrative_unit` đọc/ghi qua **`GetBbox()` / `SetBbox()`** (EF không dịch được — đọc sau truy vấn). Trung điểm
  (`center`) và khung gộp (`CommuneBbox.Union`) **tính lúc đọc, không lưu**.
- `/admin/communes` nằm ở **module Admin** (nay tham chiếu Persistence). `AdministrativeUnit` không bị lọc theo xã — capability
  `ManageCommunes` là thứ duy nhất giữ endpoint cho Quản trị.
- `PUT /admin/communes/{id}` dùng **`[JsonRequired]`** để cả hai khoá bắt buộc mà `bbox` vẫn nhận `null` — khác khuôn `JsonElement` của
  `feeder_id` / `note`, vì `bbox` là object có schema riêng. Gỡ `[JsonRequired]` thì form thiếu khoá sẽ xoá khung (canh bằng
  `Put_needs_both_keys_and_an_explicit_null_clears_the_frame`).
- Test tạo xã đặt tên tiền tố `bbox-test-` và tài khoản riêng; teardown xoá **tài khoản trước, xã sau** (`app_user_commune` giữ xã).
- 🔴 **`AccountTestFactory.DeleteTestAccountsAsync()` xoá MỌI tài khoản tiền tố `acct-`, và `AccountAdminTests` gọi nó sau MỖI test.**
  Class khác dùng chung tiền tố hay gọi chung hàm đó sẽ xoá tài khoản của nhau khi chạy song song → test đỏ ngẫu nhiên và
  `40P01 deadlock` ở teardown (chạy riêng từng class có thể che race). Class mới: **tiền tố riêng** (`cbx-`)
  và chỉ xoá tiền tố đó. Cũng đừng assert **tổng số** xã / tài khoản — class khác tạo cùng lúc; kiểm theo id hoặc tên.

## POLE-LOCATION — dời cột (14/10/2026, drift PL-1…PL-5)

- **Ngưỡng hành lang có ĐÚNG MỘT nguồn: `SurveyProcessing:RouteCorridorM`.** Assets hỏi qua port **`ISurveyCorridor`** (Survey hiện thực,
  khuôn `ICabinetDeviceLookup`) — đừng khai hằng 25 hay option thứ hai ở Assets: cột dời quá ngưỡng của khảo sát sẽ **âm thầm** rơi khỏi
  mọi sweep sau (`SurveyChainageQuery.Poles` lọc theo đúng ngưỡng đó), không lỗi nào báo.
- **Khoảng cách tới tuyến đọc `RoadSegment` BỎ query filter** — tuyến `inter_commune` có thể thuộc xã người gọi không thấy (cùng lý lẽ
  WO-12, TOPO-INFER). Chỉ một `double` rời truy vấn. Bỏ `IgnoreQueryFilters` → 500 (`The_corridor_is_measured_from_a_segment_owned_by_another_commune`).
- **`Pole` KHÔNG phải `IAudited`**: audit `relocated` do service ghi chủ động, guard không ép. Làm `Pole` thành `IAudited` sẽ bắt **mọi**
  đường ghi cột (form, import, ghi chú, sync, seeder, fixture) kèm audit — quyết định riêng. Hệ quả: sửa `geom` qua form đầy đủ / import chưa có vết.
- Thứ tự: kiểm có cột **qua filter** (404) → `FOR UPDATE` → đọc lại → đo → `AssetStamp.Touch` (không đổi ⇒ không audit). Rollback
  `AuditPoleRelocation`: xem `docs/agents/traps/migrations.md`, mục Rollback dựng lại CHECK cũ.
