# BE-28 P1a — WP5 cần gì (Codex gpt-6.1-sol, high, chỉ đọc, 07/10/2026, 171 423 token)

Nguyên văn tin nhắn cuối của Codex. Claude đã kiểm lại ba khẳng định: KPI bản đồ (`GisMapPage.tsx:125–140`), lọc tình trạng ở tài sản (`PolesTab.tsx:64–68`), route không có màn dashboard (`App.tsx`) — đúng.

## A-1 Màn hình

**Không tìm thấy route dashboard/thống kê/báo cáo phân tích riêng.** Các màn liên quan là GIS, tài sản và lịch công tác; riêng tên nhánh `work-orders-dashboard` không tương ứng với một route dashboard. (`origin/dev:src/App.tsx:45–72`; `origin/feature/work-orders-dashboard:src/App.tsx:45–72`)

Khảo sát trên ref remote **đã lưu tại máy**, không fetch. “Vai trò thấy” dưới đây là quyền vào màn theo code FE, chưa xác minh phản hồi phân quyền của API.

| Ref nhánh | Đường dẫn file / màn | Route | Vai trò thấy — dẫn chứng |
|---|---|---|---|
| `origin/dev` | `src/pages/gis-map/GisMapPage.tsx`; `src/pages/assets/AssetManagementPage.tsx`; `src/pages/work-schedule/WorkSchedulePage.tsx` | `/gis-map`; `/assets`; `/work-schedule` | Mọi tài khoản đăng nhập; ba route không đặt `allowedRoles`. `origin/dev:src/App.tsx:38–58` |
| `origin/feature/asset-import-management` | Ba file như hàng trên | Ba route như hàng trên | Mọi tài khoản đăng nhập. `origin/feature/asset-import-management:src/App.tsx:38–58` |
| `origin/feature/work-orders-dashboard` | Ba file như hàng đầu; báo cáo hiện trường tại `src/pages/work-schedule/components/CaseDetailModal.tsx` và `FieldReportDetailModal.tsx` | Ba route như hàng đầu; báo cáo là modal trong `/work-schedule` | Mọi tài khoản đăng nhập; không có guard riêng cho Superior ở route lịch. `origin/feature/work-orders-dashboard:src/App.tsx:38–58`; `origin/feature/work-orders-dashboard:src/pages/work-schedule/components/CaseDetailModal.tsx:575–580` |
| `origin/feature/sidebar-navigation-layout` | `src/pages/gis-map/GisMapPage.tsx`; `src/pages/assets/AssetManagementPage.tsx` | `/gis-map`; `/assets` | Mọi tài khoản đăng nhập. `origin/feature/sidebar-navigation-layout:src/App.tsx:37–50` |
| `origin/feature/FW-05-asset-management` | Hai file như hàng trên | `/gis-map`; `/assets` | Mọi tài khoản đăng nhập. `origin/feature/FW-05-asset-management:src/App.tsx:37–50` |
| `origin/feature/authentication` | Hai file như hàng trên | `/gis-map`; `/assets` | Mọi tài khoản đăng nhập. `origin/feature/authentication:src/App.tsx:37–50` |
| `origin/feature/FW-04-gis-lighting-map` | `src/pages/gis-map/GisMapPage.tsx` | `/gis-map` | Không có guard đăng nhập trong cấu hình route. `origin/feature/FW-04-gis-lighting-map:src/App.tsx:13–21` |
| `origin/feature/FW-03-design-system-components` | Không tìm thấy màn thống kê; `src/App.tsx` chỉ có trang giới thiệu và 404 | `/`; `*` | Không đặt guard. `origin/feature/FW-03-design-system-components:src/App.tsx:5–39` |
| `origin/feature/not-found-page` | Không tìm thấy màn thống kê; `src/App.tsx` chỉ có trang giới thiệu và 404 | `/`; `*` | Không đặt guard. `origin/feature/not-found-page:src/App.tsx:4–35` |
| `origin/feature/FW-01-web-spa-setup` | Không tìm thấy màn thống kê; `src/App.tsx` là trang giới thiệu | Không tìm thấy router | Không tìm thấy phân quyền màn. `origin/feature/FW-01-web-spa-setup:src/App.tsx:1–24` |

Ở `origin/dev`, wire role được ánh xạ: `superior → ManagementAgency`, `manager → MaintenanceEngineer`, `field_engineer → FieldCrew`, `system_admin → Admin`. Guard chỉ kiểm vai trò khi route truyền `allowedRoles`. (`origin/dev:src/utils/roleUtils.ts:60–85`; `origin/dev:src/components/ProtectedRoute.tsx:36–48`)

## A-2 Số liệu

| Màn | Chỉ số / biểu đồ | Bộ lọc thực sự áp dụng | Nguồn và tên trường chính xác — dẫn chứng |
|---|---|---|---|
| GIS — thanh KPI | Số lượng `total`, `normal`, `dim`, `out`, `unknown`; không tìm thấy biểu đồ tỷ lệ tại thanh này | Bản đồ lọc `selectedSegment`, `statusFilter`, `searchQuery`; **KPI vẫn tính trên toàn bộ `effectivePoles`**, không trên `filteredFeatures` | `src/data/mock-poles.geo.json`, `mock-cabinets.geo.json`, `mock-segments.geo.json` qua hook; đọc `properties.fixture_status`. `origin/dev:src/hooks/gis-map/useElectricalCascade.ts:7–9,102–109,222–234`; `origin/dev:src/pages/gis-map/GisMapPage.tsx:127–140`; hiển thị tại `origin/dev:src/pages/gis-map/components/MapControlBar.tsx:171,200,227,254,281` |
| GIS — chi tiết cột | Danh sách “Quang thông 3 đêm gần nhất”, phần trăm baseline và nhãn phân loại | Cột được chọn; lấy `.slice(0, 3)`. Không tìm thấy bộ lọc khoảng ngày | `src/data/mock-pole-detail.json`; `luminance_history[].observed_at`, `baseline_ratio`, `classified_as`. Đây là danh sách, không phải đồ thị. `origin/dev:src/pages/gis-map/components/GisDrawerPanel.tsx:22,510–533` |
| GIS — chi tiết tuyến | Chiều dài, số cột, trạng thái sự cố tuyến / IoT | Tuyến được chọn | UI: `activeSegmentDetail.lengthM`, `poleCount`, `hasActiveSegmentFault`, `iotStatus`; nguồn mock và phép tính hook. `origin/dev:src/hooks/gis-map/useElectricalCascade.ts:164–195`; `origin/dev:src/pages/gis-map/components/GisDrawerPanel.tsx:870–899` |
| GIS — chi tiết tủ | Số cột quản lý, số “đèn đang sáng”, phụ tải, điện áp, hệ số công suất; tần số và nhiệt độ viết cứng | Tủ được chọn | `cabinetConnectedPoles.length`, `selectedCabinet.total_poles_managed`, `current_load_kw`, `voltage_v`, `power_factor`; fallback số cột `23`, hệ số `'0.95'`; literal `50.0 Hz`, `34.2 °C`. `origin/dev:src/pages/gis-map/components/GisDrawerPanel.tsx:721–731,774–782,805` |
| Tài sản — `origin/dev` | Badge số cột, bóng, tủ/lộ, tuyến: `poles.length`, `fixtures.length`, `cabinets.length`, `segments.length` | Badge dùng toàn bộ mảng đã tải; bộ lọc bảng riêng: tìm kiếm, tình trạng có bóng, công suất / active-retired, có geometry, `road_class` | Gọi `/assets/poles`, `/assets/feeders`, `/assets/segments`; lấy `items`, mỗi loại một request `page_size: 1000`. Bóng được suy ra từ `poles[].active_fixture`. `origin/dev:src/feature/assets/assetAPI.ts:36–55`; `origin/dev:src/feature/assets/assetSaga.ts:24–38`; `origin/dev:src/hooks/assets/useAssetData.ts:58–73`; badge: `origin/dev:src/pages/assets/AssetManagementPage.tsx:111,143,175,207`; lọc: `origin/dev:src/pages/assets/components/poles/PolesTab.tsx:49–70`, `fixtures/FixturesTab.tsx:47–67`, `cabinets/CabinetsTab.tsx:40–52`, `segments/SegmentsTab.tsx:35–45` |
| Tài sản — chi tiết tuyến ở `origin/dev` | `length_m`, `pole_count`, khoảng cách trung bình mét/cột; hiện `data_source` | Tuyến được chọn; không có bộ lọc thống kê trong modal | Dữ liệu tuyến từ API tài sản; công thức `Math.round(segment.length_m / segment.pole_count)`. `origin/dev:src/pages/assets/components/segments/SegmentDetailModal.tsx:61–77,98` |
| Tài sản — các ref cũ có màn tài sản | Ba badge `poles.length`, `cabinets.length`, `segments.length`; bảng có `total_poles_managed`, `pole_count` | `searchQuery`, `statusFilter`; badge vẫn dùng mảng chưa lọc | W/S/FW-05 dùng mock GeoJSON và state tại trang: `origin/feature/work-orders-dashboard:src/pages/assets/AssetManagementPage.tsx:28–30,250–334,584,613,642,945,1050`; `origin/feature/sidebar-navigation-layout:src/pages/assets/AssetManagementPage.tsx:584,613,642`; `origin/feature/FW-05-asset-management:src/pages/assets/AssetManagementPage.tsx:584,613,642`. Authentication: `origin/feature/authentication:src/pages/assets/AssetManagementPage.tsx:28–30,498,527,556` |
| Lịch công tác — `origin/dev` và `work-orders-dashboard` | Calendar theo ngày/giai đoạn; không tìm thấy KPI tỷ lệ đúng hạn hoặc đồ thị hoàn thành | Màn gửi `scheduled_from`, `scheduled_to` theo tháng; FE lọc `filterPhase = all/survey/inspection/repair/ended`. Không tìm thấy UI lọc xã, tuyến, `data_source` | Gọi `/work-orders`, đọc `items`; đồng thời khởi tạo bằng `INITIAL_DEMO_CASES` trong slice. Chỉ thay `cases` khi kết quả map có phần tử. `origin/dev:src/pages/work-schedule/WorkSchedulePage.tsx:42–60`; `origin/dev:src/pages/work-schedule/components/ScheduleFilterBar.tsx:85–95`; `origin/dev:src/feature/work-schedule/workScheduleAPI.ts:36–48`; `origin/dev:src/feature/work-schedule/workScheduleSlice.ts:11,118,523–529`. Nhánh W: `origin/feature/work-orders-dashboard:src/pages/work-schedule/WorkSchedulePage.tsx:42–60` |
| Lịch — chi tiết sự vụ / nghiệm thu | Số cột tác nghiệp, phát hiện AI và confidence, lux, nội dung / vật tư báo cáo | Sự vụ và giai đoạn đang xem | `currentPhase.poleIds`, `caseItem.poleIds`; `currentPhase.report.submittedAt`, `summary`, `aiDetections[].confidence`, `luxMeasured`, `suggestedMaterialsNote`, `usedMaterialsNote`. `origin/dev:src/pages/work-schedule/components/CaseDetailModal.tsx:69–70,248,293–301,376–411` |
| Lịch — modal báo cáo hiện trường | “Video … (1)”, số ảnh bằng chứng; AI `91%`, model `v2.4`, `4.2 Lux`, nhãn `<15 Lux` | Tab `video/photos/ai`; không tìm thấy bộ lọc thống kê | Ảnh từ asset local; `evidencePhotos` là mảng viết trong component. Các số AI/lux nêu trên là **literal**, không lấy từ API hoặc `report.luxMeasured`. `origin/dev:src/pages/work-schedule/components/FieldReportDetailModal.tsx:16–18,33,42–60,110,123,294–330` |

Lưu ý tên trường lịch: mapper đọc cả snake_case/camelCase, gồm `work_order_id`, `case_id`, `task_kind`, `wo_status`, `assigned_to`, `scheduled_date`, `created_at`, `materials_note`, `materials_used`, `report_note`; sau đó tạo `cases[].phases[]`. Nó không đọc `due_date` hay `completed_at` trong mapper này. (`origin/dev:src/feature/work-schedule/workScheduleSlice.ts:221–285`)

Các bộ lọc thống kê theo **`data_source` không tìm thấy** ở GIS/lịch/tài sản đã khảo sát; việc khai báo hoặc hiển thị trường không phải bộ lọc. (`origin/dev:src/hooks/gis-map/useElectricalCascade.ts:12–17,222–234`; `origin/dev:src/pages/work-schedule/components/ScheduleFilterBar.tsx:43–95`; `origin/dev:src/pages/assets/components/segments/SegmentDetailModal.tsx:77`)

## A-3 Xuất báo cáo

- **CSV/PDF/Excel cho báo cáo phân tích hoặc thông tin bảo trì: không tìm thấy** trên các ref ở A-1. Route hiện có nằm trong các cấu hình đã dẫn ở A-1; báo cáo hiện trường cung cấp tab xem và nút đóng. (`origin/dev:src/App.tsx:45–72`; `origin/dev:src/pages/work-schedule/components/FieldReportDetailModal.tsx:100–140,351–368`)
- Tài sản có **tải CSV mẫu để nhập dữ liệu**: `origin/dev` dùng `href={config.templateUrl}`, `download={config.templateFileName}`; nhánh `work-orders-dashboard` tạo Blob từ `config.sampleCsv`. (`origin/dev:src/pages/assets/components/ImportAssetModal.tsx:366–386`; `origin/feature/work-orders-dashboard:src/pages/assets/components/ImportAssetModal.tsx:139–147`)
- Nút “Gửi Báo Cáo Lên Superior” cập nhật Redux, hiện toast rồi dispatch verify work order; không phải thao tác xuất file. (`origin/dev:src/pages/work-schedule/WorkSchedulePage.tsx:173–187`)

## A-4 Chỗ FE tự tính từ dữ liệu thô

| Chỗ | Phép tính / suy diễn — dẫn chứng |
|---|---|
| KPI tình trạng đèn | Duyệt `effectivePoles`, đếm theo `properties.fixture_status`; trạng thái khác `normal/dim/out` vào `unknown`. Tập đếm không chịu bộ lọc bản đồ. `origin/dev:src/pages/gis-map/GisMapPage.tsx:127–140` |
| Trạng thái đầu vào KPI | FE chia cột theo thứ tự thành nhóm tủ bằng `Math.ceil(totalInSeg / numCabs)`; tủ `status === 'fault'` làm `fixture_status` của cột thành `'out'`. `origin/dev:src/hooks/gis-map/useElectricalCascade.ts:119–145` |
| Số cột / sự cố tuyến | Đếm `effectivePoles.filter(...segment_id...).length`; suy `hasActiveSegmentFault` từ `.some(tủ.status === 'fault')`, `iotStatus` từ cùng kết quả. `origin/dev:src/hooks/gis-map/useElectricalCascade.ts:171–195` |
| Số đèn thuộc tủ | Lọc mock theo tuyến rồi chia thành các đoạn theo số tủ; dùng độ dài nhóm làm số cột và số “đèn đang sáng”. `origin/dev:src/pages/gis-map/components/GisDrawerPanel.tsx:224–240,721,805` |
| Tổng tài sản / bóng đang dùng | Đếm `.length`; dựng `fixtures` bằng lọc `p.active_fixture`. Không dùng tổng server cho các badge này. `origin/dev:src/hooks/assets/useAssetData.ts:58–73`; `origin/dev:src/pages/assets/AssetManagementPage.tsx:111,143,175,207` |
| Bộ lọc tình trạng tài sản | `normal` được hiểu là có `active_fixture`, `out` là không có; không đọc `pole_current_status` trong phép lọc này. `origin/dev:src/pages/assets/components/poles/PolesTab.tsx:64–68` |
| Số liệu tài sản ở nhánh cũ | Tự đếm số cột thuộc tủ để tạo `total_poles_managed`; cập nhật số cột khi thêm tài sản bằng cộng số phần tử. `origin/feature/work-orders-dashboard:src/pages/assets/AssetManagementPage.tsx:165–179,406–416` |
| Khoảng cách trung bình | `Math.round(length_m / pole_count)` mét/cột. `origin/dev:src/pages/assets/components/segments/SegmentDetailModal.tsx:98`; `origin/feature/work-orders-dashboard:src/pages/assets/components/SegmentDetailModal.tsx:99` |
| Tổng hợp lịch | Nhóm raw items theo `case_id`, dựng giai đoạn; đổi `done → reported`, `verified/cancelled → ended`; nhóm sự kiện theo `phase.date`. Không phải tính tỷ lệ đúng hạn. `origin/dev:src/feature/work-schedule/workScheduleSlice.ts:240–290`; `origin/dev:src/pages/work-schedule/components/CalendarGrid.tsx:42–60` |
| Đếm và định dạng trong chi tiết | Số cột từ `poleIds.length`; confidence nhân 100; baseline nhân 100 và làm tròn. Hai phép nhân là định dạng giá trị có sẵn. `origin/dev:src/pages/work-schedule/components/CaseDetailModal.tsx:248,392`; `origin/dev:src/pages/gis-map/components/GisDrawerPanel.tsx:525` |

## Khoảng trống — yêu cầu Phiếu mà FE chưa có màn nào

Trong dẫn chứng sau, `BE-working-tree` chỉ bản Phiếu đang đọc ở repo backend.

| Yêu cầu Phiếu | Khoảng trống tìm được |
|---|---|
| Superior xem thống kê sự cố trên GIS | **Không tìm thấy màn/panel tổng hợp thống kê sự cố**; KPI hiện đếm tình trạng cột, panel cột có lịch sử sự cố mẫu. Phiếu: `BE-working-tree:docs/registration/FA26SE222_v1.4.md:73`; FE: `origin/dev:src/pages/gis-map/GisMapPage.tsx:127–140`; `origin/dev:src/pages/gis-map/components/GisDrawerPanel.tsx:126–151,560` |
| Superior xem và xuất báo cáo phân tích, thông tin bảo trì | **Không tìm thấy màn báo cáo phân tích / xuất báo cáo**; có lịch và modal báo cáo từng sự vụ. Phiếu: `BE-working-tree:docs/registration/FA26SE222_v1.4.md:75`; FE: `origin/dev:src/App.tsx:45–72`; `origin/dev:src/pages/work-schedule/WorkSchedulePage.tsx:223–247` |
| P/R/F1 riêng ON/OFF; đối chiếu Normal/Dim/Out với field verification; độ lặp phép đo từng cột | **Không tìm thấy màn đánh giá các chỉ số này**; panel AI hiện confidence/lux viết cứng, panel baseline chỉ liệt kê ba điểm. Phiếu: `BE-working-tree:docs/registration/FA26SE222_v1.4.md:185`; FE: `origin/dev:src/pages/work-schedule/components/FieldReportDetailModal.tsx:294–330`; `origin/dev:src/pages/gis-map/components/GisDrawerPanel.tsx:517–533` |
| Field Trial and Evaluation Report: thêm độ chính xác gán cột, kết quả prototype điện, hiệu năng xử lý, phản hồi pilot | **Không tìm thấy màn tổng hợp báo cáo đánh giá này** trong các route khảo sát. Phiếu: `BE-working-tree:docs/registration/FA26SE222_v1.4.md:257`; FE: các cấu hình `src/App.tsx` tại A-1. Dòng 257 quy định deliverable, không tự nó quy định phải có một màn Web riêng. |

✅ Đã kiểm chứng bằng đọc code trên các ref nêu trên. ⚠️ Không chạy ứng dụng/API; chưa xác minh hành vi runtime. Không sửa file ở hai repo, không checkout/fetch/commit, không đề xuất API P1b.