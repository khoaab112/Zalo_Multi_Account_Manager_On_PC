# Phân tích, Chẩn đoán và Phản biện Bản Triển khai V2

Tài liệu này tổng hợp kết quả rà soát mã nguồn và kiểm thử đối với đợt triển khai theo [fix_plan_V2.md](fix_plan_V2.md) (so với mốc commit `e0db755` và các thay đổi chưa commit hiện có trong workspace).

**Trạng thái kiểm thử đã xác nhận tại thời điểm rà soát:**
- `build.ps1`: Biên dịch thành công `ZaloManager.exe` (`1.144.832` bytes).
- `tests/RecallMedia.Tests.ps1`: **PASS** toàn bộ ca test media (ảnh, sticker, file, thoại, video, no-source, idle timeout, redirect validation).
- `tests/IncomingCache.Tests.ps1`: **PASS** toàn bộ ca test cache, ghép nhóm, mốc 5 giờ, xóa bản ghi, chia sẻ slot tải, avatar snapshot và thumbnail đến muộn.
- `tests/Cdp.Tests.ps1`: **PASS** toàn bộ ca test hook readiness, vòng đời CDP và URL Chromium.

---

## 1. Những hạng mục V2 đã xử lý tốt

1. **Tách `ConversationAvatar` khỏi từng tin nhắn:** Giảm mạnh kích thước snapshot cache và chi phí serialize/DPAPI trong bài đo cục bộ ([IncomingMessageCache.cs](src/IncomingMessageCache.cs#L63-L70), [IncomingMessageCache.cs](src/IncomingMessageCache.cs#L161-L190)).
2. **Chính sách loại tin theo áp lực từng hồ sơ (`PruneBounds`):** Ngăn một hồ sơ nhiều tin đẩy hết tin của các hồ sơ khác khi chạm ngưỡng `20.000` tin hoặc `64 MiB` ([IncomingMessageCache.cs](src/IncomingMessageCache.cs#L202-L234)).
3. **Ghép nhóm & xử lý tin đến muộn (`ProvisionalConversation`, `PendingUndo`):** Hỗ trợ cả hai thứ tự callback `pm.onReceiveNewMessage` và `cdm.onReceiveNewMessages` ([CdpVisibleMessages.cs](src/CdpVisibleMessages.cs#L459-L475), [IncomingMessageCache.cs](src/IncomingMessageCache.cs#L442-L485)).
4. **Streaming media & tách timeout tải:** Chuyển sang `StageMedia` ghi file tạm 64 KiB + idle-timeout `10s` + trần tổng `90s` + chia slot công bằng theo hồ sơ `RecallDownloadSlots` ([RecallMediaDownload.cs](src/RecallMediaDownload.cs#L22-L95), [RecallMediaDownload.cs](src/RecallMediaDownload.cs#L383-L545)).
5. **Giải phóng media khi người dùng xóa bản ghi thu hồi:** `DeleteRecalls` thu thập danh sách ứng viên sau khi ghi kho thành công và gọi `DeleteUnreferencedMedia` để xóa file không còn tham chiếu ([RecalledMessages.cs](src/RecalledMessages.cs#L330-L372), [RecallMediaStorage.cs](src/RecallMediaStorage.cs#L215-L265)).
6. **Gom quét tiến trình nền & chia sẻ WMI:** Dùng `ScanProfileProcesses` chạy nền 1 lần/lượt và cache `FindPort` WMI `1,5s` ([Manager.cs](src/Manager.cs#L408-L428), [CdpMessageWatcher.cs](src/CdpMessageWatcher.cs#L240-L275)).
7. **UI thu hồi:** Cập nhật `SameRecallBubble` bao gồm `PreviewData`/`PreviewMime` và phân trang `60` tin/trang ([RecallConversationWindow.cs](src/RecallConversationWindow.cs#L334-L359), [RecallConversationWindow.cs](src/RecallConversationWindow.cs#L522-L532)).

---

## 2. Các lỗi, bug ngầm và hồi quy cần sửa ngay

### Lỗi 1 (Nghiêm trọng): `FileSystemWatcher` giữ khóa thư mục làm hỏng **Xóa hồ sơ** và **Import data**
- **Vị trí:** [StorageUsage.cs](src/StorageUsage.cs#L142-L173), [StorageUsage.cs](src/StorageUsage.cs#L266-L275) và [ProfileActions.cs](src/ProfileActions.cs#L44), [ProfileActions.cs](src/ProfileActions.cs#L418).
- **Nguyên nhân:** `StorageWatch` tạo `new FileSystemWatcher(profile.DataPath) { IncludeSubdirectories = true }`. Trên Windows, `FileSystemWatcher` mở một directory handle thường trực vào `profile.DataPath`.
- **Tác động:** Khi người dùng thực hiện **Xóa hồ sơ** (`ArchiveDeleted` gọi `Directory.Move(profile.DataPath, ...)`) hoặc **Import data** (`Directory.Move`), Windows sẽ từ chối thao tác di chuyển thư mục với lỗi `IOException` / `Access denied` vì handle vẫn đang mở trong `ManagerWindow`.

### Lỗi 2 (Nghiêm trọng về tài nguyên): Vòng lặp quét đĩa vô tận & giật UI mỗi 4 giây
- **Vị trí:** [Manager.cs](src/Manager.cs#L487), [Manager.cs](src/Manager.cs#L792) và [StorageUsage.cs](src/StorageUsage.cs#L223-L304).
- **Cơ chế gây lỗi:**
  1. Zalo (Electron/Chromium) ghi cache, cookie, LevelDB/IndexedDB vào `profile.DataPath` liên tục trong lúc hoạt động.
  2. Mỗi lần ghi khiến `StorageWatch.Revision` tăng.
  3. Cứ mỗi **4 giây** (`Manager.timer`), `RefreshStorageUsage()` phát hiện `revision != state.MeasuredRevision` và kích hoạt `StorageUsage.MeasureData` — duyệt đệ quy toàn bộ hàng nghìn file trong `profile.DataPath` bằng `GetCompressedFileSizeW`.
  4. Trong vòng lặp `foreach (var scan in pending)`, sau **mỗi hồ sơ** đo xong, code gọi `grid.Items.Refresh()` trên UI thread ([StorageUsage.cs](src/StorageUsage.cs#L294)). Với `10–20` hồ sơ đang chạy, Manager liên tục cày I/O đọc đĩa và vẽ lại DataGrid `10–20` lần mỗi chu kỳ `4s`.
  5. Tương tự, `recallStorageWatch` theo dõi cả `incoming-cache.dat` ([StorageUsage.cs](src/StorageUsage.cs#L86-L95)); vì cache ghi mỗi `500ms` khi có tin nhắn, `MeasureRecallStore()` liên tục bị kích hoạt để quét lại cả thư mục `media/`.

### Lỗi 3 (Rò rỉ lưu trữ dài hạn): Media của chat 1-1 hết hạn 24h vẫn không được xóa khỏi đĩa
- **Vị trí:** [IncomingMessageCache.cs](src/IncomingMessageCache.cs#L610-L646) và [RecallMediaStorage.cs](src/RecallMediaStorage.cs#L167-L189).
- **Nguyên nhân:** Trong `PruneExpired()`:
  - Chỉ khi `message.GroupExpires.HasValue` (chat nhóm 5 giờ), đường dẫn media mới được đưa vào `ExpiredGroupMedia` để `DeleteUnreferencedMedia` xóa thật trên đĩa.
  - Khi tin nhắn **chat cá nhân (1-1)** hết hạn sau `24 giờ` (`pruneDirect && pair.Value.Timestamp < cutoff`), media không được đưa vào danh sách xóa thật; trong khi `ScheduleRecallMediaScan()` mỗi giờ chỉ chạy `ScanAndReport()` ở chế độ **chỉ báo cáo**.
- **Tác động:** Ảnh, video, audio, file từ mọi cuộc chat 1-1 không bị thu hồi vẫn tích tụ mãi mãi trong `Store.Root\media`.

### Lỗi 4 (Sai lệch cấp slot): Ảnh và nhiều tin thoại mất slot ưu tiên `small`
- **Vị trí:** [RecallMediaDownload.cs](src/RecallMediaDownload.cs#L83), [RecallMediaDownload.cs](src/RecallMediaDownload.cs#L120) và [CdpVisibleMessages.cs](src/CdpVisibleMessages.cs#L558-L561).
- **Nguyên nhân:** `RecallDownloadSlots.Grant()` chừa slot thứ 4 cho các lượt có `Small == true` (`request.Small || large < 3`). Tuy nhiên `small` được tính bằng `expectedSize.HasValue && expectedSize.Value > 0 && expectedSize.Value < 2 * 1024 * 1024`.
- **Tác động:** Nhánh `isImage` trong JS hook không trích xuất `fileSize`, và nhiều tin thoại `isVoice` cũng thiếu `fileSize` (`expectedSize == null`). Vì thế hầu hết ảnh và voice bị đánh dấu `small = false` (coi như tệp lớn), bị kẹt sau ngưỡng `large < 3` khi có 3 video đang tải.

### Lỗi 5 (Đã ghi nhận ở ca thử thật): Chặn chuyển hướng `redirect-blocked` khi tải tệp ZIP
- **Vị trí:** [RecallMediaDownload.cs](src/RecallMediaDownload.cs#L241-L266) và [fix_plan_V2.md](fix_plan_V2.md#L94).
- **Nguyên nhân:** `IsRecallMediaUri` chỉ cho phép hai mẫu hostname đặc thù ngoài các domain chính là `file-stal-[0-9]+.dlfl.vn` và `video-stal-[0-9]+.dlmd.me`. Nếu CDN Zalo điều hướng tệp ZIP sang một edge node/subdomain khác, `RecallRedirectTarget` ném lỗi `redirect-blocked` và làm mất nội dung tệp.

---

## 3. Phân tích cổ chai, vòng lặp dư thừa & rủi ro khi chạy x2, x3, xN tài khoản

| Khía cạnh | Hiện trạng trong code V2 | Rủi ro ở quy mô x5–x20 tài khoản / chạy lâu |
| :--- | :--- | :--- |
| **Ghi snapshot chung dưới khóa toàn cục** | `IncomingMessageCache.Save()` ([IncomingMessageCache.cs](src/IncomingMessageCache.cs#L365-L401)) và `SaveRecallArchive()` ([RecalledMessages.cs](src/RecalledMessages.cs#L647-L678)) serialize toàn bộ kho chung của tất cả tài khoản, gọi `stream.ToArray()` (cấp phát LOH gấp đôi), mã hóa DPAPI đồng bộ và `Flush(true)` dưới `CacheGate` / `RecallGate`. | Benchmark `1.800` tin (`0,78 MiB`) chưa chạm tải thật. Khi cache tiến tới `20.000` tin / `64 MiB` hoặc kho thu hồi phình to qua nhiều tháng, mỗi đợt ghi `500ms` giữ khóa lâu, chặn dây chuyền toàn bộ worker của N tài khoản. |
| **`PruneBounds()` chạy trên mỗi sự kiện khi đầy** | `StoreCaptured`, `UpdateAttachment`, `UpdateAvatar` đều gọi `PruneBounds()` ([IncomingMessageCache.cs](src/IncomingMessageCache.cs#L202-L234)). | Khi cache chạm trần `20.000` tin, mỗi tin nhắn mới đến đều kích hoạt `GroupBy` + `OrderBy` toàn bộ cache ngay trong `lock (CacheGate)`. |
| **Giữ cả 3 khóa khi xóa file trên đĩa** | `DeleteUnreferencedMedia()` ([RecallMediaStorage.cs](src/RecallMediaStorage.cs#L220-L265)) giữ `RecallMediaGate` $\rightarrow$ `RecallGate` $\rightarrow$ `CacheGate` trong suốt lúc `File.Exists`, `File.Delete`, `GetFileStorageBytes`. | Khi hàng trăm tin nhóm đồng loạt chạm mốc hết hạn 5 giờ trong `RunRetention()` (chạy mỗi `60s`), toàn bộ luồng ghi/đọc cache và thu hồi của N tài khoản bị dừng chờ I/O xóa đĩa. |
| **Đo `FileInfo.Length` trên UI thread** | `RecallConversationWindow` gọi `GetRecallStorageInfo` mỗi khi `RecallRevision` tăng ([RecallConversationWindow.cs](src/RecallConversationWindow.cs#L327), [RecalledMessages.cs](src/RecalledMessages.cs#L143-L175)). | Khi một hồ sơ tích lũy hàng nghìn file media theo thời gian, mỗi tin thu hồi mới sẽ kích hoạt vòng lặp `FileInfo.Length` ngay trên UI thread dưới `RecallGate`. |
| **Quét cửa sổ & rò rỉ handle map mỗi 4s** | `ApplyProcessStatus` gọi `ProfileActions.ApplyAvatar` $\rightarrow$ `WindowActivation.Find` (`EnumWindows`) cho từng hồ sơ mỗi `4s`; `AppliedIcons` không xóa `HWND` đã đóng ([Manager.cs](src/Manager.cs#L781), [ProfileActions.cs](src/ProfileActions.cs#L149-L171)). | Tốn CPU lặp lại trên UI thread khi chạy nhiều hồ sơ và tích tụ entry `HWND` cũ nếu đóng/mở Zalo nhiều lần trong phiên dài. |
| **Nghiệp vụ 5 giờ với tin nhóm đã thu hồi** | `PruneGroupRecalls()` ([RecalledMessages.cs](src/RecalledMessages.cs#L374-L412)) xóa cả bản ghi **đã lưu trong kho thu hồi** sau `5 giờ` kể từ lúc gửi. | Nếu người dùng không mở Manager trong vòng 5 giờ (qua đêm hoặc cuối tuần), tin thu hồi trong nhóm sẽ tự biến mất khỏi kho trước khi người dùng kịp xem. |
| **Hợp nhất ID tin gửi ở Zalo gốc** | `MessageScopes` chỉ đánh chỉ mục theo `msg.Id` ([IncomingMessageCache.cs](src/IncomingMessageCache.cs#L137-L142), [fix_plan_V2.md](fix_plan_V2.md#L85)). | Một tin gửi trên Zalo gốc có 2 ID khác nhau cùng `cliMsgId` vẫn tạo 2 mục trong cache (tiêu tốn dung lượng cache nếu chat nhiều). |

---

## 4. Đề xuất sửa chữa theo thứ tự ưu tiên

### Đợt A — Sửa lỗi chức năng & chặn vòng lặp phá hiệu năng (Làm ngay)
1. **Gỡ `FileSystemWatcher` khỏi `profile.DataPath`:**
   - Bỏ `StorageWatch` trên `profile.DataPath` để tránh giữ handle khóa thư mục khi Xóa/Import hồ sơ và chấm dứt bão quét đĩa mỗi `4s`.
   - Đo lại dung lượng profile theo chu kỳ `2 phút/lần` (hoặc kích hoạt đo ngay sau Import/Delete/Clean), chỉ gọi `grid.Items.Refresh()` **1 lần** sau khi hoàn tất loạt đo.
   - Với `recallStorageWatch`, loại bỏ `incoming-cache.dat*` khỏi `IsRecallStorageChange` (chỉ theo dõi `media/`, `recalled-messages.dat` hoặc đo lại khi `RecallRevision` đổi).
2. **Sửa phân loại `small` cho ảnh/sticker/voice & mở rộng kiểm tra redirect CDN:**
   - Trong `QueueRecallDownload`, mặc định coi `kind` thuộc `image`, `sticker`, `audio` (khi chưa có `expectedSize`) là `small = true`, hoặc chuyển sang kiểm tra `Content-Length` sau khi nhận header.
   - Ghi nhận domain bị `redirect-blocked` khi tải ZIP thật (đã ẩn token/path) để bổ sung đúng quy tắc host CDN chính thức của Zalo vào `IsRecallMediaUri`.
3. **Thu dọn media của tin nhắn 1-1 hết hạn 24h & nhả khóa trước khi `File.Delete`:**
   - Trong `PruneExpired()`, thu thập cả đường dẫn media của tin 1-1 hết hạn 24h (khi không còn tham chiếu) thay vì chỉ xử lý nhánh `GroupExpires.HasValue`.
   - Trong `DeleteUnreferencedMedia()`, chỉ giữ khóa để lọc danh sách ứng viên cần xóa (đồng thời đánh dấu vào tập đang xử lý), **nhả toàn bộ 3 khóa**, rồi mới chạy `File.Delete` ở ngoài lock.

### Đợt B — Tối ưu dài hạn cho x5–x20 tài khoản & giao diện
4. **Phân mảnh kho theo từng hồ sơ (`incoming-cache-{id}.dat` & `recalled-messages-{id}.dat`):**
   - Mỗi hồ sơ có khóa riêng, hạn mức riêng và file snapshot riêng; triệt tiêu hoàn toàn lock contention giữa các tài khoản và giảm kích thước mỗi lần DPAPI/fsync.
5. **Tối ưu UI và bộ đếm nền:**
   - Lưu/cache sẵn `MediaBytes` hoặc tính `GetRecallStorageInfo` trên luồng nền thay vì gọi `FileInfo.Length` trên UI thread trong `RecallConversationWindow`.
   - Trong `ProfileActions.ApplyAvatar`, chỉ quét `EnumWindows` khi danh sách PID đang chạy của hồ sơ thay đổi, và dọn các `HWND` không còn hợp lệ khỏi `AppliedIcons`.
6. **Rà lại chính sách 5 giờ cho kho thu hồi nhóm:**
   - Xem xét tách riêng thời hạn giữ **cache tạm** (5 giờ) với thời hạn giữ **bản ghi đã thu hồi** (giữ lâu hơn hoặc cho người dùng chọn), tránh mất tin thu hồi khi người dùng kiểm tra muộn.
