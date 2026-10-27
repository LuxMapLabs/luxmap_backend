# ASSET-PHOTOS — Phase 1: ảnh tài sản và trạng thái mạch

Ngày khảo sát: 27/10/2026 · Codex · nhánh `docs/ASSET-PHOTOS-p1`.
Mốc code: `97c144337a83b30feadb1d6573233355a43b12b8`.

**Chỉ khảo sát. D-01…D-12 dưới đây đều CHỜ MỸ CHỐT**, không phải quyết định đã duyệt, kể cả hướng FE/Claude đã nêu trong ticket. Mọi tên bảng, prefix, endpoint, capability, constraint hoặc setting ở phần **đề xuất** là tên dự kiến, không phải khẳng định chúng đã tồn tại. Bằng chứng hiện trạng ở phụ lục E là output đọc trực tiếp checkout này.

Đã đọc `CLAUDE.md` (`AGENTS.md` vẫn là symlink), `.ai/context/sources.md`, ticket, `.ai/context/commands.md`, `.ai/README.md`, README; tám trap bắt buộc: assets, media-storage, commune-scope, migrations, faults-workorders, iot-lighting, deploy-ops, spatial-map. Đã đối chiếu controller/service/entity/configuration tài sản, import, Map, Telemetry, precedent WorkOrders/FaultPhotos, Storage/ThumbnailFactory, audit, monitoring, sync, ma trận quyền và ba test capability; Contract, OpenAPI, drift I/CAB/LC/CB, ERD, compose/backup/PLAN. Không dùng thông tin trên web: đây là khảo sát repo ở một commit cố định.

**Thiếu tài liệu tham chiếu trong checkout:** không có `.ai/results/BE-11-p1.md` hay `BE-24-p*.md` (E-01). Đã đọc `.ai/results/BE-24.md`, `BE-35-p1.md` và code hiện tại thay thế; không lấy số liệu DB/dev trong báo cáo cũ làm kết quả của lượt này. Không kết nối DB nào, không chạy API, migration, build, test hoặc container.

Kết luận khảo sát: chưa có ảnh gắn trực tiếp tài sản; chưa có ingest/bảng số đo dòng điện hay điện áp. Có MQTT heartbeat/ACK và job offline, nên nói “chỉ có device commands” là chưa đầy đủ, nhưng những đường đó **không đo điện**. `control_mode` không chứng minh `power_state`.

## A. Ảnh cho cột và tủ điện

### D-01 — Câu 1: mô hình lưu, FK, xã và ID

**Hiện trạng:** entity `Pole`/`ElectricalCabinet` không có khoá ảnh. Precedent tên thật là **`work_order_pole_photo`**, không phải bảng `pole_photo`; `pole_photo_id_seq`, prefix `PPH` và các constraint `ck_pole_photo_*` đã có. Không dùng lại những định danh đó cho ảnh tài sản (E-02).

| Phương án | Giá phải trả |
|---|---|
| Một `asset_photo(asset_kind, asset_id)` | Ít schema/code lặp; nhưng FK không thể trỏ cùng một `asset_id` tới hai bảng theo discriminator. FK đơn không bảo vệ được cha; cần trigger hoặc bảng gốc tài sản mới. `asset_kind` nếu lưu enum phải `HasContractEnum` + CHECK, nhưng CHECK không thay FK. Không đề xuất. |
| Một bảng, `pole_id?`, `cabinet_id?`, CHECK đúng một cha | Có hai FK thật `Restrict`, tránh trigger đa hình, ít cột metadata lặp; cần invariant XOR, hai nhánh truy vấn/index, và tránh lưu thêm `asset_id`/kind lệch với hai FK. Là phương án khả thi nếu Mỹ muốn một bảng. |
| Hai bảng `pole_asset_photo` / `cabinet_asset_photo` | Cha bắt buộc, FK thật rõ ràng, không discriminator hay XOR; lặp schema metadata và đăng ký storage. Có thể dùng chung phần đọc JPEG/upload, không dựng framework ảnh tổng quát. |

**Đề xuất:** hai bảng, vì chỉ hai loại tài sản và invariant dễ đọc/kiểm nhất. Mỗi bảng có `photo_id`, `pole_id` hoặc `cabinet_id`, `commune_id`, `data_source`, `created_at`, `uploaded_by`, `client_op_id`, `request_hash`, `object_key`, `thumbnail_key`, `byte_count`, `thumbnail_bytes`, `width`, `height`, `deleted_at?`, `deleted_by?`. Không lưu trạng thái đèn hay nhãn AI.

- Cả hai entity là gốc truy vấn: `ICommuneScoped` + `HasCommuneScope()` + `HasCommuneReference()`; write vẫn qua ChangeTracker/`CommuneWriteGuard`. `commune_id` và `data_source` **server sao từ cha**, client gửi hai field này → `400 SERVER_OWNED_FIELD`. `DataSource` dùng enum hiện có qua `HasContractEnum`, CHECK đầy đủ bốn giá trị. Không có `asset_kind` lưu trong phương án này.
- FK cha và tài khoản đều `Restrict`. Guard chỉ kiểm xã trong quyền, không kiểm khớp cha: service phải suy xã từ cha; đề xuất thêm FK ghép cha+xã để DB bảo vệ đường ghi khác. Pole đã có DB-only `ak_pole_photo_target(pole_id, commune_id)` từ migration ảnh phiếu; đọc lại trạng thái migration mới nhất trước khi dùng, không tạo trùng. Cabinet đã có alternate key cha+xã trong model. Không biến `Pole.CommuneId` thành EF key chỉ để tạo FK.
- Prefix mới dự kiến **`PAP` / `CAP`**, tối thiểu **6** chữ số; hai sequence dự kiến `pole_asset_photo_id_seq` / `cabinet_asset_photo_id_seq`, thêm spec vào `PrefixedIds.All`. Default qua `luxmap_format_id`, không LPAD trần, không ID client. Cấp ID trước storage bằng SQL registry tin cậy theo precedent `WorkOrderPolePhotoService`; sequence có khoảng trống khi lỗi là hợp lệ.
- List/ảnh bìa theo `created_at, length(photo_id), photo_id`; canh overflow `999999 → 1000000` với cùng transaction. Index theo cha+thứ tự, FK user; khai **index `commune_id` đầy đủ** kể cả khi có index partial ảnh chưa ẩn. CHECK byte/pixel, key/hash/UUID hợp lệ, và cặp `deleted_at/deleted_by` cùng null/cùng có. Tên constraint mới chỉ chốt sau thiết kế, tránh >63 ký tự.

### D-02 — Câu 2: bucket, object key và thumbnail

**Hiện trạng:** `StorageBucket` có Survey/Evidence/Video/Citizen; `StorageKeys` tạo `original/{id}.jpg`, `thumb/{id}.jpg`. Bốn bucket đã được provision ở cả compose dev và prod; backup lặp cứng bốn tên. `PLAN` chỉ sao **row DB**, không sao bytes MinIO. `storage.buckets[]` đếm theo đăng ký `StoredObjectColumn`; reconciliation cũng dựa vào đó (E-03).

**Phương án:** (a) dùng evidence hiện có cho ảnh vận hành, mở rộng mô tả nghiệp vụ; (b) bucket mới `luxmap-assets`, tách retention/quota về sau nhưng tăng cấu hình vận hành và đổi cam kết “đủ 4 bucket”; (c) survey/citizen/video không phù hợp nguồn và lifecycle của ảnh tài sản.

**Đề xuất:** dùng **`StorageBucket.Evidence`** trước. Bảng riêng vẫn phân biệt tài sản với bằng chứng; prefix PAP/CAP không va EVD/PPH. Khoá theo ID, **không theo hash và không có commune trong key**; hash chỉ phục vụ replay, tránh dedup xuyên xã và quyền sở hữu object dùng chung. Original nguyên bytes theo precedent ảnh phiếu; thumbnail đồng bộ, 320px cạnh dài/q80, bỏ `ExifProfile`, `XmpProfile`, `IptcProfile` tường minh qua `ThumbnailFactory.JpegOnly`. Đây là thông số cần WP5/WP6 xác nhận, chưa coi 320/q80 là chuẩn đã duyệt.

Hai bảng mới đều đăng ký `StoredObjectColumn.Original/Thumbnail` (tổng bốn đăng ký) trong module sở hữu; giữ cột key/byte kể cả khi ẩn. Nhờ vậy bytes vẫn được tính, object ẩn không bị báo orphan. Original có thể giữ EXIF/GPS, nên cũng chỉ mở qua API có scope và `Cache-Control: no-store` như thumbnail; FE không nhúng URL công khai.

Nếu Mỹ chọn bucket mới: phải thêm enum/name mapping `StorageKeys`, provision **cả** `deploy/compose.yml` và `docker-compose.yml` (`minio-mc`), backup/restore hướng dẫn và smoke check bucket, mô tả/enum OpenAPI storage, Contract §4.10 thành **5 bucket**. Monitoring hiện duyệt `Enum.GetValues<StorageBucket>()`, không cần hardcode danh sách mới; vẫn bắt buộc đăng ký key/byte. `copy_dev_to_supabase.py` phải thêm bảng vào `PLAN` sau cha/user, đúng bộ lọc cha+xã (cả ảnh ẩn); **script không có danh sách bucket để chỉ thêm một tên rồi coi là đã copy object**. Chuyển bytes sang đích vẫn là bước riêng. Không thực hiện việc nào trong Phase 1.

### D-03 — Câu 3: API, giới hạn, mã lỗi và capability

**Phương án:** một route đa hình `/assets/{kind}/{id}/photos` hoặc hai route tài nguyên tường minh; trả chỉ URL hay metadata+URL; dùng ManageAssets cho mọi ghi hay thêm quyền upload cho kỹ sư.

**Đề xuất:** hai nhóm route cùng shape, không nhận loại tài sản tuỳ ý:

| Method | Route dự kiến (thay `poles/{pole_id}` bằng `cabinets/{cabinet_id}` cho tủ) | Kết quả/quyền |
|---|---|---|
| POST | `/api/v1/assets/poles/{pole_id}/photos` | multipart đúng một `photo` + `client_op_id`; `201` item, `Location` tới collection; replay `200`. Capability mới `UploadAssetPhotos`. |
| GET | `/api/v1/assets/poles/{pole_id}/photos` | `{page,page_size,total,items[]}`; `ReadNetwork`. |
| GET | `/api/v1/assets/poles/{pole_id}/photos/{photo_id}/original` | JPEG qua API; `ReadNetwork`. |
| GET | `/api/v1/assets/poles/{pole_id}/photos/{photo_id}/thumbnail` | JPEG qua API; `ReadNetwork`. |
| DELETE | `/api/v1/assets/poles/{pole_id}/photos/{photo_id}` | `204`, **ẩn** row (D-05); `ManageAssets`. |

Không có GET metadata riêng hay endpoint đặt bìa ở bản đầu: list đã có đủ metadata, bìa chọn theo D-04. POST `Location` tới collection thật tồn tại, không dựng URL detail chưa có GET. Item dự kiến:

```json
{
  "photo_id": "PAP-000001", "pole_id": "POLE-0001",
  "created_at": "2026-10-27T10:00:00Z", "uploaded_by": "USR-004",
  "client_op_id": "8c770a4a-27d4-4d91-a541-669d5a69bc80",
  "width": 1920, "height": 1080, "byte_count": 123456,
  "thumbnail_bytes": 12345,
  "original_url": "/api/v1/assets/poles/POLE-0001/photos/PAP-000001/original",
  "thumbnail_url": "/api/v1/assets/poles/POLE-0001/photos/PAP-000001/thumbnail"
}
```

Tủ trả `cabinet_id`, không `pole_id`. Không emit object key/bucket, EXIF, request hash. Không yêu cầu GPS/capture settings hoặc `captured_at` cho ảnh nhận dạng tài sản (khác ảnh khảo sát đêm); `created_at` là thời điểm server nhận, không giả làm giờ chụp. Mốc do app sinh qua `UtcMicrosecondClock.UtcNow()`.

Giới hạn đề xuất: **10 ảnh chưa ẩn/tài sản**, 1 file/request, original ≤ **16 MiB**, request ≤ **17 MiB**, ≤ **40 triệu pixel**, JPEG magic `FF D8 FF` + decode only-JPEG. Dùng `IOriginalJpegReader.ReadAsync` có chặn bytes/pixel, không gọi thẳng pipeline copy toàn stream chưa chặn. Kiểm nội dung ngay trong service, không chỉ DataAnnotations. Không tin MIME/đuôi file hoặc `IFormFile.Length` làm giới hạn bytes duy nhất.

Mã đã có dùng lại: `400 VALIDATION_FAILED`, `SERVER_OWNED_FIELD`; `401 UNAUTHENTICATED`; `403 ROLE_FORBIDDEN`; `404 ASSET_NOT_FOUND` cho cha, đề xuất mới `ASSET_PHOTO_NOT_FOUND` cho ảnh không thuộc cha/ngoài xã/đã ẩn; `409 IDEMPOTENCY_CONFLICT`, đề xuất mới `ASSET_PHOTO_LIMIT_REACHED`; `413 UPLOAD_TOO_LARGE`; `415 UNSUPPORTED_IMAGE_FORMAT`; `503 STORAGE_OBJECT_MISSING` khi row hợp lệ nhưng object mất (adapter đã có). Lỗi có envelope và correlation id. Query xã ngoài quyền (nếu sau này có) vẫn `403 COMMUNE_FORBIDDEN`; ID lookup ngoài xã **404** sau policy. Không thêm `commune_id` query vào collection dưới một cha.

`UploadAssetPhotos` dự kiến tên wire `cap:upload_asset_photos`, roles literal **`[manager, field_engineer]`**. ReadNetwork giữ **`[superior, manager, field_engineer, system_admin]`**; ManageAssets giữ **`[manager]`**. Không mở ManageAssets cho kỹ sư, không dùng ExecuteWorkOrders vì ảnh này không có phiếu/người được giao. Cập nhật Matrix, bảng literal ở `CapabilityMatrixTests`, `RoleCapabilityMatrixTests` cùng diff; coverage route bằng `CapabilityPolicyCoverageTests`, kiểm không AllowAnonymous cấp class/action. System admin chỉ đọc.

Replay: unique `(uploaded_by, client_op_id)` trong từng bảng, hash bao cha + SHA256 bytes; cùng khoá khác cha/nội dung trong cùng loại → 409; replay ảnh đã ẩn trả 404, không khôi phục hoặc tạo lại ảnh. Namespace replay **theo loại ảnh**, không tuyên bố chống dùng lại UUID trên mọi nghiệp vụ. Chưa có ảnh thì đọc cha đã scope → bounded decode/storage **không giữ khoá** → transaction khoá cha, đọc lại cha qua filter, kiểm lại quyền/nguồn, replay và count, thêm row+audit cùng SaveChanges. Hai upload ở ngưỡng 9 phải chỉ nhận thêm một; replay được ưu tiên trước kiểm quota. Không giữ khoá xuyên upload, không xoá object để “bù” khi DB thất bại: reconciliation báo orphan sau grace 24h.

### D-04 — Câu 4: hiển thị ở đâu, bìa và kích thước response

**Phương án:** nhúng toàn bộ gallery mọi nơi; chỉ link collection; hoặc summary cho list, gallery cho detail.

**Đề xuất:** summary + detail, ảnh bìa tự chọn ảnh chưa ẩn **đầu tiên** theo thứ tự D-01; chưa cho đặt bìa thủ công (tránh endpoint, cột is_cover và đua hai ảnh bìa). WP5 cần đổi bìa thì tách quyết định bổ sung, không lén thêm trong Phase 2.

| Nơi | Trường thêm dự kiến |
|---|---|
| `/assets/poles`, `/assets/cabinets` | `photo_count`, `cover_photo` = `{photo_id,thumbnail_url}` hoặc null; count chỉ ảnh chưa ẩn. Không bytes JPEG/base64. |
| `/assets/poles/{id}` | Summary nằm trong `pole` (dùng lại list item); thêm `asset_photos[]` tối đa 10 ở ngoài wrapper. Một request đủ gallery. |
| `/assets/cabinets/{id}` | Tương tự: summary trong `cabinet`, `asset_photos[]` ngoài wrapper. |
| `/map/poles/{id}` | Thêm `asset_photos[]` tối đa 10; **giữ nguyên `recent_frames[]`** là frame khảo sát đã công bố, không trộn schema/source/quyền. Không biến ảnh tài sản thành nguồn publication. |
| `/map/cabinets` | Chỉ `photo_count`, `cover_photo`; gallery lấy collection dưới cabinet khi mở popup. Đồng thời nhận `feeders[]` theo D-08. |
| `/map/poles` bbox, `/map/segments` | **Không thêm ảnh/summary/control/power_state**. Giữ tải 2000 cột; sync dựa PoleProperties không tự phình. |

COUNT/bìa/galleries cũng phải qua scope xã, không loop request từng row. Bìa null khi count=0, upload thứ hai không âm thầm đổi bìa, ẩn bìa thì chọn ảnh kế tiếp. GeoJSON properties vẫn phẳng ở cấp business field; `cover_photo`/`feeders` là object/array mới cần WP5 parse JSON property khi dùng MapLibre như precedent `feeder_ids`/`controller_node_ids`, không giả định popup nhận nguyên object JS từ rendered feature.

### D-05 — Câu 5: giữ ảnh, xoá cha, dời cột, import và audit

**Phương án:** hard delete ảnh; cấm xoá hoàn toàn; hoặc DELETE chỉ ẩn và giữ row/bytes. Hard delete khi còn bảo hành mâu thuẫn D-R16; hết bảo hành cũng không phải quyền tự xoá. Cabinet không có warranty và pole có thể thiếu fixture: không có căn cứ tính thời điểm purge.

**Đề xuất:** **DELETE = ẩn**, giữ row, key và bytes vô thời hạn tới quyết định retention riêng; không API purge/restore ở bản đầu. Row ẩn không trả list/bìa/gallery và không mở original/thumbnail qua API thường (404). DELETE lặp cùng ảnh đã ẩn, còn đúng cha trong scope → 204, không audit thứ hai. `photo_count`/quota chỉ tính ảnh chưa ẩn; bytes ảnh ẩn vẫn tích luỹ, monitoring phải tính cả chúng. Không thêm filter soft-delete EF toàn cục gây biến mất key khỏi monitoring: lọc ảnh ẩn ở truy vấn phục vụ người dùng, giữ filter xã vốn có.

- `DELETE /assets/poles/{id}` và cabinet khi còn **bất kỳ row ảnh nào, kể cả ẩn**, bị FK Restrict → `409 ASSET_IN_USE` theo `AssetCrudService.DeleteAsync`; không cascade ảnh để cho phép xoá cha. Ảnh làm tài sản có lịch sử, không còn là dòng rỗng có thể xoá. WP5 cần thông điệp này trước khi nhận ảnh đầu tiên.
- Dời cột hiện tại là sửa toạ độ cùng ID/cùng xã; ảnh nhận dạng vẫn đi theo cột, không sửa EXIF/giờ chụp. Nếu thực sự tháo cột cũ/lắp cột mới, đó là nghiệp vụ khác, không chuyển ảnh tự động. Chuyển xã chưa được API cho phép; FK ghép chặn xã lệch. Đổi `external_ref`, import upsert cùng ID giữ ảnh; import không nhận URL/file và không xoá gallery. Đổi `data_source` cha không viết lại nguồn ảnh đã nhận: ảnh giữ snapshot nguồn lúc upload, tránh sửa provenance lịch sử; phải nêu điều này khi duyệt schema.
- Ảnh ban ngày/ảnh cột `unknown` vẫn hợp lệ. Không sửa `pole_current_status`, không gọi AI/PoleStatusPublication, không tạo fault; thay fixture không chuyển/xoá ảnh cột. Ảnh hỏng quota/format không ghi row/audit. Không gộp ảnh vào điều kiện evidence `after` khi complete sửa chữa.

**Audit thực tế:** `AuditWriteGuard` yêu cầu đúng **một event mới** khi có thay đổi `IAudited` trong mỗi SaveChanges, không phải một event cho mỗi entity; audit append-only. Pole/cabinet hiện không implement IAudited cho toàn CRUD. Chỉ đánh dấu **hai entity ảnh mới**, stage `IAuditTrail.Record` cùng DbContext khi upload/ẩn. Không thêm IAudited lên cha làm mọi CRUD/import cũ thiếu audit.

Hai lựa chọn audit: (a) thêm action `PhotoAdded`/`PhotoHidden`; (b) dùng action đã có **`Created`/`DetailsChanged`**, thêm hai entity type `PoleAssetPhoto`/`CabinetAssetPhoto`. **Đề xuất (b)**: entity_id = photo_id, after/before có cha, commune, data_source, người/thời điểm upload hoặc ẩn; một event cho thao tác, không event riêng trên cha. Không cần migration action chỉ vì “có audit”; **vẫn cần nới `ck_audit_event_entity_type`**.

Kế hoạch migration audit (chưa viết/chạy):

- Up phương án đề xuất: drop/recreate **`ck_audit_event_entity_type`**, giữ nguyên tám giá trị đọc từ snapshot (E-05), thêm `pole_asset_photo`, `cabinet_asset_photo`. Giữ nguyên actor/action và mọi audit cũ.
- Down: kiểm trước có audit mang hai entity mới không; nếu có thì **từ chối**, không sửa/xoá audit để ép rollback. Nếu không có thì recreate đúng CHECK tám giá trị cũ. Tương tự từ chối drop hai bảng ảnh nếu còn dữ liệu; rollback ứng dụng có thể giữ schema thêm.
- Nếu Mỹ chọn action mới: Up còn phải drop/recreate **`ck_audit_event_action`**, giữ **đầy đủ 27 giá trị hiện tại ở E-05**, thêm `photo_added`, `photo_hidden`; Down từ chối nếu đã dùng hai action đó, rồi dựng lại CHECK cũ nguyên vẹn. Không chỉ sửa enum .NET. Thứ tự guard của Down phải trước mọi DropTable/DropConstraint có thể làm rollback dở dang.

### D-06 — Câu 6: offline WP6

**Hiện trạng:** SyncPush nhận năm loại op, kiểm quyền từng op; ảnh nghiệp vụ upload riêng trước push. `BLOCKED_BY_EARLIER_OP` chỉ chặn thao tác sau cùng target khi thao tác trước trong **lô push** đã conflict/rejected; lỗi upload ngoài push không tự tạo một blocker (E-06).

**Phương án:** hỗ trợ capture/cache/queue media trên mobile cùng một lần; hoặc online-only cho ảnh tài sản bản đầu, không đổi bundle/op_type.

**Đề xuất:** online-only như ticket gợi ý, **cần Mỹ và WP6 xác nhận giới hạn này**. Kỹ sư không gửi ảnh lên server khi mất sóng; API này cũng chưa cam kết tính năng chụp rồi tự retry sau của app. JPEG có sẵn trên máy có thể gửi thủ công khi có mạng. `client_op_id` hỗ trợ retry HTTP an toàn, không có nghĩa đã hỗ trợ offline trọn ca cho gallery.

Không thêm fake sync op ảnh. Không chặn `work_order_complete` do ảnh tài sản chưa gửi, không thay luật bắt buộc evidence after. Nếu WP6 cần offline gallery/capture: ticket tiếp phải chỉ rõ cache bytes/metadata, queue multipart riêng, idempotency, xử lý cha mất quyền/ảnh ẩn và phụ thuộc giữa media và ops; chỉ thêm sync op khi thật sự có thao tác metadata cần đi qua service/capability gốc. Bundle hiện không có cabinets/feeders/gallery; không hứa trạng thái mới sẽ tự xuất hiện ở đó.

## B. Trạng thái mạch điện

### D-07 — Câu 7: hiện trạng đã kiểm lại từ code

**Bằng chứng E-02/E-04:**

- `FeederControl` có feeder/node/commune/cabinet/relay, `FeederControlMode? ControlMode`, `DateTime? ModeReportedAt`, `long? ModeSeq`. PK feeder_id; unique `(node_id,relay_no)`, CHECK mode/timestamp đi cùng; HasContractEnum + filter xã. Số relay 1…32 là kiểm service; CHECK DB chỉ `relay_no > 0`. Không khẳng định thiết bị luôn có ít nhất một relay đã nối: node mới có thể `relays=[]`.
- `IotNodeRelay` đúng bốn trường ticket nêu; registry projection đọc mode/timestamp từ FeederControl. FeederListItem đúng 11 trường nêu trong ticket (có `cabinet`); detail là `{feeder,geom_wkt,created_at}`, không object feeder phẳng ở gốc. CabinetProperties đúng năm trường, mới có `feeder_ids[]`, chưa có `feeders[]`.
- Tìm source và snapshot không có `telemetry_reading`, `current_a`, `voltage_v`, `current_ma`, `ampere` (lệnh/exit trong E-04). Module còn ghi rõ TelemetryReading/ingest IOT-09/10 “still to come”. MQTT có topic/ACL telemetry nhưng handler switch chỉ receipt, ack, heartbeat; khai topic không phải ingest số đo. Job NodeOffline dựa im lặng, không đọc dòng/áp.

**Phương án:** expose control_mode như trạng thái điện hoặc tách control và power. **Đề xuất:** tách rõ như D-08; điều kiện hiện tại **mọi power_state = unknown**. Không suy ra dữ liệu DB đang triển khai/VPS từ source checkout, vì lượt này không kết nối DB.

### D-08 — Câu 8: hình dạng, enum và truy vấn không N+1

**Phương án shape:** một `status` mơ hồ; hoặc hai trường rõ nghĩa. **Đề xuất** shape thêm vào `FeederListItem` (detail dùng lại ở `feeder`):

```json
{
  "control": {
    "node_id": "NODE-001", "relay_no": 1,
    "control_mode": "auto", "mode_reported_at": "2026-10-27T10:00:00Z"
  },
  "power_state": "unknown"
}
```

Chưa nối relay: `control:null`, `power_state:"unknown"`. Có relay chưa báo: control vẫn có node/relay, hai trường mode/time **null**. Node offline vẫn giữ mode/time lịch sử, power unknown; không null control chỉ vì offline. Không đưa hai trường này vào PUT/create/import; inventory PUT không được ghi đè trạng thái thiết bị.

`/map/cabinets` giữ `feeder_ids[]` và thêm:

```json
{
  "feeders": [
    {"feeder_id":"FDR-001","relay_no":1,"control_mode":"auto","mode_reported_at":"2026-10-27T10:00:00Z","power_state":"unknown"},
    {"feeder_id":"FDR-002","relay_no":null,"control_mode":null,"mode_reported_at":null,"power_state":"unknown"}
  ]
}
```

Đây là **đề xuất thêm `mode_reported_at`** so với outline bốn trường của ticket để popup không hiện mode cũ như sự thật hiện tại. Node_id đã ở cabinet.iot_node_id, ràng buộc cùng tủ bảo vệ nên không nhân lại từng item. Liệt kê **mọi feeder của tủ**, kể cả không relay, đúng cùng scope/thứ tự với `feeder_ids`; không chỉ liệt kê các feeder thiết bị điều khiển. Tủ không feeder trả `[]` ở cả hai key.

Enum mới dự kiến `FeederPowerState` = `On|Off|Unknown`, JSON chuỗi `on|off|unknown` qua options chung; không bool nullable, không int, không thêm giá trị enum published. **Tính lúc đọc, không cột power_state**, vì vậy B ban đầu **không migration, không HasContractEnum/CHECK cho trường không lưu**. FeederControlMode đang có ở Telemetry, không tạo enum control thứ hai; DTO của port không được làm Assets tham chiếu ngược Telemetry. Nếu về sau materialize power_state thì HasContractEnum + CHECK ba giá trị là bắt buộc, cần D-item/migration riêng. Chưa thêm query filter power_state; khi có filter dùng `WireEnum.ParseCsv/Name`, không Enum.TryParse hay tự đặt mã số.

**Ràng buộc một SQL trong ticket cần Mỹ chốt tường minh:**

| Cách đọc | Hệ quả |
|---|---|
| Assets tham chiếu Telemetry rồi LINQ join | Tạo vòng phụ thuộc vì Telemetry đã tham chiếu Assets: loại. |
| Mở rộng port Assets-owned theo lô, Telemetry hiện thực | Theo `ICabinetDeviceLookup` hiện tại; 1 query bổ sung cho cả page, không N+1, ít thay đổi. **Không thoả chữ “một truy vấn” cho toàn projection.** |
| Port đọc có thể ghép query, hoặc port nhận query+projection feeder và để Telemetry hiện thực join | Có thể giữ chiều phụ thuộc và một SQL cho page projection; tăng interface/provider coupling và phải chứng minh EF dịch DTO/enum. Chưa có API port như vậy trong code, không coi là sẵn có. |

**Đề xuất ưu tiên tính đơn giản:** cho phép **một truy vấn control theo lô/page** qua port (không N+1), giữ `count + page` sẵn có; Mỹ phải chấp thuận ngoại lệ này trước P2. Nếu giữ yêu cầu **đúng một SQL projection**, chọn phương án port ghép query, làm spike không DB bằng `ToQueryString()` sau duyệt để chứng minh dịch được trước khi triển khai; không dùng hai query rồi báo đã đạt. Không nên di chuyển entity FeederControl sang Assets chỉ để rút một round trip.

Map đã tham chiếu Telemetry: có thể LEFT JOIN trực tiếp FeederControl vào collection feeder trong projection cabinet, materialize một lần; thiếu relay vẫn giữ feeder. Nên lấy `feeder_ids` từ cùng tập kết quả feeder để không lệch thứ tự/membership. Dùng DbSet có filter của cabinet/feeder/control/node; không IgnoreQueryFilters, không SQL raw bỏ scope. Mode và power phải cùng một read policy, không chép quy tắc thành hai nơi.

**Chưa kiểm SQL log/EXPLAIN**: Phase 1 cấm DB và không có query mới để đo. Phase 2 đếm SELECT cho cả 1 và 200 feeder, log SQL EF thật; count phân trang là query riêng hợp lệ, số round trip không tăng theo item. EXPLAIN trên DB Testcontainers, SQL từ `ToQueryString`, xác nhận bbox cabinet dùng GIST `geom` 4326 và join theo feeder_id/cabinet_id, không biến filter commune thành đọc hết rồi lọc RAM. Không đưa “một query” thành kết luận hiệu năng chưa đo.

### D-09 — Câu 9: unknown hôm nay và đo điện sau này

**Phương án:** trả unknown; hoặc suy on/off từ mode/ACK. **Đề xuất:** unknown vô điều kiện ở bản đầu, kể cả mode=on/off/auto và node online. WP5 dùng nhãn **“Chưa có dữ liệu điện”**, màu riêng, switch ba nhánh tường minh; cấm `power_state == 'on' ? 'Có điện' : 'Mất điện'`. Unknown không tính vào off/tỷ lệ mất điện; không sinh fault từ giá trị placeholder. OpenAPI mô tả rõ trạng thái đo chưa có và ví dụ mode=on, power=unknown. Client cũ bỏ qua trường mới; client mới cần chốt cách thể hiện.

Mở rộng giữ nguyên key/type khi IOT-09/10 tới: đọc số đo hợp lệ **đúng node+relay và mapping feeder tại lúc đo**, mới nhất theo thời gian đo; mất mapping, chưa từng báo, node offline (`NodeSilence.StatusAt`), không có số đo, số đo quá cũ/không hợp lệ → unknown. Heartbeat mới không được làm số đo cũ “tươi” trở lại; cần freshness của **từng relay**, không chỉ node. Đọc `SystemSettingValues` một lần/request; `now` cố định một lần/request; đúng ngưỡng offline vẫn online theo NodeSilence, không tự tạo luật im lặng khác.

**Cần chốt nghĩa “có điện”:** dòng điện chỉ chứng minh **đang có tải**, không chứng minh có điện áp khi tải bị ngắt/hỏng hoặc không mắc đèn. Theo hướng ticket có thể dùng `I > ngưỡng` là on, còn lại off, nhưng phải đặt tên diễn giải “có dòng tải” và được firmware/WP5 xác nhận; nếu FE thực sự cần “mạch đang có điện áp”, ưu tiên đo áp **sau relay**, chưa có phép đo đó thì unknown. Không thể hứa một sensor dòng sẽ xác định được mọi trường hợp mất điện. Đề xuất chưa chốt phép phân loại thực đo trong P2-B, chỉ chốt API/unknown.

Câu hỏi gửi BE2/nhóm firmware-testbed qua Mỹ (chưa gửi tin):

1. Có sensor dòng/áp **từng relay**, phía trước hay sau relay; RMS AC hay tức thời, đơn vị/độ phân giải/nhiễu/sàn đo? Hay chỉ đo tổng node? Tổng node không chia được thành trạng thái các feeder.
2. Có tải 0A nhưng mạch vẫn có điện không; tiêu chí FE muốn là có dòng, relay đóng, hay có điện áp? Ngưỡng, biên bằng ngưỡng, hysteresis cần căn cứ đo testbed.
3. Payload telemetry cuối cùng có node_id, relay_no, reading_time UTC, firmware version, schema_version, data_source gì? Chu kỳ số đo từng relay so với heartbeat 30s; có buffer/replay, SNTP, sample lỗi/thiếu, thứ tự ngược không?
4. Sau tháo/nối lại relay, làm sao không gán số đo feeder cũ cho feeder mới? Cần mapping epoch hoặc snapshot tại ingest/điều kiện thời điểm; thời gian đo và nhận phân biệt ra sao?

Ngưỡng dòng dự kiến (chưa đặt tên key/default) sẽ qua `system_setting` + audit setting hiện có. **Hiện hệ thống chỉ cho hai key dim/offline, có CHECK key/value**: thêm ngưỡng mới sau này cần migration nới CHECK+seed, không thể chỉ INSERT một key hoặc thêm appsettings (E-07). Freshness telemetry cũng phải đặc tả cấu hình, không lấy bừa ngưỡng offline cho mọi relay. Telemetry phải idempotent theo khoá đã chốt cùng Đạt, phân biệt data_source và ghi firmware lúc đo theo BE-34; không tự đổi thiết kế payload/bảng JSON relays của ERD.

### D-10 — Câu 10: tác động BE2/WP5/WP6

**Phương án:** triển khai UI như số đo thật ngay; hoặc bàn giao shape trước, giữ unknown tới ingest được kiểm chứng. **Đề xuất:** cách thứ hai.

| Nhóm | Cần xác nhận / trách nhiệm |
|---|---|
| BE2 – Đạt (IOT-09/10) | Schema/payload I-17 node+relay, phép đo từng relay, nguồn/đơn vị/chu kỳ, timestamp/freshness, dedup, out-of-order, firmware snapshot, mapping sau rewiring; cùng một reader tính power cho Map và Assets. B hiện tại không làm hộ ingest. |
| WP5 | Nhãn unknown riêng, mode kèm thời điểm **thiết bị báo lại**, điều khiển chờ ACK không tự đổi power; parse mảng/object MapLibre; giữ `feeder_ids` cho client cũ; xác nhận thêm timestamp trong feeders; photo online/quota/ẩn/bìa/quyền. |
| WP6 | Bản đầu bundle giữ nguyên, không có feeder/cabinet telemetry/media mới. Nếu cần hiển thị offline thì đặc tả snapshot có thời điểm và luôn trình bày là dữ liệu đã tải, không điện hiện tại. Xác nhận online-only gallery là giới hạn được chấp nhận; không ảnh hưởng luồng survey/evidence đang offline. |

Chưa có xác nhận của ba nhóm và chưa gửi tin ra ngoài trong lượt này. `supports_remote_control` vẫn chỉ testbed; trạng thái đo không tự mở quyền gửi lệnh, không đề xuất IoT lên từng cột/điều khiển lưới xã.

## C. Contract và Phase 2

### D-11 — Câu 11: Contract v1.21, drift và số operation

**Hiện trạng thực:** changelog có v1.20 nhưng heading Contract vẫn v1.19, spec `info.version=1.19`; JSON parse đếm **141 operation** (E-08). Đây là điểm cần rà version ở P2, **không sửa trong P1**. Các mục “0/1” trong ticket là cách gọi cũ: bản hợp nhất dùng §1 cho quy ước, §3.1 cho enum. Không lấy heading cũ để bỏ qua changelog/quyết định mới. CAB/LC/CB và một số schema read đang là nền SELF-SIGNED; quyết định drift đã chốt thắng chữ Contract chưa hợp nhất theo CLAUDE.md.

**Phương án:** sửa Contract để hợp thức hoá code sau; hoặc đăng ký quyết định trước và phát hành shape cộng thêm. **Đề xuất:** sau Mỹ chốt, đăng ký ASSET-PHOTOS D-items thành drift mới (mã chi tiết do người cập nhật chốt, không dùng lại CAB/LC), đủ Decision/Decision maker/Date/Scope, nền **v1.21 SELF-SIGNED** tới FW kế tiếp. Không ghi rằng Mỹ đã ký trong báo cáo này. Các lệch/đề xuất ở đây chỉ là đầu vào khảo sát theo giới hạn ticket, **chưa thay đăng ký drift chính thức**.

Phạm vi Contract cần thêm/sửa sau duyệt:

- §1.2 prefix PAP/CAP; §1.4 lỗi ảnh mới; §1.6 snapshot data_source ảnh; §2 capability UploadAssetPhotos + ngoại lệ upload kỹ sư dưới `/assets`; §3.1 enum power_state mới, §3.3 hai loại ảnh/lifecycle/retention.
- §5.3 endpoint ảnh, multipart/replay/quota/ẩn; §5.3.1 summary/gallery và feeder.control/power_state; §5.1 chi tiết cột asset_photos tách recent_frames; mô tả Map cabinets theo CAB-8/11 thêm photo summary/feeders, giữ feeder_ids; §5.6 phân biệt reported mode với đo điện; §5.8 ghi giới hạn online-only, chưa đổi op/bundle.
- §4.10 vẫn bốn bucket nếu dùng Evidence; nếu chọn bucket mới thì thành năm và cập nhật enum/spec tương ứng. §10 changelog v1.21 và status/version khi xuất spec. Không nhân tiện hợp nhất toàn bộ drift cũ trong ticket này.

**Operation theo đề xuất D-03:** mỗi tài sản 5 op (POST list-route, GET list, GET original, GET thumbnail, DELETE) × 2 = **10 mới**; **141 + 10 = 151**. B chỉ thêm response field nên **+0**. Không tính GET metadata/bìa vì không đề xuất chúng. Nếu thêm PUT bìa cho cả hai → 153; thêm GET metadata cả hai nữa → 155. Đây là phép tính thiết kế, chưa là spec đã sinh.

Tất cả shape chỉ thêm, không xoá/đổi tên field/enum published, giữ `feeder_ids`, `recent_frames`, quyền cũ; không thêm field bắt buộc vào request CRUD cũ. Hệ quả cố ý cần báo: tài sản có ảnh mới bị FK giữ, ảnh DELETE có nghĩa ẩn. Client mới xác nhận nullability `control`, mode/time, cover; Swagger có bẫy `$ref` nullable trần, cần test schema không dựa vào build xanh. Xuất `luxmap-v1.json` → generator hợp nhất → lint, rà schema viết tay đè ở generator; không chỉnh JSON sinh tay. WP5/WP6 xác nhận shape, quyền, UX unknown/ẩn/online, BE2 xác nhận semantics đo. SELF-SIGNED không thay xác nhận liên nhóm này.

### D-12 — Câu 12: kế hoạch P2, PR, migration, test và phá thử

**Phương án:** một PR lớn cả hai phần; hoặc hai PR độc lập. **Đề xuất:** hai PR vào dev, làm **B trước** (shape unknown hữu ích, không DB change), rồi **A** sau khi chốt D-01…06. Chốt ngoại lệ số SQL ở D-08 trước khi code B. Nếu Mỹ giữ thứ tự A trước thì không phụ thuộc kỹ thuật bắt buộc đảo; cả hai vẫn dùng một bộ quyết định v1.21, PR đầu ghi phạm vi nào đã hiện thực, PR sau hoàn chỉnh tổng 151 op; không ghi 151 ngay khi chỉ merge B.

**Số migration không đồng nghĩa số PR:** theo khuyến nghị Evidence + hai bảng ảnh + audit entity types, B **0 migration**, A **1 migration** chứa hai bảng/sequence/index/FK/CHECK và nới audit entity CHECK. Không cần “hai migration” chỉ vì có hai bảng. Nếu Mỹ yêu cầu tách audit/schema thành hai migration thì cả hai nằm PR A, mỗi Down phải có guard dữ liệu; nếu chọn bucket mới nó là thay đổi provision, không tự sinh migration DB. Ngưỡng/telemetry thật là PR của IOT-09/10 sau này và có migration riêng khi được duyệt.

Thứ tự A: duyệt schema/lifecycle/quyền → model+schema/audit/storage registrations/PLAN → service/controller → read projections → tests/spec/docs/deploy nếu chọn bucket mới. Up chỉ tạo những đối tượng đã duyệt, tái dùng alternate key cha+xã đúng sở hữu, không đụng index/cột cũ không liên quan; đọc **cả Up/Down** trước apply. Rollback ưu tiên quay app cũ giữ schema/bucket/bytes; không drop bảng có dữ liệu hoặc thu CHECK audit khi có giá trị mới, không xoá audit để rollback. Đổi dependency dự kiến **không cần package mới**: module Assets dùng IObjectStore/IOriginalJpegReader ở Shared đã được host đăng ký, không kéo ImageSharp/S3 vào Assets. Không thêm project reference ngược.

Danh sách **test dự kiến, chưa viết/chạy**:

| Nhóm dự kiến | Trường hợp có giá trị và phá thử |
|---|---|
| `AssetPhotoSchemaTests` | Cả hai FK cha/xã/user Restrict, trực tiếp ghi ảnh khác xã nhưng caller có cả hai xã vẫn bị FK cha+xã từ chối; ICommuneScoped/query filter/write guard; enum data_source, key/byte/pixel/UUID/deleted pair CHECK, index commune đầy đủ. Phá lần lượt FK ghép/filter/guard/CHECK → test tương ứng đỏ (guard test có audit hợp lệ). |
| `AssetPhotoTests` | Upload/list/original/thumbnail cho cả pole/cabinet, original nguyên bytes và thumbnail không EXIF/XMP/IPTC; JPEG giả MIME vẫn nhận, PNG đổi đuôi/JPEG hỏng bị 415; size/pixel/extra field/bytes vượt dù khai nhỏ; count=0/bìa/null/gallery≤10; không gộp frame; ảnh không đổi trạng thái/fault. Phá magic/strip metadata/bounded read hoặc thêm publication sai → đỏ. |
| `AssetPhotoAccessTests` | 4 roles × read/upload/delete, scope foreign/parent-photo mismatch 404, chưa login 401, server-owned fields 400, * không giúp system_admin ghi, không AllowAnonymous. Phá từng policy/parent filter → đỏ; literal matrix/coverage cũng đỏ. |
| `AssetPhotoConcurrencyTests` | Hai upload cạnh quota 9→10; retry cùng UUID/content 200 cùng ID, khác content/cha 409; cha bị xoá/nguồn đổi trong lúc storage đang chạy, kiểm lại sau khoá; store lỗi/DB lỗi không row/audit rời; DELETE lặp không event mới. Phá khoá/recheck/replay trước quota → đỏ. Test khoá dùng FOR NO KEY UPDATE. |
| `AssetPhotoLifecycleTests` | Ẩn vẫn giữ bytes/key và chặn xoá cha; media 404 sau ẩn, bìa chuyển đúng; import/move/đổi bóng không mất ảnh, không đổi fixture_status; replay ảnh đã ẩn không hồi sinh (đề xuất trả 404 ảnh); no-op không audit thêm. Phá soft-delete predicate/Restrict → đỏ. |
| `SystemMonitoringTests`, `PrefixedIdOrderingTests`, `PrefixedIdOverflowTests`, `AuditGuardTests` | Bốn StoredObjectColumn được khai và có byte thật cả row ẩn, reconciliation không coi chúng orphan; thiếu object 503; ID qua ngưỡng cùng transaction/sắp có thứ tự; mỗi SaveChanges ảnh một audit, rollback giữ audit append-only. Bỏ một registration hoặc sắp ID text → đỏ. Apply/Down/reapply chỉ DB test trống và riêng; Down từ chối khi có ảnh/audit mới. |
| `FeederStateTests` | List/detail/control null, relay chưa báo, on/off/auto đều power unknown; map mọi feeder kể cả không relay, giữ feeder_ids/thứ tự; timestamp null cùng mode; scope hai xã và cùng tủ; không thêm field bbox pole/segment/sync. Phá bằng ánh xạ mode→power hoặc inner join làm mất feeder không relay → đỏ. |
| `FeederStateQueryTests`, `MapQueryPlanTests` | SQL count không tăng từ 1→200 item; theo quyết định D-08 đạt budget một projection SQL hoặc một batch bổ sung; EXPLAIN từ SQL EF thật, bbox 4326 GIST. Phá bằng query trong foreach hoặc bọc geom → đỏ. Chưa có kết quả hiệu năng ở P1. |
| `OpenApiSpecTests` và ba test capability hiện có | 151 op sau cả A+B, request multipart, lỗi, nullable control/cover/time, enum chuỗi và shape additive; snapshot schema cũ giữ field. Phá policy, số enum, schema viết tay/nullable → test/lint tương ứng đỏ. |

P2-B không cần test on/off từ số đo chưa tồn tại. Khi IOT-09/10 tới: test riêng zero-current nhưng có voltage, thiếu/sai relay, stale measurement với heartbeat mới, threshold hai phía/đúng biên, node silence đúng biên, NaN/Infinity, out-of-order/replay, rewiring, firmware/data_source và parity Map/Assets. Chốt nghĩa điện trước khi viết kỳ vọng.

Test tích hợp P2 dùng LuxMapTestHost/Testcontainers PostGIS, nhóm ghi tài sản ở AssetDatabaseCollection; teardown con→cha→user đúng Restrict, không sửa account seed, không ghim ID. Mỗi phá thử khôi phục bằng cp/touch và đọc exit code thật. Chỉ chạy tập test thích hợp cùng build disable-build-servers; không dùng `luxmap_dev`. Những việc này là **kế hoạch**, không có số test pass giả trong P1.

## Kiểm chứng và điểm dừng

- ✅ Verified bằng đọc source/snapshot/spec và output terminal: shape hiện có, hạ tầng bucket/audit, thiếu số đo điện, tổng 141 op; đủ 12 câu hỏi và D-item có phương án/đề xuất.
- ⚠️ Could not verify trong phạm vi P1: DB runtime/VPS, SQL dịch của thiết kế chưa viết, EXPLAIN, race, upload MinIO và hành vi client. Không chạy build/test vì chỉ thay báo cáo; không kết nối DB.
- 📌 Remaining assumptions: Mỹ chốt D-01…D-12; đặc biệt hai bảng vs XOR, Evidence vs bucket riêng, ẩn/retention/FK, online-only WP6, ngoại lệ một SQL, nghĩa có điện và phép đo firmware. Không coi im lặng là approve API.

Chỉ tạo/commit file kết quả này; không cập nhật tracking/task/current theo lệnh ticket và người dùng. **DỪNG HẲN ở Phase 1**, không viết P2 kể cả với phần đã rõ; không push.

## Phụ lục E — bằng chứng đọc checkout

Các đoạn bên dưới được lấy bằng lệnh chỉ đọc và chép output thực, gồm mã thoát khi tìm không thấy. Không phải output truy vấn DB.

### E-01 — mốc khảo sát và tài liệu precedent

```text
$ git rev-parse HEAD
97c144337a83b30feadb1d6573233355a43b12b8
[exit 0]

$ git branch --show-current
docs/ASSET-PHOTOS-p1
[exit 0]

$ ls -l AGENTS.md
lrwxr-xr-x@ 1 nhm809  staff  9 Oct 27 22:39 AGENTS.md -> CLAUDE.md
[exit 0]

$ git ls-files '.ai/results/*BE-11*' '.ai/results/*BE-24*'
.ai/results/BE-24.md
[exit 0]

```

### E-02 — model/shape tài sản và ảnh phiếu

```text
$ rg -n 'public class|public .* (ControlMode|ModeReportedAt|ModeSeq|FeederId|NodeId|RelayNo|CommuneId|CabinetId)' src/LuxMap.Modules.Telemetry/Entities/FeederControl.cs
22:public class FeederControl : ICommuneScoped
24:    public required string FeederId { get; set; }
26:    public required string NodeId { get; set; }
32:    public required string CommuneId { get; set; }
39:    public required string CabinetId { get; set; }
42:    public short RelayNo { get; set; }
45:    public FeederControlMode? ControlMode { get; set; }
47:    public DateTime? ModeReportedAt { get; set; }
54:    public long? ModeSeq { get; set; }
[exit 0]

$ sed -n 686,740p src/LuxMap.Modules.Assets/Crud/AssetContracts.cs
public sealed record FeederListItem
{
    public required string FeederId { get; init; }

    public string? ExternalRef { get; init; }

    public required string FeederName { get; init; }

    public required string CommuneId { get; init; }

    /// <summary>
    /// The commune's name now (ASSET-COMMUNE-NAME), read at request time from <c>administrative_unit</c> — so a renamed commune shows
    /// its new name and an inventory row needs no second call. Never stored on the asset.
    /// </summary>
    public required string CommuneName { get; init; }

    /// <summary>
    /// Whether a cable route was recorded, rather than the route itself.
    /// </summary>
    /// <remarks>
    /// Branch C surveyed no cable routes, so this is <c>false</c> for almost every feeder. Shipping a
    /// WKT column that is nearly always null in every list row costs bandwidth for nothing; the
    /// detail endpoint carries the geometry for the one feeder a caller actually opened.
    /// </remarks>
    public required bool HasGeometry { get; init; }

    public required int PoleCount { get; init; }

    /// <summary>
    /// The cabinet this circuit leaves from, with its point (CAB-6) — <c>null</c> when none is recorded. In the
    /// LIST, like a pole's <c>active_fixture</c>: an inventory row shows where the cabinet is, and a bare id would
    /// cost one request per row.
    /// </summary>
    public FeederCabinet? Cabinet { get; init; }

    public required DateTime UpdatedAt { get; init; }

    /// <summary>Who last changed the row by form, note or import (e.g. <c>USR-004</c>); <c>null</c> for system-loaded rows.</summary>
    public string? UpdatedBy { get; init; }

    /// <summary>That account's display name now, so a screen can say who without another call.</summary>
    public string? UpdatedByName { get; init; }
}

/// <summary>One feeder read on its own.</summary>
public sealed record FeederDetail
{
    public required FeederListItem Feeder { get; init; }

    /// <summary>WKT <c>LINESTRING</c>, or <c>null</c> — most feeders have no surveyed route.</summary>
    public string? GeomWkt { get; init; }

    public required DateTime CreatedAt { get; init; }
}

[exit 0]

$ sed -n 49,59p src/LuxMap.Modules.Telemetry/Registry/IotNodeContracts.cs
public sealed record IotNodeRelay
{
    public required int RelayNo { get; init; }

    public required string FeederId { get; init; }

    /// <summary>What the DEVICE last reported for this relay (I-6) — never what was last requested.</summary>
    public FeederControlMode? ControlMode { get; init; }

    public DateTime? ModeReportedAt { get; init; }
}
[exit 0]

$ sed -n 177,191p src/LuxMap.Modules.Map/Features/MapProperties.cs
public sealed record CabinetProperties
{
    public required string CabinetId { get; init; }

    public required string CabinetName { get; init; }

    public required string CommuneId { get; init; }

    /// <summary>Feeders leaving from this cabinet that the caller can see, in id order; <c>[]</c> when none.</summary>
    public required IReadOnlyList<string> FeederIds { get; init; }

    /// <summary>The device mounted in the cabinet, or <c>null</c>.</summary>
    public string? IotNodeId { get; init; }
}
[exit 0]

$ rg -n 'ToTable|HasKey|HasContractEnum|HasCommune|Restrict|HasPrefixedId' src/LuxMap.Modules.WorkOrders/Configurations/WorkOrderPolePhotoConfiguration.cs
16:        b.ToTable("work_order_pole_photo", t =>
28:        b.HasKey(x => x.PhotoId);
30:        b.Property(x => x.PhotoId).HasPrefixedId(PrefixedIds.WorkOrderPolePhoto);
31:        b.HasContractEnum(x => x.DataSource);
32:        b.HasCommuneScope();
33:        b.HasCommuneReference(x => x.CommuneId);
34:        b.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
35:        b.HasOne<Pole>().WithMany().HasForeignKey(x => x.PoleId).OnDelete(DeleteBehavior.Restrict);
37:        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UploadedBy).OnDelete(DeleteBehavior.Restrict);
38:        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.DecidedBy).OnDelete(DeleteBehavior.Restrict);
39:        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ReviewedBy).OnDelete(DeleteBehavior.Restrict)
55:            .HasPrincipalKey(x => new { x.PhotoId, x.PoleId, x.CommuneId }).OnDelete(DeleteBehavior.Restrict);
56:        b.ToTable("fault", t => t.HasCheckConstraint("ck_fault_pole_photo_source",
[exit 0]

$ rg -n 'ak_pole_photo_target|fk_pole_photo_same_commune' src/LuxMap.Persistence/Migrations/20261015153829_AddWorkOrderPolePhotos.cs
137:            migrationBuilder.Sql("ALTER TABLE pole ADD CONSTRAINT ak_pole_photo_target UNIQUE (pole_id, commune_id)");
138:            migrationBuilder.Sql("ALTER TABLE work_order_pole_photo ADD CONSTRAINT fk_pole_photo_same_commune FOREIGN KEY (pole_id, commune_id) REFERENCES pole (pole_id, commune_id) ON DELETE RESTRICT");
174:            migrationBuilder.Sql("ALTER TABLE pole DROP CONSTRAINT ak_pole_photo_target");
[exit 0]

```

### E-03 — storage, provision, monitoring, copy rows

```text
$ rg -n 'SurveyBucketName|EvidenceBucketName|VideoBucketName|CitizenBucketName|return.*prefix|StoreImageAsync|OpenAsync|ExistsAsync' src/LuxMap.Shared/Storage/ObjectStorage.cs
53:    public const string SurveyBucketName = "luxmap-survey";
55:    public const string EvidenceBucketName = "luxmap-evidence";
57:    public const string VideoBucketName = "luxmap-video";
60:    public const string CitizenBucketName = "luxmap-citizen";
73:        StorageBucket.Survey => SurveyBucketName,
74:        StorageBucket.Evidence => EvidenceBucketName,
75:        StorageBucket.Video => VideoBucketName,
76:        StorageBucket.Citizen => CitizenBucketName,
92:        return $"{prefix}/{id}{JpegExtension}";
151:    Task<StoredImage> StoreImageAsync(
155:    Task<Stream> OpenAsync(
158:    Task<bool> ExistsAsync(
[exit 0]

$ rg -n 'LongestEdgePixels =|JpegQuality =|JpegOnly|image.Metadata' src/LuxMap.Infrastructure.Storage/ThumbnailFactory.cs
22:    public const int LongestEdgePixels = 320;
25:    public const int JpegQuality = 80;
41:    public static Configuration JpegOnly { get; } = new(new JpegConfigurationModule());
62:        var options = new DecoderOptions { Configuration = JpegOnly };
80:        image.Metadata.ExifProfile = null;
81:        image.Metadata.XmpProfile = null;
82:        image.Metadata.IptcProfile = null;
[exit 0]

$ rg -n 'mc mb' deploy/compose.yml docker-compose.yml
deploy/compose.yml:86:        mc mb --ignore-existing luxmap/luxmap-survey
deploy/compose.yml:87:        mc mb --ignore-existing luxmap/luxmap-evidence
deploy/compose.yml:88:        mc mb --ignore-existing luxmap/luxmap-video
deploy/compose.yml:89:        mc mb --ignore-existing luxmap/luxmap-citizen
docker-compose.yml:180:        mc mb --ignore-existing luxmap/luxmap-survey
docker-compose.yml:181:        mc mb --ignore-existing luxmap/luxmap-evidence
docker-compose.yml:182:        mc mb --ignore-existing luxmap/luxmap-video
docker-compose.yml:183:        mc mb --ignore-existing luxmap/luxmap-citizen
[exit 0]

$ rg -n 'for bucket|mc mirror' deploy/backup.sh
27:  for bucket in luxmap-survey luxmap-evidence luxmap-video luxmap-citizen; do
28:    mc mirror --quiet --overwrite "luxmap/$bucket" "/backup/$bucket"
[exit 0]

$ rg -n 'PLAN =|work_order_pole_photo|repair_evidence|bảng chưa có' scripts/copy_dev_to_supabase.py
71:              f"(SELECT photo_id FROM public.work_order_pole_photo WHERE {POLE_PHOTO_KEPT}))")
81:PLAN = [
132:    ("work_order_pole_photo", POLE_PHOTO_KEPT, "captured_at, uploaded_at, length(photo_id), photo_id"),
136:    ("repair_evidence", f"{IN_COMMUNES} AND (fault_id IS NULL OR fault_id IN (SELECT fault_id FROM public.fault WHERE {FAULT_KEPT}))",
343:        raise Refused(f"bảng chưa có trong kế hoạch: {unplanned} — thêm vào PLAN có chủ đích")
[exit 0]

$ rg -n 'Enum.GetValues|count\(|sum\(|KeyColumn' src/LuxMap.Modules.Admin/Monitoring/StorageUsage.cs
22:            if (!Identifier().IsMatch(column.Table) || !Identifier().IsMatch(column.KeyColumn) || !Identifier().IsMatch(column.BytesColumn))
32:        var totals = Enum.GetValues<StorageBucket>().ToDictionary(bucket => bucket, _ => (0L, 0L));
36:            $"SELECT {i} AS n, count({c.KeyColumn})::bigint, (coalesce(sum({c.BytesColumn}) FILTER (WHERE {c.KeyColumn} IS NOT NULL), 0))::bigint FROM {c.Table}"));
56:                $"SELECT {column.KeyColumn} FROM {column.Table} WHERE {column.KeyColumn} IS NOT NULL");
[exit 0]

```

### E-04 — telemetry thiếu số đo, có heartbeat/ACK

```text
$ rg -n 'current_a|voltage_v|current_ma|ampere|telemetry_reading' src --glob '*.cs'
[exit 1]

$ rg -n 'still to come|IScheduledJob' src/LuxMap.Modules.Telemetry/TelemetryModule.cs src/LuxMap.Modules.Telemetry/Jobs/NodeOfflineJob.cs
src/LuxMap.Modules.Telemetry/TelemetryModule.cs:11:/// (<c>feeder_control</c>), BE-14b. TelemetryReading and ingest (IOT-09/10) are still to come.
src/LuxMap.Modules.Telemetry/TelemetryModule.cs:24:        services.AddSingleton<LuxMap.Persistence.Jobs.IScheduledJob, Jobs.NodeOfflineJob>();
src/LuxMap.Modules.Telemetry/Jobs/NodeOfflineJob.cs:39:public sealed class NodeOfflineJob(JobContextFactory contexts, ILogger<NodeOfflineJob> log) : IScheduledJob
[exit 0]

$ rg -n '\[Route|\[Http' src/LuxMap.Modules.Telemetry/Lighting/DeviceCommandsController.cs
20:[Route("api/v{version:apiVersion}/device/commands")]
28:    [HttpGet]
35:    [HttpPost("{commandId}/ack")]
[exit 0]

$ rg -n 'case MqttTopics|Telemetry =' src/LuxMap.Modules.Telemetry/Mqtt/MqttLightingHandler.cs src/LuxMap.Modules.Telemetry/Mqtt/MqttMessages.cs
src/LuxMap.Modules.Telemetry/Mqtt/MqttMessages.cs:31:    public const string Telemetry = "telemetry";
src/LuxMap.Modules.Telemetry/Mqtt/MqttLightingHandler.cs:44:                case MqttTopics.Receipt when Read<MqttReceiptMessage>(payload) is { CommandId: { } id, Seq: { } seq } && MqttTopics.IsCommandId(id):
src/LuxMap.Modules.Telemetry/Mqtt/MqttLightingHandler.cs:48:                case MqttTopics.Ack when Read<MqttAckMessage>(payload) is { CommandId: { } id } ack && MqttTopics.IsCommandId(id):
src/LuxMap.Modules.Telemetry/Mqtt/MqttLightingHandler.cs:51:                case MqttTopics.Heartbeat when Read<MqttHeartbeatMessage>(payload) is { } heartbeat:
[exit 0]

$ rg -n MqttTopics.Telemetry src/LuxMap.Modules.Telemetry/Mqtt/BrokerAuthController.cs
98:        new("allow", "publish", MqttTopics.Of(nodeId, MqttTopics.Telemetry), Retain: false),
111:        new("allow", "subscribe", "eq " + $"{MqttTopics.Root}/+/{MqttTopics.Telemetry}"),
[exit 0]

$ sed -n 275,294p src/LuxMap.Modules.Telemetry/Registry/IotNodeRegistryService.cs
            NodeRole = node.NodeRole,
            DataSource = node.DataSource,
            SupportsRemoteControl = node.SupportsRemoteControl,
            HasCredential = node.CredentialHash != null,
            CredentialSetAt = node.CredentialSetAt,
            LastReportAt = node.LastReportAt,
            Relays = db.Set<FeederControl>()
                .Where(control => control.NodeId == node.NodeId)
                .OrderBy(control => control.RelayNo)
                .Select(control => new IotNodeRelay
                {
                    RelayNo = control.RelayNo,
                    FeederId = control.FeederId,
                    ControlMode = control.ControlMode,
                    ModeReportedAt = control.ModeReportedAt,
                })
                .ToList(),
            UpdatedAt = node.UpdatedAt,
        };

[exit 0]

$ sed -n 82,128p src/LuxMap.Modules.Telemetry/Configurations/IotConfigurations.cs
public sealed class FeederControlConfiguration : IEntityTypeConfiguration<FeederControl>
{
    public void Configure(EntityTypeBuilder<FeederControl> builder)
    {
        builder.ToTable("feeder_control", table =>
        {
            table.HasCheckConstraint("ck_feeder_control_relay_no_positive", "relay_no > 0");
            table.HasCheckConstraint("ck_feeder_control_mode_reported",
                "(control_mode IS NULL) = (mode_reported_at IS NULL)");
            table.HasCheckConstraint("ck_feeder_control_mode_seq_has_mode", "mode_seq IS NULL OR control_mode IS NOT NULL");
        });

        // One device per feeder at most: the feeder IS the key (I-12).
        builder.HasKey(control => control.FeederId);

        builder.Property(control => control.FeederId).HasColumnType("text");
        builder.Property(control => control.NodeId).HasColumnType("text").IsRequired();

        // CAB-4: the database-only keys fk_feeder_control_feeder_same_cabinet and
        // fk_feeder_control_node_same_cabinet include it (raw SQL — see CabinetConstraints).
        builder.Property(control => control.CabinetId).HasColumnType("text").IsRequired();
        builder.HasContractEnum(control => control.ControlMode);

        builder.HasCommuneScope();

        // Both keys carry commune_id, so a device can only switch a feeder of its own commune.
        builder.HasOne<Feeder>().WithMany()
            .HasForeignKey(control => new { control.FeederId, control.CommuneId })
            .HasPrincipalKey(feeder => new { feeder.FeederId, feeder.CommuneId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<IotNode>().WithMany()
            .HasForeignKey(control => new { control.NodeId, control.CommuneId })
            .HasPrincipalKey(node => new { node.NodeId, node.CommuneId })
            .OnDelete(DeleteBehavior.Restrict);

        // Two feeders cannot claim the same relay of the same device.
        builder.HasIndex(control => new { control.NodeId, control.RelayNo })
            .IsUnique()
            .HasDatabaseName("ux_feeder_control_node_id_relay_no");

        // Explicit: the query filter puts commune_id in every WHERE (partial-index convention).
        builder.HasIndex(control => control.CommuneId);

        builder.HasCommuneReference(control => control.CommuneId);
    }
}
[exit 0]

```

### E-05 — audit hiện tại (snapshot, không phải catalog DB)

```text
$ cat src/LuxMap.Persistence/Audit/AuditWriteGuard.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace LuxMap.Persistence.Audit;

internal static class AuditWriteGuard
{
    public static void Enforce(ChangeTracker tracker, bool systemWrite)
    {
        var auditEntries = tracker.Entries<AuditEvent>().ToArray();
        if (auditEntries.Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Audit events cannot be updated or deleted.");
        }

        if (!systemWrite
            && tracker.Entries<IAudited>().Any(entry =>
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            && auditEntries.Count(entry => entry.State == EntityState.Added) != 1)
        {
            throw new InvalidOperationException("An audited operation requires exactly one new audit event.");
        }
    }
}
[exit 0]

$ rg -n 'ck_audit_event_action|ck_audit_event_entity_type' src/LuxMap.Persistence/Migrations/LuxMapDbContextModelSnapshot.cs
4248:                            t.HasCheckConstraint("ck_audit_event_action", "\"action\" IN ('cancellation_requested', 'cancellation_rejected', 'created', 'assigned', 'reassigned', 'unassigned', 'started', 'completed', 'verified', 'returned', 'cancelled', 'details_changed', 'confirmed', 'rejected', 'submitted', 'requested', 'delivered', 'applied', 'failed', 'expired', 'superseded', 'reported', 'revoked', 'accepted', 'merged', 'analyzed', 'relocated')");
4260:                            t.HasCheckConstraint("ck_audit_event_entity_type", "\"entity_type\" IN ('work_order', 'fault', 'survey_sweep', 'lighting_request', 'lighting_command', 'qr_code', 'citizen_report', 'pole')");
[exit 0]

```

### E-06 — port module và đồng bộ

```text
$ cat src/LuxMap.Modules.Assets/ICabinetDeviceLookup.cs
namespace LuxMap.Modules.Assets;

/// <summary>
/// What the Telemetry module knows about devices, asked from the Assets side (CABINET).
/// </summary>
/// <remarks>
/// <para>
/// A port because Assets cannot reference Telemetry — the reference already runs the other way
/// (<c>feeder_control</c> points at <c>feeder</c>). Same shape as <c>IActiveWorkOrderLookup</c> between
/// Faults and WorkOrders. Telemetry implements it.
/// </para>
/// <para>
/// Both answers go through the commune query filter. That narrows nothing that matters: a device sits in its
/// cabinet's commune and a relay row in its feeder's (composite keys), so whoever can see the cabinet or the
/// feeder sees the device too.
/// </para>
/// </remarks>
public interface ICabinetDeviceLookup
{
    /// <summary>cabinet_id → node_id for those of <paramref name="cabinetIds"/> that carry a device.</summary>
    Task<IReadOnlyDictionary<string, string>> DevicesAsync(IReadOnlyCollection<string> cabinetIds, CancellationToken ct);

    /// <summary>
    /// feeder_id → node_id for those of <paramref name="feederIds"/> a device switches. Such a feeder cannot move
    /// to another cabinet or leave its cabinet (CAB-4).
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> ControllersAsync(IReadOnlyCollection<string> feederIds, CancellationToken ct);
}
[exit 0]

$ sed -n 44,47p src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs
    public Task<PagedResult<FeederListItem>> ListFeedersAsync(
        IReadOnlyList<string>? communes, PageRequest page, CancellationToken ct)
        => ListAsync(communes, page, (Feeder feeder) => feeder.FeederId, FeederRow, ct);

[exit 0]

$ sed -n 199,208p src/LuxMap.Modules.Sync/SyncPushService.cs
    private static string PolicyFor(SyncOpType type) => type switch
    {
        SyncOpType.FaultReport => LuxMapPolicies.ReportFaults,
        SyncOpType.LuxReading => LuxMapPolicies.RecordLuxReading,
        SyncOpType.PoleNote => LuxMapPolicies.EditPoleNotes,
        SyncOpType.WorkOrderStart or SyncOpType.WorkOrderComplete => LuxMapPolicies.ExecuteWorkOrders,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    // ── Parsing: the payload is the body of the operation's own endpoint ──────────────────────────────────
[exit 0]

```

### E-07 — ngưỡng offline và cấu hình hiện có

```text
$ cat src/LuxMap.Modules.Telemetry/NodeSilence.cs
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Telemetry;

/// <summary>
/// The one rule for a silent device (I-3). The threshold is the <c>iot_offline_after_minutes</c> system setting (BE-33), read by the
/// caller from <c>SystemSettingValues</c>; the map and the <c>node_offline</c> job both come through here.
/// </summary>
public static class NodeSilence
{
    /// <summary>
    /// <c>never_reported</c> when the device never reported; <c>offline</c> when its last report is OLDER than
    /// <paramref name="offlineAfter"/>; otherwise <c>online</c>. Exactly at the threshold is still online — "silent for longer than"
    /// the threshold.
    /// </summary>
    public static NodeStatus StatusAt(DateTime? lastReportAt, DateTime now, TimeSpan offlineAfter)
        => lastReportAt is not { } last ? NodeStatus.NeverReported
            : now - last > offlineAfter ? NodeStatus.Offline
            : NodeStatus.Online;
}
[exit 0]

$ rg -n 'const string|new\(Dim|new\(Iot|ck_system_setting_key|ck_system_setting_value|ToDictionaryAsync' src/LuxMap.Persistence/Settings/SystemSetting.cs
58:    public const string DimThresholdRatio = "dim_threshold_ratio";
61:    public const string IotOfflineAfterMinutes = "iot_offline_after_minutes";
65:        new(DimThresholdRatio, 0.80, 0.50, 0.95, "ratio", WholeNumber: false),
66:        new(IotOfflineAfterMinutes, 60, 5, 1440, "minutes", WholeNumber: true),
80:        var rows = await db.Set<SystemSetting>().AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);
93:            table.HasCheckConstraint("ck_system_setting_key",
95:            table.HasCheckConstraint("ck_system_setting_value", SystemSettingSql.ValueInRange("key", "value"));
[exit 0]

```

### E-08 — đếm và đọc OpenAPI bằng JSON parser

```python
import json
s = json.load(open("docs/openapi/luxmap-v1.5.json"))
verbs = {"get", "post", "put", "patch", "delete", "head", "options", "trace"}
print("info.version:", s["info"]["version"])
print("operations:", sum(m in verbs for x in s["paths"].values() for m in x))
for name in ["FeederListItem", "FeederDetail", "CabinetProperties", "IotNodeRelay", "PoleMapDetail"]:
    print(name, list(s["components"]["schemas"][name]["properties"]))
```

```text
info.version: 1.19
operations: 141
FeederListItem ['feeder_id', 'external_ref', 'feeder_name', 'commune_id', 'commune_name', 'has_geometry', 'pole_count', 'cabinet', 'updated_at', 'updated_by', 'updated_by_name']
FeederDetail ['feeder', 'geom_wkt', 'created_at']
CabinetProperties ['cabinet_id', 'cabinet_name', 'commune_id', 'feeder_ids', 'iot_node_id']
IotNodeRelay ['relay_no', 'feeder_id', 'control_mode', 'mode_reported_at']
PoleMapDetail ['pole_id', 'segment_id', 'segment_name', 'commune_id', 'location', 'fixture', 'current_status', 'iot_node', 'luminance_baseline', 'luminance_baselines', 'luminance_history', 'runtime_history', 'open_faults', 'recent_frames', 'note']
```
