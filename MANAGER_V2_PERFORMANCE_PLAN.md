# Kế hoạch Zalo Manager v2 — Nhận sự kiện từ Zalo

Bản thảo thảo luận · 23/11/2026

**Kiến trúc v2.** Đặt bộ phát tại luồng xử lý tin nhắn của Zalo và đưa sự kiện qua một endpoint nội bộ cho Manager. Manager chờ sự kiện gửi, nhận và thu hồi, rồi lưu theo chế độ của từng hồ sơ. Mục tiêu là bắt được sự kiện ngay cả khi người dùng đang mở hội thoại khác hoặc cửa sổ Zalo đang thu nhỏ. Đường thu hồi v2 loại bỏ `Runtime.evaluate` định kỳ, `MutationObserver`, đọc DOM/React Fiber và chụp hoặc tạo preview từ giao diện.

**Thứ tự đã cập nhật.** Đợt 1 làm công tắc ba nấc và gom việc dò tiến trình, đồng thời thử endpoint thật trên gốc và clone. Hai chế độ thu hồi chỉ báo sẵn sàng trên hồ sơ đã nhận được sự kiện từ nguồn. Đợt 2 dùng `winsqlite3` cho kho metadata, lưu tệp riêng theo SHA-256 và chuyển dữ liệu từ v1.

## 1. Hiện trạng và nguyên nhân gây nặng

| Đường chạy của v1 | Điều đã xác nhận | Việc xử lý trong v2 |
|---|---|---|
| CDP phát frame WebSocket | Manager đã bật `Network.enable` và nhận frame gửi/nhận, nhưng chưa giải mã thành sự kiện nghiệp vụ. `FrameReceived` chưa nối vào luồng lưu tin thu hồi. [CdpMessageWatcher.cs](src/CdpMessageWatcher.cs#L183) · [CdpMessageWatcher.cs](src/CdpMessageWatcher.cs#L257) | Xác định điểm Zalo đã phân loại tin và thu hồi. Dùng frame và log metadata có kiểm soát để đối chiếu trong thử nghiệm. |
| Đọc khung chat | Manager đưa script vào Zalo bình thường khoảng một lần mỗi giây. `MutationObserver` gắn ở gốc tài liệu còn gọi quét khi record liên quan đến khung chat. Canvas lấy pixel từ từng avatar, ảnh hoặc sticker đã render để làm preview. [CdpMessageWatcher.cs](src/CdpMessageWatcher.cs#L206) · [CdpVisibleMessages.cs](src/CdpVisibleMessages.cs#L65) · [CdpVisibleMessages.cs](src/CdpVisibleMessages.cs#L187) · [CdpRecallMedia.cs](src/CdpRecallMedia.cs#L138) | Thay đường thu hồi đọc giao diện bằng bộ phát sự kiện trong ứng dụng. Vị trí cửa sổ, hội thoại đang xem và cấu trúc khung chat không tham gia vào việc phát hiện sự kiện. |
| Cache và tệp đính kèm | ID, nguồn và bản tin giữ trong phiên chưa có ngân sách tổng. Tệp có thể được tải trước khi thu hồi, kèm nhiều bản sao byte/Base64. Hàng chờ đầy hoặc tải lỗi có thể khiến nguồn đã ghi nhận không được thử lại. [CdpVisibleMessages.cs](src/CdpVisibleMessages.cs#L38) · [CdpVisibleMessages.cs](src/CdpVisibleMessages.cs#L117) · [RecallMediaDownload.cs](src/RecallMediaDownload.cs#L27) | Chỉ giữ nội dung cần thiết, giới hạn RAM và hàng chờ theo hồ sơ lẫn toàn Manager. Chuyển tệp theo luồng và chỉ làm khi chế độ cho phép. |
| Kho tin thu hồi | Nạp và mã hóa lại toàn kho chung khi thêm tin, cập nhật tệp hoặc đánh dấu đã đọc. Tác vụ giao diện có thể thực thi hoặc chờ cùng khóa. [RecalledMessages.cs](src/RecalledMessages.cs#L132) · [RecalledMessages.cs](src/RecalledMessages.cs#L243) | Ghi tăng dần qua luồng nền. Đọc theo trang và chỉ nạp tệp được mở. |
| Quản lý tiến trình và dung lượng | WPF liệt kê tiến trình cứ 4 giây cho từng hồ sơ. Chỉ tên tiến trình khớp mới được truy vấn đường dẫn Win32. Dò WMI có thể lặp khi không tìm được cổng CDP. Quét dung lượng nền khoảng hai phút/lần có thể gồm cả bản giữ để khôi phục. [Manager.cs](src/Manager.cs#L364) · [Manager.cs](src/Manager.cs#L653) · [CdpMessageWatcher.cs](src/CdpMessageWatcher.cs#L128) · [StorageUsage.cs](src/StorageUsage.cs#L104) | Theo dõi PID và sự kiện thoát khi có thể, dùng chung một lần chụp danh sách tiến trình trên luồng nền để nhận phiên ngoài Manager. Đo đĩa có lịch giãn, gộp cập nhật giao diện theo hồ sơ. |

### Phản biện các kết luận vượt quá bằng chứng

- **V1 đi lệch tại điểm lấy dữ liệu.** Luồng frame CDP dừng ở dữ liệu transport. Phần lưu thu hồi đi tiếp bằng DOM vì đó là nơi v1 lấy được nội dung. Vòng quét làm thêm việc trên renderer và là nghi phạm khi gõ/cuộn khựng. Chưa có phép đo trên Zalo thật để kết luận đây là nguyên nhân duy nhất hoặc gán mức tăng **8–25% CPU**, **1–2 GB RAM** hay cam kết **60 FPS**. [CdpMessageWatcher.cs](src/CdpMessageWatcher.cs#L257) · [CdpVisibleMessages.cs](src/CdpVisibleMessages.cs#L217)
- **Canvas và RAM tăng là hai cơ chế khác nhau.** Canvas lấy pixel từ avatar, ảnh hoặc sticker đã render và mã hóa preview. Đây là công việc mã hóa ảnh trên renderer. Cache `seen`, `sources` và cache C# giữ dữ liệu dài hơn cần thiết vì thiếu giới hạn theo phiên. Hook cũng cần giới hạn hàng đợi và cache để tránh lặp lại vấn đề. Nhánh media duyệt tối đa **32 tầng React Fiber** khi cần, còn observer lọc record liên quan đến khung chat trước khi quét. [CdpRecallMedia.cs](src/CdpRecallMedia.cs#L26) · [CdpRecallMedia.cs](src/CdpRecallMedia.cs#L138) · [CdpVisibleMessages.cs](src/CdpVisibleMessages.cs#L38) · [CdpVisibleMessages.cs](src/CdpVisibleMessages.cs#L188)
- **Mục tiêu lúc rảnh là không có công việc định kỳ từ đường thu hồi.** Không quét, mã hóa ảnh, tải tệp hoặc ghi kho khi không có sự kiện hay tác vụ dở. CPU toàn tiến trình vẫn có thể dùng cho Zalo, giao diện Manager, reconnect và các chức năng khác. Đo phần CPU tăng thêm so với Zalo chạy độc lập, thay cho cam kết **0% CPU**.
- **Kho ghi toàn phần là nguyên nhân cụ thể có thể làm Manager đứng.** Mốc **1,3 giây** và **7,7 giây** là hai thao tác trong cùng một phép thử kho giả lập khoảng 108 MiB, lần lượt thêm tin ở mốc năm tệp và đánh dấu cả năm đã đọc. Hai số này không đại diện cho mọi lần lưu và không đo độ khựng bên trong Zalo. [RecalledMessages.cs](src/RecalledMessages.cs#L132) · [RecalledMessages.cs](src/RecalledMessages.cs#L270)

## 2. Thiết kế endpoint sự kiện

```text
Luồng xử lý tin/thu hồi bên trong Zalo
    → Bộ phát sự kiện trong phiên Zalo
    → Endpoint IPC cục bộ
    → Manager nhận, ghép tin với sự kiện thu hồi và ghi kho
```

### Xác định điểm đặt bộ phát

1. **Ưu tiên sự kiện nghiệp vụ đã được phân loại.** Tìm điểm ứng dụng xử lý thành công tin gửi, tin nhận và thu hồi, hoặc điểm chúng đi qua IPC nội bộ. Với thu hồi, cần xác định callback nhận được định danh tin trước hay sau khi nội dung cũ bị thay thế. Tên callback và vị trí cụ thể còn phải xác minh.
2. **Kiểm tra đầu vào bằng hội thoại thử.** Đối chiếu một tin đến, một tin gửi và một lần thu hồi trong hai tài khoản thử. Lặp với hội thoại đóng và cửa sổ thu nhỏ. Ghi thời gian, loại sự kiện và định danh rút gọn để chứng minh quan hệ giữa tin và thu hồi. Kiểm tra riêng tin vừa gửi đã bị thu hồi, tin đồng bộ lại khi reconnect và trường hợp đổi tài khoản.
3. **Dùng WebSocket thô làm ứng viên có điều kiện.** Chỉ coi đây là điểm phát phù hợp khi thử nghiệm thấy có đủ loại sự kiện và ID để ghép chính xác. Bật `Network.enable` hay thấy frame tăng chưa chứng minh đã bắt được sự kiện thu hồi. [Kế hoạch lắng nghe ban đầu](PLAN_ZALO_EVENT_LISTENER.md) · [Bộ nhận frame hiện tại](src/CdpMessageWatcher.cs#L257)

### Nạp bộ phát một lần nếu điểm tích hợp nằm ở renderer

- `Page.addScriptToEvaluateOnNewDocument` là phương án thử để đăng ký script **một lần trên target CDP đang gắn**. Script được chạy khi frame mới được tạo, nên có thể được cài lại khi tài liệu reload trong phiên. Đây là đăng ký theo vòng đời target/phiên CDP, không phải cài đặt vĩnh viễn cho Zalo. Khi target thay thế, kết nối bị lập lại hoặc Zalo khởi động lại, Manager phải kiểm tra và đăng ký lại khi cần. [Đặc tả CDP Page](https://raw.githubusercontent.com/ChromeDevTools/devtools-protocol/master/pdl/domains/Page.pdl)
- Khi attach sau lúc Zalo đã tải xong, tài liệu hiện tại cần khởi tạo riêng. Thử `runImmediately` nếu phiên bản CDP của Zalo hỗ trợ, hoặc một lần khởi tạo có kiểm soát nếu attach muộn. Một lần chạy script muộn chỉ có thể đăng ký những điểm còn tiếp cận được, nên phải thử cả mở từ đầu và gắn vào phiên có sẵn. Giới hạn ở frame đúng của hồ sơ và bảo đảm mỗi ngữ cảnh chỉ cài một bộ phát. [Đặc tả CDP Page](https://raw.githubusercontent.com/ChromeDevTools/devtools-protocol/master/pdl/domains/Page.pdl)
- Với đường CDP, thử `Runtime.addBinding` để đẩy gói sự kiện nhỏ đến `Runtime.bindingCalled`. Việc đăng ký script chỉ giải quyết lúc nạp. Nó không tự phát hiện sự kiện tin hoặc thu hồi. Nếu luồng cần nghe thuộc tiến trình khác, cần điểm tích hợp và kênh tương ứng ở đó. Khi tắt, tháo bộ phát đã chạy, gỡ đăng ký script/binding và ngừng nhận sự kiện. Gỡ đăng ký cho tài liệu tương lai không tự gỡ callback trong tài liệu hiện tại. [Đặc tả CDP Runtime](https://raw.githubusercontent.com/ChromeDevTools/devtools-protocol/master/pdl/js_protocol.pdl) · [Đặc tả CDP Page](https://raw.githubusercontent.com/ChromeDevTools/devtools-protocol/master/pdl/domains/Page.pdl)

### Truyền và nhận sự kiện

- **Ứng viên IPC trên Windows là Named Pipe theo phiên/hồ sơ.** Manager mở đầu nhận cục bộ, Zalo đẩy sự kiện ngay khi phát sinh. Chốt transport sau khi biết bộ phát chạy ở đâu và phương án nào dùng chung được cho gốc lẫn clone. Endpoint giới hạn quyền cho tài khoản Windows hiện tại và gắn kết nối với phiên hồ sơ đã xác thực.
- **Tách sự kiện khỏi giao diện.** Gói tối thiểu gồm phiên bản giao thức, định danh phiên/hồ sơ, loại sự kiện, ID hội thoại, ID tin, hướng và thời gian. `message.received` và `message.sent` có thể kèm nội dung theo chế độ. `message.recalled` mang ID tin bị thu hồi. Chỉ gửi metadata đính kèm cần thiết khi chế độ cho phép.
- **Gửi bất đồng bộ qua hàng đợi hữu hạn.** Điểm phát chỉ đóng gói dữ liệu nhỏ. Manager và việc ghi đĩa có thể chậm hoặc tạm ngắt mà không chặn luồng chat. Kiểm thử ưu tiên sự kiện thu hồi khi hàng đợi đầy, báo sự kiện thất lạc nếu quá giới hạn, khử trùng lặp khi reconnect và bỏ sự kiện đến muộn của phiên đã dừng.
- **Tương thích gốc và clone là điều kiện riêng.** Clone hiện có bước thay một số chuỗi trong ASAR. Bước đó chưa cài bộ phát sự kiện. Zalo gốc có thể cần đường tích hợp hoặc khởi chạy khác, nên phải chứng minh endpoint trên gốc và ít nhất một clone trước khi chốt. Khi bản Zalo cụ thể chưa có điểm tích hợp đã xác minh, Manager hiển thị tình trạng riêng cho bản đó. [Manager.cs](src/Manager.cs#L254)

## 3. Giữ tính năng thu hồi nhẹ

| Chế độ theo hồ sơ | Hành vi |
|---|---|
| **Tắt** | Dừng tiếp nhận và tạo bản ghi thu hồi mới, hủy các tác vụ đang tải và giải phóng cache phiên. Kho cũ vẫn mở được. Nếu endpoint phục vụ thêm tính năng sau này, chỉ giữ các thuê bao thực sự cần dùng. |
| **Chỉ văn bản** | Ghép nội dung chữ với sự kiện thu hồi theo ID hồ sơ, hội thoại và tin. Giữ văn bản trong cache có giới hạn. Không mở đường lấy nguồn, đọc tệp, tải, tạo preview hay băm media. |
| **Văn bản và tệp** | Lấy nguồn từ sự kiện. Nếu đó là URL tải hợp lệ, C# đọc luồng nhị phân vào tệp tạm có bảo vệ, tính SHA-256 khi nhận byte và đặt giới hạn kích thước, số lượt tải và thời hạn. Không chuyển tệp qua chuỗi Base64. Báo rõ khi nguồn hết hiệu lực, chỉ truy cập được trong ngữ cảnh Zalo hoặc cần cơ chế tải vốn có của Zalo. Chỉ tải trước với nhóm nội dung cần giữ trong ngân sách đã chốt. |

Sự kiện thu hồi có thể chỉ chứa ID. Nếu ứng dụng đã xóa nội dung và Manager chưa từng nhận tin gốc hoặc cache đã hết hạn, Manager chỉ có thể ghi nhận dấu thu hồi. Kiểm tra giới hạn này ngay trong thử nghiệm endpoint, trước khi quyết định thời hạn giữ và chính sách media.

### Kho SQLite và tệp nhị phân

- **Chọn `winsqlite3` qua P/Invoke.** `IdentityReader.cs` đã dùng đúng thư viện này để mở và truy vấn dữ liệu Zalo ở chế độ chỉ đọc. Thử nghiệm độc lập trên môi trường x64 hiện tại với `winsqlite3` **3.53.4** đã tạo bảng, chèn, cập nhật và commit cơ sở dữ liệu trong RAM. Thêm đường ghi riêng cho kho Manager, tái dùng phần interop phù hợp và giữ cách mở chỉ đọc của `IdentityReader`. Dùng DLL hệ thống nên không cần đóng gói DLL SQLite hay NuGet mới. Kích thước bản build vẫn có thể đổi theo lượng mã mới. Kiểm tra các hàm cần dùng trên bản Windows thấp nhất muốn hỗ trợ, thay vì suy ra một mốc build tối thiểu từ lần thử này. [IdentityReader.cs](src/IdentityReader.cs#L68) · [Microsoft Learn về SQLite hệ thống](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/custom-versions)
- **Chỉ lưu metadata và tham chiếu tệp trong DB.** Mỗi tin có khóa theo hồ sơ, hội thoại và ID tin. Dùng giao dịch ghi nền và truy vấn phân trang. Đánh dấu đã đọc chỉ cập nhật các hàng metadata liên quan. Tệp nhị phân có tên theo SHA-256 dưới `Storage/`. Ghi và xác minh tệp tạm trước khi công bố, chỉ ghi tham chiếu sau khi tệp sẵn sàng. Khi dọn tệp dùng chung, kiểm tra mọi tham chiếu và xử lý tệp mồ côi sau lỗi hoặc thoát giữa chừng.
- **Thay cách dùng DPAPI, giữ bảo vệ dữ liệu.** `winsqlite3` và tên SHA-256 không tự mã hóa nội dung. Giữ mức bảo vệ theo tài khoản Windows cho nội dung nhạy cảm của DB và tệp. Có thể dùng DPAPI cho dữ liệu nhỏ hoặc bọc khóa mã hóa theo người dùng, thay cho việc dùng DPAPI để ghi lại toàn bộ kho. Phần tệp cần cách bảo vệ vẫn đọc/ghi theo luồng. [RecalledMessages.cs](src/RecalledMessages.cs#L243) · [Microsoft Learn về các bản dựng SQLite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/custom-versions)
- **Chuyển kho có thể chạy lại.** Giữ `recalled-messages.dat` làm bản phục hồi trong quá trình nhập. Đối chiếu số tin theo hồ sơ/hội thoại, trạng thái chưa đọc và checksum dữ liệu tệp. Chỉ chuyển kho đang dùng sau khi kiểm tra xong. Thử đầy đĩa, tắt giữa chừng, nhập lại và xác nhận xóa tin không xóa nhầm tệp còn được tham chiếu.

**Dung lượng ngoài tính năng.** Tách số liệu app đang dùng, phiên bản cũ, dữ liệu phiên đăng nhập, bản trước import và staging xuất dữ liệu. Ưu tiên thu hồi staging của chính lượt export sau khi xác minh kết quả. Các bản phục hồi cần giữ theo chính sách người dùng đã chọn. [StorageUsage.cs](src/StorageUsage.cs#L31) · [ProfileActions.cs](src/ProfileActions.cs#L175) · [ProfileActions.cs](src/ProfileActions.cs#L247)

## 4. Số liệu v1 dùng làm đường cơ sở

| Phép thử | Kết quả |
|---|---|
| Giả lập 200 khung tin đứng yên, gọi định kỳ 100 lần | **20.000 lượt kiểm tra khung tin**. Đếm số lần thực hiện, chưa đo độ trễ render Zalo thật. |
| Giả lập 100 màn hình mới sau màn hình đầu | **20.200 ID giữ lại** với 200 khung trên màn hình. |
| Đo payload lệnh CDP từ bản `.exe` hiện có | **28.894 byte/lệnh**, tương đương khoảng **99,2 MiB/giờ/hồ sơ** ở nhịp đủ 1 Hz. Đây là dữ liệu cục bộ chỉ của lệnh quét. Một endpoint đẩy sự kiện bỏ nhịp gửi lệnh này. |
| Tệp 16 MiB đưa vào chuỗi Base64 .NET | Khoảng **42,7 MiB RAM** cho một chuỗi, chưa tính bản sao và bộ đệm. Năm tệp độc lập tương đương khoảng **213 MiB** cho các chuỗi. |
| Kho giả lập có 5 tệp × 16 MiB | Tệp kho **108,34 MiB**. Ở lượt đo, ghi ở mốc năm tệp khoảng **1,3 giây**, đánh dấu cả năm tin đã đọc khoảng **7,7 giây**. Đây là số đo của một lượt thử. |

Bộ kiểm thử CDP và thu hồi media của v1 đã pass trên dữ liệu giả lập. Chưa có số liệu CPU, RAM hay độ trễ gõ/cuộn trên phiên Zalo thật trong đợt điều tra này. [Kiểm thử CDP](tests/Cdp.Tests.ps1) · [Kiểm thử media](tests/RecallMedia.Tests.ps1)

## 5. Thứ tự triển khai và điều kiện nghiệm thu

### Đợt 1 · Giảm tải và xác minh endpoint

1. **Công tắc và vòng tiến trình.** Đưa ba nấc theo hồ sơ vào Manager. Dùng một snapshot trên luồng nền để đối chiếu tất cả hồ sơ. Các phiên do Manager mở được theo dõi bằng PID, thời điểm bắt đầu và sự kiện thoát, có cùng snapshot để nhận phiên mở bên ngoài hoặc thay PID. Bỏ WMI khỏi đường kiểm tra định kỳ. Nếu còn cần dò cổng để nhận phiên cũ, chỉ dò khi có lý do cụ thể, có lần thử tối đa và làm mới khi phiên đổi. Cập nhật từng hồ sơ có thay đổi trên UI.
2. **Thử endpoint song song.** Trên Zalo gốc và ít nhất một clone, quan sát đủ sự kiện gửi, nhận, thu hồi từ luồng ứng dụng. Ghép đúng ID và hồ sơ cả khi mở hội thoại khác, cửa sổ thu nhỏ, reload, restart và reconnect. Kiểm tra cả trường hợp gắn vào Zalo đang chạy. Đo khoảng thời gian chưa gắn được để phân biệt hụt sự kiện với lỗi ghép ID.
3. **Kích hoạt hai chế độ thu hồi sau khi đạt thử nghiệm endpoint.** Không mượn lại bộ quét DOM. Kiểm tra khi `Tắt` không còn phát/nhận tin thu hồi và tác vụ media. `Chỉ văn bản` không chạy nhánh media. `Văn bản và tệp` chuyển nhị phân trong ngân sách. Giữ bộ thu hồi tách khỏi luồng chat khi Manager đóng, chậm hoặc mất kết nối.

### Đợt 2 · Thay kho, đo lại và chốt mặc định

1. **Dùng `winsqlite3` và `Storage/`.** Chuyển thành công kho v1, cập nhật chưa đọc bằng giao dịch nhỏ và chạy thử mất điện/thoát giữa chừng. Phần lưu mới chỉ nhận dữ liệu từ luồng sự kiện đã xác minh. Thử dài trên nhiều hồ sơ để kiểm tra giới hạn hàng chờ, cache và bộ đệm nhị phân.
2. **Đo trên máy thật.** So Zalo độc lập, v1 và ba chế độ v2 trên cùng máy, cùng phiên bản và số hồ sơ. Khi rảnh, đo CPU tăng thêm và xác minh không có lệnh quét hoặc tải/ghi mới từ luồng thu hồi. Khi hoạt động, ghi CPU, private bytes, I/O, p95 độ trễ gõ/cuộn, độ trễ nhận sự kiện, tỷ lệ phát hiện thu hồi, mục trùng, nhầm hồ sơ và độ bao phủ media. Dùng kết quả trên máy yếu để chốt ngưỡng và chế độ mặc định.

Các quyết định còn mở gồm điểm đặt bộ phát trên từng bản Zalo, khả năng nạp qua CDP trên gốc và clone, kênh vận chuyển cuối cùng, ngân sách cache và chế độ mặc định khi nâng cấp. Kết quả thử `winsqlite3` mới xác nhận đường ghi trên môi trường x64 hiện tại. Điểm phát sự kiện Zalo, chuyển kho và hiệu năng trên máy thật vẫn cần thử nghiệm.

