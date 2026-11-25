# Zalo Manager

## App này là gì
Zalo Manager là công cụ chạy và quản lý nhiều tài khoản Zalo PC song song trên cùng một tài khoản Windows. Mỗi hồ sơ clone chạy độc lập trong một box **Sandboxie-Plus** riêng, dùng chung tệp cài đặt Zalo gốc đã xác minh chữ ký số VNG.

## Dùng ở đâu
- Dùng trực tiếp trên máy tính cá nhân hoặc máy làm việc chạy hệ điều hành Windows để quản lý tập trung nhiều tài khoản Zalo mà không cần đăng xuất/đăng nhập lại hoặc tạo nhiều tài khoản Windows khác nhau.

## Thích hợp với các môi trường nào
- **Hệ điều hành**: Windows 10 / Windows 11 (x64).
- **Phần mềm nền**: Đã cài sẵn **Zalo PC** bản chính thức của VNG và **Sandboxie-Plus** (từ v5.74.1 / v1.19.1 trở lên; bộ cài `ZaloManagerSetup.exe` tự cài kèm nếu máy chưa có).
- **Quyền hạn**: Cần quyền quản trị (Administrator) khi chạy bộ cài đặt `ZaloManagerSetup.exe`.

## Các chức năng
- **Nhận diện & đồng bộ Zalo gốc**: Tự động tìm hoặc trỏ chọn thủ công cửa sổ/icon Zalo gốc, kiểm tra chữ ký số VNG và đồng bộ phiên bản mới cho các clone đã dừng.
- **Tạo & quản lý nhiều hồ sơ clone**: Khởi tạo nhanh box Sandboxie riêng, mở/dừng độc lập từng tài khoản, xóa hồ sơ an toàn (chuyển dữ liệu vào thư mục `Deleted`).
- **Theo dõi trạng thái & dung lượng**: Hiển thị trạng thái đăng nhập (`Đã đăng nhập` / `Chưa đăng nhập`), trạng thái chạy (`Đang chạy` / `Đã dừng`) và dung lượng dữ liệu thực tế của từng hồ sơ.
- **Tùy chỉnh hồ sơ (kể cả Zalo gốc)**: Đổi tên hồ sơ, đổi avatar/icon app (tự chọn ảnh hoặc tự động lấy từ tài khoản Zalo hiện tại) khi hồ sơ đã đăng nhập.
- **Shortcut & Khởi động cùng Windows**: Tạo shortcut riêng cho từng hồ sơ ra Desktop/Start Menu; tùy chọn tự mở Manager và tự mở các clone đã chọn khi đăng nhập Windows.
- **Sao lưu & di chuyển dữ liệu**: Xuất/nhập dữ liệu hồ sơ qua tệp `.zmdata` có kiểm tra toàn vẹn SHA-256 và đối chiếu đúng ID tài khoản (hỗ trợ nhập đè hoặc nhập thành hồ sơ mới).
- **Chạy nền khay hệ thống**: Đóng cửa sổ Manager sẽ thu gọn xuống khay hệ thống để các cửa sổ Zalo đang mở tiếp tục hoạt động bình thường.

## Hướng dẫn cài
1. Tải và chạy `ZaloManagerSetup.exe` với quyền quản trị (Administrator).
2. Bộ cài sẽ tự giữ lại Sandboxie-Plus tương thích nếu đã có, hoặc tự cài đặt phiên bản đi kèm và tắt tự mở giao diện Sandboxie.
3. Sau khi cài xong, mở **Zalo Manager** từ shortcut trên Desktop hoặc Start Menu.
*(Nếu máy đã cài sẵn Sandboxie-Plus tương thích, có thể chạy trực tiếp `ZaloManager.exe`.)*

## Cách sử dụng
1. **Kiểm tra nguồn**: Khi mở app, Manager tự nhận diện Zalo gốc. Nếu chưa thấy, bấm **Trỏ chọn Zalo gốc…** và click vào cửa sổ hoặc icon Zalo trên taskbar.
2. **Thêm hồ sơ mới**: Nhập tên hồ sơ → bấm **+ Thêm hồ sơ** → bấm **Mở** ở hàng vừa tạo rồi đăng nhập trực tiếp trong cửa sổ Zalo.
3. **Tùy chỉnh & thao tác nâng cao**: Sau khi hồ sơ đã đăng nhập, bấm nút **`…`** trên hàng hồ sơ để tạo shortcut, bật tự mở cùng Windows, đổi tên/avatar hoặc xuất/nhập dữ liệu `.zmdata`.
4. **Cập nhật khi Zalo ra bản mới**: Dừng các hồ sơ đang chạy → bấm **Kiểm tra & Đồng bộ** để các clone dùng phiên bản Zalo gốc mới mà vẫn giữ nguyên dữ liệu.

## Lưu ý khi dùng
- Đóng các hồ sơ clone trước khi **Đồng bộ**, **Xuất/Nhập data** hoặc **Xóa hồ sơ** để tránh lỗi khóa tệp.
- Các nút tùy chỉnh (`Tạo shortcut`, đổi tên/avatar, xuất/nhập data) chỉ khả dụng sau khi hồ sơ đã đăng nhập.
- Xuất/nhập `.zmdata` chỉ áp dụng cho hồ sơ clone cùng phiên bản Zalo; không bao gồm tệp tải xuống nằm ngoài thư mục dữ liệu hoặc dữ liệu chỉ lưu trên cloud.
- Không xóa thủ công thư mục `C:\Users\<tên>\AppData\Local\ZaloManagerPoC\Sandboxie` nếu đang sử dụng các hồ sơ clone.
- Bấm nút **×** trên cửa sổ Manager chỉ ẩn xuống khay hệ thống; muốn tắt hẳn ứng dụng, hãy chọn **Thoát** ở góc trên bên phải hoặc chuột phải icon khay hệ thống → **Thoát hoàn toàn**.
