# Third-party notices

Hezb Clips itself is licensed under the PolyForm Noncommercial License 1.0.0 (see `LICENSE`).
It uses the following components, each under its own licence.

| Component | Use | Licence |
|---|---|---|
| [NAudio](https://github.com/naudio/NAudio) 2.2.1 | audio capture | MIT |
| [SkiaSharp](https://github.com/mono/SkiaSharp) 4.157.0 (+ Skia) | image decoding, encoding, drawing | MIT (Skia: BSD-3-Clause) |
| [Tesseract](https://github.com/charlesw/tesseract) 5.2.0 .NET wrapper (+ Tesseract OCR, Leptonica) | text recognition (OCR) | Apache-2.0 (Leptonica: BSD-2-Clause) |
| [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows) Direct3D11 3.8.3 | screen capture (Direct3D) | MIT |
| [Tesseract tessdata_fast](https://github.com/tesseract-ocr/tessdata_fast) `vie` / `eng` | OCR language data, downloaded on first use / by the installer | Apache-2.0 |
| [FFmpeg](https://ffmpeg.org) 8.1.1 "essentials" build by [gyan.dev](https://www.gyan.dev/ffmpeg/builds/) | video encoding, GIF, upscaling | GPL-3.0 (it includes libx264) |
| [Real-ESRGAN ncnn Vulkan](https://github.com/xinntao/Real-ESRGAN-ncnn-vulkan) | optional AI image upscaling, downloaded on request | MIT |

FFmpeg and the OCR data are **not** part of this repository or of the application files: the installer (or the
application on first use) downloads them from the sources above and checks their SHA-256. FFmpeg is a separate
program that Hezb Clips starts as a process; its licence (GPL-3.0, copy in `tools\ffmpeg-LICENSE.txt` after
installation) applies to it, and its source code is available from https://ffmpeg.org and https://www.gyan.dev/ffmpeg/builds/.
