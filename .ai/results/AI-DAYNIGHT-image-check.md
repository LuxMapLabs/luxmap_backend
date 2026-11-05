# AI-DAYNIGHT — direct image diagnostic (02/11/2026)

Requested image: `E:/LuxMap_Dataset/images/Screenshot 2026-11-02 143206.jpeg`.
Image size 460x717; SHA256 e79115c015d728b66dfc28a2f89fe638c5bc3774ef3ba7ed2b0ed326ea7b1d21.
Model: src/LuxMap.Modules.AI/Models/daynight.onnx;
SHA256 2666c67fece9909cd1de012d027c5350b4642e07e4c4838220ab4bb688fbf04b.

## Actual terminal evidence

Direct call to the current DayNightOnnxService, then direct ONNX inference on its ToTensor result
(.NET ONNX Runtime 1.20.1; final diagnostic exit 0):
```
Backend decision: night
Names: {0: 'day', 1: 'night'}
Input images [1,3,224,224] System.Single
Output output0 [1,2]
Image size 460x717
Backend probabilities day/night: 0.014712928, 0.9852871
Pillow tensor (.NET ORT): 0.01394237, 0.9860576
```

Independent Python/Pillow reconstruction of classification preprocessing (exit 0):
```
Pillow 12.3.0 ONNX Runtime 1.23.2
Original size (460, 717) resize (224, 349) crop offset (0, 62)
Metadata names {0: 'day', 1: 'night'}
backend tensor (Python ORT) [[0.014712927863001823, 0.985287070274353]]
Pillow bilinear center crop [[0.013942370191216469, 0.9860575795173645]]
full-image stretch (diagnostic only) [[0.00015272930613718927, 0.9998472929000854]]
Tensor difference mean/max 0.0012052477104589343 0.050980404019355774
```

Downloaded the exact `ultralytics==8.4.198` wheel named in model metadata (not installed into the
backend), inspected `ultralytics/data/augment.py` and `models/yolo/classify/predict.py`:
shortest-edge bilinear resize -> CenterCrop -> ToTensor -> Normalize;
DEFAULT_MEAN=(0,0,0), DEFAULT_STD=(1,1,1); BGR input converted to RGB.
This resolves the earlier inability to fetch that exact version through web browsing.
Python reconstruction uses Pillow directly, not the full Ultralytics/Torch runtime. Small pixel
rounding/resampling differences from ImageSharp exist, but both produce the same wrong class.
Cross-running each tensor in .NET/Python ORT confirms runtime does not explain the label.

## Conclusion and limits

The ONNX artifact classifies this visibly daytime photo as night with high confidence under both
backend and independently reconstructed reference preprocessing. No backend label inversion,
double-softmax, layout or normalization error was found explaining this photo. No production code,
threshold, class mapping or model weights changed in this diagnostic. Do not patch the label or
threshold just to force this sample to day. Threshold 0.7 correctly accepts the model's wrong result.

This does not establish the training root cause: need original best.pt, labeled validation/test
examples and confusion matrix to distinguish learned classifier error, dataset/label problems,
or export/checkpoint mismatch. There is no best.pt supplied here for that comparison.

Scratch program, Python portable environment, tensors and crop live outside the repo:
`C:/Users/Nguyen Tien Dat/.codex/visualizations/2026/09/22/01a0c730-76ad-7201-bff0-36858375a52b/daynight-check/`.
Run Check.csproj with dotnet run; compare.py with its python/python.exe. The original image/model
were only read. The first diagnostic save of a preview failed because the JPEG-only configuration
had no PNG encoder; using an explicit PNG encoder fixed the diagnostic preview only. Inference
numbers above are from the successful rerun. Existing pending PNG upload work is not implemented
by this diagnostic.
