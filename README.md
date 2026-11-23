- Ứng dụng Windows quản lý nhiều tài khoản Zalo PC trong một giao diện.
- Nhận diện Zalo gốc, tạo clone với thư mục dữ liệu riêng.
- Mở, dừng, đổi tên, đổi avatar và tạo shortcut cho từng hồ sơ.
- Hiển thị dung lượng data và app của từng hồ sơ.
- Kiểm tra và cập nhật clone từ bản Zalo gốc theo thao tác người dùng.
- Xuất/nhập dữ liệu, kiểm tra ID tài khoản và giữ bản dữ liệu cũ trước khi nhập.
- Hỗ trợ nhập bản sao lưu thành hồ sơ riêng; chưa bảo đảm khôi phục toàn bộ lịch sử hoặc phiên đăng nhập.
- Yêu cầu Windows 64-bit, .NET Framework 4.x có bộ biên dịch C# và Zalo PC đã cài đặt.
- Build bằng PowerShell: `.\build.ps1`.
- Chạy ứng dụng: `.\ZaloManager.exe`.
- Cập nhật Manager bằng cách đóng cửa sổ Manager và thay `ZaloManager.exe` tại cùng vị trí. Chạy lại file đó để tiếp tục dùng các hồ sơ hiện có. Với mã nguồn, chạy `.\build.ps1` để build và thay file.
- Cấu hình hồ sơ nằm trong `%LOCALAPPDATA%\ZaloManagerPoC`. Dữ liệu đăng nhập Zalo nằm trong `%APPDATA%` theo từng hồ sơ. Lịch sử tin thu hồi được lưu riêng theo tài khoản trong kho cục bộ mã hóa cho người dùng Windows hiện tại. Đọc tin chỉ bỏ số thông báo, còn nội dung được giữ đến khi bấm Xóa.
- Khi mở hồ sơ từ Manager, ứng dụng bật CDP trên cổng localhost riêng và theo dõi frame WebSocket. Di chuột lên trạng thái hồ sơ để xem kết nối và bộ đếm frame gửi/nhận.
- Shortcut tạo từ Manager mở đúng hồ sơ qua Manager để bật CDP cho phiên mới. Với shortcut cũ, chọn **… → Tạo Shortcut** để cập nhật. Nếu hồ sơ hiện **Chưa theo dõi**, dừng rồi mở lại hồ sơ. Listener tự kết nối lại và dừng khi đóng Manager.
- Sự kiện C# `CdpMessageWatcher.FrameReceived` cung cấp ID hồ sơ, chiều truyền, opcode và payload trong bộ nhớ. Payload binary dùng Base64. Việc giải mã thành sự kiện tin nhắn Zalo cần được triển khai tiếp.
- Mã nguồn tại `src/`, giao diện và biểu tượng tại `src/` và `assets/`, kiểm thử tại `tests/`.

### Kiểm thử tin thu hồi đa phương tiện

Mở đúng hội thoại ở hồ sơ nhận. Manager hiển thị **Đang theo dõi** khi quét được khung chat, **Chờ mở chat** khi đã kết nối CDP nhưng chưa thấy khung chat và **Chưa theo dõi** khi chưa kết nối. Với tệp trong hội thoại đang theo dõi, Manager thử dùng tệp cùng tên ở thư mục `Downloads` của hồ sơ Windows khi kích thước và mã kiểm tra Zalo đều khớp. Nếu thiếu bản đó và có URL CDN hợp lệ, Manager tải qua HTTPS. Giới hạn lưu là 16 MiB cho mỗi nội dung đính kèm. Trong kho Thu hồi, dùng **Đọc tệp** với `.txt`, **Tải về** để xuất tệp hoặc **Nghe** và **Lưu ghi âm** cho bản ghi âm có dữ liệu. Phần đọc `.txt` hỗ trợ UTF-8 và tệp có BOM Unicode đến 256 KiB.

Chạy một lệnh từ thư mục dự án với Node.js 22+ và Windows PowerShell có .NET Framework.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\RecallMedia.Tests.ps1
```

Lệnh build bản kiểm thử tạm, chạy dữ liệu mô phỏng cho ảnh, sticker, tệp, ghi âm và video, rồi kiểm tra byte lưu lại và nút hiển thị sau khi mở lại kho. Kịch bản cũng thử tệp chưa tải xong lúc thu hồi và tin thiếu nguồn tải. Kết quả `PASS` xác nhận luồng mô phỏng. Kết nối, quyền tải và codec trên Zalo thật nằm ngoài phép thử này.

Để xác minh kho thật, mở hội thoại trên hồ sơ **nhận**, dùng một tệp mẫu có tên riêng, rồi gửi và thu hồi từ tài khoản gửi trong lúc lệnh sau chờ. Thay `123456` bằng ID hồ sơ nhận, hoặc `original` nếu hồ sơ nhận là Zalo gốc.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\RecallLiveFile.Tests.ps1 -ProfileId 123456 -ExpectedFile .\phase1-lab\ZMC_MEDIA_TEST.txt -WaitSeconds 60
```

Lệnh xác minh tệp trong đúng kho hồ sơ nhận. `PASS` yêu cầu SHA-256 của bản lưu trùng tệp gốc và ghi nguồn bản sao nếu có. Nếu chỉ có tên, lệnh báo lỗi kèm trạng thái thu bản sao. Các thao tác gửi và thu hồi thực hiện trong Zalo.

- Di chuột lên trạng thái hồ sơ để xem tin văn bản mới nhất trong hội thoại Zalo đang mở, cùng chiều gửi/nhận. Manager chỉ theo dõi khi đang chạy và giữ bản xem trước trong bộ nhớ.
