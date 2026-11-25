# Kế hoạch sửa lỗi và tối ưu Zalo Manager V2

Kế hoạch này tổng hợp đợt sửa sau khi đối chiếu phản biện của Antigravity với mã nguồn. Trước khi bắt đầu, ghi lại commit và diff làm mốc so sánh vì phần triển khai hiện có thay đổi chưa commit. Giữ CDP binding làm kênh sự kiện nội bộ. Tiếp tục giữ lịch sử hiện có và công tắc theo dõi từng hồ sơ. [Kế hoạch endpoint](IMPLEMENTATION_PLAN_ENDPOINT_THU_HOI.md)

## Đợt 1 · Sửa lỗi chức năng đã xác nhận

### 1. P1 · Ghép tin nhóm và khử trùng sự kiện theo ID

- **Vấn đề.** Đã tái hiện bằng fixture từ hook hiện tại. Cùng một tin có thể đi qua PM với khóa hội thoại là ID người gửi, rồi qua CDM với ID nhóm. Undo dạng object mới, thiếu `convId`, có thể khớp hai bản cache và bị bỏ qua. Trên Zalo thật cũng đã thấy một tin gửi có hai ID nhưng cùng `cliMsgId). Quan hệ giữa hai ID này cần được xác minh trước khi hợp nhất.
- **Giải pháp.** Đối chiếu payload trên Zalo gốc và clone. Ghi nguồn và mức tin cậy của ID hội thoại. Nếu CDM hoặc payload cung cấp ID nhóm đáng tin hơn, cập nhật bản tin đã nhận cùng chỉ mục liên quan. Dùng ID đầy đủ và bí danh đã xác minh trong phạm vi hồ sơ. Bỏ phụ thuộc vào việc hai callback dùng chung một object. Nếu một ID hoặc bí danh vẫn khớp nhiều tin thực sự khác nhau, giữ kết quả chưa xác định và ghi bộ đếm chẩn đoán.
- **Nghiệm thu.** Thử cả PM đến trước và CDM đến trước, hai object khác nhau, undo thiếu `convId`, tin gốc đến muộn, hai hồ sơ và hai hội thoại cùng tên. Tin nhóm tạo đúng một bản thu hồi ở phía nhận, đúng hội thoại và đúng nội dung. Tin do tài khoản gửi tự thu hồi không tạo bản ghi ở phía gửi. Hành vi xóa phía mình vẫn được kiểm tra riêng.
- **Đầu ra.** Quy tắc xác định ID hội thoại, bộ ghép và khử trùng, fixture đã ẩn danh và kết quả kiểm thử trên Zalo thật. [Hook](src/CdpVisibleMessages.cs#L650) · [Tra cache](src/IncomingMessageCache.cs#L268)

### 2. P1 · Giải phóng media sau khi người dùng xóa bản ghi

- **Vấn đề.** Xóa hội thoại hoặc xóa tất cả hiện chỉ lưu việc xóa bản ghi. Quét media ở chế độ báo cáo nên tệp vẫn chiếm đĩa.
- **Giải pháp.** Sau khi ghi thành công thao tác xóa, lấy danh sách media từ đúng những bản ghi vừa xóa. Chụp danh sách tham chiếu dưới khóa rồi quét ứng viên ngoài khóa. Trước khi xóa từng tệp, kiểm tra lại tham chiếu trong kho thu hồi, cache tin đến, media đang chờ ghi và tệp đang tải hoặc công bố. Phối hợp bước cuối với luồng đăng ký media. Chuẩn hóa đường dẫn và chỉ dọn tệp thuộc kho Manager. Khi kho lỗi hoặc tệp đang được mở, giữ tệp để dọn lại sau.
- **Phạm vi.** Cho phép dọn media gắn với thao tác xóa của người dùng sau khi các ca an toàn đạt. Quét định kỳ vẫn chỉ báo cáo. Tuổi tệp chỉ dùng để chọn ứng viên dọn định kỳ. Chỉ bật xóa định kỳ sau khi kiểm chứng đồng thời và phục hồi sau crash.
- **Nghiệm thu.** Xóa mục dùng chung giữ nguyên tệp còn tham chiếu. Xóa tham chiếu cuối giải phóng đúng tệp. Ghi lỗi giữ nguyên bản ghi và media. Thử cả tệp đang tải, đang công bố, đang mở và kho không đọc được. Bảng dung lượng đo lại và báo số byte thực sự giải phóng.
- **Đầu ra.** Luồng dọn trong thao tác xóa, kết quả dọn trên UI, test tệp dùng chung, tranh chấp lúc ghi, lỗi kho và kiểm tra cập nhật dung lượng. [Xóa bản ghi](src/RecalledMessages.cs#L329) · [Quét media](src/RecallMediaStorage.cs#L144) · [Dung lượng](src/StorageUsage.cs#L99)

### 3. P2 · Cập nhật thumbnail đến muộn

- **Giải pháp.** Dùng chung phép so nội dung hiển thị cho cả nhánh làm mới danh sách và nhánh nối tin mới. So sánh cả `PreviewData`, `PreviewMime` và các trường đang ảnh hưởng tới ô tin.
- **Nghiệm thu.** Mở cửa sổ thu hồi khi video chưa có thumbnail. Khi chỉ thumbnail thay đổi, ô tin cập nhật và giữ vị trí cuộn. Thử thêm trường hợp thumbnail đổi cùng lúc có tin mới.
- **Đầu ra.** Phép so dùng chung và ca kiểm thử thumbnail đến muộn. [Làm mới danh sách](src/RecallConversationWindow.cs#L344)

## Đợt 2 · Giảm chi phí chạy nhiều hồ sơ

### 4. P2 · Tách avatar và chia quota cache công bằng

- **Giải pháp.** Lưu avatar một lần theo hồ sơ và ID hội thoại. Hook gửi lại avatar khi có dữ liệu mới hoặc thay đổi. Bản tin tham chiếu avatar thay vì nhúng chuỗi Base64. Khi đọc kho cũ, gom avatar theo ID đáng tin và giữ khả năng mở bản ghi cũ. Đo lại byte trước khi chốt quota. Chia mức giữ tối thiểu cho từng hồ sơ trong trần ứng dụng, cho mượn phần dư và thu hồi phần đã mượn trước. Tính riêng các bản ghi đang chờ ghi để việc giới hạn cache không làm mất đường thử lại.
- **Nghiệm thu.** Mẫu 1.000 tin chung một avatar chỉ lưu một bản avatar theo phạm vi. Một hồ sơ tạo tải cao không lấy mất mức giữ đã dành cho hồ sơ khác. Báo số tin hết hạn và số tin bị loại sớm theo hồ sơ. Trần tổng có hiệu lực khi tăng số hồ sơ.
- **Đầu ra.** Lưu avatar tách riêng, tương thích kho cũ, chính sách quota và bộ đếm theo hồ sơ. So sánh RAM, kích thước snapshot và số tin bị loại trước 24 giờ trước/sau. [Tính dung lượng cache](src/IncomingMessageCache.cs#L92) · [Giới hạn cache](src/IncomingMessageCache.cs#L114)

### 5. P2 · Gom dò tiến trình và WMI ra khỏi UI

- **Giải pháp.** Quét tiến trình liên quan của Zalo một lần mỗi lượt ở nền, map kết quả theo đường dẫn hồ sơ và chỉ cập nhật trạng thái trên UI. Giữ tập tiến trình hỗ trợ đang được theo dõi. Với WMI, chia sẻ kết quả ngắn hạn và cả truy vấn đang chạy để nhiều hồ sơ reconnect đồng thời dùng chung một lượt. Vẫn xác minh target CDP theo hồ sơ khi kết nối.
- **Nghiệm thu.** Mở, dừng và reconnect đồng thời nhiều hồ sơ. Kết quả không gán nhầm cổng hoặc trạng thái. Một đợt reconnect chia sẻ truy vấn WMI. UI không chờ quét tiến trình. Đo số lần đọc đường dẫn tiến trình và thời gian UI bận trước/sau.
- **Đầu ra.** Bộ dò tiến trình dùng chung, cơ chế dùng chung truy vấn WMI, test vòng đời và kết quả đo lúc rảnh. [Dò tiến trình](src/Manager.cs#L365) · [Timer UI](src/Manager.cs#L487) · [WMI](src/CdpMessageWatcher.cs#L240)

### 6. P2 · Tách timeout và cấp lượt tải công bằng

- **Giải pháp.** Tách thời hạn chờ kết nối/phản hồi, thời hạn không nhận được byte mới khi đang đọc mạng và thời hạn tổng. Dùng 8–10 giây không có byte và 60–90 giây tổng làm mốc thử ban đầu. Phân biệt mạng đứng, hết thời hạn tổng, hủy và lỗi đĩa. Dùng hàng đợi theo hồ sơ ở trước bộ cấp slot. Điểm bắt đầu để đo là bốn lượt toàn Manager, hai lượt mỗi hồ sơ và tối đa ba lượt tải lớn, dành chỗ cho tệp nhỏ. Tệp chưa biết kích thước dùng nhóm thông thường cho đến khi có thông tin. Giữ giới hạn kích thước hiện tại trong đợt sửa.
- **Nghiệm thu.** Tệp lớn truyền đều qua mốc 8 giây vẫn hoàn tất. Nguồn đứng bị hủy đúng loại lỗi. Ghi đĩa chậm không bị báo là mạng đứng. Tệp nhỏ từ hồ sơ khác có lượt khi đang tải nhiều video, video vẫn tiến triển và hủy giữa chừng giải phóng slot.
- **Đầu ra.** Chính sách timeout, bộ cấp slot, trạng thái lỗi có thể phân biệt và test mạng chậm, mạng đứng, hủy tải, tải trộn nhiều hồ sơ. [Slot](src/RecallMediaDownload.cs#L17) · [Timeout](src/RecallMediaDownload.cs#L309)

### 7. P2 · Rút ngắn thời gian giữ khóa khi ghi media và quét kho

- **Giải pháp.** Đưa giải mã Base64 và ghi tệp tạm ra ngoài khóa cache. Giữ phần công bố tệp và cập nhật tham chiếu ngắn, đồng bộ với bộ dọn. Giữ đúng trình tự ghi, công bố và phục hồi khi việc lưu metadata thất bại. Đo riêng thời gian chờ khóa, serialize, DPAPI và `Flush(true)` trước khi quyết định đổi định dạng kho.
- **Nghiệm thu.** Báo cáo quét kho lớn không giữ khóa cache suốt lượt duyệt thư mục. Tải, quét và xóa đồng thời không làm rơi tham chiếu. Lỗi ghi và khởi động lại vẫn đọc được dữ liệu đã lưu.
- **Đầu ra.** Luồng ghi tạm và công bố tệp, số liệu giữ/chờ khóa và test va chạm với bộ dọn. [Giữ khóa cache](src/IncomingMessageCache.cs#L254) · [Công bố media](src/CdpVisibleMessages.cs#L1012)

## Đợt 3 · Đo tải và quyết định có đổi kho hay không

1. Chạy cùng bộ dữ liệu trên bản đối chiếu và bản sửa ở **2, 5 và 10 hồ sơ**. Cho một hồ sơ tạo tải cao và các hồ sơ còn lại gửi đều. Bài thử dài cần có cả thời gian rảnh, tin nhóm, tải media và reconnect. Dùng tải giả cho quy mô chưa có đủ tài khoản thật và ghi rõ giới hạn của phép đo.
2. Báo tỷ lệ ghép đúng nội dung thu hồi, số sự kiện mất/trùng, độ trễ p95, số tin bị loại trước 24 giờ, thời gian chờ slot theo loại tệp, RAM đỉnh và RAM khi chạy lâu, I/O ghi, thời gian chờ khóa và số lượt dò tiến trình/WMI.
3. Nếu snapshot vẫn chi phối I/O sau khi tách avatar và rút ngắn khóa, cân nhắc tách `.dat` theo hồ sơ như bước trung gian hoặc chuyển metadata sang SQLite có giao dịch. Giữ bảo vệ dữ liệu nhạy cảm và kiểm thử nhập dữ liệu cũ, ghi lỗi, crash và quay lui. Chọn phương án dựa trên số liệu của bài thử. [Thiết kế kho v2](MANAGER_V2_PERFORMANCE_PLAN.md#64)

## Bàn giao và điều kiện hoàn tất

- **Thứ tự.** Sửa ghép tin nhóm, xóa media an toàn và thumbnail trước. Tiếp theo tách avatar, gom quét tiến trình, sửa timeout/cấp slot và thu hẹp khóa. Chốt phương án kho sau benchmark.
- **Bản bàn giao.** Diff hoặc commit theo đợt, tên file build, cách chạy lại test, báo cáo trước/sau và danh sách ca còn chưa xác minh. Dùng dữ liệu thử hoặc fixture đã ẩn danh.
- **Nghiệm thu tính năng.** Trên Zalo gốc và ít nhất một clone, thử cả hai chiều gửi/nhận với văn bản và tệp đính kèm, thu hồi, xóa phía mình, chat nhóm, reload/reconnect và hai hồ sơ chạy cùng lúc. Đối chiếu nội dung và byte tệp trong kho phía nhận.
- **Điều kiện phát hành.** Các ca P1 đạt, lịch sử cũ mở được, dung lượng giải phóng khớp thao tác xóa và số liệu chịu tải được ghi lại. Xóa tự động có mốc nghiệm thu riêng sau khi đã kiểm chứng an toàn khi ghi đồng thời và khởi động lại sau crash.

## Kết quả triển khai và đo cục bộ

- Bản bàn giao dùng tên chuẩn `ZaloManager.exe`. Số liệu benchmark bên dưới dùng bản đối chiếu được build từ cùng workspace ngay trước đợt sửa V2.
- Đã sửa ghép callback nhóm, dọn media liên quan khi người dùng xóa bản ghi, thumbnail đến muộn, lưu avatar riêng, chia quota theo áp lực dung lượng, quét tiến trình ở nền, chia sẻ WMI và giới hạn tải theo hồ sơ. Quét media định kỳ vẫn chỉ báo cáo.
- Khi đổi định dạng cache, lần ghi đầu giữ `incoming-cache.dat.v1.bak` nếu chưa có bản sao cũ. Kho tin đã thu hồi giữ định dạng hiện hành. Nếu cần quay lại build cũ, đóng Manager, giữ bản V2 riêng và khôi phục bản cache dự phòng. Phần cache ghi thêm sau lúc nâng cấp nằm trong bản V2.

### Benchmark cache bằng dữ liệu giả

Mỗi lượt dùng 1.800 tin, 70% vào một hồ sơ. Tất cả tin có avatar Base64 lặp dài 6.000 ký tự. Lưu cache mỗi 150 tin và đo p95 của 12 lượt lưu. Đây là bài đo snapshot cục bộ với dữ liệu avatar lặp, dùng để đánh giá thay đổi trong kho cache. Bài đo không có lưu lượng CDP, video hay UI chạy lâu.

| Hồ sơ | Snapshot trước → sau | p95 lưu trước → sau | Working set đỉnh trước → sau |
|---|---:|---:|---:|
| 2 | 10,71 → 0,50 MiB | 514 → 56 ms | 262,9 → 121,5 MiB |
| 5 | 10,71 → 0,54 MiB | 545 → 52 ms | 263,7 → 122,2 MiB |
| 10 | 10,71 → 0,78 MiB | 533 → 68 ms | 263,0 → 124,4 MiB |

Chạy lại bằng `tests/RecallCache.Benchmark.ps1 -Executable <bản-build> -Profiles <2|5|10>`. Với kết quả cục bộ hiện có, giữ kho snapshot để tiếp tục nghiệm thu. Quyết định đổi kho cần số liệu tải dài hạn.

**Còn chờ nghiệm thu.** Reconnect đồng thời và tải dài hạn vẫn cần đo trên nhiều hồ sơ. Trong ca thử thực tế, một tin gửi ở Zalo gốc có hai ID cùng `cliMsgId`. Cần xác minh quy tắc hợp nhất các ID đó. Phần phát hiện trùng scope dùng ID tin khớp trực tiếp và kiểm tra bí danh không mâu thuẫn.

### Thời hạn lưu tin nhóm

Manager giữ tin được xác định là tin nhóm trong tối đa 5 giờ tính từ lúc gửi nếu Zalo cung cấp thời gian hợp lệ. Nếu thiếu thời gian gửi, dùng lúc Manager bắt được tin. Thu hồi dùng cùng mốc, kể cả khi nội dung đã chuyển từ cache sang kho thu hồi. Lượt dọn chạy khi mở Manager và mỗi phút khi Manager đang hoạt động. Tệp đính kèm chỉ được xóa khi không còn tham chiếu. Giữ nguyên các bản ghi cũ chưa xác định được là tin nhóm và chính sách lưu của chat cá nhân.

### Kết quả thử trên nhóm thật với bản lưu 5 giờ

- **Văn bản đạt.** Gửi và thu hồi từ Zalo gốc. Tin xuất hiện ở cả hai cửa sổ trước khi thu hồi. Clone lưu đúng một bản thu hồi có nội dung với hạn 5 giờ. Kho thu hồi của Zalo gốc không có bản ghi cho tin thử.
- **ZIP đạt sau khi sửa chuyển hướng.** Ca ban đầu chỉ lưu tên tệp. Ca thử lại trên hai tài khoản xác nhận hai bước chuyển hướng HTTPS của đúng nguồn tải. Với quy tắc giới hạn ở hai bước đã xác minh, kho thu hồi của clone lưu đúng một bản ZIP 175 byte có SHA-256 khớp bản gửi. Kho thu hồi của Zalo gốc không có bản ghi cho tin thử.
- **Mốc 5 giờ.** Đã đối chiếu hạn lưu ghi trong kho cho cả hai ca. Kiểm thử tự động kiểm tra ranh giới hết hạn. Ca trên nhóm thật chưa chờ đủ 5 giờ để quan sát lượt dọn thực tế.

### Dọn media theo hạn và dung lượng cache

Cache ghi danh sách tệp chờ dọn cùng snapshot khi loại tin hết hạn hoặc khi vượt hạn mức. Mỗi lô xử lý tối đa 32 đường dẫn trong một lần giữ khóa và kiểm tra lại tham chiếu của cả cache lẫn kho thu hồi. Lượt quét chung vẫn lập báo cáo cho file mồ côi cũ. Kiểm thử tự động đã xác nhận dọn qua nhiều lô, giữ file khi ghi kho thất bại, thử lại file đang bị khóa và dọn media của tin chưa hết hạn bị đẩy khỏi cache đầy.
