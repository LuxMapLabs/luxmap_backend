---
ticket: FEEDER-SCOPE
reviewer: codex (gpt-6.1-sol, reasoning high)
date: 2026-10-07
scope: khảo sát chỉ đọc — luxmap-web (origin/dev, FW-04, FW-05), mobile (origin/dev, origin/main), Phiếu v1.4, tasks-backend.csv
tokens: 151337
---

> Nguyên văn. Hai phát hiện FE nặng nhất (useElectricalCascade chia đều cột, AddPoleModal bắt buộc chọn tủ) Claude đã kiểm lại bằng git show.
> Tổng hợp: `.ai/results/FEEDER-SCOPE-impact.md`.

**Bỏ feeder tác động trực tiếp đến quản lý tài sản trên web, bản đồ điện và mô phỏng mất điện. Bỏ thêm tủ điện làm mất các màn hình quản lý/chi tiết tủ. Mobile hiện chưa dùng feeder/tủ trong UI, nhưng có các trường IoT bắt buộc khi đọc dữ liệu. Đăng ký v1.4 cho phép thiếu dữ liệu feeder; vẫn cam kết chức năng quản lý feeder/tủ và nghiên cứu liên quan.**

✅ Đã kiểm tra mã/tài liệu, chỉ đọc; không ghi file, build, test hay đọc `luxmap_backend/src`.  
⚠️ Các ảnh hưởng dưới đây là phân tích tĩnh, chưa xác minh runtime. Không xác minh độc lập việc EVN/xã có dữ liệu.

Quy ước ref; số dòng thuộc **ref**, không thuộc working tree:

| Ký hiệu | Repository / ref | SHA |
|---|---|---|
| W-D | `luxmap-web` — `origin/dev` | `03227aa` |
| W-04 | `luxmap-web` — `origin/feature/FW-04-gis-lighting-map` | `d20b5c0` |
| W-05 | `luxmap-web` — `origin/feature/FW-05-asset-management` | `538bd50` |
| M-D | `mobile` — `origin/dev` | `6148fe4` |
| M-main | `mobile` — `origin/main` | `71c9b99` |

Đã chạy grep theo mẫu được yêu cầu, đọc bằng `git show`, bổ sung truy vết import/call-site. Không fetch remote.

**A — luxmap-web**

**Bản đồ GIS: cả ba ref**

| Screen/component/type | Bằng chứng file:dòng | Ảnh hưởng khi bỏ feeder / thêm bỏ tủ |
|---|---|---|
| `GisMapPage`; `PoleProperties`, `SegmentProperties`, `SegmentInfo` | W-D `src/pages/gis-map/GisMapPage.tsx:10,21,42,61,105,119,207,223`; W-04 `:11,22,43,62`; W-05 `:11,22,43,62` | Wiring cascade, lớp điện, chọn tủ và marker cần sửa. Thống kê hiện lấy từ `effectivePoles` đã qua cascade — W-D `:132`. |
| `useElectricalCascade` — W-04 | `src/hooks/gis-map/useElectricalCascade.ts:41,79,89,106,163,295,307` | Mô phỏng tủ tổng → cả tuyến; tủ nhánh → khoảng chỉ số cột. Phân đoạn dây điện dựa tủ tổng/nhánh mất cơ sở nếu bỏ topology/tủ. |
| `useElectricalCascade` — W-D/W-05 | `src/hooks/gis-map/useElectricalCascade.ts:42,101,123,126,141,155,190,314,327,338` | **Code tự chia đều cột trong từng đoạn cho các tủ**, gán `cabinet_id`/`feeder_id`, nối tọa độ tủ qua các cột và tạo feeder ID dự phòng. Đây là quan hệ mô phỏng, không chứng minh tủ thực tế cấp những cột đó. |
| `useFeederLinesLayer`; props cùng file | W-D/W-05 `src/hooks/gis-map/useFeederLinesLayer.ts:6,40,81,105,221,273`; W-04 `:6,32,55` | Lớp `feeder-lines`, màu trạng thái, tooltip/click và highlight mất đối tượng. W-D/W-05 có lớp **đường giao thông riêng** tại `:40`, có thể giữ. |
| `useGisMarkers`; props cùng file | W-D/W-05 `src/hooks/gis-map/useGisMarkers.ts:13,126,129,183`; W-04 `:13,176,177` | Bỏ tủ: marker tủ, chọn tủ và highlight các cột thuộc tủ thành dead code. Marker cột vẫn có chức năng độc lập. |
| `GisDrawerPanel`; `GisDrawerPanelProps`, `IotNodeProperties`, `IotNodeFeature` | W-D/W-05 `src/pages/gis-map/components/GisDrawerPanel.tsx:31,41,50,102,222,468,670,715,744,768,880,890`; W-04 `:32,42,51,101,221,450,652,725,751,773,893` | Bỏ tủ: mất panel điện áp/tải, aptomat, IoT tủ, danh sách cột tủ phụ trách, tủ cha ở W-04. Bỏ feeder: mất thông tin lộ. **IoT trên cột tìm bằng `pole_id`**, W-D/W-05 `:105`, không phụ thuộc feeder. |
| Danh sách “cột tủ phụ trách” trong drawer | W-D/W-05 `src/pages/gis-map/components/GisDrawerPanel.tsx:222,237,240`; W-04 `:803,815` | W-D/W-05 chia đều danh sách cột; W-04 cắt theo `start_pole_idx`/`end_pole_idx`. Không phải danh sách cấp điện đã xác minh. |
| `GisMapLegend`; `CabinetLegendIcon` | W-D/W-05 `src/pages/gis-map/components/GisMapLegend.tsx:70,239,248`; W-04 `:78,255` | Chú giải tủ/đường điện thành thừa nếu bỏ các lớp đó. |
| `MapControlBar` | Cả ba: `src/pages/gis-map/components/MapControlBar.tsx:112,118` | Nhánh gợi ý tìm kiếm loại `cabinet` mất đối tượng. |
| `gisSearchUtils`; `SearchCategory` và kết quả tìm kiếm | Cả ba: `src/utils/gis-map/gisSearchUtils.ts:3,21,54,65,84` | Bỏ tìm tủ và subtitle “Tủ…”/“Lộ…”. Tìm tuyến/cột vẫn độc lập. |
| `markerUtils`; `getCabinetSvgString`, `createCabinetMarkerElement`, params | W-D/W-05 `src/utils/gis-map/markerUtils.ts:4,110,111,246,254`; W-04 `:4,215,223` | Bỏ tủ: factory/params marker tủ và hiệu ứng cột theo tủ thành dead code. |
| `tooltipUtils`; tooltip tủ/feeder và params | W-D/W-05 `src/utils/gis-map/tooltipUtils.ts:34,35,44,48,74,79`; W-04 `:42,46,80` | Bỏ thông tin cấp nguồn, feeder, tooltip tủ/điện. Tooltip cột/đường vẫn có phần độc lập. |
| `useGisMapInstance`, CSS, SVG | Cả ba `src/hooks/gis-map/useGisMapInstance.ts:86`; W-D/W-05 `src/index.css:37`; W-04 `:40`; `src/assets/icons/cabinet.svg:2`, `cabinet-root.svg:2` | CSS tên feeder cần rà lại theo popup còn dùng. SVG tủ thành thừa nếu bỏ tủ; root SVG được import ở W-04 `GisDrawerPanel.tsx:21`. |
| `LayerControl`; `LayerControlProps` | Cả ba `src/pages/gis-map/components/LayerControl.tsx:4,7,41` | Toggle feeder thành dead code. **Không tìm thấy import/call-site trong `src` ở cả ba ref**. |
| `PoleDetailDrawer`; `PoleDetail`, props | W-D/W-05 `src/pages/gis-map/components/PoleDetailDrawer.tsx:14,37,56,213`; W-04 `:15,38,57,215` | Card IoT gắn cột không phụ thuộc feeder/tủ. **Không tìm thấy import/call-site trong `src` ở cả ba ref**. |
| `MapToolbar`; props | Cả ba `src/pages/gis-map/components/MapToolbar.tsx:4,54,57,61` | Comment gọi “Feeder”, nhưng dropdown thực tế lọc `selectedSegment` và ghi “tuyến đường”; không cần bỏ vì feeder. **Không tìm thấy import/call-site trong `src`**. |

**Quản lý tài sản: W-D**

Trong ref này, “tủ điện” dùng **`FeederListItem` và API `/assets/feeders`**, chưa phải luồng cabinet độc lập.

| Screen/component/state/API | Bằng chứng file:dòng | Ảnh hưởng |
|---|---|---|
| `AssetManagementPage`; `AssetCategory` | `src/pages/assets/AssetManagementPage.tsx:11,16,147,216,243` | Tab “Tủ điện & Mạch nguồn”, số lượng và điều hướng từ cột sang tủ cần bỏ/sửa. |
| `assetAPI`; `ImportAssetCategory` | `src/feature/assets/assetAPI.ts:23,45,79,87,140` | Các call phụ thuộc: **GET `/assets/feeders`**, **POST `/assets/feeders`**, **PUT `/assets/feeders/{id}`**, **POST `/assets/import/feeders`**. Thành dead API nếu bỏ feeder. |
| `assetSaga` | `src/feature/assets/assetSaga.ts:24,28,30,37,41,64` | **Nếu GET feeder lỗi, nhóm tải tuyến–feeder–cột đi vào catch và không dispatch `fetchAssetsSuccess`.** Import category `cabinets` thực tế gọi import feeder. |
| `assetSlice` | `src/feature/assets/assetSlice.ts:5,11,41,57` | State `cabinets: FeederListItem[]` cần bỏ/sửa. |
| `useAssetData` | `src/hooks/assets/useAssetData.ts:30,94,101,146,153,224,233,241,262,277` | Handler thêm/sửa “tủ” gọi feeder API. POST/PUT cột hiện gửi `feeder_id`; cần đổi payload nếu bỏ field. |
| `CabinetsTab`, props | `src/pages/assets/components/cabinets/CabinetsTab.tsx:11,45,69` | Tìm kiếm, danh sách, thêm/sửa/chi tiết tủ đều phụ thuộc `FeederListItem`. |
| `CabinetTable`, props | `src/pages/assets/components/cabinets/CabinetTable.tsx:5,39,47` | Bảng tủ mất dữ liệu/đối tượng. |
| `AddCabinetModal`; `CabinetRowDraft`, props | `src/pages/assets/components/cabinets/AddCabinetModal.tsx:17,28,222` | Form tạo tủ tạo đối tượng có `feeder_id`, `feeder_name`; thành dead UI. |
| `EditCabinetModal`, props | `src/pages/assets/components/cabinets/EditCabinetModal.tsx:7,25,33,35` | Sửa tên “tủ” thực tế sửa feeder. |
| `CabinetDetailModal`, props | `src/pages/assets/components/cabinets/CabinetDetailModal.tsx:6,37,57,67` | Chi tiết tên feeder, số cột, trạng thái geometry thành dead UI. |
| `AddPoleModal`, props | `src/pages/assets/components/poles/AddPoleModal.tsx:25,65,96,165,318,345` | **Chặn thêm hàng và submit nếu chưa chọn tủ/feeder.** Phải bỏ điều kiện này nếu loại feeder hoặc chỉ thiếu dữ liệu. |
| `EditPoleModal` | `src/pages/assets/components/poles/EditPoleModal.tsx:21,29,42,121` | Input/payload feeder cần bỏ. |
| `PolesTab`, props | `src/pages/assets/components/poles/PolesTab.tsx:13,46,54,92` | Tìm kiếm theo tủ, modal tủ và callback chọn tủ cần sửa. |
| `PoleTable`, props | `src/pages/assets/components/poles/PoleTable.tsx:9,58,77,90` | Cột “tủ” và link chi tiết tủ mất cơ sở. |
| `PoleDetailModal`, props | `src/pages/assets/components/poles/PoleDetailModal.tsx:13,33,125,137` | Khối đấu nối feeder/tên tủ thành thừa. |
| `AddFixtureModal`, `EditFixtureModal` | `src/pages/assets/components/fixtures/AddFixtureModal.tsx:98`; `EditFixtureModal.tsx:111` | Bỏ phần “Tủ…” trong nhãn chọn cột; chức năng gắn bóng không trực tiếp phụ thuộc feeder. |
| `ImportAssetModal` | `src/pages/assets/components/ImportAssetModal.tsx:81,86,250,625,646,718` | Bỏ category/template/review tủ, thông tin tủ trong review cột/bóng. |
| Schema/type import | `src/validations/assetImport.schema.ts:20,26,40,47,78,84,90,296,487,571,579` | `ParsedItemReview`, `ValidationContext`, `FeederImportInput`, nhánh validate tủ và resolve tủ cần sửa. **Import cột hiện cho phép thiếu feeder** (`:90`); chỉ kiểm tra tồn tại nếu có nhập (`:571`). |
| CSV template | `public/templates/feeders.csv:1`; `public/templates/poles.csv:1` | Template feeder thành thừa; template cột cần bỏ/điều chỉnh `feeder_external_ref`. |

**Quản lý tài sản: W-05**

| Screen/component/type/API | Bằng chứng file:dòng | Ảnh hưởng |
|---|---|---|
| `AssetManagementPage`; `AssetCategory`, `AssetPoleItem`, `AssetCabinetItem` | `src/pages/assets/AssetManagementPage.tsx:32,34,45,47,58,104,118,129,158,209,259,372,814,834` | Tab/bảng tủ, tìm kiếm, link cột→tủ và state tủ cần sửa. Khởi tạo **tự chia cột cho tủ và sinh CAB/FDR dự phòng**. Handler thêm/sửa đang cập nhật local state (`:400,477`). |
| `AddCabinetModal`; `CabinetOption`, `NewCabinetData`, props | `src/pages/assets/components/AddCabinetModal.tsx:22,38,41,222,227` | Tự sinh feeder ID cho tủ; form mất đối tượng nếu bỏ tủ. |
| `AddPoleModal`; `NewPoleData`, `CabinetOption`, props | `src/pages/assets/components/AddPoleModal.tsx:16,25,43,150,219,265` | Bắt buộc chọn tủ; feeder lấy từ tủ hoặc tự sinh `FDR-…`. Phải sửa điều kiện và dữ liệu tạo cột. |
| `EditPoleModal`; `EditablePoleData` | `src/pages/assets/components/EditPoleModal.tsx:5,15,63,167` | Type/input/payload feeder cần bỏ. |
| `CabinetDetailModal`, props | `src/pages/assets/components/CabinetDetailModal.tsx:6,68,104,123,126` | Chi tiết mất điện, số cột quản lý và feeder mất cơ sở. |
| `PoleDetailModal` | `src/pages/assets/components/PoleDetailModal.tsx:112,124,127` | Bỏ khối tủ/lộ; hiện có FDR dự phòng. |
| `ImportAssetModal` | `src/pages/assets/components/ImportAssetModal.tsx:39,46,52,232,234,274,286,302` | **POST `/assets/import/feeders`** cho category tủ. Import cột gửi CSV có `feeder_external_ref` đến **POST `/assets/import/poles`**; preview tự tạo tủ/FDR. |
| Type API cũ — W-04 và W-05 | `src/types/assets.ts:11,29,32` | `CreateFeederRequest` thành thừa; `CreatePoleRequest.feeder_id` cần đổi nếu bỏ field. |

**Type, enum và mock**

| Phạm vi | Bằng chứng file:dòng | Ảnh hưởng |
|---|---|---|
| W-D — type feeder | `src/types/assets/feeders.ts:7,14,20,32,39,46` | `CreateFeederRequest`, `FeederDetail`, `FeederListItem`, `FeederListItemPagedResult`, `TopologyPolePagedResult`, `UpdateFeederRequest`: nhóm contract feeder cần bỏ/sửa. |
| W-D — type topology cột | `src/types/assets/poles.ts:9,12,27,31,58,66,69`; `src/types/assets/segments.ts:44,47` | `CreatePoleRequest`, `PoleListItem`, `SetPoleFeederRequest`, `UpdatePoleRequest`, `TopologyPole` chứa feeder. |
| W-D — IoT map types | `src/types/map/iot-nodes.ts:8,13,14,15,19,25` | `IotNodeProperties`, feature/collection chứa `segment_ids`, `feeder_ids`, `supports_remote_control`. Bỏ feeder: đổi `feeder_ids`; không tự động mất node. |
| W-D — map cột/đoạn | `src/types/map/poles.ts:17,25,62,96,110,114,120`; `src/types/map/segments.ts:8,14,18,24` | `PoleMapDetail`, `PoleMapIotNode`, `PoleProperties`/feature/collection; `SegmentProperties`/feature/collection chứa IoT/controller. Cần giữ/đổi theo mô hình IoT còn lại. |
| W-D — cluster contract | `src/types/faults/faults.ts:28,41,88,101`; `src/types/workorders/orders.ts:91,97,135,141`; `src/types/workSchedule.ts:53,59`; `src/feature/work-schedule/workScheduleAPI.ts:47,55` | `FaultItem`, `ReportedFault`, `WorkOrderDetail`, `WorkOrderItem` có cluster. **Không thấy frontend tính cluster theo feeder**; field nullable/optional không chứng minh phải bỏ. |
| Enum tất cả ref | W-D/W-05 `src/constants/enums.ts:43,46,76,107,115`; W-04 `:45,48,78,109,117`; W-D `src/types/common/enums.ts:12,20,22,34` | `FaultType`, `SourceChannel`, `NodeRole`, `NodeStatus`. Bỏ feeder không đồng nghĩa bỏ `iot`, `node_offline`, `segment_outage`; cần quyết định theo nghiệp vụ còn giữ. |
| Mock tủ | W-D/W-05 `src/data/mock-cabinets.geo.json:15,18,28`; W-04 `:15,18,26,27,33` | W-D/W-05: cabinet→feeder→node; W-04: tủ tổng/nhánh, phạm vi chỉ số cột. Bỏ tủ/topology: dữ liệu mô phỏng này thành thừa. |
| Mock IoT | Cả ba `src/data/mock-iot-nodes.geo.json:15,16,17,18,72,73,75` | Có `segment_controller` gắn đoạn và `sampled_fixture` gắn cột. Có thể giữ phần node trên cột nếu vẫn triển khai. |
| Mock cột/chi tiết/đoạn | Cả ba `src/data/mock-poles.geo.json:28`; `mock-pole-detail.json:24`; `mock-segments.geo.json:118,211,320` | `has_iot_node`, `iot_node`, `controller_node_id`; cần nhất quán với IoT còn lại. |
| Mock cluster | Cả ba `src/data/mock-faults.json:208,212`; `mock-work-orders.json:10` | Có `segment_outage`/`cluster_id`; không có bằng chứng thuật toán feeder trong các mock. Không tìm thấy import hai mock này trong `src`. |
| Mock tài sản riêng — W-D | `src/data/mock-asset-data.json:15,16,17` | Chứa cabinet/feeder đã gán sẵn; không tìm thấy import trong `src`. |
| Sinh type — W-D | `scripts/generate-types.js:143,148,228,230` | Mapping sinh type feeder/IoT/topology cần cập nhật theo contract mới. |
| Nhãn IoT ngoài GIS | W-D/W-05 `src/pages/login/LoginPage.tsx:260`; W-D `src/pages/work-schedule/components/CreateManualModal.tsx:137` | Chỉ nhãn “IoT Gateway” và nguồn tin “Dân/IoT”; chưa thấy phụ thuộc feeder. |
| Tài liệu web | Cả ba `GUIDE.md:232`, `RULES.md:55` | Enum/example commit có liên quan; không phải call-site chạy ứng dụng. |

**B — mobile**

M-main: grep toàn ref không có khớp mẫu yêu cầu, cũng không có khớp bổ sung IoT/controller trong `app/src/main/java`. Không có bằng chứng để gán ảnh hưởng feeder/tủ cho ref này.

M-D: **không tìm thấy feeder/cabinet trong mã ứng dụng**. Các phụ thuộc tìm được:

Đường dẫn Kotlin dưới đây tương đối với `app/src/main/java/com/luxmap/`.

| Screen/component/type/API | Bằng chứng file:dòng | Ảnh hưởng |
|---|---|---|
| `MapScreen` | `feature/map/ui/MapScreen.kt:773,995,1007` | Badge IoT theo `has_iot_node`; không phụ thuộc feeder/tủ. |
| `MapLegend`, icon registry, drawable | `feature/map/ui/MapLegend.kt:102`; `core/map/MapMarkerIcons.kt:12,41,42`; `app/src/main/res/drawable/ic_iot_badge.xml:6` | Chú giải/asset “Có thiết bị IoT”; chỉ thành thừa nếu bỏ toàn bộ IoT trên cột. |
| `PolePropertiesDto`, feature/collection; `PoleMarker` | `feature/map/data/dto/PoleFeatureDto.kt:9,15,29,43`; `feature/map/data/PoleMarker.kt:10,17,30` | **`has_iot_node: Boolean` không có default.** Nếu bỏ field khỏi dữ liệu nhưng giữ decoder hiện tại, dữ liệu thiếu trường bắt buộc; cần sửa DTO hoặc tiếp tục trả `false`. |
| `PoleGeoJsonRenderer`; `RenderProperties` | `feature/map/ui/PoleGeoJsonRenderer.kt:28,61,64` | Chuyển cờ IoT sang GeoJSON dùng cho badge; không có feeder. |
| `RoadSegmentPropertiesDto`, feature/collection | `feature/map/data/dto/RoadSegmentFeatureDto.kt:9,15,29,35` | **`controller_node_id: String` không nullable/default.** Xóa trường khỏi dataset cần sửa decoder, dù UI không dùng controller. |
| `RoadSegmentLine`, renderer | `feature/map/data/RoadSegmentLine.kt:7,10,24`; `feature/map/ui/RoadSegmentGeoJsonRenderer.kt:26,50,52` | Domain bỏ controller ID nhưng giữ `has_active_segment_fault`; bản đồ đoạn đường không cần topology feeder. |
| `PoleDetailDto`, `PoleDetailIotNodeDto`, runtime DTO | `feature/map/data/dto/PoleDetailDto.kt:18,22,53,78` | `iot_node` nullable mặc định null; runtime history mặc định rỗng. Thiếu node được model hỗ trợ. |
| `PoleDetail`, mapping, `PoleDetailScreen` | `feature/map/data/PoleDetail.kt:29,78`; `feature/map/ui/PoleDetailScreen.kt:239,376,402,639,683,720` | Hiển thị node/trạng thái/lần báo cáo hoặc “Không có”; không phụ thuộc feeder/tủ. Các preview có/không node cần đồng bộ nếu đổi IoT. |
| `FakeMapRepository`, `FakePoleDetailRepository`, DI | `feature/map/data/FakeMapRepository.kt:31,33,37,39,46,47`; `FakePoleDetailRepository.kt:30,31,38,39,40`; `di/RepositoryModule.kt:29,32` | **Map và chi tiết cột hiện đọc mock, chưa gọi API bản đồ/IoT.** Phải sửa mock cùng DTO nếu đổi schema. |
| `HomeScreen`, `WorkOrderCluster`, `HomeData`, fake/real repository | `feature/home/ui/HomeScreen.kt:172,173,174`; `feature/home/data/HomeModels.kt:30,33,38`; `FakeHomeRepository.kt:26,28,29`; `RealHomeRepository.kt:25,39,42` | “Cluster” ở Home là **`groupBy(communeId)`**, không phải feeder. Bỏ feeder/tủ không làm mất nhóm này. |
| Cluster marker bản đồ | `feature/map/ui/MapScreen.kt:185,186,349,1016,1018,1020,1021` | Gom điểm theo radius/zoom, lấy severity cao nhất; không phải clustering nguyên nhân điện. |
| `WorkOrderItemDto`, `WorkOrdersApi` | `feature/workorder/data/dto/WorkOrderItemDto.kt:18`; `core/network/WorkOrdersApi.kt:13,20` | GET work-orders nhận `cluster_id` nullable/default null. Không tìm thấy xử lý `clusterId` ngoài DTO; không cần suy ra phải bỏ API này. |
| Nhãn sự cố | `feature/workorder/ui/detail/WorkOrderFaultLabels.kt:7` | `segment_outage` → “Mất điện cả tuyến”; nhãn không phụ thuộc feeder trong code. |

| Mock/tài liệu/test M-D | Bằng chứng file:dòng | Ảnh hưởng |
|---|---|---|
| Mock cột, đoạn | `app/src/main/assets/mock-poles.geojson:28`; `mock-segments.geojson:118,211,320`; bản sao `docs/mock-poles.geojson:28`, `docs/mock-segments.geojson:118` | Cờ IoT/controller phải khớp DTO bắt buộc. |
| Ba mock chi tiết | `app/src/main/assets/mock-pole-detail-POLE-0001.json:24`, `…POLE-0014.json:24`, `…POLE-0047.json:24`; ba bản sao cùng tên trong `docs`, cũng `:24` | Hai node null, một node có dữ liệu; không gắn feeder/tủ. |
| Mock fault/work-order trong docs | `docs/mock-faults.json:208,212`; `docs/mock-work-orders.json:10` | Ví dụ segment outage/cluster; không phải thuật toán feeder. |
| Test nhóm theo xã/Home | `app/src/test/java/com/luxmap/feature/home/data/RealHomeRepositoryTest.kt:118`; `…/home/ui/HomeViewModelTest.kt:74`; `app/src/androidTest/java/com/luxmap/feature/home/ui/HomeScreenTest.kt:33` | Kiểm tra nhóm theo xã/Home, không phụ thuộc feeder. |
| Test nhãn/mapping outage | `app/src/test/java/com/luxmap/feature/workorder/data/WorkOrderDetailMappingTest.kt:76,97`; `…/workorder/ui/detail/WorkOrderFaultLabelsTest.kt:11` | Chỉ cần sửa nếu đổi/bỏ fault type, không tự động vì bỏ feeder. |
| Style/design docs | `app/src/main/java/com/luxmap/core/theme/Color.kt:238`; `docs/LuxMap_Mobile_Design_System_v2.0.md:285,289,340`; `docs/LuxMap_Mobile_Global_Design_System.md:718,731,741` | Cluster card/marker thông thường; không chứng minh phụ thuộc điện. |
| Proposal cũ | `docs/LuxMap_Rural_Road_Lighting_GIS_phuonglhk.md:32,40,94,101,108,114,125` | Mô tả controller, feeder topology và spatial fault clustering; sẽ lệch scope nếu bỏ, nhưng không phải bằng chứng đã triển khai trên mobile. |
| Ghi chú IoT/BLE và contract | `CLAUDE.md:31`; `docs/LuxMap_Mobile_Luong_Hoat_Dong_v1.0.md:119`; `docs/contract-drift.md:25`; `docs/superpowers/plans/2026-10-05-work-order-detail-plan.md:133,174` | Phân biệt cảm biến khảo sát/BLE với node cố định; contract-drift xác nhận Home cluster dùng mã xã. Không suy ra BLE phải bỏ theo feeder. |

**C — cam kết trong đăng ký v1.4**

Nguồn tất cả dòng dưới đây: `luxmap_backend/docs/registration/FA26SE222_v1.4.md`. Trích nguyên văn phần liên quan.

Phân loại: **(1)** dữ liệu/quan hệ có điều kiện; **(2)** chức năng, sản phẩm hoặc nội dung nghiên cứu cam kết không có điều kiện “where available”. “Selected/supported assets” giới hạn đối tượng, không miễn toàn bộ chức năng.

| Dòng | Trích cam kết | Loại | Khi bỏ feeder/tủ |
|---|---|---|---|
| 45 | “A sparse set of IoT nodes monitors the electrical operating conditions of selected lighting assets, including power state and current consumption.” | 2 | Giữ được nếu IoT vẫn gắn tài sản được chọn. |
| 49 | “IoT nodes installed on selected lighting assets monitor electrical operating conditions” | 2 | Không bắt buộc feeder/tủ; sửa nếu bỏ luôn IoT monitoring. |
| 61 | “Electrical operating state and current consumption from selected IoT-monitored lighting assets are analyzed using predefined rules” | 2 | Vẫn cam kết phân tích điện trên tài sản được chọn. |
| 63 | “lighting assets with abnormal electrical operating conditions are displayed on the GIS map.” | 2 | Giữ nếu còn monitoring điện. |
| 95 | “Review lighting faults reported by Field Engineers and IoT-monitored devices.” | 2 | Giữ nếu còn nguồn IoT. |
| 101, 103 | “Control supported streetlights (**ON, OFF, AUTO**).”; “View the electrical operating status of supported lighting devices.” | 2 | Chức năng trên thiết bị hỗ trợ vẫn được cam kết; không chỉ định bắt buộc dùng tủ/feeder. |
| 175 | “Lighting poles should be associated with their corresponding feeder or electrical supply group where such information is available.”; “Missing feeder information should not prevent an asset from being managed or monitored by the system.” | 1 | **Không cần đoán topology khi thiếu dữ liệu.** Nếu bỏ khả năng feeder hoàn toàn, cập nhật mô tả phạm vi. |
| 179 | “a limited number of IoT monitoring devices rather than requiring dedicated monitoring equipment on every lighting pole.” | 2 | Giữ kiến trúc sparse IoT; không đòi mỗi cột một node. |
| 181 | “IoT devices must support temporary buffering of telemetry when network connectivity is unavailable” | 2 | Vẫn áp dụng cho node còn giữ. |
| 189 | “IoT telemetry, fixture condition results, fault records, and maintenance activities must be retained” | 2 | Giữ retention/audit cho IoT còn lại. |
| 197 | “Representation of lighting poles, electrical cabinets, road segments, and electrical feeders as spatial entities with their relationships and topology” | 2 | **Phải sửa** phần feeder; bỏ tủ thì sửa cả cabinets/topology tương ứng. |
| 209 | “Feeder relationships are also considered to support investigation of possible shared electrical causes” | 2 | **Phải sửa** phần feeder/shared-cause investigation. |
| 213 | “a pilot deployment that combines GIS asset data, smartphone-based night surveys, IoT sensing, and electrical monitoring.” | 2 | Không cần sửa vì riêng feeder/tủ; vẫn phải có IoT monitoring. |
| 215 | “The register includes lighting poles, fixtures, electrical cabinets, road segments, power sources, and feeder relationships where such information is available.” | 1, câu có điều kiện | Sửa danh mục nếu bỏ hẳn. **Phạm vi bổ nghĩa của “where…” chưa rõ**; dòng 239 cam kết tủ/feeder rõ ràng hơn. |
| 217 | “Establish the feeder relationships of the pilot network” … “using available records and field verification where practical.” | 1 | Thiếu dữ liệu/không thực tế được giới hạn; bỏ hoạt động này thì cập nhật. |
| 219 | “Design and assemble the IoT monitoring prototype” … “for selected lighting assets” | 2 | Prototype IoT vẫn là phần phải làm nếu chỉ bỏ feeder/tủ. |
| 231 | “Implement electrical operating-condition monitoring” … “and use feeder relationships in the GIS” | 2 | **Phải sửa** vế sử dụng feeder; giữ vế monitoring nếu còn prototype. |
| 233 | “Build the Web GIS dashboard, offline Mobile Application, and maintenance workflow” … “IoT monitoring information” | 2 | Không bắt buộc feeder; vẫn cam kết hiển thị IoT. |
| 235 | “while also evaluating the electrical monitoring prototype and the overall survey-to-maintenance workflow.” | 2 | Evaluation prototype điện vẫn còn. |
| 239 | “A map-based platform for managing lighting poles, fixtures, electrical cabinets, feeders, and road segments” | 2 — deliverable | **Phải sửa** feeder và cabinets nếu bỏ. |
| 243 | “A prototype for monitoring selected lighting assets using electrical measurements such as power state and current consumption” | 2 — deliverable | Không cần feeder/tủ cụ thể; không thể coi việc bỏ feeder là tự bỏ prototype. |
| 247 | “Backend services and a spatial database for managing lighting assets, road segments, feeder relationships” … “IoT telemetry” | 2 — deliverable | **Phải sửa** feeder relationships; IoT vẫn giữ nếu còn monitoring. |
| 257 | “electrical monitoring prototype results” | 2 — báo cáo đánh giá | Giữ nếu còn prototype. |
| 264 | “IoT device registration; telemetry data ingestion; power state, current, ambient light and device-status monitoring; implement **Remote Lighting Control (Force ON / Force OFF / AUTO)**; support GIS and IoT integration” | 2 — nhiệm vụ Member 2 | Không có điều kiện dữ liệu feeder. Sửa nếu giảm monitoring/control/integration. |
| 265 | “remote lighting control interface; integration with backend APIs” | 2 — nhiệm vụ Member 3 | Interface control vẫn được cam kết; sửa nếu bỏ control. |
| 273 — RQ chính | “whether per-pole illuminance peaks” … “can identify dimmed fixtures that still appear ON.” | 2 — nghiên cứu | RQ ON/OFF–Dim, association và repeatability không yêu cầu feeder. |
| 273 — RQ phụ | “A secondary research focus is whether electrical operating information, such as power state and current consumption from a limited number of IoT-monitored lighting assets, together with feeder relationships represented in the GIS, can provide useful supporting evidence for identifying abnormal electrical behavior and investigating possible shared electrical problems affecting multiple lighting assets.” | 2 — nghiên cứu | **Phải sửa** RQ phụ nếu bỏ feeder; có thể giữ phần nghiên cứu monitoring điện. |
| 287 | “Evaluate whether electrical operating information, particularly power state and current consumption from selected IoT-monitored lighting assets, can provide useful indicators” | 2 — objective | Giữ nếu còn IoT electrical monitoring. |
| 289 | “Examine whether feeder relationships represented in the GIS can provide useful contextual information” … “within the same electrical supply group.” | 2 — objective | **Phải sửa/bỏ** objective này nếu bỏ feeder/group. |
| 295 | “selected grid-connected lighting assets represented in the GIS, and a limited IoT prototype for electrical condition monitoring.” | 2 — scope | Không bắt buộc tủ/feeder; giữ prototype trên thiết bị được chọn. |
| 305 | “an IoT electrical monitoring prototype is implemented on selected lighting equipment”; “Lighting poles and their feeder relationships, where known, are represented in the GIS” | 2 cho prototype; 1 cho feeder | Thiếu topology được cho phép; bỏ hoàn toàn thì cập nhật vế feeder. |
| 313 | “the project provides an empirical evaluation of whether sparse electrical IoT monitoring” … “Feeder relationships represented in the GIS provide additional context” | 2 — scientific contribution | **Phải sửa** contribution feeder; giữ evaluation IoT nếu còn. |
| 315 | “a reference architecture that combines GIS-based lighting asset and feeder management” … “sparse electrical IoT monitoring” | 2 — scientific contribution | **Phải sửa** architecture claim về feeder management. |
| 339 | “The project does not require a dedicated IoT monitoring node on every lighting pole.”; “a limited number of IoT devices provide electrical operating information for selected lighting assets.” | 2 — kiến trúc | Không yêu cầu node mỗi cột, cũng không nói IoT chỉ được gắn tủ/feeder. |
| 343 | “where this information is available”; “Feeder information may be obtained from existing records or entered and verified manually”; “incomplete feeder information does not prevent other LuxMap functions from operating.” | 1 | Cho phép thiếu; **không cho phép coi quan hệ tự chia mock là đã xác minh**. Bỏ hẳn capability thì cập nhật. |
| 349 | “Lighting poles, fixtures, electrical cabinets, feeders, and road segments may be imported from existing records when available or entered and updated manually” | 1 về nguồn dữ liệu | Không giả định có hồ sơ số đầy đủ; bỏ loại tài sản/chức năng thì cập nhật danh mục. |

Dòng `37` giải thích chi phí node mỗi cột; `319,327` là hướng tài liệu tham khảo về IoT/control; `335` nhắc tài liệu spatial clustering. Chúng không phải deliverable riêng. Tên đề tài `19,21` vẫn chứa IoT: chỉ bỏ feeder/tủ không bắt buộc đổi tên.

**Các chỗ cần sửa chắc chắn khi bỏ hẳn feeder:** `197,209,231,239,247,273` (RQ phụ), `289,313,315`. Các dòng có điều kiện `175,215,217,305,343,349` cần đồng bộ nếu capability bị loại khỏi scope; **chỉ thiếu dữ liệu** thì các điều kiện hiện tại đã cho phép. Bỏ thêm tủ phải sửa rõ danh mục tại `197,239`, đồng bộ `215,349`.

**D — tickets trong tasks-backend.csv**

Nguồn: `luxmap_backend/docs/tasks-backend.csv`. **Tất cả ticket dưới đây có trạng thái “Chưa bắt đầu”, 0% trong CSV**; đây không phải kết luận về tiến độ code hiện tại.

| Ticket — dòng | Tuần | Scope liên quan | Ảnh hưởng |
|---|---|---|---|
| BE-03 — `:6` | W1 | LineString cho tuyến đường **và feeder** | Bỏ phần feeder; vẫn cần geometry đường. |
| BE-09 — `:12` | W2–W3 | Entity Pole, Fixture, RoadSegment, **Feeder**, migration/index | Bỏ phần entity/topology feeder; không bỏ toàn ticket. |
| BE-12 — `:15` | W3–W4 | CRUD tài sản + import kiểm kê | Scope feeder chịu ảnh hưởng qua BE-09; CSV không liệt kê cabinet riêng. |
| **BE-13 — `:16`** | **W4** | Gán cột vào tuyến/feeder; truy vấn “tất cả cột trên feeder X” phục vụ clustering; ghi “Đầu vào bắt buộc cho CV-15” | Bỏ nhánh feeder/query feeder; giữ gán cột vào đoạn đường. |
| BE-14 — `:17` | W4 | Map bbox `/poles`, `/segments`, `/iot-nodes`; phụ thuộc BE-13 | Sửa contract topology/IoT theo scope còn lại; map cột/đường vẫn cần. |
| BE-18 — `:21` | W7–W8 | Fault do engine `cv/iot` hoặc manual | Chỉ phần phát hiện theo topology bị ảnh hưởng; CSV không chứng minh tất cả fault phụ thuộc feeder. |
| BE-20 — `:23` | W8–W9 | Chi tiết cột trả `iot_node`, runtime history… | Phụ thuộc IoT gắn cột, không ghi bắt buộc feeder/tủ. |
| BE-26 — `:29` | W12 | Phát hiện node ngừng báo cáo | Giữ nếu còn node IoT; không có phụ thuộc feeder được nêu. |
| BE-34 — `:37` | W16 | Quản trị node IoT, firmware, model AI | Giữ phần IoT nếu còn thiết bị; không tự động bỏ vì feeder. |
| BE-36 — `:39` | W17–W18 | Test PostGIS/bbox, phụ thuộc BE-14 | Phần contract/query đổi phải điều chỉnh; không phải ticket riêng cho feeder. |
| BE-39 — `:42` | W20–W21 | Seed/demo dùng bộ mock GeoJSON FO-26 | Rà seed/kịch bản điện theo mock thay đổi; CSV không nêu cabinet cụ thể. |
| BE-40 — `:43` | W8 | GET faults lọc `cluster_id` | Cần rà cluster contract; CSV không khẳng định mọi cluster là feeder-based. |
| **CV-15** | **Chưa xác minh** | Chỉ được nhắc trong BE-13 `:16` | **Không có dòng ticket** để lấy tuần/trạng thái/scope đầy đủ. |
| **IOT-\***, ticket lighting control | **Chưa xác minh** | Không có trong CSV được chỉ định | Không được suy ra tuần/trạng thái từ đăng ký hoặc repo khác. |

CSV chỉ chứa **BE-00–BE-43**, không có ticket cabinet riêng. Lighting control vẫn là cam kết trong đăng ký tại `:101,264,265`, nhưng không có ticket tương ứng trong file này.
