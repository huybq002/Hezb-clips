# Hezb Clips Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** App Windows chụp và quay màn hình, xuất 1080p/1440p/2160p theo lựa chọn người dùng, tự chọn chế độ upscale video theo cấu hình máy.

**Architecture:** `HezbClips.Core` chứa logic thuần (cài đặt, tính kích thước, chọn chế độ, dựng lệnh FFmpeg, hàng đợi upscale), test bằng xunit với fake. `HezbClips.Capture` bọc Windows Graphics Capture và NAudio. `HezbClips.App` (WPF) chỉ chứa giao diện: tray, phím tắt, overlay, popup. FFmpeg và Real-ESRGAN (ncnn Vulkan) chạy như tiến trình ngoài.

**Tech Stack:** C# .NET 8 (`net8.0-windows10.0.19041.0`), WPF, xunit, SixLabors.ImageSharp (resize Lanczos, PNG/WebP/JPEG), NAudio (WASAPI), FFmpeg, realesrgan-ncnn-vulkan.

**Spec:** `docs/superpowers/specs/2026-11-16-hezb-clips-design.md`

## Global Constraints

- Windows 10/11 only; C# trên .NET 8, giao diện WPF.
- Chất lượng xuất chỉ có 3 mức: 1080p, 1440p (2K), 2160p (4K). Thấp nhất 1080p, cao nhất 4K.
- Chụp bằng Windows Graphics Capture; encode bằng FFmpeg với encoder GPU (NVENC, QuickSync, AMF), fallback `libx264`.
- Upscale AI: Real-ESRGAN qua Vulkan (ncnn). Fallback Lanczos khi không có GPU phù hợp.
- Ảnh: upscale ngay khi chụp, PNG mặc định, tùy chọn WebP/JPEG. Video mặc định MP4/H.264.
- Video có 2 chế độ: live (máy mạnh) và nền (máy yếu). Rớt khung kéo dài khi quay live thì tự hạ về chế độ nền. Người dùng có thể ép một chế độ.
- Âm thanh hệ thống và mic bật tắt độc lập, đều tùy chọn.
- Thanh điều khiển quay không được lọt vào video.
- Không làm: macOS/Linux, upload dịch vụ ngoài, tài khoản, trình chỉnh sửa video đầy đủ (chỉ cắt đầu cuối).
- UI phải nói rõ upscale không thêm chi tiết gốc.

## Review Focus

1. Kích thước nguồn lẻ (ví dụ vùng chọn 1001x501): kích thước encode phải chẵn, nếu không H.264 lỗi.
2. Nguồn lớn hơn mức xuất (màn 4K, xuất 1080p): phải thu nhỏ bằng Lanczos, không được gọi AI upscale hay phóng to.
3. Mic thiếu lúc bắt đầu hoặc rút ra giữa lúc quay: video vẫn quay tiếp, có cảnh báo, phần mic đã thu được giữ lại.
4. Phím tắt bị app khác chiếm: không crash, báo cho người dùng biết phím nào lỗi.
5. Màn hình DPI 125%/150%: vùng chọn tính bằng DIP phải đổi đúng sang pixel vật lý trước khi chụp.

---

### Task 1: Scaffold và cài đặt

**Files:**
- Create: `HezbClips.sln`, `.gitignore`, `src/HezbClips.Core/HezbClips.Core.csproj`, `src/HezbClips.Core/Settings/AppSettings.cs`, `src/HezbClips.Core/Settings/SettingsStore.cs`, `tests/HezbClips.Core.Tests/HezbClips.Core.Tests.csproj`, `tests/HezbClips.Core.Tests/SettingsStoreTests.cs`

**Interfaces:**
- Produces:
  - `enum ExportQuality { P1080 = 1080, P1440 = 1440, P2160 = 2160 }`
  - `enum ImageFormat { Png, WebP, Jpeg }`
  - `enum VideoMode { Auto, Live, Background }`
  - `class AppSettings` với: `ExportQuality Quality = P1080`, `ImageFormat ImageFormat = Png`, `VideoMode VideoMode = Auto`, `bool CaptureSystemAudio = true`, `bool CaptureMicrophone = false`, `int Fps = 30`, `string OutputDir` (mặc định `%USERPROFILE%\Videos\HezbClips`), `string HotkeyRegionShot = "Ctrl+Shift+S"`, `string HotkeyFullShot = "Ctrl+Shift+F"`, `string HotkeyRecord = "Ctrl+Shift+R"`, `bool StartWithWindows = false`
  - `class SettingsStore(string path)` với `AppSettings Load()` và `void Save(AppSettings s)`

- [ ] **Step 1:** `git init`, tạo solution và hai project (Core `net8.0-windows10.0.19041.0` classlib, test xunit), thêm `.gitignore` cho `bin/ obj/ .vs/`.
- [ ] **Step 2: Viết test lỗi** trong `SettingsStoreTests.cs`:

```csharp
[Fact] public void Roundtrip_keeps_values() {
    var p = Path.GetTempFileName(); var s = new SettingsStore(p);
    s.Save(new AppSettings { Quality = ExportQuality.P2160, CaptureMicrophone = true });
    var r = s.Load();
    Assert.Equal(ExportQuality.P2160, r.Quality); Assert.True(r.CaptureMicrophone);
}
[Fact] public void Missing_or_corrupt_file_gives_defaults() {
    var p = Path.GetTempFileName(); File.WriteAllText(p, "{not json");
    Assert.Equal(ExportQuality.P1080, new SettingsStore(p).Load().Quality);
    Assert.Equal(ExportQuality.P1080, new SettingsStore(p + ".none").Load().Quality);
}
[Fact] public void Quality_below_1080_is_clamped_to_1080() {
    var p = Path.GetTempFileName(); File.WriteAllText(p, "{\"Quality\":720}");
    Assert.Equal(ExportQuality.P1080, new SettingsStore(p).Load().Quality);
}
```

- [ ] **Step 3: Chạy test, phải lỗi biên dịch**
  Run: `dotnet test tests/HezbClips.Core.Tests`
  Expected: FAIL, lỗi `AppSettings`/`SettingsStore` chưa định nghĩa.
- [ ] **Step 4:** Cài `AppSettings` và `SettingsStore` (System.Text.Json; giá trị `Quality` ngoài ba mức thì trả về `P1080`).
- [ ] **Step 5: Chạy test**
  Run: `dotnet test tests/HezbClips.Core.Tests`
  Expected: PASS (3 tests).
- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: scaffold solution and settings store"
```

---

### Task 2: Tính kích thước xuất

**Files:**
- Create: `src/HezbClips.Core/Imaging/TargetSizeCalculator.cs`
- Test: `tests/HezbClips.Core.Tests/TargetSizeCalculatorTests.cs`

**Interfaces:**
- Consumes: `ExportQuality` (Task 1)
- Produces: `static class TargetSizeCalculator { static (int W, int H) Calculate(int srcW, int srcH, ExportQuality q) }`. Hộp đích là 1920x1080 / 2560x1440 / 3840x2160. Giữ tỉ lệ, vừa khít trong hộp, cả hai chiều làm tròn xuống số chẵn. Dùng số nguyên để tránh sai số float.

- [ ] **Step 1: Viết test lỗi**

```csharp
[Theory]
[InlineData(1280, 720, ExportQuality.P2160, 3840, 2160)]
[InlineData(3840, 2160, ExportQuality.P1080, 1920, 1080)]
[InlineData(1001, 501, ExportQuality.P1080, 1920, 960)]
[InlineData(1080, 1920, ExportQuality.P1080, 606, 1080)]
public void Fits_box_keeps_aspect_and_even(int sw, int sh, ExportQuality q, int w, int h)
    => Assert.Equal((w, h), TargetSizeCalculator.Calculate(sw, sh, q));
```

- [ ] **Step 2: Chạy test, phải FAIL** (`dotnet test --filter TargetSizeCalculatorTests`).
- [ ] **Step 3:** Cài `Calculate` như mô tả ở Interfaces.
- [ ] **Step 4: Chạy test, phải PASS** (4 cases).
- [ ] **Step 5: Commit** `git add -A && git commit -m "feat: target size calculator"`

---

### Task 3: Hồ sơ phần cứng, chọn chế độ, theo dõi rớt khung

**Files:**
- Create: `src/HezbClips.Core/Hardware/HardwareProfile.cs`, `src/HezbClips.Core/Hardware/ModeSelector.cs`, `src/HezbClips.Core/Hardware/FrameDropMonitor.cs`
- Test: `tests/HezbClips.Core.Tests/ModeSelectorTests.cs`

**Interfaces:**
- Consumes: `ExportQuality`, `VideoMode`
- Produces:
  - `record HardwareProfile(int CpuCores, bool HasHardwareEncoder, long GpuVramMb, bool VulkanAvailable, double BenchmarkFps)`
  - `enum RecordMode { Live, Background }`
  - `static class ModeSelector { static RecordMode Choose(HardwareProfile p, ExportQuality q, VideoMode forced) }`. `Live`/`Background` bị ép thì trả đúng giá trị đó. `Auto`: `Live` khi `HasHardwareEncoder && VulkanAvailable && BenchmarkFps >= ngưỡng` với ngưỡng 30 (P1080), 45 (P1440), 60 (P2160); ngược lại `Background`.
  - `class FrameDropMonitor(int window = 60, double dropRatio = 0.1)` với `void Report(bool dropped)` và `bool ShouldDowngrade`. Chỉ true khi đã đủ `window` mẫu và tỉ lệ rớt >= `dropRatio`.

- [ ] **Step 1: Viết test lỗi**

```csharp
static HardwareProfile P(double fps, bool enc = true, bool vk = true) => new(8, enc, 4096, vk, fps);
[Fact] public void Auto_strong_machine_is_live() => Assert.Equal(RecordMode.Live, ModeSelector.Choose(P(70), ExportQuality.P2160, VideoMode.Auto));
[Fact] public void Auto_below_threshold_is_background() => Assert.Equal(RecordMode.Background, ModeSelector.Choose(P(50), ExportQuality.P2160, VideoMode.Auto));
[Fact] public void Auto_without_hw_encoder_is_background() => Assert.Equal(RecordMode.Background, ModeSelector.Choose(P(200, enc: false), ExportQuality.P1080, VideoMode.Auto));
[Fact] public void Forced_mode_wins() => Assert.Equal(RecordMode.Live, ModeSelector.Choose(P(1, false, false), ExportQuality.P2160, VideoMode.Live));
[Fact] public void Monitor_downgrades_after_window_with_enough_drops() {
    var m = new FrameDropMonitor();
    for (int i = 0; i < 60; i++) m.Report(i < 6);
    Assert.True(m.ShouldDowngrade);
}
[Fact] public void Monitor_waits_for_full_window() {
    var m = new FrameDropMonitor();
    for (int i = 0; i < 10; i++) m.Report(true);
    Assert.False(m.ShouldDowngrade);
}
```

- [ ] **Step 2: Chạy test, phải FAIL.**
- [ ] **Step 3:** Cài ba kiểu trên (monitor dùng cửa sổ trượt `window` mẫu gần nhất).
- [ ] **Step 4: Chạy test, phải PASS** (6 tests).
- [ ] **Step 5: Commit** `git add -A && git commit -m "feat: mode selection and frame drop monitor"`

---

### Task 4: Upscale ảnh

**Files:**
- Create: `src/HezbClips.Core/Imaging/IImageUpscaler.cs`, `LanczosUpscaler.cs`, `RealEsrganUpscaler.cs`, `ImageResizer.cs`
- Test: `tests/HezbClips.Core.Tests/ImageResizerTests.cs`

**Interfaces:**
- Consumes: `HardwareProfile` (Task 3)
- Produces:
  - `interface IImageUpscaler { Image<Rgba32> Upscale(Image<Rgba32> src, int targetW, int targetH); }`
  - `LanczosUpscaler : IImageUpscaler` (ImageSharp `Resize` với `KnownResamplers.Lanczos3`)
  - `RealEsrganUpscaler(string exePath) : IImageUpscaler`: ghi ảnh ra PNG tạm, chạy `realesrgan-ncnn-vulkan -i in.png -o out.png -s 4`, rồi Lanczos về đúng `targetW x targetH`.
  - `static class ImageResizer { static IImageUpscaler CreateUpscaler(HardwareProfile p, string? realEsrganExe); static Image<Rgba32> Fit(Image<Rgba32> src, int targetW, int targetH, IImageUpscaler ai) }`. `CreateUpscaler` trả `RealEsrganUpscaler` khi `p.VulkanAvailable` và file exe tồn tại, ngược lại `LanczosUpscaler`. `Fit` luôn dùng Lanczos khi đích nhỏ hơn hoặc bằng nguồn ở cả hai chiều; chỉ gọi `ai` khi đích lớn hơn.

- [ ] **Step 1: Viết test lỗi**

```csharp
class SpyUpscaler : IImageUpscaler { public int Calls;
    public Image<Rgba32> Upscale(Image<Rgba32> s, int w, int h) { Calls++; return new Image<Rgba32>(w, h); } }
[Fact] public void Lanczos_gives_exact_size() {
    using var src = new Image<Rgba32>(100, 50);
    using var r = new LanczosUpscaler().Upscale(src, 200, 100);
    Assert.Equal((200, 100), (r.Width, r.Height));
}
[Fact] public void Factory_falls_back_to_lanczos_when_exe_missing() {
    var p = new HardwareProfile(8, true, 4096, true, 60);
    Assert.IsType<LanczosUpscaler>(ImageResizer.CreateUpscaler(p, @"C:\none\x.exe"));
}
[Fact] public void Factory_falls_back_when_vulkan_unavailable() {
    var p = new HardwareProfile(8, true, 4096, false, 60);
    Assert.IsType<LanczosUpscaler>(ImageResizer.CreateUpscaler(p, null));
}
[Fact] public void Fit_downscale_never_calls_ai() {
    var spy = new SpyUpscaler(); using var src = new Image<Rgba32>(3840, 2160);
    using var r = ImageResizer.Fit(src, 1920, 1080, spy);
    Assert.Equal(0, spy.Calls); Assert.Equal((1920, 1080), (r.Width, r.Height));
}
[Fact] public void Fit_upscale_calls_ai_once() {
    var spy = new SpyUpscaler(); using var src = new Image<Rgba32>(1280, 720);
    ImageResizer.Fit(src, 3840, 2160, spy).Dispose();
    Assert.Equal(1, spy.Calls);
}
```

- [ ] **Step 2: Chạy test, phải FAIL.**
- [ ] **Step 3:** Cài các kiểu trên; thêm gói `SixLabors.ImageSharp`.
- [ ] **Step 4: Chạy test, phải PASS** (5 tests).
- [ ] **Step 5: Commit** `git add -A && git commit -m "feat: image upscaling with lanczos fallback"`

---

### Task 5: Đặt tên file và dịch vụ chụp ảnh

**Files:**
- Create: `src/HezbClips.Core/Output/FileNamer.cs`, `src/HezbClips.Core/Capture/ICaptureSource.cs`, `src/HezbClips.Core/Capture/ScreenshotService.cs`
- Test: `tests/HezbClips.Core.Tests/ScreenshotServiceTests.cs`

**Interfaces:**
- Consumes: `AppSettings`, `TargetSizeCalculator`, `ImageResizer`, `IImageUpscaler`
- Produces:
  - `static class FileNamer { static string Next(string dir, string prefix, string ext, DateTime now, Func<string, bool> exists) }` trả `dir\{prefix}_{yyyyMMdd_HHmmss}.{ext}`; nếu `exists` thì thêm hậu tố `_1`, `_2`... đến khi chưa tồn tại.
  - `interface ICaptureSource { Image<Rgba32> CaptureRegion(Rectangle physicalPixels); Image<Rgba32> CaptureWindow(IntPtr hwnd); Image<Rgba32> CaptureScreen(int monitorIndex); }` (toạ độ luôn là pixel vật lý)
  - `class ScreenshotService(ICaptureSource src, IImageUpscaler ai, AppSettings s)` với `Task<string> TakeRegionAsync(Rectangle physicalPixels)`, `TakeScreenAsync(int monitorIndex)`, `TakeWindowAsync(IntPtr hwnd)`. Mỗi hàm: chụp, `ImageResizer.Fit` tới `TargetSizeCalculator.Calculate(...)`, lưu đúng định dạng `s.ImageFormat` vào `s.OutputDir` (tạo thư mục nếu thiếu), trả đường dẫn file.

- [ ] **Step 1: Viết test lỗi**

```csharp
[Fact] public void Collision_adds_suffix() {
    var now = new DateTime(2026, 11, 16, 9, 30, 0);
    var first = @"D:\o\Clip_20261116_093000.png";
    var r = FileNamer.Next(@"D:\o", "Clip", "png", now, f => f == first);
    Assert.Equal(@"D:\o\Clip_20261116_093000_1.png", r);
}
[Fact] public async Task Region_shot_saved_at_export_size() {
    var dir = Directory.CreateTempSubdirectory().FullName;
    var s = new AppSettings { Quality = ExportQuality.P1440, OutputDir = dir };
    var svc = new ScreenshotService(new FakeSource(1280, 720), new LanczosUpscaler(), s);
    var path = await svc.TakeRegionAsync(new Rectangle(0, 0, 1280, 720));
    using var img = Image.Load(path);
    Assert.Equal((2560, 1440), (img.Width, img.Height));
    Assert.EndsWith(".png", path);
}
[Fact] public async Task Webp_format_is_respected() {
    var dir = Directory.CreateTempSubdirectory().FullName;
    var s = new AppSettings { ImageFormat = ImageFormat.WebP, OutputDir = dir };
    var path = await new ScreenshotService(new FakeSource(640, 360), new LanczosUpscaler(), s)
        .TakeScreenAsync(0);
    Assert.EndsWith(".webp", path);
}
```

(`FakeSource` trả ảnh trống đúng kích thước truyền vào, đặt trong file test.)

- [ ] **Step 2: Chạy test, phải FAIL.**
- [ ] **Step 3:** Cài `FileNamer`, `ICaptureSource`, `ScreenshotService`.
- [ ] **Step 4: Chạy test, phải PASS** (3 tests).
- [ ] **Step 5: Commit** `git add -A && git commit -m "feat: screenshot service and file naming"`

---

### Task 6: Chụp bằng Windows Graphics Capture

**Files:**
- Create: `src/HezbClips.Capture/HezbClips.Capture.csproj` (`net8.0-windows10.0.19041.0`), `src/HezbClips.Capture/WgcCaptureSource.cs`, `src/HezbClips.Capture/WgcFrameSource.cs`

**Interfaces:**
- Consumes: `ICaptureSource` (Task 5)
- Produces:
  - `class WgcCaptureSource : ICaptureSource` (chụp màn hình/cửa sổ bằng `GraphicsCaptureItem`, vùng = cắt từ khung màn hình chứa vùng đó)
  - `interface IVideoFrameSource { void Start(Rectangle physicalPixels, int fps, Action<ReadOnlyMemory<byte>> onBgraFrame); void Stop(); }` (đặt trong Core, `Capture/IVideoFrameSource.cs`)
  - `class WgcFrameSource : IVideoFrameSource` (khung BGRA liên tục đúng `physicalPixels.Width x Height`, chạy theo `fps`)

- [ ] **Step 1:** Cài hai lớp WGC (dùng WinRT `Windows.Graphics.Capture`, Direct3D11 qua `Vortice.Direct3D11` hoặc `SharpDX`; chọn một và dùng nhất quán).
- [ ] **Step 2: Kiểm tra thủ công** bằng console tạm trong `tools/CaptureSmoke/` (project bỏ khỏi solution khi xong): chụp toàn màn hình và vùng 500x300 vào PNG.
  Expected: ảnh đúng nội dung, vùng 500x300 đúng 500x300 pixel trên màn hình DPI 150%.
- [ ] **Step 3: Commit** `git add -A && git commit -m "feat: windows graphics capture sources"`

---

### Task 7: Thu âm thanh hệ thống và mic

**Files:**
- Create: `src/HezbClips.Core/Audio/AudioSources.cs`, `AudioRecorder.cs`, `IAudioInput.cs`; `src/HezbClips.Capture/NAudioInputFactory.cs`
- Test: `tests/HezbClips.Core.Tests/AudioRecorderTests.cs`

**Interfaces:**
- Produces:
  - `[Flags] enum AudioSources { None = 0, System = 1, Microphone = 2 }`
  - `interface IAudioInput { void Start(Stream wavOut); void Stop(); event Action<Exception?> Stopped; }`
  - `interface IAudioInputFactory { IAudioInput? CreateSystem(); IAudioInput? CreateMicrophone(); }` (trả `null` khi không có thiết bị)
  - `record AudioResult(string? SystemWav, string? MicWav, IReadOnlyList<string> Warnings)`
  - `class AudioRecorder(IAudioInputFactory f)` với `void Start(AudioSources which, string systemWavPath, string micWavPath)` và `AudioResult Stop()`
  - `NAudioInputFactory : IAudioInputFactory` (`WasapiLoopbackCapture` cho hệ thống, `WasapiCapture` thiết bị thu mặc định cho mic)

- [ ] **Step 1: Viết test lỗi**

```csharp
[Fact] public void Missing_mic_warns_and_does_not_throw() {
    var rec = new AudioRecorder(new FakeFactory(system: new FakeInput(), mic: null));
    rec.Start(AudioSources.System | AudioSources.Microphone, "s.wav", "m.wav");
    var r = rec.Stop();
    Assert.Null(r.MicWav); Assert.NotNull(r.SystemWav);
    Assert.Contains(r.Warnings, w => w.Contains("mic", StringComparison.OrdinalIgnoreCase));
}
[Fact] public void Mic_unplugged_midway_keeps_file_and_warns() {
    var mic = new FakeInput(); var rec = new AudioRecorder(new FakeFactory(null, mic));
    rec.Start(AudioSources.Microphone, "s.wav", "m.wav");
    mic.RaiseStopped(new InvalidOperationException("device removed"));
    var r = rec.Stop();
    Assert.Equal("m.wav", r.MicWav); Assert.Single(r.Warnings);
}
[Fact] public void Only_requested_sources_are_started() {
    var sys = new FakeInput(); var mic = new FakeInput();
    new AudioRecorder(new FakeFactory(sys, mic)).Start(AudioSources.System, "s.wav", "m.wav");
    Assert.True(sys.Started); Assert.False(mic.Started);
}
```

(Fakes đặt trong file test.)

- [ ] **Step 2: Chạy test, phải FAIL.**
- [ ] **Step 3:** Cài `AudioRecorder` (không bao giờ ném lỗi do thiết bị; lỗi đưa vào `Warnings`) và `NAudioInputFactory`.
- [ ] **Step 4: Chạy test, phải PASS** (3 tests).
- [ ] **Step 5: Commit** `git add -A && git commit -m "feat: system and mic audio recording"`

---

### Task 8: Dựng lệnh FFmpeg và chọn encoder

**Files:**
- Create: `src/HezbClips.Core/Video/VideoEncoder.cs`, `EncoderDetector.cs`, `FfmpegCommandBuilder.cs`
- Test: `tests/HezbClips.Core.Tests/FfmpegCommandBuilderTests.cs`

**Interfaces:**
- Produces:
  - `enum VideoEncoder { H264Nvenc, H264Qsv, H264Amf, Libx264 }`
  - `static class EncoderDetector { static VideoEncoder Pick(string ffmpegEncodersOutput) }` — thứ tự ưu tiên `h264_nvenc`, `h264_qsv`, `h264_amf`, cuối cùng `Libx264`.
  - `static class FfmpegCommandBuilder`:
    - `string VideoPipeArgs(int srcW, int srcH, int fps, (int W, int H)? scaleTo, VideoEncoder enc, string outPath)`: đọc `rawvideo bgra` từ stdin; nếu `scaleTo` có giá trị thì thêm `-vf scale=W:H:flags=lanczos`. Ném `ArgumentException` nếu bất kỳ kích thước nào lẻ.
    - `string MuxArgs(string videoPath, string? systemWav, string? micWav, string outPath)`: `-c:v copy`, audio AAC; khi có cả hai wav thì dùng `-filter_complex amix=inputs=2`.
    - `string ConcatArgs(string listFilePath, string outPath)`: `-f concat -safe 0 -c copy`.

- [ ] **Step 1: Viết test lỗi**

```csharp
[Fact] public void Picks_nvenc_first() => Assert.Equal(VideoEncoder.H264Nvenc,
    EncoderDetector.Pick(" V....D h264_amf  AMD\n V....D h264_nvenc NVIDIA\n"));
[Fact] public void Falls_back_to_libx264() => Assert.Equal(VideoEncoder.Libx264,
    EncoderDetector.Pick(" V....D libx264 x264\n"));
[Fact] public void Odd_size_throws() => Assert.Throws<ArgumentException>(() =>
    FfmpegCommandBuilder.VideoPipeArgs(1001, 501, 30, null, VideoEncoder.Libx264, "o.mp4"));
[Fact] public void Live_args_scale_with_lanczos() {
    var a = FfmpegCommandBuilder.VideoPipeArgs(1920, 1080, 30, (3840, 2160), VideoEncoder.H264Nvenc, "o.mp4");
    Assert.Contains("-f rawvideo", a); Assert.Contains("-pix_fmt bgra", a);
    Assert.Contains("scale=3840:2160:flags=lanczos", a); Assert.Contains("h264_nvenc", a);
}
[Fact] public void Native_args_have_no_scale() =>
    Assert.DoesNotContain("scale=", FfmpegCommandBuilder.VideoPipeArgs(1920, 1080, 30, null, VideoEncoder.Libx264, "o.mp4"));
[Fact] public void Mux_both_audio_uses_amix() {
    var a = FfmpegCommandBuilder.MuxArgs("v.mp4", "s.wav", "m.wav", "o.mp4");
    Assert.Contains("amix=inputs=2", a); Assert.Contains("-c:v copy", a);
}
[Fact] public void Mux_without_audio_just_copies() =>
    Assert.DoesNotContain("amix", FfmpegCommandBuilder.MuxArgs("v.mp4", null, null, "o.mp4"));
```

- [ ] **Step 2: Chạy test, phải FAIL.**
- [ ] **Step 3:** Cài các kiểu trên.
- [ ] **Step 4: Chạy test, phải PASS** (7 tests).
- [ ] **Step 5: Commit** `git add -A && git commit -m "feat: ffmpeg command builder and encoder detection"`

---

### Task 9: Hàng đợi upscale nền

**Files:**
- Create: `src/HezbClips.Core/Video/UpscaleJob.cs`, `BackgroundUpscaleQueue.cs`, `FfmpegUpscaleRunner.cs`
- Test: `tests/HezbClips.Core.Tests/BackgroundUpscaleQueueTests.cs`

**Interfaces:**
- Consumes: `HardwareProfile`, `FfmpegCommandBuilder`
- Produces:
  - `record UpscaleJob(Guid Id, string InputPath, string OutputPath, (int W, int H) Target)`
  - `interface IUpscaleRunner { Task RunAsync(UpscaleJob job, IProgress<double> progress, CancellationToken ct); }`
  - `class BackgroundUpscaleQueue(IUpscaleRunner runner)` với `Guid Enqueue(string input, string output, (int W, int H) target)`, events `Progress(Guid, double)`, `Completed(Guid, string outputPath)`, `Failed(Guid, Exception)`. Chạy lần lượt từng job (một worker).
  - `FfmpegUpscaleRunner(HardwareProfile p, string ffmpeg, string? realEsrganExe) : IUpscaleRunner`: có Real-ESRGAN và Vulkan thì tách khung PNG → Real-ESRGAN theo thư mục → ghép lại giữ audio gốc; ngược lại một lệnh ffmpeg `scale=W:H:flags=lanczos`. Nguồn lớn hơn hoặc bằng đích ở cả hai chiều thì luôn dùng Lanczos.

- [ ] **Step 1: Viết test lỗi** (runner giả ghi lại thứ tự)

```csharp
[Fact] public async Task Jobs_run_one_at_a_time_in_order() {
    var log = new List<string>(); var runner = new FakeRunner(log, delayMs: 50);
    var q = new BackgroundUpscaleQueue(runner); var done = new TaskCompletionSource();
    int n = 0; q.Completed += (_, _) => { if (++n == 2) done.SetResult(); };
    q.Enqueue("a.mp4", "a_up.mp4", (3840, 2160)); q.Enqueue("b.mp4", "b_up.mp4", (3840, 2160));
    await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(new[] { "start a", "end a", "start b", "end b" }, log);
}
[Fact] public async Task Failure_raises_Failed_and_queue_continues() {
    var runner = new FakeRunner(new List<string>()) { FailOn = "a.mp4" };
    var q = new BackgroundUpscaleQueue(runner);
    var failed = new TaskCompletionSource<Guid>(); var ok = new TaskCompletionSource<Guid>();
    q.Failed += (id, _) => failed.SetResult(id); q.Completed += (id, _) => ok.SetResult(id);
    var a = q.Enqueue("a.mp4", "x", (3840, 2160)); var b = q.Enqueue("b.mp4", "y", (3840, 2160));
    Assert.Equal(a, await failed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    Assert.Equal(b, await ok.Task.WaitAsync(TimeSpan.FromSeconds(5)));
}
```

- [ ] **Step 2: Chạy test, phải FAIL.**
- [ ] **Step 3:** Cài hàng đợi (dùng `Channel<UpscaleJob>`) và `FfmpegUpscaleRunner`. File gốc không bao giờ bị xóa hay ghi đè khi job lỗi.
- [ ] **Step 4: Chạy test, phải PASS** (2 tests).
- [ ] **Step 5: Commit** `git add -A && git commit -m "feat: background upscale queue"`

---

### Task 10: Bộ ghi video (live, nền, hạ chế độ theo đoạn)

**Files:**
- Create: `src/HezbClips.Core/Video/RecordRequest.cs`, `IFfmpegProcess.cs`, `VideoRecorder.cs`, `SegmentPlan.cs`
- Test: `tests/HezbClips.Core.Tests/VideoRecorderTests.cs`

**Interfaces:**
- Consumes: `IVideoFrameSource` (Task 6), `AudioRecorder` (Task 7), `FfmpegCommandBuilder`, `BackgroundUpscaleQueue`, `FrameDropMonitor`, `ModeSelector`, `TargetSizeCalculator`
- Produces:
  - `record RecordRequest(Rectangle Region, int Fps, AudioSources Audio, ExportQuality Quality, RecordMode Mode)`
  - `record RecordResult(string FinalPath, Guid? PendingUpscaleJob, IReadOnlyList<string> Warnings)`
  - `interface IFfmpegProcess { Stream StdIn { get; } Task<int> ExitAsync(); void Start(string args); }` và `Func<IFfmpegProcess>` để tiêm vào recorder
  - `class SegmentPlan` ghi lại các đoạn: mỗi đoạn có `Scaled: bool` và đường dẫn; `Scaled=false` là đoạn chạy sau khi hạ chế độ. Phương thức `IReadOnlyList<string> SegmentsNeedingUpscale()`.
  - `class VideoRecorder(IVideoFrameSource frames, AudioRecorder audio, Func<IFfmpegProcess> ffmpegFactory, BackgroundUpscaleQueue queue, VideoEncoder enc, AppSettings s)` với `void Start(RecordRequest r)` và `Task<RecordResult> StopAsync()`.
  - Hành vi: `Live` → ffmpeg có `scaleTo`. `Background` → ffmpeg không scale, khi dừng đưa file vào `queue`, trả `PendingUpscaleJob`. Khi `FrameDropMonitor.ShouldDowngrade` thì đóng đoạn hiện tại, mở đoạn mới không scale; khi dừng, mỗi đoạn `Scaled=false` được upscale rồi nối bằng `ConcatArgs`, sau đó ghép audio bằng `MuxArgs`. Kích thước nguồn lẻ được cắt xuống chẵn trước khi tạo lệnh. Kích thước encode luôn lấy từ `TargetSizeCalculator`.

- [ ] **Step 1: Viết test lỗi** (fake frame source, fake ffmpeg ghi lại `args`)

```csharp
[Fact] public async Task Live_mode_passes_scale_to_ffmpeg() {
    var h = new Harness(); h.Recorder.Start(Req(new Rectangle(0, 0, 1920, 1080), ExportQuality.P2160, RecordMode.Live));
    await h.Recorder.StopAsync();
    Assert.Contains("scale=3840:2160", h.FirstVideoArgs);
}
[Fact] public async Task Background_mode_records_native_and_enqueues_upscale() {
    var h = new Harness(); h.Recorder.Start(Req(new Rectangle(0, 0, 1920, 1080), ExportQuality.P2160, RecordMode.Background));
    var r = await h.Recorder.StopAsync();
    Assert.DoesNotContain("scale=", h.FirstVideoArgs); Assert.NotNull(r.PendingUpscaleJob);
}
[Fact] public async Task Odd_region_is_cropped_to_even_before_encoding() {
    var h = new Harness(); h.Recorder.Start(Req(new Rectangle(0, 0, 1001, 501), ExportQuality.P1080, RecordMode.Background));
    await h.Recorder.StopAsync();
    Assert.Contains("-s 1000x500", h.FirstVideoArgs);
}
[Fact] public async Task Sustained_drops_in_live_mode_start_unscaled_segment() {
    var h = new Harness(); h.Recorder.Start(Req(new Rectangle(0, 0, 1920, 1080), ExportQuality.P1080, RecordMode.Live));
    h.Frames.EmitWithDrops(total: 60, dropped: 10);
    var r = await h.Recorder.StopAsync();
    Assert.Equal(2, h.VideoProcessCount); Assert.DoesNotContain("scale=", h.SecondVideoArgs);
    Assert.NotNull(r.PendingUpscaleJob);
}
[Fact] public async Task Audio_warning_is_returned_in_result() {
    var h = new Harness(micMissing: true); h.Recorder.Start(Req(new Rectangle(0, 0, 1280, 720), ExportQuality.P1080, RecordMode.Live, AudioSources.Microphone));
    Assert.NotEmpty((await h.Recorder.StopAsync()).Warnings);
}
```

(`Harness` và `Req` đặt trong file test.)

- [ ] **Step 2: Chạy test, phải FAIL.**
- [ ] **Step 3:** Cài recorder và `SegmentPlan`.
- [ ] **Step 4: Chạy test, phải PASS** (5 tests).
- [ ] **Step 5: Commit** `git add -A && git commit -m "feat: video recorder with live/background modes"`

---

### Task 11: Phím tắt toàn cục

**Files:**
- Create: `src/HezbClips.Core/Hotkeys/HotkeyParser.cs`, `HotkeyService.cs`, `IHotkeyNative.cs`; `src/HezbClips.App/Hotkeys/Win32Hotkeys.cs`
- Test: `tests/HezbClips.Core.Tests/HotkeyTests.cs`

**Interfaces:**
- Produces:
  - `static class HotkeyParser { static (uint Modifiers, uint VirtualKey) Parse(string text) }` — `"Ctrl+Shift+S"` → `(MOD_CONTROL|MOD_SHIFT = 0x2|0x4 = 6, 0x53)`; ném `FormatException` khi sai. `MOD_ALT=1`, `MOD_WIN=8`.
  - `interface IHotkeyNative { bool Register(int id, uint modifiers, uint vk); void Unregister(int id); }`
  - `record HotkeyFailure(string Name, string Combo)`
  - `class HotkeyService(IHotkeyNative native)` với `IReadOnlyList<HotkeyFailure> RegisterAll(IDictionary<string, string> nameToCombo)`. Không ném lỗi khi đăng ký thất bại; trả danh sách phím lỗi.
  - `Win32Hotkeys : IHotkeyNative` dùng `RegisterHotKey`/`UnregisterHotKey` trên cửa sổ ẩn.

- [ ] **Step 1: Viết test lỗi**

```csharp
[Fact] public void Parses_ctrl_shift_s() => Assert.Equal((6u, 0x53u), HotkeyParser.Parse("Ctrl+Shift+S"));
[Fact] public void Bad_combo_throws() => Assert.Throws<FormatException>(() => HotkeyParser.Parse("Ctrl+"));
[Fact] public void Conflict_is_reported_not_thrown() {
    var svc = new HotkeyService(new FakeNative(failFor: 0x52)); // R bị chiếm
    var fails = svc.RegisterAll(new Dictionary<string, string> { ["Record"] = "Ctrl+Shift+R", ["Shot"] = "Ctrl+Shift+S" });
    Assert.Equal(new[] { new HotkeyFailure("Record", "Ctrl+Shift+R") }, fails);
}
```

- [ ] **Step 2: Chạy test, phải FAIL.**
- [ ] **Step 3:** Cài các kiểu trên.
- [ ] **Step 4: Chạy test, phải PASS** (3 tests).
- [ ] **Step 5: Commit** `git add -A && git commit -m "feat: global hotkeys"`

---

### Task 12: Đổi DIP sang pixel vật lý và cắt đầu cuối

**Files:**
- Create: `src/HezbClips.Core/Imaging/DpiMath.cs`, `src/HezbClips.Core/Video/TrimService.cs`
- Test: `tests/HezbClips.Core.Tests/DpiAndTrimTests.cs`

**Interfaces:**
- Produces:
  - `static class DpiMath { static Rectangle ToPhysical(double x, double y, double w, double h, double scale) }` — nhân với `scale`, làm tròn gần nhất.
  - `static class TrimService { static string BuildArgs(string input, string output, TimeSpan start, TimeSpan end, VideoEncoder enc) }` — `-ss`, `-to`, encode lại bằng `enc` (cắt chính xác, không dùng `-c copy`).

- [ ] **Step 1: Viết test lỗi**

```csharp
[Fact] public void Dip_to_physical_at_150() =>
    Assert.Equal(new Rectangle(150, 75, 300, 150), DpiMath.ToPhysical(100, 50, 200, 100, 1.5));
[Fact] public void Dip_to_physical_at_125_rounds() =>
    Assert.Equal(new Rectangle(125, 63, 250, 125), DpiMath.ToPhysical(100, 50, 200, 100, 1.25));
[Fact] public void Trim_args_have_range_and_reencode() {
    var a = TrimService.BuildArgs("in.mp4", "out.mp4", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), VideoEncoder.Libx264);
    Assert.Contains("-ss 00:00:02", a); Assert.Contains("-to 00:00:10", a);
    Assert.Contains("libx264", a); Assert.DoesNotContain("-c copy", a);
}
```

- [ ] **Step 2: Chạy test, phải FAIL.**
- [ ] **Step 3:** Cài hai lớp (`MidpointRounding.AwayFromZero` cho 62.5 → 63).
- [ ] **Step 4: Chạy test, phải PASS** (3 tests).
- [ ] **Step 5: Commit** `git add -A && git commit -m "feat: dpi math and trim service"`

---

### Task 13: Vỏ WPF — tray, cài đặt, overlay, thanh điều khiển, popup

**Files:**
- Create: `src/HezbClips.App/HezbClips.App.csproj`, `App.xaml(.cs)`, `app.manifest` (PerMonitorV2 DPI), `Tray/TrayHost.cs`, `Windows/SettingsWindow.xaml(.cs)`, `Windows/RegionSelectorWindow.xaml(.cs)`, `Windows/RecordControlBar.xaml(.cs)`, `Windows/PostCapturePopup.xaml(.cs)`

**Interfaces:**
- Consumes: mọi thứ ở Task 1–12.
- Produces: ứng dụng chạy được. Hành vi bắt buộc:
  - Khởi động ẩn, chỉ có icon khay; menu chuột phải: chụp vùng, chụp toàn màn hình, quay, cài đặt, thoát.
  - `RegionSelectorWindow` phủ mọi màn hình; kéo chọn vùng, Esc huỷ; trả `Rectangle` pixel vật lý qua `DpiMath.ToPhysical`.
  - `RecordControlBar`: nút bật tắt mic, bật tắt âm thanh hệ thống, đồng hồ, nút dừng; gọi `SetWindowDisplayAffinity(hwnd, 0x11)` (`WDA_EXCLUDEFROMCAPTURE`) để không lọt vào video.
  - `PostCapturePopup`: copy, lưu/mở thư mục, xóa, cắt đầu cuối (video). Tự tắt sau 8 giây nếu không tương tác.
  - `SettingsWindow`: chọn chất lượng 1080p/2K/4K, định dạng ảnh, chế độ video (Tự động/Live/Nền), nguồn âm thanh, FPS, thư mục lưu, phím tắt, khởi động cùng Windows. Có dòng chú thích "Upscale làm ảnh nét và mượt hơn, không thêm chi tiết gốc".
  - Phím tắt lỗi (từ `HotkeyService.RegisterAll`) hiện bong bóng tray nêu tên phím và mở cài đặt khi bấm.
  - Hiện tiến trình upscale nền trên tray (tooltip) và thông báo khi xong hoặc lỗi.

- [ ] **Step 1:** Dựng project WPF, tray, `SettingsWindow` gắn `SettingsStore`; ứng dụng chạy và lưu cài đặt.
- [ ] **Step 2:** Dựng `RegionSelectorWindow` và nối "chụp vùng" với `ScreenshotService`.
- [ ] **Step 3:** Dựng `RecordControlBar` và nối "quay" với `VideoRecorder`; nối `PostCapturePopup`.
- [ ] **Step 4:** Nối `HotkeyService` và thông báo phím lỗi.
- [ ] **Step 5: Kiểm tra thủ công**
  Run: `dotnet run --project src/HezbClips.App`
  Expected, trên một máy ở DPI 150%:
  - Ctrl+Shift+S, kéo vùng 500x300 DIP, ảnh lưu đúng kích thước theo mức chất lượng đã chọn.
  - Ctrl+Shift+R quay 10 giây có mic; video có tiếng; thanh điều khiển không xuất hiện trong video.
  - Rút mic giữa lúc quay: video vẫn lưu, tray báo cảnh báo mic.
  - Chiếm sẵn Ctrl+Shift+R bằng app khác, khởi động lại: tray báo phím lỗi, app không crash.
- [ ] **Step 6: Commit** `git add -A && git commit -m "feat: wpf shell with tray, overlay, control bar, popup"`

---

### Task 14: Dò cấu hình lần chạy đầu

**Files:**
- Create: `src/HezbClips.Core/Hardware/HardwareProfiler.cs`
- Modify: `src/HezbClips.App/App.xaml.cs` (gọi profiler khi chưa có `hardware.json`)
- Test: `tests/HezbClips.Core.Tests/HardwareProfilerTests.cs`

**Interfaces:**
- Consumes: `HardwareProfile`, `EncoderDetector`
- Produces: `class HardwareProfiler(Func<string> runFfmpegEncoders, Func<bool> vulkanProbe, Func<double> benchmarkFps)` với `HardwareProfile Detect()` — `HasHardwareEncoder = EncoderDetector.Pick(...) != Libx264`; nếu `vulkanProbe` hoặc `benchmarkFps` ném lỗi thì `VulkanAvailable=false`, `BenchmarkFps=0` (không crash). Kết quả lưu `hardware.json` cạnh `settings.json`; nút "Dò lại cấu hình" trong cài đặt xóa file và dò lại.

- [ ] **Step 1: Viết test lỗi**

```csharp
[Fact] public void Hardware_encoder_detected() {
    var p = new HardwareProfiler(() => " V....D h264_nvenc x\n", () => true, () => 80).Detect();
    Assert.True(p.HasHardwareEncoder); Assert.Equal(80, p.BenchmarkFps);
}
[Fact] public void Probe_failures_give_safe_weak_profile() {
    var p = new HardwareProfiler(() => " V....D libx264 x\n", () => throw new Exception(), () => throw new Exception()).Detect();
    Assert.False(p.HasHardwareEncoder); Assert.False(p.VulkanAvailable); Assert.Equal(0, p.BenchmarkFps);
}
```

- [ ] **Step 2: Chạy test, phải FAIL.**
- [ ] **Step 3:** Cài `HardwareProfiler`. Benchmark thật: chạy `ffmpeg` encode 5 giây `testsrc2` 1080p có scale lên mức 4K, đo fps; Vulkan probe: chạy `realesrgan-ncnn-vulkan -h` và xem mã thoát.
- [ ] **Step 4: Chạy test, phải PASS** (2 tests); rồi chạy toàn bộ `dotnet test` và kiểm tra thủ công lần chạy đầu tạo `hardware.json`.
- [ ] **Step 5: Commit** `git add -A && git commit -m "feat: first-run hardware profiling"`
