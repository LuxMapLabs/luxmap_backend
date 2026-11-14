# CABINET-MULTI-NODE-DEV — phạm vi và quyết định tích hợp

Mỹ yêu cầu trong chat 15/11/2026: đưa phase 1 lên dev, giải conflict; phase 1 xong mới khảo sát phase 2 bỏ relay / hợp nhất
Telemetry vào IoT/Bridge. Khi hỏi quy tắc tạm cho hai kênh, Mỹ chọn **chặn thao tác mâu thuẫn, trả lỗi rõ ràng**.
Đây là phê duyệt guard tương thích, không phê duyệt hiện thực phase 2 hoặc push/deploy.

## Nền thực tế

```text
$ gh pr view 256 --json state,mergeCommit,mergedAt
state: MERGED
mergeCommit: df356656c6643b29e72b8d9d6b2c625a2713c476
mergedAt: 2026-11-14T16:55:13Z
$ git log -1 --oneline origin/dev
df35665 Merge pull request #256 from LuxMapLabs/iot
```

Worktree riêng: `/Users/nhm809/Documents/LuxMap/wt-cabinet-multi-dev`, branch `feat/CABINET-multiple-iot-nodes-dev`.
Chỉ port thay đổi của feature `6c7c979`; không merge toàn nhánh tài liệu SRS cũ. Các báo cáo review lịch sử được mang sang
như bằng chứng, không được coi là xác nhận cho bản tích hợp mới.

## Conflict và cách giải

- Asset CRUD: giữ gallery / photo count / cover, thay scalar assignment bằng mảng node.
- Map: giữ `feeders[]` / `power_state` / ảnh, thêm mảng node có thứ tự và scalar được suy từ mảng.
- Test shape: hợp nhất đủ keys ảnh, feeder state và mảng node. Giữ luật nullable segment / geometry của dev.
- Drift: giữ các quyết định mới của dev và CAB-MN-1…4; bổ sung CAB-MN-5 do Mỹ chốt trong chat.
- OpenAPI: giữ nguồn generator mới của dev (bao gồm commune boundary, topology guard), rồi xuất/sinh lại.
- ERD: giữ quan hệ pole-segment nullable, đổi cabinet-node 0..n. Hai file DBML 51 bảng không có trên dev nên không hồi sinh.
- Migration: bỏ bản metadata chưa publish của nhánh cũ trong worktree tích hợp; sinh lại theo model dev với ID
  `20261114171551_AllowMultipleIotNodesPerCabinet`. Up vẫn chỉ đổi index cabinet unique→thường; Down có khóa và từ chối
  khi tủ chứa nhiều node. Snapshot chỉ khác index cabinet; giữ nguyên CHECK không liên quan, kể cả newline metadata.

`cherry-pick --no-commit` gặp conflict; sau khi giải, sequencer nhiều commit từ chối tiếp tục với index chưa commit.
Đã dùng `--quit` để giữ toàn bộ thay đổi đã giải, hoàn thiện và kiểm chứng trước một commit tích hợp; không abort/rollback
hoặc đổi bất kỳ file nào ở workspace gốc. Hai commit review cũ được lưu dưới dạng artifact thay vì replay HTML card SRS.

## D-item đã chốt

| Mã | Quyết định |
|---|---|
| D-1 | Giữ ảnh / feeder power / API dev mới cùng mảng node phase 1; không đổi thành công của endpoint hiện hữu ngoài phần thêm mảng đã duyệt |
| D-2 | Relay và Bridge cùng feeder phải cùng node. Hai writer khóa node→feeder và kiểm lại trong service / transaction. Tĩnh + DB trùng nguồn hoặc lịch sử mâu thuẫn bị 409 rõ ràng khi đọc/gửi/giao lệnh; không chọn nguồn đầu, không xóa dữ liệu |
| D-3 | Port ở Telemetry, IOT hiện thực. Caller truyền DbContext đã scope vì MQTT dùng fixed commune context không có HTTP principal; singleton không giữ DbContext. Không thêm project reference ngược |
| D-4 | Giữ UNIQUE expression source-feeder của PR #256 và test bảo toàn qua apply/down/reapply migration cabinet |
| D-5 | Phase 2 chỉ khảo sát sau khi phase 1 đã kiểm chứng/commit; chưa bỏ module, bảng, endpoint, credential, firmware hoặc lịch sử |

Kết quả kiểm chứng cuối và output thực: `.ai/results/CABINET-MULTI-NODE-DEV-p2.md`.
