# PHOTO-REVIEW — Phase 1 (Codex)

✅ Đã đọc toàn bộ task, luồng upload/decision/publication, cấu hình DB, audit, copy script và test liên quan.

⚠️ Chỉ đối chiếu mã nguồn; không sửa file, build, chạy test hay migration. Thiết kế dưới đây giữ R-1…R-3; các lựa chọn chưa được owner quyết định được đánh dấu tại danh sách D cuối phần.

**1. Ngữ nghĩa `final_cv_state`**

Khuyến nghị:

- Lưu `final_cv_state` **nullable**, chỉ nhận `on|off`.
- Chưa review: cả bốn trường mới đều null.
- Không tự backfill từ `cv_state`.
- Khi công bố:

  ```csharp
  var effectiveCvState = row.FinalCvState ?? row.CvState;
  ```

`final_cv_state = null` nghĩa là **chưa có review của kỹ sư**, không phải AI inconclusive. AI vẫn được biểu diễn nguyên trạng bằng `cv_state`, `confidence`, `model_version`, `artifact_version`, `analyzed_at`.

Đây là cách phân biệt được “AI nói off, chưa ai review” với “kỹ sư đã xem và đồng ý off”. Nếu tự trả `cv_state` vào `final_cv_state`, hai tình huống đó nhập làm một.

Thêm bốn property nullable vào entity và `PolePhotoItem`:

```csharp
public string? FinalCvState { get; set; }
public string? ReviewNote { get; set; }
public string? ReviewedBy { get; set; }
public DateTime? ReviewedAt { get; set; }
```

Tất cả response đang đi qua `Item`, nên thêm mapping tại đúng một nơi. Xem [WorkOrderPolePhotoService.cs:217](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:217) và [WorkOrderPolePhotoContracts.cs:7](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoContracts.cs:7).

Upload replay/GET chỉ trả dữ liệu đã lưu, không suy luận lại và không tự tạo review.

**2. AI null/inconclusive, provenance, confidence và fault**

Khuyến nghị cho kỹ sư gán `on/off` cả khi AI null/inconclusive; **bắt buộc note** vì nhãn mới khác AI. Sau review đó, Manager confirm được.

Nếu không có review và AI null/inconclusive, giữ 409 `POLE_PHOTO_INCONCLUSIVE`. Hiện điều kiện này đọc trực tiếp `row.CvState`, rồi dereference confidence và model khi publish tại [WorkOrderPolePhotoService.cs:198](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:198); cả hai chỗ phải đổi.

Phân biệt hai khái niệm:

```csharp
var hasReview = row.FinalCvState is not null;
var labelChanged = hasReview && row.FinalCvState != row.CvState;
```

| Trường hợp | Nguồn kết luận công bố | Confidence/model dùng cho kết luận |
|---|---|---|
| Không review, AI on/off | AI | Bộ AI gốc |
| Review đúng nhãn AI on/off | AI được người xác nhận | Bộ AI gốc, vẫn ghi rõ đây là score của AI |
| Review đổi nhãn AI | Kỹ sư | Confidence/model kết luận = null |
| AI null/inconclusive, kỹ sư gán nhãn | Kỹ sư | Confidence/model kết luận = null |

Bộ AI gốc luôn giữ nguyên ở ảnh và snapshot audit. Không gán `.91` của AI `on` cho kết luận người sửa thành `off`; không gán `1.0` để làm DB chấp nhận.

Đề xuất đổi **DTO nội bộ** của port, giữ nguyên method:

```csharp
public enum PolePhotoLabelSource { Ai, Engineer }

public sealed record PolePhotoPublication(
    string PhotoId,
    string WorkOrderId,
    string PoleId,
    string CommuneId,
    DataSource DataSource,
    string EffectiveCvState,
    PolePhotoLabelSource LabelSource,
    string? AiCvState,
    double? AiConfidence,
    string? AiModelVersion,
    string? AiArtifactVersion,
    string? ReviewedBy,
    DateTime? ReviewedAt,
    string? ReviewNote,
    DateTime CapturedAt,
    DateTime Now);

Task<PolePhotoPublicationResult> PublishAsync(
    PolePhotoPublication input,
    CancellationToken ct);
```

Đây vẫn là đường `IPolePhotoPublication` → `ReviewedPolePhotoPublisher` → `PoleStatusPublication.StageAsync`, không tạo writer thứ hai. Port hiện tại có confidence/model không-null tại [IPolePhotoPublication.cs:5](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/IPolePhotoPublication.cs:5).

Adapter phải kiểm invariants:

- `EffectiveCvState` chỉ on/off.
- Nhánh AI: nhãn hiệu lực bằng nhãn AI, confidence hữu hạn 0..1, model/artifact không trắng.
- Nhánh Engineer: có reviewer/time/note hợp lệ; score của kết luận không lấy từ bộ AI.

**Điểm phải sửa thêm ngoài bốn cột review:** `PoleStatusPublication.StageAsync` hiện cấm mọi trạng thái biết rõ mà confidence null; DB cũng áp `NULL ⇔ unknown`. Vì vậy chỉ nullable hóa DTO port sẽ vẫn lỗi. Xem [PoleStatusPublication.cs:15](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/Review/PoleStatusPublication.cs:15) và [AssetConfigurations.cs:350](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Assets/Configurations/AssetConfigurations.cs:350).

Theo D-3 đề xuất:

```csharp
// Signature giữ nguyên: confidence vốn đã là double?.
if (status == FixtureStatus.Unknown && confidence is not null
    || confidence is { } c && (!double.IsFinite(c) || c is < 0 or > 1))
    throw new InvalidOperationException(...);
```

- Human OFF → current `out`, confidence null.
- Human ON từ out/unknown/chưa có row → `normal`, confidence null.
- ON khi đang dim/normal → giữ classification và confidence cũ, tiến observation clock như hiện tại. Confidence này thuộc kết luận cũ, không thuộc ảnh mới.
- Giữ luật capture mới hơn, `LastSweepId/LastRunId=null`, không history/baseline, không tự đóng fault. Các luật hiện nằm tại [ReviewedPolePhotoPublisher.cs:25](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/Review/ReviewedPolePhotoPublisher.cs:25).

Không cần thêm cột provenance vào current-status cho ticket này: ảnh lưu review, fault giữ origin ảnh, audit publication giữ `label_source` và bộ trước/sau. Đổi nghĩa nullable confidence phải được ghi cho các consumer.

**Fault `lamp_out`:**

- Nhánh AI: giữ `source_channel=cv`, confidence AI, `DetectionModelVersion=AiArtifactVersion`.
- Nhánh người sửa: khuyến nghị `source_channel=field_report`, `StatusConfidence=null`, `DetectionModelVersion=null`, `ReportedBy=ReviewedBy`.
- Cả hai giữ `OriginPolePhotoId`, trạng thái khởi tạo `confirmed`, `ConfirmedBy` là Manager, severity từ cấu hình survey.
- Chống trùng theo **effective lamp_out + `FaultStatusSets.Open`**, không phân biệt source channel.

Code đã có tiền lệ `field_report` cho lamp_out/lamp_dim tại [FaultReportService.cs:94](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Faults/FaultReportService.cs:94). Tuy nhiên CHECK riêng của ảnh hiện bắt source CV; phải đổi theo D-4. Xem [WorkOrderPolePhotoConfiguration.cs:48](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/Configurations/WorkOrderPolePhotoConfiguration.cs:48).

Đây là chỗ “thêm, không breaking” của R-1 cần hiểu giới hạn: không xóa endpoint/trường là additive về hình dạng; mở rộng ý nghĩa confidence/source vẫn là thay đổi ngữ nghĩa API cần ghi drift.

**3. Endpoint review, review lại và đua với decision**

Body theo khuôn class:

```csharp
public sealed class PolePhotoReviewRequest
{
    [Required]
    public string FinalCvState { get; init; } = null!;

    [MaxLength(2000)]
    public string? ReviewNote { get; init; }
}
```

Service/controller:

```csharp
public Task<PolePhotoItem> ReviewAsync(
    string id,
    string photoId,
    PolePhotoReviewRequest request,
    CancellationToken ct);

[HttpPatch("{photo_id}/review")]
[Authorize(Policy = LuxMapPolicies.ExecuteWorkOrders)]
public Task<PolePhotoItem> Review(
    string id,
    [FromRoute(Name = "photo_id")] string photoId,
    PolePhotoReviewRequest request,
    CancellationToken ct);
```

Kiểm nhãn và độ dài ở **service**, không chỉ DataAnnotations. Trim note; rỗng thành null; khác `cv_state` mà note null → 400 `VALIDATION_FAILED`, `field=review_note`. Nhãn ngoài on/off, kể cả `inconclusive`, null, số hoặc `"ON"` → 400.

Policy hiện chỉ nhận FieldEngineer; `/decision` tiếp tục chỉ Manager qua `ReviewSurveys`. Xem [LuxMapPolicies.cs:122](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Shared/Authorization/LuxMapPolicies.cs:122).

Trình tự review:

1. Đọc cha bằng `Order(id, own:true)` rồi ảnh đúng `(id, photoId)` qua filter, không tracking.
2. Mở transaction.
3. Giữ nguyên thứ tự:

   ```sql
   SELECT 1 FROM work_order
   WHERE work_order_id = @id
   FOR UPDATE;

   SELECT 1 FROM pole
   WHERE pole_id = @pole AND commune_id = @commune
   FOR UPDATE;

   SELECT 1 FROM work_order_pole_photo
   WHERE photo_id = @photo AND commune_id = @commune
   FOR UPDATE;
   ```

4. Sau khóa cha kiểm lại người được giao và `in_progress`; sau đủ khóa đọc lại ảnh/reload.
5. Nếu `Decision != null` → 409 `POLE_PHOTO_ALREADY_DECIDED`.
6. Gán bốn trường review; stage một audit; một `SaveChanges`; commit.
7. Không gọi model, storage hay publication.

Khuôn khóa và reload hiện tại của `/decision` ở [WorkOrderPolePhotoService.cs:186](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:186). Không đổi sang `FromSql("SELECT * ...")` với entity có xmin.

Lỗi quyền/trạng thái:

| Tình huống | Kết quả |
|---|---|
| Role khác FieldEngineer | 403 `ROLE_FORBIDDEN` |
| Không phải người được giao/cha ngoài xã | 404 `WORK_ORDER_NOT_FOUND` |
| Ảnh ngoài phiếu hoặc xã ảnh ngoài scope | 404 `POLE_PHOTO_NOT_FOUND` |
| Phiếu không in_progress | 409 `WORK_ORDER_NOT_IN_PROGRESS` |
| Ảnh đã confirm/reject | 409 `POLE_PHOTO_ALREADY_DECIDED` |

Giữ kiểm xã của ảnh riêng với xã cha: ảnh có thể ở xã khác trên tuyến liên xã. Các helper đang làm đúng việc này tại [WorkOrderPolePhotoService.cs:33](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:33) và [WorkOrderPolePhotoService.cs:173](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:173).

**Review lại — đề xuất D-2:**

- Cho sửa nhiều lần khi chưa decision và phiếu còn in_progress.
- Mỗi thay đổi thực sự lưu snapshot review mới nhất; audit giữ các phiên bản trước.
- Gửi lại cùng label, note và reviewer → 200 dữ liệu cũ; không tiến `reviewed_at`, không thêm audit.
- Đổi về đúng nhãn AI vẫn lưu `final_cv_state` và reviewer/time. Note có thể null; không tự xóa note mà request đã gửi.
- Không nhận `final_cv_state=null` để “reset review”; đó chưa phải yêu cầu của ticket.
- Hai review khác nhau chạy đồng thời: lượt được khóa sau ghi sau.

**Đua với `/decision`:**

- Decision thắng khóa trước: review chờ rồi trả `ALREADY_DECIDED`.
- Review thắng trước: decision đọc lại và publish nhãn review mới.
- Reject vẫn hợp lệ bất kể nhãn AI/review.
- Không thêm điều kiện in_progress vào `/decision`; R-2 chỉ đặt điều kiện này cho review.

**Rủi ro còn lại:** khóa bảo đảm đọc dữ liệu nhất quán, nhưng Manager có thể confirm một review vừa đổi mà màn hình chưa hiển thị. Đây là D-5; không được mô tả khóa như đã giải quyết việc người duyệt nhìn dữ liệu cũ.

**4. Schema, migration, PLAN và audit**

Migration đề xuất tên `AddPolePhotoReviews`, sau khi Mỹ chốt các D-item schema.

Thêm vào `work_order_pole_photo`:

```sql
final_cv_state text NULL,
review_note    text NULL,
reviewed_by    text NULL,
reviewed_at    timestamptz NULL
```

Không default, không backfill.

FK và index:

```sql
CONSTRAINT fk_pole_photo_reviewed_by
FOREIGN KEY (reviewed_by)
REFERENCES app_user(user_id)
ON DELETE RESTRICT;

CREATE INDEX ix_pole_photo_reviewed_by
ON work_order_pole_photo(reviewed_by);
```

Các CHECK đề xuất, với `IS NOT NULL` tường minh để tránh SQL CHECK cho lọt kết quả `UNKNOWN`:

```sql
CONSTRAINT ck_pole_photo_review_complete CHECK (
    (final_cv_state IS NULL
     AND review_note IS NULL
     AND reviewed_by IS NULL
     AND reviewed_at IS NULL)
    OR
    (final_cv_state IS NOT NULL
     AND final_cv_state IN ('on', 'off')
     AND reviewed_by IS NOT NULL
     AND reviewed_at IS NOT NULL)
);

CONSTRAINT ck_pole_photo_review_note CHECK (
    review_note IS NULL
    OR (
        char_length(review_note) BETWEEN 1 AND 2000
        AND btrim(review_note, E' \t\n\r\f\v') <> ''
    )
);

CONSTRAINT ck_pole_photo_review_note_required CHECK (
    final_cv_state IS NULL
    OR final_cv_state IS NOT DISTINCT FROM cv_state
    OR review_note IS NOT NULL
);
```

`ReviewNote.Trim()` ở service xử lý whitespace .NET; CHECK chặn chuỗi trắng thông dụng ở tầng DB. Giới hạn HTTP vẫn theo `MaxLength(2000)`.

Thay `ck_pole_photo_decision`, giữ nguyên các nhánh cũ nhưng kiểm nhãn hiệu lực:

```sql
CHECK (
    (
        decision IS NULL
        AND decided_by IS NULL
        AND decided_at IS NULL
        AND decision_note IS NULL
        AND NOT status_applied
        AND status_reason IS NULL
    )
    OR
    (
        decision IS NOT NULL
        AND decision IN ('confirm', 'reject')
        AND decided_by IS NOT NULL
        AND decided_at IS NOT NULL
        AND status_reason IS NOT NULL
        AND (
            decision <> 'confirm'
            OR (
                COALESCE(final_cv_state, cv_state) IS NOT NULL
                AND COALESCE(final_cv_state, cv_state) IN ('on', 'off')
            )
        )
        AND (NOT status_applied OR decision = 'confirm')
    )
);
```

CHECK cũ bắt buộc chính `cv_state` là on/off tại [WorkOrderPolePhotoConfiguration.cs:23](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/Configurations/WorkOrderPolePhotoConfiguration.cs:23).

Theo D-3, thay `ck_pole_current_status_confidence_matches_status` bằng:

```sql
CHECK (
    fixture_status <> 'unknown'
    OR status_confidence IS NULL
);
```

Giữ nguyên `ck_pole_current_status_confidence_range`. Không nới CHECK history/observation: nhánh khảo sát vẫn cần score đúng như hiện tại.

Theo D-4, thay `ck_fault_pole_photo_source` bằng:

```sql
CHECK (
    origin_pole_photo_id IS NULL
    OR (
        source_channel IN ('cv', 'field_report')
        AND fault_type = 'lamp_out'
        AND pole_id IS NOT NULL
        AND origin_observation_id IS NULL
        AND origin_node_id IS NULL
        AND (
            source_channel <> 'field_report'
            OR (
                reported_by IS NOT NULL
                AND status_confidence IS NULL
                AND detection_model_version IS NULL
            )
        )
    )
);
```

CHECK không chứng minh `reported_by` chính là reviewer của ảnh; service kiểm và test phải ghim điều đó. Không giả định CHECK một hàng đọc được hàng ảnh.

**Up:** AddColumn ×4 → index/FK/check review → thay decision CHECK → thay confidence CHECK → thay photo-source CHECK. Snapshot/Designer phải phản ánh các thay đổi này.

Không được làm mất hai constraint DB-only `ak_pole_photo_target`, `fk_pole_photo_same_commune`, các index đang có hoặc tạo cột `xmin`.

**Down:** ưu tiên rollback ứng dụng và giữ schema trên môi trường có dữ liệu. Đề xuất preflight từ chối rollback schema nếu có review đã lưu, current-status biết rõ nhưng confidence null, hoặc photo-origin fault không mang source CV. Sau preflight mới phục hồi CHECK cũ, bỏ FK/index và bốn cột. Không tự bịa confidence, xóa review hoặc đổi nguồn để ép Down thành công. Apply/down/reapply chỉ kiểm trên container riêng phù hợp; khuôn test hiện ở [PolePhotoMigrationTests.cs:15](/Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/PolePhotoMigrationTests.cs:15).

**Copy script:** không thêm bảng vào `PLAN`. Thêm vào `POLE_PHOTO_KEPT`:

```python
f" AND (reviewed_by IS NULL OR reviewed_by IN {COPIED_USERS})"
```

`FAULT_KEPT` đã phụ thuộc tập ảnh giữ lại, nên tự loại fault có ảnh bị loại. Vị trí hiện tại: [copy_dev_to_supabase.py:64](/Users/nhm809/Documents/LuxMap/luxmap_backend/scripts/copy_dev_to_supabase.py:64). Không bỏ reviewer hoặc ghi null để né FK.

**Audit:** dùng `AuditAction.DetailsChanged` đã có, `ActorKind.User`, `EntityType.WorkOrder`, `EntityId=id`; before/after có `photo_id` và bốn trường review. Không cần thêm enum/action và CHECK audit mới. Action có sẵn tại [AuditEvent.cs:39](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Persistence/Audit/AuditEvent.cs:39).

Decision/publication/fault audit phải thêm:

```text
effective_cv_state
label_source
label_changed
reviewed_by / reviewed_at / review_note
ai_hint { cv_state, confidence, model_version, artifact_version }
```

Giữ các save riêng trong cùng transaction:

```text
current-status + audit Applied
fault mới + audit Created          (nếu có)
photo decision + audit Confirmed/Rejected
```

Không gán decision lên entity trước khi publisher chạy, tránh nó bị lưu ké ở save publication. Guard chỉ đếm đúng một event, không chứng minh event mô tả đúng đối tượng; xem [AuditWriteGuard.cs:16](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Persistence/Audit/AuditWriteGuard.cs:16).

**5. Test, sabotage, drift và OpenAPI**

| Test đề xuất | Assert chính; sabotage phải làm đỏ |
|---|---|
| `Unreviewed_photo_has_four_null_review_fields_on_every_response` | Upload, replay, list, detail, decision đều có bốn khóa; tự fallback `final_cv_state=cv_state` phải đỏ |
| `Review_is_assignee_only_and_decision_remains_manager_only` | Literal bốn role, kỹ sư cùng xã nhưng không được giao, ảnh xã ngoài scope; bỏ policy/own/parent gate phải đỏ |
| `Review_requires_in_progress_and_an_undecided_photo` | Phủ mọi trạng thái phiếu, confirm/reject trước review; bỏ một guard phải đỏ |
| `A_changed_or_missing_AI_label_requires_a_nonblank_note` | AI on/off/inconclusive/null × final on/off; note null, whitespace, quá dài; bỏ null-AI hoặc note guard phải đỏ |
| `Review_changes_no_AI_snapshot_status_fault_or_objects` | Bộ AI byte-for-byte tương đương về giá trị, không inference/storage/current/fault; sửa AI fields hoặc publish trong review phải đỏ |
| `Repeated_identical_review_is_a_noop_and_changed_review_is_audited` | No-op giữ timestamp/count; thay label/note lưu một audit before/after; luôn stamp hoặc thiếu audit phải đỏ |
| `Returning_to_the_AI_label_preserves_the_fact_of_review` | Final bằng AI vẫn có reviewer/time; tự reset bốn trường về null phải đỏ |
| `Manager_confirmation_uses_the_reviewed_label` | AI on→review off tạo out; AI off→review on không tạo lamp_out; dùng `row.CvState` phải đỏ |
| `Reviewed_inconclusive_or_unavailable_photo_can_be_confirmed` | Kỹ sư gán on/off có note rồi Manager confirm; giữ guard cũ hoặc dereference `.Confidence.Value` phải đỏ |
| `Human_labels_never_borrow_AI_confidence_or_model` | AI `.91`/artifact A nhưng nhãn đảo: current/fault score null, fault model null, ảnh vẫn `.91`/A; copy AI score/version hoặc gán 1.0 phải đỏ |
| `Human_OFF_fault_has_field_report_origin_and_the_correct_two_actors` | ReportedBy=kỹ sư, ConfirmedBy=Manager, origin ảnh, confirmed; dùng CV/nhầm actor phải đỏ |
| `Reviewed_ON_preserves_dim_and_old_confidence_but_advances_the_clock` | Giữ dim/score cũ; OFF cũ sau đó không ghi đè; bỏ clock hoặc giữ score AI mới phải đỏ |
| `Review_and_decision_serialize_and_freeze_the_winning_review` | Ép cả hai thứ tự thắng; decision không publish snapshot trước khóa; review sau decision phải 409 |
| `Review_waits_for_parent_and_pole_locks` | Hai test giữ `FOR NO KEY UPDATE` từ connection khác, xác nhận chưa tới save; bỏ khóa tương ứng phải đỏ |
| `Review_lock_commands_precede_the_tracked_photo_read_in_order` | Interceptor ghi thứ tự work_order→pole→photo→fresh read, cùng transaction; bỏ/đảo khóa hoặc đọc tracking sớm phải đỏ |
| `Photo_review_checks_and_reviewer_FK_are_enforced_by_Postgres` | SQL trực tiếp có WHERE: nửa bộ review, final sai/null, note thiếu/trắng/dài, reviewer không tồn tại; bỏ CHECK/FK tương ứng phải đỏ |
| `Review_and_decision_failures_roll_back_their_whole_transaction` | Inject lỗi save review và save decision sau publication/fault; không có metadata/current/fault/audit ghi dở |
| `Photo_review_schema_roundtrips_and_preserves_DB_only_constraints` | Container riêng; metadata schema/index/FK/CHECK và hai DB-only constraints vẫn còn |
| `Copied_photos_keep_their_reviewers_or_are_excluded_with_their_faults` | Reviewer không được copy → cả ảnh và fault phụ thuộc bị loại; bỏ predicate reviewer phải đỏ |

**Không dùng riêng việc request chờ khóa ảnh làm bằng chứng có explicit photo lock.** `UPDATE` ảnh tự lấy row lock nên test có thể xanh dù bỏ `SELECT … FOR UPDATE`. Cần test thứ tự command/fresh-read nói trên. Test giữ pole `NO KEY UPDATE` đã có tiền lệ tại [WorkOrderPolePhotoTests.cs:542](/Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderPolePhotoTests.cs:542).

Giữ các regression hiện có: ảnh cũ/bằng không apply, ON giữ dim, một open lamp_out khi đua, ảnh không thay after evidence. Test “inconclusive chỉ được reject” hiện tại phải đổi nghĩa thành **“inconclusive chưa được kỹ sư review chỉ được reject”**; xem [WorkOrderPolePhotoTests.cs:258](/Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderPolePhotoTests.cs:258).

Drift đề xuất:

- **PP-11:** bốn trường review nullable và endpoint/policy/quyền người được giao.
- **PP-12:** final override, note bắt buộc, AI unavailable/inconclusive.
- **PP-13:** review lại, no-op, khóa và đóng băng sau decision.
- **PP-14:** confidence/provenance của nhãn người sửa; thay invariant current-status.
- **PP-15:** source channel và attribution fault từ nhãn người sửa.
- **PP-16:** phát hiện Manager đang duyệt snapshot cũ, nếu D-5 được nhận.

OpenAPI:

- Thêm PATCH `/api/v1/work-orders/{id}/pole-photos/{photo_id}/review`.
- Response 200 `PolePhotoItem`; khai 400/401/403/404/409 theo convention.
- Schema `PolePhotoReviewRequest`: required `final_cv_state`, enum literal on/off; note nullable, maxLength 2000.
- `PolePhotoItem.final_cv_state`: enum on/off, nullable; thêm ba trường còn lại.
- Giữ nguyên route, policy và bốn trường decision.
- Thêm wire enum vào `WorkOrderSchemaFilter`; hiện filter đang đặc tả enum ảnh tại [WorkOrderSchemaFilter.cs:14](/Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Api/OpenApi/WorkOrderSchemaFilter.cs:14).
- Export `luxmap-v1.json` → chạy `gen_consolidated_spec.py` → lint; sửa mô tả generator nếu có nội dung cũ đè lên.
- Mở rộng spec test hiện tại ở [OpenApiSpecTests.cs:230](/Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/OpenApiSpecTests.cs:230): assert literal quyền review và decision, nullability, required fields và sự tồn tại trường cũ.

**6. D-item còn mở**

- **D-1 — Chưa review biểu diễn thế nào:** khuyến nghị lưu/trả `final_cv_state=null`, cả bộ review null; chỉ tính fallback lúc publication.
- **D-2 — Review unavailable và vòng đời sửa:** khuyến nghị cho gán on/off khi AI null/inconclusive với note bắt buộc; cho sửa nhiều lần trước decision; PATCH giống hệt là no-op; không có thao tác reset-null trong ticket này.
- **D-3 — Confidence của nhãn người sửa:** khuyến nghị null, giữ nguyên bộ AI gốc; chấp thuận nới invariant current-status để known status có thể không có score. Không dùng score AI trái nhãn hoặc số 1.0 giả.
- **D-4 — Source channel của fault người sửa:** khuyến nghị `field_report` khi nhãn khác AI/AI không kết luận; `cv` khi dùng đúng nhãn AI. Nhánh người sửa có `ReportedBy=reviewed_by`, score/model null; cần sửa CHECK photo-origin và ghi drift.
- **D-5 — Manager confirm dữ liệu vừa đổi:** khuyến nghị bổ sung precondition tùy chọn bằng `If-Match` trên hash snapshot review, trả 409 `POLE_PHOTO_REVIEW_CHANGED` khi lệch; FE mới gửi precondition. Nếu chưa làm, phải chấp nhận và ghi rõ `/decision` công bố review mới nhất sau khóa, có thể khác nội dung Manager vừa xem.