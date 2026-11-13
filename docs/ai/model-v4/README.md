# Model ON/OFF — YOLO11n trên Roboflow v4, chia theo video (12/11/2026)

`src/LuxMap.Modules.AI/Models/best.onnx`, SHA-256 `9007bdbd8ddebe19c4db4e2189d3166e2f4c5bb4c4474dda2cb9883cf1969278`
(ghim ở `Ai:ModelSha256`). Thay bản `d778e343…` (PR #232), cũng train trên Roboflow v4 nhưng theo cách chia ngẫu nhiên từng ảnh.

## Vì sao train lại

Phần lớn ảnh v4 là frame cắt từ video (`IMG_0579_00032` = video `IMG_0579`, frame 32). Chia ngẫu nhiên từng ảnh đặt các frame
gần như trùng nhau vào cả train lẫn test, nên điểm test cao giả. Bản này chia **theo nhóm nguồn**.

## Tái lập

[`luxmap_train_v4.ipynb`](luxmap_train_v4.ipynb) chạy trên Colab T4; cần Colab Secret `ROBOFLOW_API_KEY`.

- Dataset: Roboflow `luxmap-street-light-detection` **version 4** (472 ảnh), Ultralytics `8.4.173`, seed 0.
- Chia: frame cùng video chung một tập; ảnh chụp đơn đánh số liên tiếp là một đợt; ảnh trùng byte chung nhóm.
  Valid và test **chỉ lấy video thực địa**; screenshot, ảnh Roboflow Universe, ảnh WhatsApp và ảnh chụp đơn chỉ vào train.
  Kết quả từng file: [`split_manifest.csv`](split_manifest.csv).

| Tập | Nhóm | Ảnh | Hộp `normal` | Hộp `out` |
|---|---|---|---|---|
| train | 68 | 392 | 462 | 200 |
| valid | 6 video | 34 | 39 | 17 |
| test | 13 video | 46 | 47 | 22 |

- Train: `imgsz=640, epochs=200, patience=50, cos_lr=True, flipud=0, fliplr=0.5, close_mosaic=15`.
- Export: `format=onnx, imgsz=640, nms=False, simplify=True, dynamic=False, batch=1` — input `[1,3,640,640]`,
  output `[1,6,8400]`, `names = {0: 'normal', 1: 'out'}`.

## Kết quả trên test (46 ảnh, 13 video chưa thấy khi train)

Ở điểm vận hành của backend: conf 0.25, NMS theo lớp IoU 0.45, khớp hộp IoU ≥ 0.5. Chi tiết: [`report.json`](report.json).

| | YOLO11n (chọn) | YOLO11s |
|---|---|---|
| `out` recall | 0.91 (20/22) | 0.91 (20/22) |
| `out` precision | **0.83** (4 báo nhầm) | 0.56 (16 báo nhầm) |
| `normal` recall | 0.55 (26/47) | 0.62 (29/47) |
| `normal` precision | 0.90 | 0.85 |
| `out` bị gọi thành `normal` | 0 | 0 |
| mAP50 | 0.80 | 0.77 |
| CPU (Colab, trung vị) | 126 ms | 245 ms |

Chọn YOLO11n: cùng recall `out`, ít sự cố giả hơn nhiều, nhanh gấp đôi.

## Giới hạn

- Test nhỏ (22 hộp `out`): một ảnh đổi recall khoảng 4–5 điểm.
- `normal` recall 0.55: khoảng nửa số đèn sáng bị bỏ sót. Ở khảo sát, cột không có phát hiện thành `unknown`, không thành `out`.
- Không so được với bản `d778e343…` trên tập test này: bản đó train theo cách chia ngẫu nhiên nên nhiều khả năng đã thấy các ảnh test.
