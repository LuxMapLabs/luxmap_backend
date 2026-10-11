# BE-31 — P1a: consumer WP5/WP6

Khảo sát ngày 11/10/2026; chỉ đọc ref remote **đã có tại máy**, không fetch/checkout.
Đã đọc `AGENTS.md`, `.ai/tasks/BE-31.md` và các tài liệu nền được ticket dẫn.
Các trích đoạn dưới lấy từ `git show`/`git grep -n`; đường dẫn thuộc repo ghi ở từng mục.
✅ Kiểm chứng tĩnh trên code. ⚠️ Không chạy UI/API; chưa kiểm nội dung tài liệu DOCX/XLSX.

**Phạm vi cố định** — web `/Users/nhm809/Documents/LuxMap/luxmap-web`:

| Ref | Commit |
|---|---|
| `origin/dev` | `0532fec2b120f843a3c18568c39099aa75c8dc60` |
| `origin/feature/work-orders-dashboard` | `ba3fd83588eaed7fbc92f2bced23f4e77c75af8d` |
| `origin/feat/gis-map-topology-and-real-api` | `281c5ad307dd43d7908c42d2862597478fe56b99` |
| `origin/feat/asset-cabinet-management` | `bcb9f1365b88f5832dc7ce23b3a869234e127868` |
| `origin/feature/asset-management-enhancements` | `95190eef56bf4ade38dd8adf5190560ae72ee079` |
| `origin/feature/asset-import-management` | `0bf085db46eea17bd4719bea72aea2da0c86a85d` |
| `origin/feature/FW-05-asset-management` | `538bd500bbb0021aaf87f6e940fef54b96defe8f` |

Chọn 6 nhánh tên liên quan gần nhất theo ngày commit; bỏ `origin/feature/FW-04-gis-lighting-map` vì ngân sách.
Chỉ dùng `git log --remotes=origin --no-walk` để liệt kê metadata chọn nhánh; không khảo sát nội dung các ref khác.
Mobile `/Users/nhm809/Documents/LuxMap/mobile`: chỉ `origin/dev` tại `6148fe4abdd709a2fac0d4d48155fee3210d10a9` (06/10/2026).

## A-1. Chỗ hiện / định hiện bảo hành

**Web — vai trò hiện tại là Manager.** Route tài sản và bản đồ giới hạn `UserRole.MaintenanceEngineer`;
`manager` được chuẩn hóa về enum này:
- `origin/dev:src/App.tsx:67`: `<ProtectedRoute allowedRoles={[UserRole.MaintenanceEngineer]}>` (bản đồ); dòng 91 giống vậy cho `/assets`.
- `origin/dev:src/utils/roleUtils.ts:73`: `case 'manager':`; dòng 78: `return UserRole.MaintenanceEngineer`.

**Web dev có ba nơi dùng ngày bảo hành:**
- Tab **Bóng đèn**, nguồn là bóng đang dùng lấy từ danh sách cột:
  `origin/dev:src/hooks/assets/useAssetData.ts:61`: `.filter((p) => p.active_fixture)`;
  dòng 67: `...(p.active_fixture as ActiveFixture),`.
  `origin/dev:src/pages/assets/components/fixtures/FixtureTable.tsx:167`: `{fixture.warranty_expiry || '—'}`.
- Modal thêm/sửa bóng và preview import:
  `origin/dev:src/pages/assets/components/fixtures/AddFixtureModal.tsx:179`: `<label className="block font-semibold text-slate-700 mb-1">Hạn Bảo Hành</label>`;
  dòng 181: `type="date"`; dòng 44: `warranty_expiry: warrantyExpiry || null,`.
  `origin/dev:src/pages/assets/components/fixtures/EditFixtureModal.tsx:38`: `setWarrantyExpiry(fixture.warranty_expiry || '')`;
  dòng 55: `warranty_expiry: warrantyExpiry || null,`.
  `origin/dev:src/pages/assets/components/ImportAssetModal.tsx:832`:
  `return \`Đèn LED ${item.data.lamp_watt}W • Nguồn: ${item.data.power_source === 'grid' ? 'Lưới điện' : 'Mặt trời'} • Hạn BH: ${item.data.warranty_expiry || 'Chưa có'}\``.
- Drawer cột trên **bản đồ**, ưu tiên chi tiết bóng rồi properties bản đồ, cuối cùng ngày cố định:
  `origin/dev:src/pages/gis-map/components/GisDrawerPanel.tsx:405`:
  `{poleDetail?.fixture?.warranty_expiry || selectedPole.properties.warranty_expiry || '2026-12-31'}`.
  API consumer: `origin/dev:src/feature/map/mapAPI.ts:70`:
  `const response = await apiClient.get<PoleMapDetail>(\`/map/poles/${encodeURIComponent(poleId)}\`)`.
  Component được gắn ở `origin/dev:src/pages/gis-map/GisMapPage.tsx:379`: `<GisDrawerPanel`.

**Đối chiếu đủ 6 nhánh** (không coi nhánh cũ là hành vi dev):

| Ref:đường dẫn:dòng | Code thật / khác biệt |
|---|---|
| `origin/feature/work-orders-dashboard:src/pages/assets/components/fixtures/FixtureTable.tsx:167` | `{fixture.warranty_expiry || '—'}`, như dev |
| `origin/feat/gis-map-topology-and-real-api:src/pages/gis-map/components/GisDrawerPanel.tsx:405` | `{poleDetail?.fixture?.warranty_expiry || selectedPole.properties.warranty_expiry || '2026-12-31'}`, như dev |
| `origin/feat/asset-cabinet-management:src/pages/gis-map/components/GisDrawerPanel.tsx:434` | `<span className="text-slate-800 dark:text-slate-200">{selectedPole.properties.warranty_expiry || '2027-03-24'}</span>` |
| `origin/feature/asset-management-enhancements:src/pages/assets/components/fixtures/FixtureTable.tsx:167` | `{fixture.warranty_expiry || '—'}` |
| `origin/feature/asset-import-management:src/pages/assets/components/fixtures/FixtureTable.tsx:150` | `{fixture.warranty_expiry || '—'}` |
| `origin/feature/FW-05-asset-management:src/pages/assets/components/PoleDetailModal.tsx:99` | `{pole.warranty_expiry || 'Không có'}`; bảng cột còn tính hết hạn (A-2) |

Có component khác chứa bảo hành, nhưng chưa tìm thấy call site trong `src` của dev:
`origin/dev:src/pages/gis-map/components/PoleDetailDrawer.tsx:206`:
`<span className="font-bold text-emerald-700">{pole.fixture?.warranty_expiry || '2027-03-24'}</span>`.
Không tính component này thành màn đang hoạt động.

**Mobile — chi tiết cột, Field Engineer.**
- Vai trò theo tài liệu consumer:
  `origin/dev:docs/LuxMap_Mobile_Luong_Hoat_Dong_v1.0.md:13`:
  “App Mobile chỉ phục vụ **duy nhất 1 vai trò: Field Engineer** (Tổ khảo sát/sửa chữa).”
- UI:
  `origin/dev:app/src/main/java/com/luxmap/feature/map/ui/PoleDetailScreen.kt:381`:
  `label = "Bảo hành",`; dòng 382:
  `value = "Đến ${DateFormatUtils.formatPlainDate(detail.warrantyExpiry)}",`.
- Nguồn là `fixture.warrantyExpiry`:
  `origin/dev:app/src/main/java/com/luxmap/feature/map/data/PoleDetail.kt:73`:
  `warrantyExpiry = fixture.warrantyExpiry,`.
  DTO chi tiết **yêu cầu String không null**:
  `origin/dev:app/src/main/java/com/luxmap/feature/map/data/dto/PoleDetailDto.kt:39`:
  `@SerialName("warranty_expiry") val warrantyExpiry: String,`;
  DTO properties bản đồ cũng vậy:
  `origin/dev:app/src/main/java/com/luxmap/feature/map/data/dto/PoleFeatureDto.kt:38`:
  `@SerialName("warranty_expiry") val warrantyExpiry: String,`.
- Hiện **mock**, chưa chứng minh nhận ngày từ API thật:
  `origin/dev:app/src/main/java/com/luxmap/di/RepositoryModule.kt:32`:
  `abstract fun bindPoleDetailRepository(impl: FakePoleDetailRepository): PoleDetailRepository`;
  `origin/dev:app/src/main/java/com/luxmap/feature/map/data/FakePoleDetailRepository.kt:30`:
  `val text = context.assets.open(assetName).bufferedReader().use { it.readText() }`.

**Kết quả âm:** chưa tìm thấy dashboard bảo hành trong code web được khảo sát; từ “dashboard” trong tên nhánh work-orders không chứng minh có màn thống kê bảo hành.

## A-2. Client có tự tính sắp / đã hết không?

- **Web dev và 5 nhánh mới:** chỉ hiện ngày; chưa thấy phân loại bảo hành hay ngưỡng “sắp hết”.
  Bảng bóng dùng trực tiếp `{fixture.warranty_expiry || '—'}` như A-1.
  **Có tính ngày mặc định lúc thêm bóng**, không phải ngưỡng cảnh báo:
  `origin/dev:src/pages/assets/components/fixtures/AddFixtureModal.tsx:22`:
  `const defaultExpiry = new Date(Date.now() + 3 * 365 * 24 * 60 * 60 * 1000).toISOString().split('T')[0]`.
  Luồng import cột+kèm bóng cũng có fallback:
  `origin/dev:src/validations/assetImport.schema.ts:732`:
  `const warrantyExpiry = data.warranty_expiry || existingPole?.active_fixture?.warranty_expiry || '2026-12-31'`;
  dòng 859: `warranty_expiry: warrantyExpiry || null,`.
- **Nhánh cũ FW-05:** có hai nhãn “Hết hạn” / “Còn hạn”, so với đồng hồ trình duyệt, không có cửa sổ “sắp hết”.
  `origin/feature/FW-05-asset-management:src/pages/assets/AssetManagementPage.tsx:848`:
  `const expDate = new Date(pole.warranty_expiry)`;
  dòng 849: `const isExpired = !isNaN(expDate.getTime()) && expDate < new Date()`;
  dòng 853:
  `title={isExpired ? \`Đã hết hạn bảo hành từ ${pole.warranty_expiry}\` : \`Còn bảo hành đến ${pole.warranty_expiry}\`}`.
  Nguồn ngày được map từ properties mock, có fallback:
  cùng file dòng 148: `warranty_expiry: p.warranty_expiry || '2026-12-31',`.
- **Mobile:** chỉ định dạng ngày, không so hôm nay:
  `origin/dev:app/src/main/java/com/luxmap/core/common/DateFormatUtils.kt:52`:
  `if (date.isNullOrBlank()) return "Chưa cập nhật"`;
  dòng 54: `LocalDate.parse(date).format(plainDateFormatter)`.
  Chưa tìm thấy logic “sắp hết / đã hết” hoặc ngưỡng cảnh báo trong mã ứng dụng.

## A-3. Danh sách / xuất báo cáo bảo hành hoặc tài sản

- **Web:** có danh sách kiểm kê bóng với ngày bảo hành (A-1), chưa có danh sách riêng sắp/đã hết.
  Bộ lọc hiện tại:
  `origin/dev:src/pages/assets/components/fixtures/FixturesTab.tsx:36`:
  `const [wattFilter, setWattFilter] = useState<string>('all')`;
  dòng 37: `const [statusFilter, setStatusFilter] = useState<'all' | 'active' | 'retired'>('all')`;
  dòng 65: `const isRetired = Boolean(item.removed_date)`.
  `active/retired` ở đây là dùng/tháo bóng, không phải trạng thái bảo hành.
- **Web:** chưa tìm thấy xuất dữ liệu kiểm kê/bảo hành CSV/Excel/PDF hoặc API xuất báo cáo trên 7 ref.
  Nút tải hiện có là **template import tĩnh**:
  `origin/dev:src/pages/assets/components/ImportAssetModal.tsx:444`:
  `href={config.templateUrl}`; dòng 445: `download={config.templateFileName}`;
  dòng 450: `<span>Tải file mẫu CSV</span>`.
  Nhánh FW-05 tạo template ngay tại client:
  `origin/feature/FW-05-asset-management:src/pages/assets/components/ImportAssetModal.tsx:140`:
  `const blob = new Blob([config.sampleCsv], { type: 'text/csv;charset=utf-8;' })`.
  Đây không phải xuất báo cáo từ dữ liệu hiện có.
- **Mobile:** chưa tìm thấy màn danh sách bảo hành/kiểm kê hoặc chức năng xuất CSV/Excel/PDF trong `app/src/main`.
  Có **yêu cầu trong tài liệu tổng quan cũ**, chưa có màn/code tương ứng:
  `origin/dev:docs/LuxMap_Rural_Road_Lighting_GIS_phuonglhk.md:45`: `- Management Agency`;
  dòng 50: `- Track fixtures approaching warranty expiry and export management reports`.
  Tài liệu không chốt ngưỡng, định dạng hay cách xuất; actor này thuộc mô tả tổng quan, không chứng minh mobile có màn cho vai trò đó.

## A-4. Fault / work order cần biết bảo hành ở đâu?

**Hiện chưa có consumer đọc/hiện bảo hành trên các màn này.** Các điểm có tài sản liên quan:

- **Web Manager — hồ sơ kiểm tra/sửa chữa:** hiển thị cột và vật tư.
  `origin/dev:src/pages/work-schedule/components/CaseDetailModal.tsx:292`:
  `{(currentPhase.poleIds || caseItem.poleIds || []).map((pId) => (`;
  dòng 297: `{pId}`;
  dòng 435: `Đề xuất vật tư từ Kỹ sư kiểm tra:`;
  dòng 448: `Vật tư thực tế đã sử dụng (Quản lý có thể hiệu chỉnh):`.
  Đây là vị trí liên quan để WP5 xác nhận nhu cầu cảnh báo, **chưa có yêu cầu bảo hành trong code**.
- **Web DTO:** fault có tham chiếu cột/bóng, chi tiết phiếu có cột, không có ngày/cờ bảo hành.
  `origin/dev:src/types/faults/faults.ts:30`: `pole_id?: string | null`;
  dòng 31: `fixture_id?: string | null`.
  `origin/dev:src/types/workorders/orders.ts:169`: `pole_id?: string | null`.
  Chưa tìm thấy màn danh sách fault độc lập trong cây `src/pages` đã khảo sát.
- **Mobile Field Engineer — chi tiết phiếu:** card fault hiện cột, loại/trạng thái lỗi và điều hướng.
  `origin/dev:app/src/main/java/com/luxmap/feature/workorder/ui/detail/WorkOrderDetailScreen.kt:204`:
  `Text(text = fault.poleId ?: fault.faultId, style = MaterialTheme.typography.titleMedium)`;
  dòng 206: `Text(text = faultTypeLabel(fault.faultType), style = MaterialTheme.typography.bodyMedium)`.
  DTO:
  `origin/dev:app/src/main/java/com/luxmap/feature/workorder/data/dto/WorkOrderDetailDto.kt:22`:
  `@SerialName("pole_id") val poleId: String? = null,`.
  API thật:
  `origin/dev:app/src/main/java/com/luxmap/core/network/WorkOrdersApi.kt:20`:
  `@GET("api/v1/work-orders/{id}")`.
  Chưa thấy trường bảo hành, lookup bảo hành hay nhãn chi trả trên màn này.

**Bằng chứng âm** — lệnh `git grep -n -i -E` đã chạy; không có stdout, exit **1**:

| Pattern | Ref / pathspec |
|---|---|
| `warranty\|bảo hành\|bao hanh` | 7 ref web trên; `src/pages/work-schedule src/types/faults src/types/workorders src/types/workSchedule.ts src/feature/work-schedule` |
| `warranty\|bảo hành\|bao hanh\|csv\|excel\|xlsx\|pdf` | mobile `origin/dev`; `app/src/main/java/com/luxmap/feature/workorder`, `app/src/main/java/com/luxmap/core/network/WorkOrdersApi.kt`, hai spec work-order 05–06/10 |
| `expiring\|warranty_status\|warrantyStatus\|sắp hết\|hết bảo hành` | 7 ref web trên; `src ':!src/data' ':!src/test'` |
| `csv\|xlsx\|excel\|pdf\|warrantyStatus\|expiring\|isExpired\|hết bảo hành\|sắp hết\|hết hạn bảo hành` | mobile `origin/dev`; `app/src/main/java app/src/main/res` |
| `dashboard\|statistics\|báo cáo\|report` | 7 ref web trên; `src/App.tsx src/components/Sidebar.tsx` |

Tìm thêm `handleExport|exportReport|exportCsv|exportCSV|exportExcel|saveAs|jsPDF|jspdf|sheetjs|writeFile|application/pdf|application/vnd|text/csv|createObjectURL|xuất báo cáo|xuất excel|xuất csv`
trên `src` (trừ data/test) của 7 ref web: chỉ gặp template/download và CSV trung gian để **import** ở FW-05; không gặp xuất báo cáo.

### Cho P1b

- A-1/A-2: WP5/WP6 xác nhận cách hiện **không biết hạn/không có bóng**; consumer đang có fallback ngày cố định và DTO mobile yêu cầu chuỗi không null.
- A-2: Chưa có dữ kiện consumer chốt ngưỡng “sắp hết”, múi giờ hoặc biên hết hạn đúng hôm nay; phép so của FW-05 là hành vi nhánh cũ.
- A-3: WP5 xác nhận màn báo cáo, vai trò, bộ lọc/cột và định dạng xuất cần dùng; code hiện chưa có luồng này.
- A-4: WP5/WP6 xác nhận cảnh báo cần hiện ở hồ sơ, từng fault/cột hay chi tiết cột; chưa có code chứng minh nhu cầu hiển thị cụ thể.
