Đã rà toàn bộ **2.293 dòng** tại `HEAD=a9a85df`, trùng `origin/dev`. `AGENTS.md` là symlink tới [CLAUDE.md](/Users/nhm809/Documents/LuxMap/luxmap_backend-claudemd-review/CLAUDE.md). Không sửa file, build hay chạy test; working tree vẫn sạch. Các mục dưới đây được xác minh bằng đọc code, tài liệu và git history.

**S — STALE**

1. **S-1. Quyết định v1.4 vẫn bị ghi là chưa chốt.**  
   `CLAUDE.md:L10-L18, L115-L120, L221` → D-R20…D-R28 chưa ký; ground truth `dim` và việc giữ `source_channel=cv` còn chờ. → D-R20, D-R22, D-R23 đã chốt ngày 07/10: giữ `cv`, ngưỡng out trả `null`, ground truth chọn B trước/A sau (`docs/contract-drift.md:150-153,1068-1075`).  
   **Sửa:** cập nhật từng quyết định riêng, không gom cả dải thành “chưa chốt”.

2. **S-2. Upload video vẫn bị coi là tương lai.**  
   `CLAUDE.md:L17, L545-L548, L614-L619` → D-R28 còn chặn, video chỉ PLANNED, đề xuất presigned, BE-15 chưa có đặc tả. → D-R28=A đã chốt 02/10; proxy streaming, MP4 H.264/H.265, cap 300 MiB (`docs/contract-drift.md:740-747`); upload đã có ở `src/LuxMap.Modules.Survey/Ingest/SweepsController.cs:31-39`.  
   **Sửa:** thay trạng thái đề xuất bằng quy tắc upload hiện hành và dẫn quyết định BE-15.

3. **S-3. Danh sách consumer thiếu máy.**  
   `CLAUDE.md:L21-L22` → chỉ ba consumer, “không có consumer nào khác”. → Thiết bị gọi poll/ACK (`src/LuxMap.Modules.Telemetry/Lighting/DeviceCommandsController.cs:10-42`); broker gọi callback auth (`src/LuxMap.Modules.Telemetry/Mqtt/BrokerAuthController.cs:49-57`).  
   **Sửa:** bổ sung thiết bị IoT và broker, phân biệt với ba consumer ứng dụng ban đầu.

4. **S-4. Version Contract hiện hành sai.**  
   `CLAUDE.md:L29` → bản hợp nhất v1.8. → Tài liệu hiện là **v1.14** (`docs/api-contract-v1.1.md:1-16`); tên spec vẫn cố ý giữ `luxmap-v1.5.json` (`:20-23`).  
   **Sửa:** cập nhật v1.14; giữ nguyên tên file spec.

5. **S-5. Con trỏ tới nguyên bản FW-00 chưa chính xác.**  
   `CLAUDE.md:L41-L45` → năm mục nằm ở đầu log drift hiện hành. → Log mới chỉ tóm tắt và trỏ nguyên bản sang archive (`docs/contract-drift.md:15-20`); nguyên bản bắt đầu tại `docs/archive/contract-drift-v1.md:21`.  
   **Sửa:** phân biệt liên kết tới bản tóm tắt và nguyên bản.

6. **S-6. Bảng phạm vi vẫn mô tả nền điện thoại cũ.**  
   `CLAUDE.md:L97-L118` → đo bằng điện thoại, controlled reference còn trong bảng phạm vi, chờ D-R21. → D-R21 đã chốt hướng 27/09 (`docs/contract-drift.md:151`); pipeline nhận log lux theo sweep (`src/LuxMap.Modules.Survey/Ingest/SurveyRawParser.cs:55-59`).  
   **Sửa:** cập nhật bảng theo v1.4; giữ riêng phần D-R25 thực sự còn treo.

7. **S-7. `external_ref` còn nằm trong danh sách cần xét lại.**  
   `CLAUDE.md:L118-L120` → danh tính vĩnh viễn là follow-up ưu tiên cao chưa quyết. → Đã chốt 03/10: mã nhóm quản lý là vĩnh viễn, mã xã vào `inventory_code` riêng (`docs/contract-drift.md:893-900`); chính `CLAUDE.md:L1442-L1447` cũng ghi đã chốt.  
   **Sửa:** bỏ mục này khỏi danh sách quyết định đang chờ.

8. **S-8. “Contract sẽ lên v1.2” đã lỗi thời.**  
   `CLAUDE.md:L132` → thay đổi `data_source` sẽ vào v1.2. → Contract đã v1.14; quy tắc nguồn dữ liệu nằm tại `docs/api-contract-v1.1.md:167-181`.  
   **Sửa:** thay lời hẹn version bằng tham chiếu hiện hành.

9. **S-9. Ba mô tả payload endpoint đã thiếu trường.**  
   `CLAUDE.md:L237, L239, L243` → PATCH fault chỉ nêu ba trường; POST work order không có `task_kind`; sweep chỉ liệt kê hình dạng ban đầu. → PATCH đã có `severity` (`src/LuxMap.Modules.Faults/FaultReviewService.cs:15-28`); `task_kind` bắt buộc và nhiều trường bổ sung ở `src/LuxMap.Modules.WorkOrders/WorkOrderRequests.cs:7-24`; sweep còn có `work_order_id,status,data_source,submitted_at,clips,raw_files,version` (`src/LuxMap.Modules.Survey/Ingest/SurveyContracts.cs:18-20`).  
   **Sửa:** cập nhật ba schema hoặc dẫn trực tiếp DTO/spec hiện hành.

10. **S-10. Properties bản đồ cột thiếu `warranty_status`.**  
    `CLAUDE.md:L247-L254` → danh sách properties không có trường này. → `WarrantyStatus` là property bắt buộc (`src/LuxMap.Modules.Map/Features/MapProperties.cs:59-65`), đã đăng ký W-2 (`docs/contract-drift.md:1209`).  
    **Sửa:** bổ sung `warranty_status`.

11. **S-11. Bảng “chưa có đặc tả” chứa các phần đã đặc tả và hiện thực.**  
    `CLAUDE.md:L275-L291` → BE-15/16, BE-27 và toàn dải BE-33→35 còn thiếu đặc tả. → BE-15 có quyết định/bề mặt tại `docs/contract-drift.md:740-800`; BE-27 đã vào Contract v1.10 (`docs/api-contract-v1.1.md:12`) và có controller (`src/LuxMap.Modules.Notifications/NotificationsController.cs:21-46`); node registry/control đã có LIGHT-CTRL (`docs/contract-drift.md:1160-1184`).  
    **Sửa:** chỉ giữ phần phạm vi thực sự chưa đặc tả; không gắn trạng thái chung cho cả dải ticket.

12. **S-12. Các số lượng entity/bảng thuộc snapshot cũ.**  
    `CLAUDE.md:L311-L313, L322, L409` → “17 bảng hiện có”, “22 bảng mới”, “14 entity còn lại”, neo cho “15/16 entity”. → Snapshot hiện có **46 bảng**:
    ```text
    rg -c '\.ToTable\(' src/LuxMap.Persistence/Migrations/LuxMapDbContextModelSnapshot.cs
    46
    ```
    ERD cũng tự xác định là lược đồ đích, không phải inventory schema đang chạy (`docs/database/erd.md:7-9`).  
    **Sửa:** bỏ số lượng cố định khỏi quy tắc hiện hành hoặc ghi rõ mốc lịch sử.

13. **S-13. Nợ FK `last_sweep_id` đã được xử lý.**  
    `CLAUDE.md:L424-L425` → `pole_current_status.last_sweep_id` chưa có FK, BE-15 sẽ thêm. → FK đã cấu hình tại `src/LuxMap.Modules.Survey/Configurations/SurveyIngestConfigurations.cs:41-45`.  
    **Sửa:** ghi FK `Restrict` đã tồn tại.

14. **S-14. Inventory bucket chỉ còn phản ánh BE-11 ban đầu.**  
    `CLAUDE.md:L501-L506` → hai bucket. → Hiện có bốn: survey, evidence, video, citizen (`src/LuxMap.Shared/Storage/ObjectStorage.cs:53-60,71-76`).  
    **Sửa:** cập nhật danh sách và phân biệt quy tắc của từng luồng.

15. **S-15. Điều kiện metadata từng JPEG không còn là pipeline hiện hành.**  
    `CLAUDE.md:L529-L530, L1384` → thiếu ISO/shutter/aperture/GPS/heading trên từng `SurveyFrame` thì từ chối. → D-R24 chuyển kiểm sang cấp phiên (`docs/contract-drift.md:154`); camera config đọc tại `src/LuxMap.Modules.Survey/Ingest/SurveyRawParser.cs:34-48`, heading GPS là tùy chọn tại `:85`.  
    **Sửa:** mô tả kiểm capture config/profile và chất lượng track cấp phiên.

16. **S-16. Hangfire vẫn xuất hiện trong stack và lời giải thích thumbnail.**  
    `CLAUDE.md:L555-L558, L1404` → BE-26 dùng Hangfire, tới W12 mới có. → Đã quyết không thêm Hangfire (`docs/contract-drift.md:1075`); API đăng ký scheduler DB lease (`src/LuxMap.Api/Program.cs:86-87`), đúng như `CLAUDE.md:L2183-L2186`.  
    **Sửa:** thay Hangfire bằng scheduler DB lease/`BackgroundService`; bỏ lịch dự kiến cũ.

17. **S-17. “Không chạm dịch vụ ngoài khi startup” không còn đúng toàn repo.**  
    `CLAUDE.md:L565-L568` → chưa có tiền lệ, kể cả PostgreSQL. → Worker khi bật gọi `EnsureAsync` trong vòng khởi động, thực hiện INSERT DB (`src/LuxMap.Persistence/Jobs/ScheduledJobRunner.cs:38-43,98-114`).  
    **Sửa:** giới hạn phát biểu thành “Storage không provision bucket lúc startup”.

18. **S-18. Phân biệt lux/history còn dùng lý do nghiên cứu cũ.**  
    `CLAUDE.md:L614-L638` → history do CV đo sáng ảnh; đưa lux vào history là CV tự chấm mình; phần mới chưa hiện thực. → History đã lưu `PeakLux`, `CvState`, `BaselineRatio` (`src/LuxMap.Modules.Survey/Entities/SurveyPublication.cs:31-49`); detail đọc dữ liệu thật (`src/LuxMap.Modules.Map/Features/PoleDetailService.cs:131-149`).  
    **Sửa:** phân biệt **LuxReading thủ công** với **lux theo sweep**, bỏ lập luận photometric CV cũ.

19. **S-19. `nearest_luminance=null` đúng, nhưng lý do và mốc chờ sai.**  
    `CLAUDE.md:L653-L656` → bảng history chưa tồn tại, chờ BE-17. → Bảng/model đã có; service vẫn trả null (`src/LuxMap.Modules.Survey/LuxReadings/LuxReadingService.cs:220-232`); việc nối được giao cho giai đoạn A của D-R23 (`docs/contract-drift.md:1072`).  
    **Sửa:** ghi đúng integration debt còn lại, không nói thiếu bảng hay đợi BE-17.

20. **S-20. URL bản đồ cũ còn trong quy tắc assets.**  
    `CLAUDE.md:L673-L675` → endpoint hiện là `GET /poles`. → Đã dời sang `/map/poles` (`docs/contract-drift.md:880-889`), đúng với `CLAUDE.md:L228-L229`.  
    **Sửa:** đổi URL trong đoạn này.

21. **S-21. Assets/import thiếu cabinet và phạm vi `external_ref` bị mô tả sai.**  
    `CLAUDE.md:L678-L682, L703-L714, L745-L747` → bốn loại file; `external_ref` trên ba bảng và không emit API. → Import hiện theo chuỗi **segments → cabinets → feeders → poles → fixtures** (`src/LuxMap.Modules.Assets/Import/ImportContracts.cs:4-23`); bốn bảng có `HasExternalRef` (`src/LuxMap.Modules.Assets/Configurations/AssetConfigurations.cs:90,119,169,199`); inventory emit `ExternalRef` (`src/LuxMap.Modules.Assets/Crud/AssetContracts.cs:469-482`).  
    **Sửa:** bổ sung cabinets/`cabinet_external_ref`; giới hạn “không emit” vào properties bản đồ.

22. **S-22. Response import thiếu kết quả không đổi và cảnh báo.**  
    `CLAUDE.md:L735` → `{inserted,updated,failed,total_errors,truncated,rows[]}`. → Còn `unchanged,total_warnings,warnings` (`src/LuxMap.Modules.Assets/Import/ImportContracts.cs:65-74`).  
    **Sửa:** cập nhật response đầy đủ.

23. **S-23. Ví dụ capability và kích thước ma trận đã cũ.**  
    `CLAUDE.md:L768-L778` → `ControlLighting`, `ManageUsers` chưa có endpoint; HTTP test là 4×6 capability. → Test đã gọi endpoint thật (`tests/LuxMap.Api.Tests/RoleCapabilityMatrixTests.cs:24-26,55-56`); bảng literal hiện có **24 capability** (`:44-70`), khớp `src/LuxMap.Shared/Authorization/LuxMapPolicies.cs:122-145`.  
    **Sửa:** bỏ ví dụ capability chưa dùng; đổi 4×6 thành 4×24 hoặc bỏ số cố định.

24. **S-24. Các ticket “sắp tạo cột” và lỗ confidence đã qua.**  
    `CLAUDE.md:L930-L931, L996-L1007` → BE-15/17 sắp tạo cột; confidence đang hở. → Observation/history đã có cấu hình (`src/LuxMap.Modules.Survey/Configurations/SurveyProcessingConfigurations.cs:84-88`, `SurveyPublicationConfigurations.cs:44-48`); confidence đã có CHECK hữu hạn 0..1 (`src/LuxMap.Modules.Assets/Configurations/AssetConfigurations.cs:361-365`). `normalized_luminance` hiện là DTO tương thích gán từ ratio (`PoleDetailService.cs:145`).  
    **Sửa:** bỏ lời hẹn và cảnh báo đã đóng; giữ quy tắc hữu hạn.

25. **S-25. Bảng ngoại lệ bulk write không còn đúng số lượng/phân loại.**  
    `CLAUDE.md:L952-L957` → AuthService bốn chỗ, test chỉ teardown 13 chỗ. → AuthService có năm lời gọi (`src/LuxMap.Modules.Identity/Auth/AuthService.cs:220,241,267,305,329`). Grep test có 24 lời gọi thực, ngoài bốn chuỗi mẫu; có cả thao tác kiểm tra, không chỉ teardown, ví dụ `tests/LuxMap.Api.Tests/AuthEndpointTests.cs:252`.  
    **Sửa:** bỏ số đếm dễ trôi; mô tả ngoại lệ bằng lý do và pragma.

26. **S-26. Mô tả DB dùng chung và số class chưa cập nhật BE-36.**  
    `CLAUDE.md:L1011-L1014` → DB phát triển chung giữa mọi lượt chạy; collection 16 class. → Mỗi run có container riêng (`tests/LuxMap.Api.Tests/TestDatabase.cs:17-24`); hiện có **53 class** gắn `AssetDatabaseCollection`. Chính `CLAUDE.md:L1041-L1043` đã mô tả đúng phạm vi dùng chung trong một run.  
    **Sửa:** giữ rủi ro sequence trong cùng run; bỏ “DB dev chung mọi run” và số 16.

27. **S-27. Audit vẫn bị hẹn sang ticket đã xong.**  
    `CLAUDE.md:L1072-L1076, L1389, L1518` → thiết kế ở Phase 1 BE-19; WorkOrder sẽ gắn marker, Fault đợi BE-19. → WorkOrder/WorkOrderFault/WorkOrderSegment đã `IAudited` (`src/LuxMap.Modules.WorkOrders/Entities/WorkOrder.cs:10,55,66`); Fault cũng vậy (`src/LuxMap.Modules.Faults/Entities/Fault.cs:21`); review đã ghi audit (`FaultReviewService.cs:129-136`).  
    **Sửa:** mô tả hạ tầng BE-23a và các writer hiện có.

28. **S-28. Deviation `commune_id` của POST fault đã được hợp nhất.**  
    `CLAUDE.md:L1091-L1092` → Contract chưa có `commune_id` trong body. → Contract đã quy định trường này (`docs/api-contract-v1.1.md:697`), changelog xác nhận O-10 được gộp (`:977`); service xử lý tại `src/LuxMap.Modules.Faults/FaultReportService.cs:232-244`.  
    **Sửa:** bỏ câu deviation cũ.

29. **S-29. Tính lại priority score không phải việc BE-26 đang chờ.**  
    `CLAUDE.md:L1107` → BE-33 đổi trọng số thì BE-26 W12 tính lại toàn bảng. → Chấm điểm tự động đã hoãn (`docs/contract-drift.md:624`); BE-26 hiện thực hai job quá hạn/node offline (`:1335-1345`); phần điểm trả về BE-33/CV-16 (`.ai/results/BE-26-p1.md:17`).  
    **Sửa:** ghi đúng owner của follow-up, bỏ lời hẹn BE-26.

30. **S-30. Bảng cluster đã có writer seed; BE-13 đã xong.**  
    `CLAUDE.md:L1116` → chưa có gì ghi bảng, BE-13 chưa làm. → Seed INSERT `fault_cluster` tại `scripts/seed_mock_set.py:186`; ba endpoint topology có tại `src/LuxMap.Modules.Assets/Crud/AssetsController.cs:416,430,455`; tracking ghi hoàn tất (`tracking.html:565`).  
    **Sửa:** phân biệt pipeline CV-15 còn chờ với dữ liệu seed và BE-13 đã hoàn thành.

31. **S-31. Định nghĩa fault mở không còn thiếu trong Contract.**  
    `CLAUDE.md:L1130-L1147` → Contract không định nghĩa, drift 27 còn chờ FW-00. → Contract định nghĩa tập mở tại `docs/api-contract-v1.1.md:296-298`; changelog ghi đã gộp drift 27–31 từ v1.4 (`:977`).  
    **Sửa:** giữ `FaultStatusSets.Open` và dẫn Contract; bỏ trạng thái pending.

32. **S-32. Lỗ teardown được kể như vẫn đang xảy ra.**  
    `CLAUDE.md:L1248-L1256` → `AssetImportFixture` thiếu xóa fault/lux, mọi test ghi fault đang rò cột. → Fixture đã xóa fault, lux rồi pole (`tests/LuxMap.Api.Tests/AssetImportFixture.cs:192,198,203`); comment ghi đã sửa từ BE-14 (`:186-191`).  
    **Sửa:** chuyển sự cố sang quá khứ; giữ quy tắc thứ tự teardown.

33. **S-33. Danh sách action auth vẫn có register.**  
    `CLAUDE.md:L1336-L1339` → hướng dẫn liệt kê `login,register,refresh,logout`. → Register đã gỡ, đúng như `CLAUDE.md:L1364-L1365`. Chứng minh:
    ```text
    rg -n '\[HttpPost\("register"\)\]|RegisterAsync\(' src/ tests/
    không có kết quả; exit 1
    ```
    Test tên `It_is_not_declared_anonymous_even_though_its_four_siblings_are` **vẫn tồn tại** (`tests/LuxMap.Api.Tests/CurrentUserTests.cs:171`).  
    **Sửa:** bỏ register khỏi danh sách hiện hành; không báo nhầm test bị mất.

34. **S-34. Số node trong bộ mock sai.**  
    `CLAUDE.md:L1438` → 12 node, POLE-0047 có NODE-047 như mô hình hiện hành. → `mocks/mock-iot-nodes.geojson` có **ba features**, NODE-001…003; thay đổi đã ghi tại `docs/contract-drift.md:577`. NODE-047 còn trong mock detail là deviation chưa sửa (`:558,853`), không phải ngoại lệ mô hình.  
    **Sửa:** đổi 12→3; gắn nhãn rõ phần detail/runtime cũ còn lệch.

35. **S-35. Seed mock được diễn đạt như việc chưa làm.**  
    `CLAUDE.md:L1440` → BE-39 còn phải seed. → Đã seed ngày 20/09, giữ đúng ID (`tracking.html:318`); script hiện hành là `scripts/seed_mock_set.py`.  
    **Sửa:** giữ yêu cầu bảo toàn ID, bỏ trạng thái “còn phải”.

36. **S-36. Cảnh báo khảo sát ảnh lần hai đã được sửa bằng code.**  
    `CLAUDE.md:L1448-L1451` → `--existing` chỉ báo review, chưa dùng lại mã. → Commit **`068b1b1`** sửa reuse mã cột; code lấy `group.matched.external_ref` (`scripts/photos_to_poles.py:755-756`), không đưa cột cũ vào import (`:762-764`), ghi quan sát theo mã cũ (`:770-776`).  
    **Sửa:** thay “chưa sửa” bằng quy tắc chạy `--existing` và xử lý ca mơ hồ.

37. **S-37. Lịch “hiện tại”, “kế tiếp” và bảng người đang chờ đã hết hạn.**  
    `CLAUDE.md:L319, L1419-L1432, L1487-L1491, L1503` → cần xong BE-42 W4; đang W2–W3, kế tiếp BE-13; BE-40/41/43 đang chặn WP6. → Bảng giao endpoint ghi các mục đã xong (`tracking.html:562-571`); BE-40 merge #61 (`:686-687`), BE-41 #91 (`:247-252`), BE-43 #102 (`:645-646`).  
    **Sửa:** bỏ snapshot lịch khỏi quy tắc, dẫn tracking cho blocker hiện tại.

38. **S-38. Registry prefix không còn 16 spec.**  
    `CLAUDE.md:L1501` → 16 spec, mười entity còn lại chưa tạo. → Hiện có **24 spec** (`src/LuxMap.Shared/Contracts/PrefixedId.cs:55-101`); Fault/SurveyFrame/LuxReading đã có entity/migration. **Sáu cột trong migration cũ vẫn đúng** (`20260901084931_FixPrefixedIdOverflow.cs:35-93`).  
    **Sửa:** giữ số sáu với mốc migration; bỏ phần inventory 16/10 ở thì hiện tại.

39. **S-39. Port lookup work order được mô tả quá rộng.**  
    `CLAUDE.md:L1562-L1563` → port không đọc `WorkOrder`. → Chỉ đúng với `ActiveWorkOrdersAsync`; `ActiveRepairsAsync` JOIN `WorkOrder.IgnoreQueryFilters` (`src/LuxMap.Modules.WorkOrders/ActiveWorkOrderLookup.cs:23-48`), được BE-19 gọi (`src/LuxMap.Modules.Faults/FaultReviewService.cs:77`). Git: **`fd6fdd9`**.  
    **Sửa:** phân biệt hai phương thức và giải thích scope vẫn neo từ bảng nối.

40. **S-40. Chủ thể resolve model của Citizen đã chuyển.**  
    `CLAUDE.md:L1951-L1954` → `YoloCitizenPhotoHinter` resolve lười trong catch. → Class này hiện là adapter (`src/LuxMap.Modules.Citizen/CitizenPhotoHints.cs:16-21`); resolver/catch nằm ở `src/LuxMap.Modules.AI/Detection/YoloSingleLampPhotoHinter.cs:24-36`, chuyển trong **`2dcea35`**.  
    **Sửa:** đổi chủ thể thành `YoloSingleLampPhotoHinter`; giữ Citizen là adapter.

41. **S-41. MQTT adapter không còn là tương lai.**  
    `CLAUDE.md:L2121-L2122` → “Adapter MQTT sau này”. → Handler và worker đã đăng ký (`src/LuxMap.Modules.Telemetry/TelemetryModule.cs:38-43`), hiện thực trong **`bb23e03`**.  
    **Sửa:** dùng thì hiện tại và dẫn adapter đang có.

42. **S-42. Symbol `DeviceEndpoints` đã đổi tên.**  
    `CLAUDE.md:L2140-L2141` → `CapabilityPolicyCoverageTests.DeviceEndpoints`. → Hiện là `MachineEndpoints` (`tests/LuxMap.Api.Tests/CapabilityPolicyCoverageTests.cs:38-43`); git `-S DeviceEndpoints` chỉ ra **`bb23e03`** thay tên. Chứng minh thiếu:
    ```text
    rg -n '\bDeviceEndpoints\b' src/ tests/
    không có kết quả; exit 1
    ```
    **Sửa:** đổi sang `CapabilityPolicyCoverageTests.MachineEndpoints`.

43. **S-43. Model không còn chỉ hai lối vào.**  
    `CLAUDE.md:L2147-L2148` → “Một model, hai lối vào”. → Có ba caller trực tiếp: `YoloOnOffDetector.cs:91`, `Controllers/AiController.cs:51`, `YoloSingleLampPhotoHinter.cs:26-31`, đều dưới `src/LuxMap.Modules.AI/`; nhánh hinter phục vụ Citizen và pole photo.  
    **Sửa:** liệt kê survey, detect endpoint và hinter ảnh đơn; bỏ số hai.

44. **S-44. “Chỉ mắt người bắt được” không còn đúng như mệnh đề tuyệt đối.**  
    `CLAUDE.md:L908-L910` → không công cụ nào bắt được các lỗi vừa nêu. → Đã có test tích hợp kiểm overflow/collision (`tests/LuxMap.Api.Tests/PrefixedIdOverflowTests.cs:46,74,140,167`) và test catalog kiểm index đầy đủ cạnh partial (`tests/LuxMap.Api.Tests/ScheduledJobTests.cs:402-406`).  
    **Sửa:** giữ yêu cầu đọc migration; nói rõ test hiện có chỉ phủ những invariant được viết thành assertion.

**C — CONTRADICTION**

1. **C-1. Ví dụ LPAD sai và khẳng định “không ràng buộc nào bắt được” sai.**  
   `CLAUDE.md:L163, L908-L910` → `12345` bị cắt thành `2345`, lỗi luôn âm thầm. → LPAD giữ **bốn chữ số đầu**, tức `1234`; test ghi rõ tại `tests/LuxMap.Api.Tests/PrefixedIdOverflowTests.cs:98,209`. Collision có thể bị PK chặn bằng **23505**, được ghi ngay trong migration `src/LuxMap.Persistence/Migrations/20260901084931_FixPrefixedIdOverflow.cs:15-20`.  
   **Sửa:** sửa hướng cắt và phân biệt sai ID với collision bị constraint từ chối.

2. **C-2. Thu hồi scope không có hiệu lực ngay request kế tiếp.**  
   `CLAUDE.md:L493-L495` → chuyển kỹ sư sang xã khác thì query filter chặn ngay. → Scope đọc từ claim JWT (`src/LuxMap.Api/Authorization/CommuneScopeAccessor.cs:34-45`); đổi role/xã có hiệu lực ở lần refresh, tối đa 60 phút (`docs/api-contract-v1.1.md:515-516`).  
   **Sửa:** mô tả đúng cửa sổ access token; vẫn giữ lý do không dùng presigned.

3. **C-3. Lamp fault không chỉ đến từ CV.**  
   `CLAUDE.md:L221` ↔ `L238, L1371` → đoạn đầu nói `lamp_out/lamp_dim` chỉ từ CV, đoạn sau cho kỹ sư báo tại chỗ. → Service chỉ nhận hai loại này và ghi `FieldReport` (`src/LuxMap.Modules.Faults/FaultReportService.cs:94,109-113,162-163,208-209`). Contract cũng đang mang hai phát biểu không khớp (`docs/api-contract-v1.1.md:285,703`).  
   **Sửa:** giới hạn “chỉ CV” vào nguồn tự động từ sweep; nêu rõ nguồn báo tại chỗ/citizen.

4. **C-4. Thứ tự mặc định fault trái quyết định đã hiện thực.**  
   `CLAUDE.md:L236, L1094-L1098` → mặc định `-priority_score`. → P-3 chốt `-severity` (`docs/contract-drift.md:626`); code xác nhận `FaultSort.Default` (`src/LuxMap.Modules.Faults/FaultListQuery.cs:54`). Contract văn bản vẫn giữ thứ tự cũ (`docs/api-contract-v1.1.md:667`).  
   **Sửa:** ghi `-severity` và dẫn P-3, đồng thời giữ deviation đúng tại drift.

5. **C-5. Ngưỡng out và phạm vi phân loại bằng ratio trái v1.4.**  
   `CLAUDE.md:L267, L1385` → out mặc định 0.15; mọi ngưỡng phân loại theo ratio baseline. → `out_threshold_ratio` trả `null` (`src/LuxMap.Modules.Map/Features/PoleDetailService.cs:124-125`; D-R22 tại `docs/contract-drift.md:1071`). CV OFF quyết out trước khi tính ratio (`src/LuxMap.Modules.Survey/Processing/Frames/FrameClassification.cs:22-31`).  
   **Sửa:** ratio chỉ dùng cho dim; out do CV, trường threshold giữ null.

6. **C-6. Nguồn current status không còn chỉ sweep.**  
   `CLAUDE.md:L234, L240, L419-L420` ↔ `L2274-L2278` → chưa có sweep thì unknown; WO trả trạng thái sweep gần nhất. → Writer chung phục vụ cả accepted sweep và confirmed pole photo (`src/LuxMap.Modules.Survey/Review/PoleStatusPublication.cs:8-24`); ảnh có thể tạo normal/out với `LastSweepId=null` (`tests/LuxMap.Api.Tests/WorkOrderPolePhotoTests.cs:185-204`).  
   **Sửa:** dùng “quan sát đã công bố gần nhất”, nêu cả hai nguồn.

7. **C-7. Inventory cascade chỉ gồm hai bảng không còn đúng.**  
   `CLAUDE.md:L379-L380` ↔ `L1732-L1734` → mọi bảng cascade chỉ fixture/current-status từ pole. → Còn account token (`src/LuxMap.Modules.Identity/Configurations/IdentityConfigurations.cs:130-135`) và sync operation (`src/LuxMap.Modules.Sync/Configurations/SyncOperationConfiguration.cs:23-25`).  
   **Sửa:** giới hạn lập luận vào cascade của dữ liệu commune-scoped; liệt kê ngoại lệ credential/sync riêng.

8. **C-8. “Mọi ảnh gốc nguyên byte” thiếu ngoại lệ Citizen.**  
   `CLAUDE.md:L527-L530` ↔ `L1910-L1913` → original không re-encode/strip metadata, nhưng Citizen bắt buộc sanitize trước lưu. → Port storage cũng ghi ngoại lệ này (`src/LuxMap.Shared/Storage/ObjectStorage.cs:22-23`).  
   **Sửa:** giới hạn nguyên byte cho survey/evidence/pole-photo; nêu Citizen là ngoại lệ.

9. **C-9. Coverage không cấm tuyệt đối policy ngoài ma trận.**  
   `CLAUDE.md:L765-L769` ↔ `L2097-L2100, L2168-L2170` → endpoint dùng policy ngoài Matrix phải đỏ. → Coverage có ngoại lệ Device/Broker ngoài Matrix (`tests/LuxMap.Api.Tests/CapabilityPolicyCoverageTests.cs:34-43,93-115`).  
   **Sửa:** áp quy tắc capability cho endpoint người dùng và nêu ngoại lệ máy có tên.

10. **C-10. Hai đoạn confidence nói ngược nhau.**  
    `CLAUDE.md:L1003-L1007` ↔ `L1196-L1198` → vừa “đang hở”, vừa “đã có CHECK”. → Đoạn sau đúng (`src/LuxMap.Modules.Assets/Configurations/AssetConfigurations.cs:361-365`).  
    **Sửa:** bỏ cảnh báo cũ; xem S-24.

11. **C-11. Ví dụ lịch sử fault sử dụng chuyển trạng thái bị cấm.**  
    `CLAUDE.md:L1069-L1070` → `detected → confirmed → rejected`. → `confirmed → rejected` không thuộc máy trạng thái (`docs/api-contract-v1.1.md:289-294`; `src/LuxMap.Modules.Faults/FaultTransitions.cs:16-19`).  
    **Sửa:** dùng chuỗi hợp lệ như `detected → confirmed → in_progress → resolved`.

12. **C-12. Hai phát biểu so độ sáng giữa các cột không thống nhất phạm vi.**  
    `CLAUDE.md:L631-L634` ↔ `L1386` → một đoạn cho lux tương đối dùng so giữa các cột, đoạn kia cấm mọi so sánh giữa hai cột.  
    **Sửa:** ghi rõ giới hạn của tín hiệu phân loại theo baseline từng cột và mục đích nghiên cứu của số đo lux thủ công.

13. **C-13. Quy tắc ưu tiên nguồn có ngoại lệ đang phủ định câu “thắng mọi thứ”.**  
    `CLAUDE.md:L29-L33` ↔ `L219` → Contract luôn thắng; riêng IoT lại bắt theo drift, không theo Contract. Các lệch đã hiện thực như sort/ngưỡng out cũng tương tự (`docs/contract-drift.md:626,1071`).  
    **Sửa:** làm rõ cách áp quyết định drift đã duyệt khi Contract chưa hợp nhất, tránh hai lệnh ưu tiên đối nghịch.

14. **C-14. Multipart ảnh không phải lúc nào cũng tránh early-413/502.**  
    `CLAUDE.md:L2256-L2259` ↔ `L2285-L2286` → đoạn đầu nói multipart ảnh không bị; đoạn sau yêu cầu guard Caddy riêng. → Pole-photo là multipart (`src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotosController.cs:20-23`), từ chối trước đọc body (`PolePhotoBodyLimitAttribute.cs:15-16`), và Caddy có guard (`deploy/caddy/Caddyfile:30-39`).  
    **Sửa:** giới hạn kết luận cũ vào endpoint evidence đã đo; nêu ngoại lệ pole-photo.

**D — DUPLICATE**

Các range dưới đây là các lần lặp cùng quy tắc; phần ví dụ riêng vẫn có thể giữ nếu cần.

1. **D-1. Đúng tầng ghi phát hiện:** `CLAUDE.md:L47-L66` và `L88-L91`.  
   **Bản tốt hơn:** L47-L66 có đủ bảng phân loại. **Sửa:** phần `.ai/` chỉ dẫn lại bảng này.

2. **D-2. Nền v1.4/BH1750 thay điện thoại:** `L10-L18`, `L97-L100`, `L115-L120`, `L614-L619`.  
   **Bản tốt hơn:** L10-L18 sau cập nhật quyết định. **Sửa:** các đoạn sau chỉ nêu hệ quả riêng và dẫn lại.

3. **D-3. Tách `data_source`, không dùng field cho testbed:** `L112-L113`, `L122-L134`, `L1467`; danh sách enum còn lặp tại `L126-L130` và `L193`.  
   **Bản tốt hơn:** L122-L134 vì phân biệt được với `source_channel`. **Sửa:** giữ một định nghĩa và một liên kết tới enum.

4. **D-4. Enum trả chuỗi, không số:** `L144`, `L185-L200`, `L1413`, `L1461`.  
   **Bản tốt hơn:** L185-L200 cạnh danh sách giá trị. **Sửa:** các mục khác dẫn lại; giữ riêng cách serializer thực thi.

5. **D-5. Thời gian UTC:** `L145` và `L1410`.  
   **Bản tốt hơn:** L145 có wire format và `DateTimeKind.Utc`. **Sửa:** bỏ bản nhắc thiếu ngữ cảnh ở stack.

6. **D-6. Sinh ID bằng DB, không LPAD trần:** `L162-L163`, `L1493-L1495`.  
   **Bản tốt hơn:** L162-L163 sau sửa C-1. **Sửa:** đoạn sau chỉ giữ lưu ý thứ tự migration riêng.

7. **D-7. Không dùng `feature.id`:** `L173`, `L1462`.  
   **Bản tốt hơn:** L173 có tên thay thế `properties.pole_id`. **Sửa:** bỏ bản lặp trong anti-pattern.

8. **D-8. Không đưa tọa độ 3405 ra API:** `L177-L179`, `L468-L473`, `L1459`.  
   **Bản tốt hơn:** L177-L179 là quy tắc ngắn; L468-L473 chỉ giữ hạn chế implementation. **Sửa:** bỏ lần nhắc thứ ba.

9. **D-9. Không gộp unknown vào out:** `L218`, `L1387`, `L1466`.  
   **Bản tốt hơn:** L218 giải thích nghĩa nghiệp vụ. **Sửa:** giữ một quy tắc chuẩn.

10. **D-10. Bbox bắt buộc cho map:** `L233`, `L673-L675`, `L1463`; yêu cầu dùng index còn lặp tại `L443-L466`, `L1411`.  
    **Bản tốt hơn:** L233 cho giao diện; L443-L466 cho bẫy query. **Sửa:** giữ hai phạm vi đó, bỏ bản nhắc lại không thêm điều kiện.

11. **D-11. Chi tiết cột đủ một request:** `L234`, `L1464`.  
    **Bản tốt hơn:** L234 sau cập nhật nguồn trạng thái. **Sửa:** bỏ bản anti-pattern lặp.

12. **D-12. Fault list là JSON phân trang, không GeoJSON:** `L236`, `L1460`.  
    **Bản tốt hơn:** L236 sau sửa sort. **Sửa:** giữ một mô tả.

13. **D-13. Backend tính ratio/classification:** `L269`, `L1465`.  
    **Bản tốt hơn:** L269 có lý do consumer. **Sửa:** anti-pattern dẫn lại.

14. **D-14. Không hardcode ngưỡng:** `L267`, `L1468`.  
    **Bản tốt hơn:** rule cạnh cấu hình hiện hành, sau bỏ ngưỡng out cũ. **Sửa:** giữ một rule có nguồn cấu hình đúng.

15. **D-15. EF cấm sửa commune key của Feeder:** `L358-L365`, `L1314-L1322`.  
    **Bản tốt hơn:** L1314-L1322 có cả lý do không bắt `InvalidOperationException` chung. **Sửa:** đoạn guard chỉ dẫn tới ngoại lệ.

16. **D-16. Không công bố số đo sai của guard:** `L401-L405`, `L840-L889`.  
    **Bản tốt hơn:** L401-L405 giữ kết luận ngắn. **Sửa:** để dữ liệu đo trong lịch sử, không chép lại hai nơi.

17. **D-17. Xóa metadata thumbnail tường minh:** `L532-L536`, `L587-L593`.  
    **Bản tốt hơn:** L587-L593 đủ EXIF/XMP/IPTC và test canh. **Sửa:** giữ bản này.

18. **D-18. JPEG-only là tiền đề suppression advisory:** `L538-L543`, `L595-L599`, `L1910-L1913`, `L2149-L2150`.  
    **Bản tốt hơn:** L595-L599 giải thích ràng buộc bảo mật; Citizen giữ riêng chuỗi sanitize. **Sửa:** dẫn quy tắc decode chung từ các consumer.

19. **D-19. Nguồn commune của tài sản và ngoại lệ tuyến liên xã:** `L808-L817`, `L1168-L1173`, `L1275-L1307`.  
    **Bản tốt hơn:** L1275-L1307 giải thích cả FK ghép và lớp thông điệp ứng dụng. **Sửa:** các đoạn trước chỉ nêu nguồn dữ liệu khác biệt rồi dẫn lại.

20. **D-20. Audit chung/append-only, cột fault chỉ giữ quyết định cuối:** `L1062-L1076`, `L1389`, `L1512-L1518`, `L1585-L1588`.  
    **Bản tốt hơn:** L1512-L1517 mô tả cơ chế chung và giới hạn thật. **Sửa:** phần fault chỉ giữ yêu cầu riêng của `IAudited`.

21. **D-21. Sequence test dùng chung/chọn dải/khôi phục:** `L1015-L1043`, `L1258-L1271`.  
    **Bản tốt hơn:** L1258-L1271 có điều kiện thiết yếu `try/finally` trả sequence. **Sửa:** hợp nhất quy tắc; bỏ nhật ký chạy lặp.

22. **D-22. Điều khiển chỉ testbed, không lưới xã:** `L1373-L1376`, `L1471`.  
    **Bản tốt hơn:** L1373-L1376 có capability và cờ thiết bị. **Sửa:** bỏ bản nhắc lại trong anti-pattern.

23. **D-23. Schema OpenAPI viết tay đè schema sinh:** `L1637-L1640`, `L1879-L1880`.  
    **Bản tốt hơn:** L1637-L1640 giải thích vì sao CI tự khớp vẫn xanh; generator có bản viết tay tại `docs/openapi/tools/gen_consolidated_spec.py:359,386`. **Sửa:** mục CSV dẫn lại.

24. **D-24. Test model không chứng minh accuracy:** `L1957-L1958`, `L2156-L2157`.  
    **Bản tốt hơn:** L2156-L2157 tổng quát và xác định trách nhiệm WP4. **Sửa:** Citizen chỉ giữ ví dụ riêng nếu cần.

25. **D-25. Adapter ngoài HTTP phải kiểm validation:** `L1991-L1995`, `L2166-L2167`.  
    **Bản tốt hơn:** L1991-L1995 nêu cả chuyển validation vào service hoặc gọi lại tường minh. **Sửa:** dùng một rule chung, giữ ví dụ Sync/MQTT ngắn.

26. **D-26. Device query phải lọc node của principal:** `L2101-L2103`, `L2139`.  
    **Bản tốt hơn:** L2101-L2103 giải thích vì sao scope cùng xã chưa đủ. **Sửa:** phần command dẫn lại.

27. **D-27. Machine endpoints là ngoại lệ có tên:** `L2097-L2100`, `L2140-L2141`, `L2168-L2170`.  
    **Bản tốt hơn:** L2168-L2170 bao cả thiết bị/broker và dùng tên mới. **Sửa:** giữ thêm yêu cầu OpenAPI của Device nhưng bỏ ba bản mô tả coverage.

28. **D-28. `Response.Clear()` xóa header:** `L1755-L1756`, `L2110-L2112`.  
    **Bản tốt hơn:** cần hợp hai ví dụ `Retry-After` và challenge an toàn; middleware chung ở `src/LuxMap.Api/Http/ExceptionHandlingMiddleware.cs:88-94`. **Sửa:** một nguyên nhân chung, hai trường hợp header.

29. **D-29. FK ghép MATCH SIMPLE bỏ kiểm khi có null:** `L1280-L1289`, `L1685-L1691`.  
    **Bản tốt hơn:** L1685-L1691 nêu cảnh báo tổng quát/FK đơn bù; đoạn O-7 giữ lý do không dùng MATCH FULL. **Sửa:** dẫn chung một rule, giữ hai hệ quả khác nhau.

30. **D-30. Test khóa cần tránh xanh giả do FK lock:** `L1894-L1896`, `L1942`, `L2136-L2138`.  
    **Bản tốt hơn:** L2136-L2138 giải thích KEY SHARE/NO KEY UPDATE. **Sửa:** các fixture chỉ dẫn lại cách giữ khóa.

31. **D-31. QR code không đi trong path/log/audit:** `L1884-L1886`, `L1909`.  
    **Bản tốt hơn:** L1884-L1886 phân biệt code với `qr_id`. **Sửa:** phần public endpoint chỉ dẫn rule này.

32. **D-32. Down migration dựng lại CHECK cũ có thể gãy:** `L1603-L1605`, `L1898-L1899`, `L1934-L1935`, `L1959-L1960`, `L2142-L2143`, `L2204-L2205`, `L2240-L2241`.  
    **Bản tốt hơn:** L1603-L1605 giải thích cơ chế; các đoạn sau cung cấp tên migration/giá trị cụ thể. **Sửa:** một cảnh báo chung kèm bảng điều kiện từng migration, tránh lặp nguyên lý.

**W — WRONG-LAYER**

Các phân loại này áp dụng bảng do chính file đặt tại `CLAUDE.md:L47-L52`; không đề xuất bố cục tách file.

1. **W-1. Snapshot tiến độ và lịch ticket.**  
   `CLAUDE.md:L118-L120, L275-L299, L319, L389-L391, L524-L525, L605-L610, L653-L656, L768-L769, L930-L931, L996-L1007, L1107, L1116, L1419-L1440, L1487-L1491, L1503, L1518, L2093` → đang chờ, cần xong tuần nào, ai là caller đầu tiên, việc chưa làm. → Theo L52, đích là **tracking.html**; tracking đã có cả bảng dependency (`tracking.html:560-571`) và follow-up topology (`:622`).  
   **Sửa:** giữ constraint bền vững; trạng thái/owner/deadline đặt trong tracking.

2. **W-2. Bản sao deviation đang tồn tại hoặc đã đóng.**  
   `CLAUDE.md:L219, L228-L229, L234, L545-L548, L614-L619, L653-L656, L689, L1091-L1092, L1100-L1107, L1130-L1147, L1438` → kể code/mock lệch Contract, SELF-SIGNED, nợ sửa hình dạng. → Theo L49, đích là **contract-drift.md**; các trường hợp tương ứng đã có mục tại `docs/contract-drift.md:523-579,626,740-800,841-863,880-889,1071`.  
   **Sửa:** giữ con trỏ tới decision/deviation hiện hành, không duy trì một log drift thứ hai.

3. **W-3. Lịch sử bản AGENTS bị chép đông cứng.**  
   `CLAUDE.md:L75-L77` → bản untracked ngày 17/09 thiếu 74 dòng. → Checkout hiện có symlink; lịch sử tạo vùng bàn giao/symlink nằm trong **`d22dc99`**.  
   **Sửa:** giữ “AGENTS là symlink, không tạo file thường”; để chi tiết bản cũ trong git history.

4. **W-4. Nhật ký benchmark guard.**  
   `CLAUDE.md:L401-L405, L840-L889` → 30 cặp, warm-up, IQR, chu kỳ ba, từng lượt µs/entity. → Đây là kết quả đo theo môi trường/lần chạy, không phải invariant của `CommuneWriteGuard`.  
   **Sửa:** giữ kết luận không dùng số đo này để nới guard; để bảng đo trong lịch sử.

5. **W-5. Nhật ký EXPLAIN và phép đo không gian.**  
   `CLAUDE.md:L435-L436, L446-L451, L458-L460, L471-L479` → thời gian, buffers, cost, số hàng ước lượng, offset/ppm đo tại điểm khảo sát. → Quy tắc cần giữ là dùng mét đúng phép chiếu, prefilter bằng bbox/index và không xuất 3405; số đo cụ thể đã có phần giải thích trong `src/LuxMap.Persistence/SpatialConstants.cs:17-35`.  
   **Sửa:** giữ quy tắc và con trỏ bằng chứng, đưa bảng kết quả đo về lịch sử.

6. **W-6. Nhật ký dữ liệu và lỗi test trong những lượt cũ.**  
   `CLAUDE.md:L151-L152, L205-L214, L347-L349, L1020-L1032, L1206-L1215, L1255-L1267` → 103 timestamp trùng, 45 hàng đổi, tám test cũ, số lượt đỏ/xanh, sequence 396625, 217 cột/20 fault rác. → Đây là snapshot lịch sử; BE-36 đã thay vòng đời DB test (`tests/LuxMap.Api.Tests/TestDatabase.cs:17-24`).  
   **Sửa:** giữ invariant được rút ra và test canh; bỏ nhật ký số lượng khỏi quy tắc hiện hành.

7. **W-7. Tình trạng registry và cache máy.**  
   `CLAUDE.md:L572-L584` → HTTP 404/401 theo ngày, máy nào còn cache mới chạy được. → Compose chỉ chứng minh image đang pin (`docker-compose.yml:125-129`), không chứng minh tình trạng registry hiện tại.  
   **Sửa:** giữ rule pin manifest-list digest; tình trạng pull và blocker vận hành về tracking/issue.

8. **W-8. Log kiểm MinIO thủ công.**  
   `CLAUDE.md:L607-L608` → 8.2 KiB gốc, 1.8 KiB thumbnail, SHA-256 vòng tròn. → Đây là kết quả một lượt kiểm; không phải đảm bảo hiện hành của adapter.  
   **Sửa:** giữ nợ thiếu test S3 thật ở tracking; để bytes/hash của lần kiểm trong history.

9. **W-9. Nhật ký policy cũ trước v1.7.**  
   `CLAUDE.md:L664-L667` → kể POST lux từng mở cho mọi role. → Quy tắc hiện hành chỉ cần `RecordLuxReading`/`ReadLuxReadings`; thay đổi role/capability đã ghi tại `docs/contract-drift.md:93-103`.  
   **Sửa:** giữ policy hiện tại, dẫn lịch sử thay vì kể lại lỗi đã sửa.

10. **W-10. Log xóa stub/schema đã hoàn tất.**  
    `CLAUDE.md:L2028-L2029` → liệt kê các stub/schema đã xóa. → Đây là log thay đổi; phần còn hữu ích là ràng buộc `object?`/lint fix hiện có tại `docs/openapi/tools/gen_consolidated_spec.py:66-71`.  
    **Sửa:** bỏ inventory những thứ đã xóa, giữ quy tắc sinh schema.

11. **W-11. Số liệu từng lần diễn tập/phá thử ở các phần mới.**  
    `CLAUDE.md:L1665-L1667, L1736-L1739, L1888-L1889, L1953-L1954, L2226-L2229, L2256-L2259, L2287-L2289` → HEVC 148 s, SMTP Mac/Linux cùng phút, 3/3, 11 test đỏ, 120 MB, chạy riêng 10/10. → Đây là measurement log; khác với giới hạn cấu hình có hiệu lực như 40 MP, 17 MiB hoặc batch 500.  
    **Sửa:** giữ failure mode và regression test; chuyển kết quả từng lượt về lịch sử.

**Các điểm đã kiểm nhưng không đánh dấu sai**

- **“IdentitySeeder chưa cần backdoor” vẫn đúng.** Trong production source, grep `EnterUnscopedSystemWriteBackdoor` chỉ thấy khai báo/tài liệu ở `LuxMapDbContext`; chưa có caller seeder.
- **Redis vẫn có trong Compose** (`docker-compose.yml:42-54`), dù chưa tìm thấy consumer runtime trong `src/`/`tests/`; chưa đủ cơ sở gọi dòng stack này sai.
- `FaultConfigurations.cs` vẫn tồn tại; không được coi tên file này là class bị mất.
- `PointGeometry`/`LineStringGeometry` tại L2029 được nói là **đã xóa**, nên việc chúng vắng mặt không phải stale symbol.
- Các tên test/method khác được viện dẫn đã tra trong phần phụ trách; không xác nhận thêm symbol bị mất ngoài `DeviceEndpoints` và action register nêu trên.

**Các phần chưa thể xác minh đầy đủ**

- **FW ngoài repo:** chữ ký xác nhận các nền SELF-SIGNED; riêng quyết định đặt `LuxMapPolicies` trong Shared tại L785-L790 chưa tìm được xác nhận/lật rõ ràng.
- **Dịch vụ và máy đang chạy:** registry MinIO, quyền DB local, dữ liệu dev/VPS, deploy thực tế, chứng chỉ SMTP.
- **Số đo lịch sử:** benchmark, EXPLAIN, sabotage, FFmpeg/Caddy/SMTP theo OS; chỉ đọc được bằng chứng lưu lại, không tái chạy.
- **Ngoài worktree:** FE/mobile đang gọi gì, Camera2 trên máy thật, bàn giao nhóm và accuracy YOLO trên dữ liệu thực địa.