# WO-SURVEY-CHAIN — Phase 1 (Codex)

✅ Đã đọc `AGENTS.md`, toàn bộ task và code liên quan. HEAD = `origin/dev` = `a9a85df18e56c438dcb98f8bfd8a4605e2097bae`.

⚠️ Đây là thiết kế qua đọc mã nguồn; không sửa file, không build, không chạy test. Các tên phương thức/test mới bên dưới là **đề xuất**, không phải code đã tồn tại.

**1. Nguồn “lần khảo sát đã duyệt” và nhiều sweep**

Nguồn đúng là:

```text
SurveySweep.WorkOrderId = phiếu cha
SurveySweep.Status = Accepted
SurveySweep.AcceptedRunId = LuminanceHistory.RunId
SurveySweep.SweepId = LuminanceHistory.SweepId
```

Đọc `LuminanceHistory.ClassifiedAs`, không đọc trực tiếp `PoleObservation.ClassifiedAs`, không đọc `PoleCurrentStatus`.

Lý do:

- Một run có nhiều lượt qua cùng cột; `SurveyPublicationRules.Choose` có thể biến hai lượt ON/OFF xung đột thành `unknown`. Đọc một observation riêng sẽ bỏ mất quyết định tổng hợp này. Xem [SurveyPublicationRules.cs:72](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/Review/SurveyPublicationRules.cs:72).
- `History` lưu đúng kết quả tổng hợp đã công bố. Xem [SurveyPublicationRules.cs:48](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/Review/SurveyPublicationRules.cs:48).
- Khóa chính history là `(sweep_id, pole_id)`, và history trỏ tới đúng cặp run/sweep. Xem [SurveyPublicationConfigurations.cs:50](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/Configurations/SurveyPublicationConfigurations.cs:50).
- Accept và publish nằm trong cùng transaction; accept ghi `AcceptedRunId` trước khi gọi `Publish`. Xem [SurveyReviewService.cs:244](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:244).

**Nhiều sweep là lựa chọn còn mở.** Đề xuất D-1: chọn **history mới nhất của từng cột trong các sweep accepted thuộc chính phiếu**, theo:

```text
evaluated_at DESC,
published_at DESC,
length(sweep_id) DESC,
sweep_id DESC
```

Chọn hàng mới nhất **trước**, sau đó mới lọc `out/dim`. Nếu lọc `out/dim` trước, một lần cũ `out` sẽ sống lại dù lần mới đã thấy `normal`.

Theo đề xuất này:

| History cũ → mới của cùng cột | Kết quả dùng cho follow-up |
|---|---|
| `out → normal` | Không mang fault |
| `out → dim` | Chỉ tìm effective `lamp_dim` |
| `out → unknown` | Không mang fault; đây là hệ quả cần Mỹ duyệt |
| `out`, sau đó có ảnh cột công bố `normal` | Vẫn xét `out` của phiếu khảo sát theo C-2 |

Dòng cuối là giới hạn có chủ ý của C-2: đây là chuỗi công việc theo kết quả khảo sát của phiếu, không phải truy vấn trạng thái hiện tại toàn hệ thống.

C-4 phải áp bằng **`history.commune_id = parent.commune_id`**, không bằng xã sở hữu tuyến. Tuyến liên xã có thể mang cột ở xã khác.

**2. Port, adapter và DI**

Khai trong WorkOrders:

```csharp
public sealed record AcceptedSurveyFaultTarget(
    string PoleId,
    FaultType FaultType,
    DataSource DataSource);

public interface IAcceptedSurveyFaultTargets
{
    Task<AcceptedSurveyFaultTarget[]> TargetsAsync(
        string workOrderId,
        CancellationToken ct);
}
```

Adapter đề xuất:

```csharp
public sealed class AcceptedSurveyFaultTargets(
    LuxMapDbContext db) : IAcceptedSurveyFaultTargets;
```

Đặt adapter tại `LuxMap.Modules.Survey/Review/AcceptedSurveyFaultTargets.cs`; đăng ký scoped trong `SurveyModule.RegisterServices`:

```csharp
services.AddScoped<
    IAcceptedSurveyFaultTargets,
    Review.AcceptedSurveyFaultTargets>();
```

WorkOrders inject port vào `WorkOrderService`. Không thêm project reference. Chiều phụ thuộc hiện tại đã là Survey → WorkOrders; DI của `IPolePhotoPublication` đang dùng đúng khuôn này. Xem [SurveyModule.cs:36](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/SurveyModule.cs:36) và [LuxMap.Modules.Survey.csproj:4](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/LuxMap.Modules.Survey.csproj:4).

LINQ đọc dữ liệu của adapter:

```csharp
var parent = await db.Set<WorkOrder>().AsNoTracking()
    .SingleOrDefaultAsync(w => w.WorkOrderId == workOrderId, ct)
    ?? throw /* WORK_ORDER_NOT_FOUND, 404 */;

var histories = await (
    from s in db.Set<SurveySweep>().AsNoTracking()
    join h in db.Set<LuminanceHistory>().AsNoTracking()
        on s.SweepId equals h.SweepId
    where s.WorkOrderId == workOrderId
       && s.Status == SweepStatus.Accepted
       && s.AcceptedRunId == h.RunId
       && h.CommuneId == parent.CommuneId
    select h
).ToArrayAsync(ct);
```

Sau materialize:

1. `GroupBy(PoleId)`.
2. Chọn hàng mới nhất theo thứ tự ở mục 1.
3. Gọi **`SurveyPublicationRules.FaultFor(h.ClassifiedAs)`**.
4. Bỏ hàng trả `null`; trả `PoleId`, loại fault và nguồn dữ liệu.

Ánh xạ duy nhất hiện có ở [SurveyPublicationRules.cs:83](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/Review/SurveyPublicationRules.cs:83).

Tập đọc bị giới hạn theo một phiếu và xã neo. Nếu sau này cần đẩy việc chọn mới nhất xuống SQL, dùng `DISTINCT ON (pole_id)` với cùng thứ tự; không đổi ngữ nghĩa.

Port chỉ trả mục tiêu, không trả ảnh, không đọc object store, không sinh fault và không kiểm active link. Không cần kéo luật quyền xem toàn bộ media liên xã vào truy vấn metadata xã neo này.

**3. `FollowUp`, lỗi và tranh chấp**

Hiện tại `FollowUp` chỉ lấy `WorkOrderFault.InspectionOutcome == FaultPresent`; `Insert` khóa fault rồi đọc lại trạng thái nhưng vẫn trả `FAULT_ALREADY_IN_WORK_ORDER` khi thấy active link. Xem [WorkOrderService.cs:207](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:207) và [WorkOrderService.cs:233](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:233).

**Chỉ lọc C-3 trước `Insert` là chưa đủ.** Một request khác có thể lấy fault sau bước lọc và trước khi request này lấy khóa.

Đề xuất tách nhánh survey, giữ nguyên hành vi inspection → repair:

```csharp
private Task<WorkOrderDetail> FollowUpSurvey(
    string id,
    FollowUpWorkOrderRequest request,
    CancellationToken ct);

private Task<WorkOrderDetail> InsertCore(
    NewWorkOrder order,
    CancellationToken ct);
```

`InsertCore` chứa phần kiểm tra và ghi dùng chung; không tự mở/commit transaction, không lấy thêm khóa. Caller phải truyền fault vừa đọc lại dưới khóa. `Insert` hiện tại giữ transaction và `FaultLocks`, rồi gọi core. Nhánh survey tự quản lý transaction của mình.

Trình tự nhánh survey:

1. Kiểm request: `task_kind`, lịch, title, `fault_ids` không rỗng nếu có, không phần tử trắng/null, không trùng.
2. Đọc cha qua query filter, không tracking; ngoài phạm vi → 404.
3. Mở transaction.
4. Khóa cha:

   ```sql
   SELECT 1
   FROM work_order
   WHERE work_order_id = @id AND commune_id = @commune
   FOR NO KEY UPDATE;
   ```

5. Đọc lại cha qua filter; kiểm `verified`, cặp survey → inspection.
6. Gọi port. Khóa cha giữ tập accepted ổn định với `SurveyReviewService.Review`, vốn khóa cùng cha trước khi accept.
7. Tìm fault đang mở, đúng cột, **đúng `data_source`**, đúng loại hiệu lực. Theo từng nhóm `(FaultType, DataSource)` từ port:

   ```csharp
   var open = FaultStatusSets.Open.ToArray();
   FaultType? overridden = type;

   var branch = db.Set<Fault>().AsNoTracking().Where(f =>
       f.CommuneId == parent.CommuneId
       && f.DataSource == source
       && poleIds.Contains(f.PoleId!)
       && open.Contains(f.FaultStatus)
       && (f.OverrideFaultType == overridden
           || (f.OverrideFaultType == null && f.FaultType == type)));
   ```

   Ghép các nhánh bằng `Union`; không dùng `Contains(override ?? type)` vì code đã ghi nhận vấn đề dịch enum nullable. Khuôn đúng hiện ở [SurveyReviewService.cs:293](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:293).

8. Kiểm `fault_ids` được yêu cầu là tập con của tập khớp tại bước 7. Không yêu cầu fault phải có `OriginObservationId` thuộc sweep: C-2 bắt buộc lấy cả fault mở từ trước.
9. `FaultLocks.LockAsync` trên các ID ứng viên; đọc lại **không tracking** và kiểm lại open/type/source/xã.
10. Sau khóa mới loại active link:

    ```csharp
    !db.Set<WorkOrderFault>().Any(link =>
        link.FaultId == f.FaultId && link.ReleasedAt == null)
    ```

    Không join sang `WorkOrder`: assignee filter có thể giấu phiếu đang giữ fault. Nguồn quyết định C-3 là `released_at`, không phải danh sách trạng thái phiếu tự viết.
11. Giao tập còn lại vào `InsertCore`, ghi phiếu con, link của **phiếu con**, một audit `Created` và notification hiện có; commit.

Không bổ sung fault mới xuất hiện sau khi đã chốt tập khóa. Không lọc lại rồi lấy thêm khóa ngoài thứ tự.

**Thứ tự khóa:** cha `NO KEY UPDATE` → fault theo thứ tự của `FaultLocks` → ghi child/link.

`FaultLocks` hiện sắp `fault_id` để khóa; đây là thứ tự đồng bộ, không phải thứ tự hiển thị. Giữ nguyên helper tại [FaultLocks.cs:22](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Faults/FaultLocks.cs:22).

**Vì sao không dùng `FOR UPDATE` cho cha?** Phiếu repair tạo từ inspection cùng case có FK về root survey. Nếu survey follow-up giữ root `FOR UPDATE` rồi chờ fault, còn repair giữ fault rồi chờ FK `KEY SHARE` về root, có thể thành deadlock. `NO KEY UPDATE` vẫn xung đột với thao tác duyệt/sửa cha nhưng tương thích với FK `KEY SHARE`. Các FK root/parent có thật tại [WorkOrderConfigurations.cs:46](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/Configurations/WorkOrderConfigurations.cs:46).

Hành vi lỗi đề xuất:

| Tình huống | Kết quả |
|---|---|
| Cha ngoài phạm vi/không tồn tại | 404 `WORK_ORDER_NOT_FOUND` |
| Cha chưa verified hoặc sai cặp | 409 `INVALID_STATE_TRANSITION`; giữ `allowed_follow_up_kinds` |
| Không còn fault đủ điều kiện, kể cả tất cả đã bận | 409 `NOTHING_TO_FOLLOW_UP` |
| `fault_ids` rỗng, trùng, trắng hoặc chứa ID ngoài tập khớp | 400 `VALIDATION_FAILED`, `field=fault_ids`, `carried_fault_ids` là tập hiện có thể mang |
| Fault hợp lệ ban đầu bị lấy, đóng hoặc đổi loại trong lúc chờ khóa | Loại fault đó; còn phần khác thì tạo tiếp, hết thì `NOTHING_TO_FOLLOW_UP` |
| Assignee không đủ điều kiện | Giữ 409 `ASSIGNEE_NOT_ELIGIBLE` |

Danh sách trả về, kể cả `carried_fault_ids`, sắp:

```text
created_at, length(fault_id), fault_id
```

Hiện lỗi 400 dùng `present.Order()` tại [WorkOrderService.cs:222](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:222); cần bỏ cách sắp chuỗi trần này.

Unique partial `ux_work_order_fault_fault_id_active` vẫn giữ làm backstop. Không cố truy vấn tiếp trong transaction đã gặp `23505`; handler hiện có đã rollback trước lookup tại [WorkOrderService.cs:499](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:499). Với các writer tuân thủ `FaultLocks`, nhánh survey phải giải quyết C-3 trước khi chèn.

`root_work_order_id` của root vẫn null; child dùng `parent.RootWorkOrderId ?? parent.WorkOrderId`, như code hiện tại tại [WorkOrderService.cs:277](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:277).

**4. `AllowedActions` và FE**

Thay duy nhất bảng cặp:

```csharp
TaskKind.Survey => [TaskKind.Inspection],
TaskKind.Inspection => [TaskKind.Repair],
_ => [],
```

`AllowedActions` đã dựa trên `FollowUpKinds` và `Allows`; thay bảng này đủ để Manager thấy `follow_up` trên survey verified. Không cần truy vấn DB từ `AllowedActions`. Xem [WorkOrderRules.cs:22](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/WorkOrderRules.cs:22).

FE cần biết:

- Survey verified xuất hiện hành động `follow_up`, chỉ cho tạo inspection.
- Có nút không đảm bảo có fault để mang: endpoint vẫn có thể trả `NOTHING_TO_FOLLOW_UP`.
- Fault bận bị bỏ qua; response `fault_ids` là tập thực sự đã nhận.
- Khảo sát liên xã chỉ nối công việc trong xã neo theo C-4. Các xã khác cần phiếu riêng.
- Không dùng `parent.fault_ids` để dựng tập chọn cho survey: C-5 cố ý để survey không có link fault.

Endpoint vẫn là `ManageWorkOrders`, không thay policy; xem [WorkOrdersController.cs:135](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/WorkOrdersController.cs:135).

**5. Test, sabotage và tài liệu**

Các test đề xuất phải có kỳ vọng literal:

| Test | Dữ liệu/assert chính; sabotage phải làm đỏ |
|---|---|
| `Verified_survey_allows_only_inspection_follow_up` | Literal ba task kind × trạng thái × bốn role; thêm survey→repair hoặc mở quyền role khác phải đỏ |
| `Survey_follow_up_uses_only_the_accepted_run_of_its_own_order` | Có run mới chưa duyệt, sweep returned và sweep của phiếu khác; bỏ một điều kiện join phải đỏ |
| `Published_conflict_unknown_does_not_carry_a_raw_out_observation` | Cùng run có ON/OFF nhưng history unknown; đọc observation thay history phải đỏ |
| `Latest_history_is_chosen_before_filtering_fault_classes` | `out→normal`, `out→dim`, `out→unknown`, và thứ tự accept ngược thứ tự capture; lọc out/dim trước hoặc dùng published time làm thời gian chính phải đỏ |
| `Preexisting_open_faults_are_carried_by_effective_type` | Fault cũ không thuộc origin sweep; phủ override đúng/sai và cả sáu trạng thái; chỉ lấy origin sweep hoặc bỏ nhánh override phải đỏ |
| `Survey_follow_up_keeps_only_anchor_commune_and_matching_data_source` | Manager thấy hai xã; fault xã phụ và nguồn khác không được mang; bỏ một predicate phải đỏ |
| `Busy_faults_are_skipped_after_acquiring_fault_locks` | Hai ứng viên; request khác lấy một fault khi follow-up đang chờ khóa; nhận 201 chỉ với fault còn lại; lọc trước khóa hoặc giữ `LinkConflict` cho cả request phải đỏ |
| `All_busy_faults_produce_nothing_to_follow_up_without_writes` | Không child/link/audit/notification khi tất cả bị lấy; trả 409 sai mã hoặc ghi dở phải đỏ |
| `Follow_up_subset_errors_return_numerically_ordered_carried_ids` | ID hai phía ngưỡng độ rộng, cùng `created_at`; assert thứ tự trực tiếp; `.Order()` trần phải đỏ |
| `Concurrent_survey_follow_ups_never_claim_the_same_fault` | Một request thắng; request còn lại bỏ fault/hết tập; không duplicate link hay audit thừa |
| `Survey_follow_up_and_repair_from_the_same_case_do_not_deadlock` | Dựng root survey, inspection verified, cùng fault; repair giữ fault rồi chèn FK về root trong lúc survey follow-up chờ fault; đổi root lock sang `FOR UPDATE` phải gây timeout/test đỏ |
| `Survey_inspection_repair_share_one_case_without_mutating_the_survey` | Chạy đủ chuỗi; root không có `WorkOrderFault`, không tăng số fault, FR-2a vẫn confirmed đúng |

Giữ regression inspection → repair tại [WorkOrderTests.cs:472](/Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:472) và hành vi fault đã bận trả 409 của **nhánh inspection** tại [WorkOrderTests.cs:515](/Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:515).

Test có run/history bất biến phải dùng tài khoản/xã riêng giữ lâu dài, không gắn vào tài khoản fixture sẽ bị teardown xóa.

Tài liệu cần ghi khi Phase 2:

- Drift đề xuất `WSC-1…WSC-5`: cặp survey→inspection; nguồn accepted history và luật nhiều sweep; C-2/C-3; xã neo; lỗi và hành vi tập con.
- Không migration, không đổi CHECK/FK/index, không đổi `PLAN`.
- OpenAPI không thêm endpoint hay hình dạng body/response, nhưng **có đổi mô tả ngữ nghĩa**. Cần sửa XML comment `FollowUpWorkOrderRequest.FaultIds`, mô tả endpoint và summary viết tay của generator. Generator đang ghi chỉ mang `fault_present` tại [gen_consolidated_spec.py:230](/Users/nhm809/Documents/LuxMap/luxmap_backend/docs/openapi/tools/gen_consolidated_spec.py:230).
- Sau implement mới export spec → chạy generator → lint; phiên này chưa chạy.

Các quyết định C-1…C-5 khả thi. Hai rủi ro cần ghi rõ là C-4 làm case chỉ bao phủ xã neo, và C-2 không bảo đảm kết quả khảo sát còn là trạng thái mới nhất toàn hệ thống.

**6. D-item còn mở**

- **D-1 — Nhiều sweep accepted trên cùng cột:** khuyến nghị lấy history mới nhất theo `evaluated_at`, rồi `published_at`, rồi độ dài/ID sweep; chọn trước khi lọc out/dim. Theo đó `unknown` mới hơn cũng loại kết quả lỗi cũ. Mỹ cần chốt điểm này; C-1…C-5 chưa quyết định nó.

