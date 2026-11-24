# Yêu cầu review lại endpoint tin nhắn và kho thu hồi

Review mã nguồn hiện tại và đối chiếu với commit `a84b2f9`. Nếu HEAD đã thay đổi, ghi rõ commit đang review và chỉ ra phát hiện nào đã được sửa. Đây là yêu cầu review. Giữ nguyên mã nguồn trong lượt này.

Mục tiêu là nhận đúng sự kiện gửi, nhận và thu hồi từ Zalo Desktop qua endpoint nội bộ, đồng thời giữ dữ liệu đúng hồ sơ và kiểm soát RAM, dung lượng đĩa. Hiện Manager nhận sự kiện qua `Runtime.bindingCalled` của CDP. Hãy đánh giá luồng này trên Zalo gốc và clone.

## 1. Các lỗi cần ưu tiên

| Ưu tiên | Điều cần xác minh | Phương án để phản biện |
|---|---|---|
| P1 · Thứ tự sự kiện | `ThreadPool.QueueUserWorkItem` xử lý từng sự kiện độc lập. Nếu worker thu hồi chạy trước worker nhận tin gốc, bản ghi có thể chỉ chứa dòng báo chưa đọc được nội dung. Kết quả tải tệp cũng có thể cập nhật cùng bản ghi song song. [Bộ nhận CDP](src/CdpMessageWatcher.cs#L278) · [Xử lý thu hồi](src/CdpVisibleMessages.cs#L951) | Dùng hàng đợi hữu hạn và một bộ xử lý tuần tự cho mỗi hồ sơ. Gửi kết quả tải tệp về cùng bộ xử lý. Xác định cách bổ sung nội dung khi tin gốc thực sự đến muộn, cách xử lý hàng đợi đầy và cách bỏ sự kiện muộn từ phiên đã dừng. |
| P1 · Trạng thái hook | Phản hồi CDP lỗi vẫn có thể dẫn đến `Connected = true`. Phản hồi ID `2` thuộc `Runtime.enable` nhưng đi vào nhánh quét cũ. `setupPmHooks` có thể thoát sớm sau khi gắn `_pm` và bỏ qua hook phụ nếu UI chưa sẵn sàng. [Phản hồi CDP](src/CdpMessageWatcher.cs#L262) · [Cài hook](src/CdpVisibleMessages.cs#L580) | Xác nhận phản hồi từng lệnh và chỉ báo đang theo dõi sau khi hook phát tín hiệu sẵn sàng từ đúng trang. Rà lại vòng đời attach, reload, reconnect, thay target và dừng theo dõi. |
| P1 · Phân loại thu hồi | Một số callback xóa và action chứa `delete` hoặc `remove` đều có thể sinh bản ghi thu hồi. Cần đối chiếu payload thật để biết thao tác xóa phía mình có đi qua các nhánh này. Kiểm tra luôn ID hội thoại của tin nhóm, vì hook nhận batch có `convId` nhưng chưa truyền vào bộ xử lý từng tin. [Gắn hook batch](src/CdpVisibleMessages.cs#L675) · [Phân loại action](src/CdpVisibleMessages.cs#L701) | Tách loại sự kiện xóa và thu hồi theo callback hoặc payload đã xác minh. Truyền ID hội thoại riêng với ID người gửi. |
| P1 · Định danh dữ liệu | Code tạo khóa phụ bằng phần ID trước dấu `_`, nên `123_a` và `123_b` có thể cùng ghi vào khóa `123`. Đây là ví dụ về va chạm do code tạo ra. Kho còn dùng tên để ghi, gộp, mở và xóa hội thoại. [Khóa cache](src/IncomingMessageCache.cs#L53) · [Danh sách hội thoại](src/RecalledMessages.cs#L133) · [Xóa hội thoại](src/RecalledMessages.cs#L258) | Dùng ID đầy đủ trong phạm vi hồ sơ và hội thoại. Xác minh `msgId` và `cliMsgId` bằng payload rồi mới đổi cách ghép. Bỏ quy tắc theo tên ở cả bốn đường. Dữ liệu cũ đã gộp chỉ tách khi còn ID đủ tin cậy. |
| P1 · Ghi lỗi và thoát | Ghi kho có thể thất bại nhưng luồng vẫn bỏ bản cache. Luồng còn đánh dấu `Recalled` trước khi ghi, nên chỉ thêm giá trị trả về `bool` có thể làm lần thử lại bị bỏ qua. Đường thoát hiện chưa chờ worker hoàn tất và ghi phần cache đang chờ. [Xử lý thu hồi](src/CdpVisibleMessages.cs#L895) · [Ghi kho](src/RecalledMessages.cs#L346) · [Thoát](src/TrayManager.cs#L169) | Giữ tin gốc và trạng thái chờ ghi đến khi kho báo thành công. Làm rõ cách thử lại và xử lý bản ghi trùng. Dùng một trình tự thoát chung gồm dừng nhận, hoàn tất công việc đang chạy, ghi phần còn chờ rồi đóng ứng dụng. |

## 2. RAM và dung lượng đĩa

- **Dọn media.** Cache 24 giờ chỉ xóa bản ghi. Tệp đã tải, tệp bị thay thế và preview bị bỏ qua có thể nằm lại. Không gắn `File.Delete` trực tiếp vào `Remove`, vì kho thu hồi có thể đang dùng cùng tệp. Nếu bộ dọn chỉ đọc hai file `.dat`, nó có thể xóa nhầm media vừa tải xong trong lúc cache đang chờ ghi. Đề xuất tách thư mục tạm và phối hợp bộ dọn với luồng đăng ký tham chiếu của cả hai kho. Tính cả tệp mồ côi sau crash và tệp đang mở. [Lịch ghi cache](src/IncomingMessageCache.cs#L156) · [Hết hạn cache](src/IncomingMessageCache.cs#L355) · [Preview](src/CdpVisibleMessages.cs#L819)
- **Tải theo luồng.** Ghi từng khối vào tệp tạm, kiểm tra kích thước và checksum trong lúc đọc, rồi công bố tệp. Áp dụng cả cho đường lấy tệp cục bộ đang chuyển sang Base64. Đặt giới hạn tải chung cho Manager. Buffer 64 KiB chỉ là bộ nhớ sao chép dữ liệu, nên hãy đo RAM khi tải nhiều tệp lớn trên nhiều hồ sơ. [Đọc tệp cục bộ](src/RecallMediaDownload.cs#L174) · [Tải qua mạng](src/RecallMediaDownload.cs#L341)
- **Xem lại ảnh.** Thử thumbnail khoảng 440 px cho danh sách và chỉ giải mã ảnh đầy đủ khi người dùng mở. Giới hạn cache theo số byte ảnh đã giải mã. Rà thêm tuổi và dung lượng của `captured` khi Manager chạy nền lâu. [Cache ảnh](src/RecallAttachmentView.cs#L623) · [Cache trong phiên](src/CdpVisibleMessages.cs#L39)
- **Hiển thị dung lượng thật.** Đo kho cache, kho thu hồi, media và tệp tạm dưới thư mục Manager. Tính kho dùng chung một lần ở cấp ứng dụng, tránh cộng lại toàn bộ kho cho mỗi hồ sơ. Cập nhật số liệu sau khi dọn và báo số byte thực sự thu hồi được. [Đo dung lượng](src/StorageUsage.cs#L31) · [Cộng tổng](src/StorageUsage.cs#L93)

## 3. Ca nghiệm thu cần đề xuất

1. Nhận tin rồi thu hồi ngay. Kho lưu được đúng nội dung và chỉ tạo một bản ghi khi có callback trùng.
2. Xóa phía mình, thu hồi thật và tin nhóm. Manager phân loại đúng, giữ đúng ID hội thoại và người gửi.
3. Zalo gốc và ít nhất một clone. Thử khi không mở hội thoại, cửa sổ thu nhỏ, reload, reconnect và hook cài thất bại. Trạng thái theo dõi phản ánh đúng kết quả.
4. Hai hội thoại trùng tên và hai ID có cùng tiền tố. Mở, tra và xóa đúng dữ liệu đã chọn.
5. Đĩa đầy lúc ghi kho hoặc thoát khi cache còn chờ ghi. Tin gốc còn để thử lại nếu ghi lỗi. Lượt thoát thông thường lưu được công việc đã nhận.
6. Cache hết hạn, tệp vừa tải, hai bản ghi cùng dùng một tệp và crash giữa lúc ghi. Chỉ tệp hết tham chiếu được dọn, số dung lượng cập nhật đúng.
7. Tải đồng thời nhiều tệp lớn và mở hội thoại dài có ảnh. Đo RAM đỉnh, độ trễ xử lý p95, số sự kiện mất hoặc trùng, và dung lượng trước/sau khi dọn.

## 4. Kết quả review cần trả lại

- Với mỗi mục, ghi **xác nhận**, **bác bỏ**, hoặc **cần kiểm chứng trên Zalo thật**, kèm `file:line` và kịch bản cụ thể.
- Phản biện từng phương án, chỉ ra trường hợp có thể làm mất tin, xóa nhầm tệp hoặc đổi hành vi của Zalo. Phân biệt lỗi thấy từ code với giả thuyết cần thử.
- Đề xuất thứ tự sửa và ca kiểm thử cho từng đợt. Ưu tiên thứ tự sự kiện, trạng thái hook và phân loại thu hồi trước các tối ưu RAM và dung lượng. Nêu điều kiện để giữ CDP hoặc khi nào việc đổi transport mới đáng làm.
