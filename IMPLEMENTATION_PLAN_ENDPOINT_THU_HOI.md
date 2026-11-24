# Kế hoạch triển khai endpoint tin nhắn và kho thu hồi

Mốc đối chiếu `a84b2f9`. Trước khi sửa, đối chiếu HEAD hiện tại và ghi lại các mục đã được xử lý. Giữ CDP binding làm kênh IPC nội bộ ở đợt này. Mục tiêu là nhận đúng sự kiện gửi, nhận và thu hồi trên Zalo gốc và clone, lưu đúng dữ liệu, rồi kiểm soát RAM và dung lượng đĩa khi chạy nền.

## Đợt 1 · Độ tin cậy của endpoint và dữ liệu

### 1. P1 · Xử lý sự kiện tuần tự theo hồ sơ

- **Giải pháp.** Thay worker riêng cho từng sự kiện bằng hàng đợi hữu hạn và một bộ xử lý tuần tự cho mỗi hồ sơ. Đưa kết quả media vào cùng hàng đợi. Gắn sự kiện với phiên hiện tại, dừng nhận từ phiên cũ và ghi nhận số sự kiện bị bỏ khi đầy. Với thu hồi đã xác minh mà tin gốc chưa tới, giữ một mục chờ có giới hạn. Có thể thử cửa sổ khoảng 3 giây rồi điều chỉnh theo dữ liệu thực tế. Khi hết chờ, lưu dấu thu hồi và cho phép bổ sung nội dung đến muộn vào cùng bản ghi.
- **Kết quả cần đạt.** Nhận rồi thu hồi ngay vẫn giữ được nội dung. Tin gốc đến trước, trong hoặc sau thời gian chờ đều ghép đúng và chỉ tạo một bản ghi. Nhiều hồ sơ không làm lẫn dữ liệu. Sự kiện bị bỏ vì hàng đợi đầy có thể được phát hiện.
- **Đầu ra.** Bộ xử lý và hàng đợi theo hồ sơ, cơ chế ghép thu hồi đến sớm, bộ đếm hàng đợi và test đảo thứ tự, trùng sự kiện, kết quả media đến muộn, phiên cũ và hàng đợi đầy. [Bộ nhận CDP](src/CdpMessageWatcher.cs#L278) · [Ghép tin thu hồi](src/CdpVisibleMessages.cs#L882)

### 2. P1 · Xác nhận hook đã sẵn sàng và quản lý vòng đời

- **Giải pháp.** Theo dõi kết quả từng lệnh CDP, xử lý riêng `Runtime.enable` và kiểm tra lỗi JavaScript khi chạy hook. Mỗi hook tự kiểm tra đã gắn hay chưa để hook chính gắn trước vẫn có thể bổ sung hook phụ. Hook gửi xác nhận từ đúng target và ngữ cảnh. Kiểm tra lại khi reload, reconnect hoặc thay target. Khi dừng, hủy vòng dò và gỡ đăng ký, wrapper thuộc phiên theo dõi khi có thể.
- **Kết quả cần đạt.** Giao diện phản ánh đúng ba trạng thái đang kết nối, đang theo dõi và mất theo dõi. Lệnh lỗi, hook chưa gắn hoặc phiên đã dừng không được báo đang theo dõi. Reload và reconnect không tạo nhiều wrapper cho cùng callback.
- **Đầu ra.** Trạng thái theo dõi trên UI, xác nhận từ hook, xử lý vòng đời và log theo hồ sơ/phiên. Kèm test lệnh lỗi, hook xuất hiện muộn, reload, reconnect và dừng. [Phản hồi CDP](src/CdpMessageWatcher.cs#L262) · [Cài hook](src/CdpVisibleMessages.cs#L580)

### 3. P1 · Phân biệt thu hồi, xóa phía mình và tin nhóm

- **Giải pháp.** Thu mẫu payload đã ẩn nội dung và định danh cá nhân trên Zalo gốc và clone. Ghi rõ phiên bản, callback và trường nào xác nhận loại sự kiện. Giữ `DELETE` và `REMOVE` làm nguồn quan sát khi cần. Chỉ ghi thu hồi khi có tín hiệu xác nhận. Truyền `convId` từ callback nhóm và tách ID hội thoại, ID người gửi và hướng tin. Người gửi tin gốc không xác định được ai thực hiện thao tác xóa.
- **Kết quả cần đạt.** Xóa phía mình không tạo bản thu hồi. Thu hồi thật được ghi đúng theo chính sách hiện có. Tin nhóm đi vào đúng hội thoại. Payload thiếu trường xác nhận không tự biến thành thu hồi qua điều kiện như `deleteType !== 1`.
- **Đầu ra.** Bảng đối chiếu payload theo phiên bản, bộ phân loại sự kiện và fixture kiểm thử. Các callback chưa xác minh được ghi rõ trong báo cáo chạy thật. [Hook batch](src/CdpVisibleMessages.cs#L675) · [Hook action](src/CdpVisibleMessages.cs#L701)

### 4. P1 · Định danh tin nhắn và giữ lịch sử cũ

- **Giải pháp.** Dùng ID đầy đủ trong phạm vi hồ sơ và hội thoại. Chỉ dùng `cliMsgId` làm bí danh khi payload xác nhận quan hệ với `msgId`. Bỏ khóa tự cắt theo dấu `_`. Ghi, liệt kê, mở và xóa hội thoại theo ID. Giữ khóa kho cũ để lịch sử còn mở được. Chỉ tách lịch sử đã gộp khi còn dữ liệu đủ để xác định từng tin. Khóa `name:...` trong luồng dựng UI không phải căn cứ để đổi khóa đã lưu.
- **Kết quả cần đạt.** Hai hội thoại cùng tên mở và xóa độc lập. Hai ID cùng tiền tố không ghi đè nhau. Mở được kho cũ sau nâng cấp và không tự gán lịch sử chưa xác định sang hội thoại khác.
- **Đầu ra.** Cách tra cứu mới, luồng tương thích dữ liệu cũ và bộ test hội thoại trùng tên, bí danh, cùng tiền tố, hai hồ sơ và nâng cấp từ kho cũ. [Khóa cache](src/IncomingMessageCache.cs#L53) · [Danh sách hội thoại](src/RecalledMessages.cs#L133) · [Xóa hội thoại](src/RecalledMessages.cs#L258)

### 5. P1 · Ghi dữ liệu, thử lại và thoát an toàn

- **Giải pháp.** Chỉ đánh dấu hoàn tất và bỏ tin gốc khỏi cache sau khi kho thu hồi xác nhận đã lưu. Khi lưu lỗi, giữ tin gốc và trạng thái chờ thử lại. Thử lại cùng ID phải cập nhật hoặc hoàn tất cùng bản ghi. Khi xóa hội thoại mà ghi kho thất bại, giữ lại trạng thái trước thao tác. Dùng một quy trình thoát chung để dừng nhận, hoàn tất hoặc lưu lại việc đang chờ, kiểm tra kết quả ghi cache rồi đóng. Hiển thị trạng thái nếu không thể lưu.
- **Kết quả cần đạt.** Đĩa đầy hoặc mất quyền ghi không làm mất bản cache gốc. Thử lại thành công không tạo tin thu hồi trùng. Thoát bình thường lưu được công việc đã nhận theo trình tự đã định. Xóa thất bại không làm hội thoại biến mất khỏi UI.
- **Đầu ra.** Kết quả ghi có thể kiểm tra, đường thử lại, quy trình thoát và test lỗi ghi, xóa thất bại, thoát khi có sự kiện hoặc thao tác lưu còn chờ. [Ghi kho](src/RecalledMessages.cs#L346) · [Chuyển tin khỏi cache](src/CdpVisibleMessages.cs#L895) · [Thoát Manager](src/TrayManager.cs#L169)

## Đợt 2 · RAM và hiệu năng khi chạy nền

### 6. P2 · Tải media theo luồng và giới hạn tài nguyên

- **Giải pháp.** Đọc từng khối từ nguồn mạng hoặc nguồn cục bộ và ghi vào tệp tạm trong kho do Manager quản lý. Kiểm tra kích thước, loại tệp và checksum nếu có trước khi công bố đường dẫn. Dùng ngân sách tải đồng thời chung cho Manager. Đặt giới hạn số mục, tuổi và dung lượng của cache trong phiên. Ghi metadata theo lô và đo thời gian chờ khóa kho khi chạy nhiều hồ sơ.
- **Kết quả cần đạt.** Tệp lớn không cần giữ hai bản đầy đủ trong RAM hay đi qua Base64 trên đường lưu mới. Tệp tải lỗi không được công bố. RAM ổn định trong bài chạy dài với nhiều hồ sơ và nội dung đã lưu vẫn mở được.
- **Đầu ra.** Pipeline tải theo luồng, giới hạn tài nguyên, test tệp sát giới hạn, hủy tải, đĩa đầy, checksum sai và báo cáo RAM/I/O trước và sau. [Đọc tệp cục bộ](src/RecallMediaDownload.cs#L174) · [Tải qua mạng](src/RecallMediaDownload.cs#L341)

### 7. P2 · Thumbnail và danh sách hội thoại dài

- **Giải pháp.** Giải mã thumbnail theo kích thước hiển thị và DPI, chỉ giải mã ảnh đầy đủ khi mở trình xem. Giới hạn cache ảnh theo byte. Phân trang hoặc ảo hóa danh sách dài để giới hạn số item và bitmap đang giữ.
- **Kết quả cần đạt.** Cuộn hội thoại dài không giải mã tất cả ảnh gốc. Mở ảnh đầy đủ và lưu tệp vẫn hoạt động. RAM giảm so với cùng bài thử trên bản đối chiếu.
- **Đầu ra.** Luồng thumbnail tách khỏi ảnh đầy đủ, cache giới hạn theo byte và số liệu RAM, độ trễ mở/cuộn. [Cache ảnh](src/RecallAttachmentView.cs#L623) · [Dựng tin trong hội thoại](src/RecallConversationWindow.cs#L198)

## Đợt 3 · Dọn tệp và hiển thị dung lượng thực

### 8. P2 · Dọn media hết tham chiếu

- **Giải pháp.** Quét media thuộc Manager và đối chiếu tham chiếu từ cả cache, kho thu hồi và dữ liệu đang được ghi. Phối hợp bước kiểm tra cuối với bước công bố tham chiếu. Tách tệp đang tải ra khỏi phạm vi dọn tệp đã công bố. Hoãn dọn khi một kho không đọc được. Tệp quá tuổi chỉ là ứng viên dọn. Bắt đầu bằng chế độ báo cáo ứng viên, sau đó bật xóa khi các ca nghiệm thu đạt.
- **Kết quả cần đạt.** Cache hết hạn, media bị thay thế và tệp bỏ lại sau crash được dọn khi hết tham chiếu. Tệp đang tải, đang chờ ghi hoặc vẫn được một bản ghi dùng còn nguyên. Lỗi dọn có thể thử lại và báo đúng kết quả.
- **Đầu ra.** Bộ dọn dùng chung cho dọn định kỳ và dọn thủ công, log tệp/byte đã giải phóng, test tệp dùng chung, tệp sắp công bố, tệp đang mở và lỗi đọc kho. [Hết hạn cache](src/IncomingMessageCache.cs#L355) · [Xóa media](src/RecalledMessages.cs#L215)

### 9. P2 · Hiển thị dung lượng thực trên đĩa

- **Giải pháp.** Đo cache, kho thu hồi, media và tệp tạm dưới thư mục Manager. Cộng kho dùng chung một lần ở cấp ứng dụng. Nếu có số theo hồ sơ, tính theo tham chiếu của hồ sơ và ghi rõ cách tính tệp dùng chung. Đo lại sau khi dọn.
- **Kết quả cần đạt.** Tổng dung lượng không tăng giả khi thêm hồ sơ. Số byte đã giải phóng phản ánh tệp thực sự được xóa. Lỗi đọc dung lượng được hiển thị là số liệu chưa đầy đủ.
- **Đầu ra.** Mục dung lượng kho thu hồi trên UI, kết quả dọn và test đối chiếu số đo khi có nhiều hồ sơ hoặc tệp dùng chung. [Đo dung lượng](src/StorageUsage.cs#L31) · [Tổng Manager](src/StorageUsage.cs#L93)

## Bàn giao và điều kiện hoàn tất

- **Mỗi đợt bàn giao** gồm diff hoặc commit, danh sách ca test và kết quả, hướng dẫn chạy lại, ca còn chưa xác minh và hướng quay lui nếu thay đổi cách lưu dữ liệu.
- **Nghiệm thu trên Zalo thật** dùng Zalo gốc và ít nhất một clone. Thử nhận rồi thu hồi ngay, xóa phía mình, chat nhóm, không mở hội thoại, thu nhỏ, mất kết nối, reload và hai hồ sơ chạy cùng lúc. Dùng dữ liệu thử, không đưa nội dung chat hoặc dump cache thật vào repo.
- **So sánh trước và sau** trên cùng máy, bản Zalo và dữ liệu thử. Báo số sự kiện mất/trùng, độ trễ p95 từ nhận sự kiện đến ghi nhận, RAM đỉnh, RAM khi chạy lâu, I/O ghi và dung lượng trước/sau khi dọn. Chốt ngưỡng hiệu năng sau khi có số đo đường cơ sở.
- **Điều kiện bật mặc định** là các ca P1 đạt, dữ liệu cũ mở được, và dọn media đã được kiểm chứng trong tình huống ghi đồng thời hoặc khởi động lại sau crash. Chỉ mở lại quyết định về transport nếu kiểm thử cho thấy CDP không đáp ứng vòng đời hoặc độ bao phủ cần thiết.
