# BE-17 Phase 2 — review Codex (13/10/2026)

Diff: `git diff 393befa..HEAD` (HEAD `6a3bf36`). Đã đọc task, Phase 1 / quyết định Mỹ D-1…D-7, SC-1…SC-6 và các mục BE-10/15/17/20 của AGENTS.md. Chỉ ghi file này.

## Standards

- **P2 — Schema không cho phép null ở hai trường mới.** `docs/openapi/luxmap-v1.5.json:17343` (`latest_run`) và `:13467` (`CoverageGap.geometry`) chỉ có `$ref`, không khai nullable; bản `luxmap-v1.json` cũng vậy. SC-1/code trả `latest_run: null` trước lần xử lý đầu; geometry có thể null khi tuyến snapshot không còn bản ghi hiện tại. Client sinh từ spec có thể từ chối response hợp lệ. **Đề xuất:** sửa nguồn sinh Swagger để tham chiếu cho phép null, tái sinh cả hai spec; assert schema và JSON của ca chưa có run / thiếu tuyến.

- **P3 — Coverage tải snapshot lớn hơn dữ liệu cần dùng.** `src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:90` materialize cả run rồi parse snapshot; `CoversWholeRun` cũng parse `ExpectedPoles` hai lần. Snapshot thật chứa `projected_gps`, lặp `route_geometry_json` theo từng mẫu GPS và cột (`SurveyProcessor.cs:76`, `SurveyChainageQuery.cs:54`). Endpoint chỉ cần segments/communes/pole IDs, nhưng chi phí đọc/parse tăng theo số mẫu × số đỉnh tuyến. **Đề xuất:** projection phần snapshot cần dùng ngay trong SQL và dùng chung cho guard; đo với snapshot có kích thước thực tế. Chưa đo latency, không kết luận vi phạm NFR.

## Spec

- **P3 — Bỏ nhầm khoảng trống đúng 1 m.** `src/LuxMap.Modules.Survey/Review/SweepCoverage.cs:54`, `:58` dùng `> minimum`, trong khi SC-4 chỉ bỏ khoảng **< 1 m**. Với `Gaps([(0.25, 1.0)], 4)`, khoảng đầu đúng 1 m bị bỏ, `covered_ratio = 1` thay vì `0.75`. **Đề xuất:** giữ khoảng có độ rộng dương và đạt ngưỡng 1 m; thêm test biên đầu/giữa/cuối, giữ nguyên việc loại khoảng rỗng khi length null.

- **P2 — Test hình học không canh được phép chiếu.** `tests/LuxMap.Api.Tests/SweepCoverageTests.cs:141` dùng tuyến ngang khoảng 107 m, chỉ assert tọa độ trong khoảng khá rộng. Sabotage SQL thành `ST_LineSubstring(r.geom, f, t)` trên 4326 cho chính hai đầu `[108.0004,16]`, `[108.0006,16]`, vẫn thỏa assert hiện tại. **Đề xuất:** tuyến gấp khúc Bắc–Nam + Đông–Tây; assert hai đầu gap theo oracle cắt trên 3405 rồi chuyển về 4326, kiểm JSON `[lng,lat]`. Đây là phân tích tĩnh, chưa chạy sabotage.

- **P2 — Chốt chọn run của coverage chưa được test.** `tests/LuxMap.Api.Tests/SweepCoverageTests.cs:125`, `:171`: mỗi sweep được gọi coverage chỉ có một run; mọi lời gọi đều truyền `runId = null`. Đổi riêng `OrderByDescending` thành `OrderBy` ở `SurveyReviewService.cs:91`, hoặc bỏ lọc `run_id`, không bị các test này bắt. Test chọn run ở `:184` kiểm đường `Detail`, không kiểm `Coverage`. **Đề xuất:** hai run succeeded có dữ liệu khác nhau, thêm failed mới hơn; assert mặc định lấy succeeded mới nhất, chỉ định lấy run cũ, run của sweep khác / failed trả `RUN_NOT_FOUND`.

## Đối chiếu còn lại

✅ Quyền: `ReadSurveys` ở cửa; `Find` qua filter phiếu/người được giao/tuyến; coverage kiểm xã quan sát, xã snapshot, xã hiện tại của cột và mọi xã phiếu. SQL hình học giới hạn tuyến snapshot + xã đã kiểm. Chưa thấy rò cụ thể từ `latest_run`: chỉ projection summary, không trả snapshot/GPS/cột.

✅ Hình học/khoảng: cắt đúng 3405 → 4326 trong SQL; hợp chồng/chạm/reverse và kẹp phân số đúng. Gap rỗng không tới `ST_LineSubstring`; DB chỉ nhận `LineString`, không `MultiLineString`, và CHECK chặn pass ngoài [0,1]. Tuyến/cột lấy từ snapshot; length/geometry hiện tại đúng quy ước. Nhánh thiếu tuyến cho mét/geometry null, chưa có test riêng; length DB là int NOT NULL.

✅ Hiệu năng/spec: coverage 7–8 truy vấn, không N+1; detail 5. Hai spec chỉ thêm 1 operation + 6 schema; schema cũ không đổi. Generator chạy trong bộ nhớ, chặn ghi file, cho JSON khớp bản commit. `Geometry.coordinates` là object C# nhưng runtime dựng `double[][]`; schema `{}` là hạn chế kiểu sẵn có, chưa có bằng chứng JSON sai.

⚠️ Không chạy Docker/dotnet/build/test/sabotage. Kết quả xanh 963/46/45/383, 2 skip MQTT và build `-warnaserror` là thông tin Claude do Mỹ cung cấp. Chưa kiểm dữ liệu thực địa. 📌 Nền API SELF-SIGNED tới FW; D-7 còn “chưa báo” WP5/WP6 trong drift.

Tổng: Standards 2 phát hiện (cao nhất P2: nullable spec); Spec 3 phát hiện (cao nhất P2: chốt test phép chiếu / chọn run). Không có P1.

## Đã xử lý (Claude, 13/10/2026)

| Phát hiện | Xử lý | Canh bằng (phá thử → đỏ) |
|---|---|---|
| P2 spec thiếu `nullable` cho `latest_run` / `geometry` | **Không vá riêng**: hạn chế chung của tầng sinh Swagger — `PoleMapDetail.fixture` đã như vậy từ trước. Ghi "Vấn đề đang mở" (tracking) + AGENTS.md; sửa một lần cho mọi schema, báo WP5/WP6 trước | — |
| P3 đọc / parse cả GIS snapshot | Ghi giới hạn; chưa đo được (chưa có phiên thật trên `luxmap_dev`) | — |
| P3 khoảng đúng 1 m bị bỏ | `Keeps`: giữ khoảng ≥ 1 m (dung sai 1e-9) | `A_gap_of_exactly_one_metre_is_kept_only_shorter_ones_are_dropped` — đổi lại `>` → đỏ |
| P2 test hình học không phân biệt phép chiếu | Tuyến chữ L: cắt trên 4326 rơi đúng góc, cắt trên 3405 nằm dưới góc ~1,7 m | `A_gap_is_cut_on_the_projection_the_pass_was_measured_on` — cắt trên 4326 → đỏ |
| P2 chưa test chọn run của coverage | Hai run thành công + một run hỏng mới hơn + run của sweep khác | `Coverage_reads_the_latest_succeeded_run_or_the_one_asked_for` — chọn run cũ nhất → đỏ |
