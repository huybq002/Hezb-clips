# Hezb Clips — Thiết kế

Ngày: 2026-11-16. Trạng thái: chờ duyệt.

## Ý định và lý do

App desktop chụp và quay màn hình kiểu ShareX. Điểm khác biệt: chất lượng file xuất do người dùng chọn, từ 1080p đến 4K, kể cả khi máy và màn hình yếu. Thao tác phải nhanh và ít bước. Quay có âm thanh hệ thống và mic, cả hai tùy chọn.

## Mục tiêu

- Chọn chất lượng xuất: 1080p, 1440p (2K), 2160p (4K). Thấp nhất 1080p, cao nhất 4K.
- Chụp ảnh: vùng, cửa sổ, toàn màn hình. Quay video: vùng, cửa sổ, toàn màn hình.
- Quay kèm âm thanh hệ thống và/hoặc mic, mỗi nguồn bật tắt độc lập.
- Thao tác nhanh: chạy ở khay hệ thống, phím tắt toàn cục, popup sau khi chụp hoặc quay (copy, lưu, mở thư mục, chỉnh sửa).
- Chế độ upscale video tự chọn theo cấu hình máy (xem mục dưới).
- Chạy mượt trên máy yếu: không làm rớt khung hình khi quay.

## Không phải mục tiêu (bản đầu)

- Không chạy macOS hay Linux.
- Không có upload lên dịch vụ ngoài, không có tài khoản.
- Không có trình chỉnh sửa video đầy đủ. Chỉ cắt đầu cuối.

## Điều tránh

- Quay giật hoặc rớt khung vì upscale live trên máy không đủ mạnh.
- Hứa hẹn "chi tiết 4K thật". Upscale chỉ làm ảnh nét và mượt hơn, không thêm thông tin gốc. UI cần nói rõ điều này.

## Ràng buộc

- Windows 10/11.
- C# trên .NET 8, giao diện WPF.
- Chụp bằng Windows Graphics Capture. Encode bằng FFmpeg với encoder GPU (NVENC, QuickSync, AMF), fallback x264 phần mềm.
- Upscale AI: Real-ESRGAN qua Vulkan (ncnn). Fallback: Lanczos khi không có GPU phù hợp.

## Các cách đã cân nhắc

- **A. C# native (WPF)** — chọn. Nhẹ, quay mượt trên máy yếu, gần với ShareX. Đổi lại chỉ chạy Windows.
- **B. Tauri/Electron.** Làm giao diện nhanh, đa nền tảng dễ. Bị loại vì nặng RAM và phần chụp sâu vẫn phải viết native.
- **C. Lõi Rust + Tauri.** Nhẹ, đa nền tảng về sau. Bị loại vì tốn công nhất cho bản đầu.

## Quyết định về cách hoạt động

### Chụp ảnh
Upscale lên mức đã chọn ngay khi chụp, vì nhẹ. Lưu PNG mặc định, tùy chọn WebP/JPEG.

### Quay video: tự chọn chế độ
Lần chạy đầu, app dò GPU/CPU và chạy thử tải ngắn để chọn chế độ mặc định:
- **Chế độ live (máy mạnh):** upscale và encode ngay lúc quay, file có sẵn khi dừng.
- **Chế độ nền (máy yếu):** quay ở độ phân giải gốc, xuất file gốc ngay khi dừng. Bản upscale chạy nền và báo khi xong.
- Đang quay mà rớt khung hình kéo dài thì app tự hạ về chế độ nền.
- Người dùng có thể ép một chế độ trong cài đặt.

### Thao tác nhanh (đề xuất, chưa phải điều bạn đã chốt)
- Tray icon, khởi động cùng Windows tùy chọn.
- Phím tắt mặc định: chụp vùng, chụp toàn màn hình, bắt đầu/dừng quay. Người dùng đổi được.
- Thanh điều khiển quay gọn: bật tắt mic, bật tắt âm thanh hệ thống, đồng hồ, nút dừng. Thanh này không lọt vào video.
- Popup sau chụp/quay: copy, lưu, mở thư mục, xóa.

## Việc để người xây quyết

- Bố cục chi tiết các màn hình.
- Định dạng video mặc định (đề xuất MP4/H.264 vì tương thích, tùy chọn HEVC).
- Chi tiết cách dò cấu hình và ngưỡng chuyển chế độ.
- Cấu trúc project và thư viện.
