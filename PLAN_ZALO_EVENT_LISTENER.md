# Kế hoạch Triển khai Lắng nghe Sự kiện Tin nhắn Zalo PC cho ZaloManager

Tài liệu này hướng dẫn chi tiết hai phương án kỹ thuật để tích hợp tính năng **bắt tín hiệu gửi/nhận tin nhắn Zalo thời gian thực** trực tiếp trong ứng dụng **ZaloManager** (.NET / C#).

---

## 1. So sánh Tổng quan Hai Phương án

| Tiêu chí | Cách 1: Remote Debugging Port (CDP) | Cách 2: ASAR Hooking & Named Pipe IPC |
| :--- | :--- | :--- |
| **Cơ chế can thiệp** | Bật cờ `--remote-debugging-port` khi start process | Sửa code trong `app.asar` để hook WebSocket/IPC |
| **Độ bền khi Zalo Update** | **Rất cao** (Cờ tiêu chuẩn Chromium không bị ảnh hưởng) | **Trung bình** (Cần re-patch khi Zalo cập nhật) |
| **Độ an toàn / Antivirus** | **Rất an toàn** (Tính năng chính thức của Chromium) | Cần cẩn trọng khi chỉnh sửa binary của app |
| **Tốc độ & Độ trễ** | Rất thấp (Vài mili-giây qua WebSocket cục bộ) | Cực thấp (Gần 0 ms qua Win32 Named Pipe) |
| **Mức độ phức tạp C#** | Vừa phải (HTTP discovery + ClientWebSocket) | Dễ ở C# (Named Pipe Server), phức tạp ở khâu sửa ASAR |
| **Đánh giá** | ⭐ **Khuyên dùng số 1** | Phương án nâng cao / Dự phòng |

---

## 2. Chi tiết Phương án 1: Remote Debugging Port (CDP)

### 2.1. Kiến trúc Hoạt động

```text
+-----------------------+     Process.Start(--remote-debugging-port=920x)     +-----------------------------+
|    ZaloManager (C#)   | -------------------------------------------------> |   Zalo.exe (Profile tương ứng) |
|                       |                                                     |   (Mở cổng CDP localhost:920x)|
|                       | <---------- 1. GET /json/list --------------------- |                             |
|                       | <---------- 2. Kết nối ws://127.0.0.1:920x/... ---- |                             |
|  CdpMessageWatcher.cs | ----------- 3. Gửi {"method":"Network.enable"} ---> |                             |
|                       | <---------- 4. Frame Network.webSocketFrame... ---- | (Gửi/Nhận tin nhắn Zalo)    |
+-----------------------+                                                     +-----------------------------+
```

### 2.2. Các bước triển khai trong ZaloManager

#### Bước 1: Sửa đổi `src/Manager.cs` để cấp port cho từng Profile
Tại hàm `Instance.Open(Profile profile)`, thêm tham số dòng lệnh gán cổng debug cố định theo ID của profile:

```csharp
// Trong src/Manager.cs -> class Instance -> hàm Open()
public static void Open(Profile profile)
{
    if (IsRunning(profile)) WindowActivation.HideForReopen(profile);

    // Cấp port debug: Profile "original" dùng 9220, clone "01" dùng 9221, "02" dùng 9222...
    int debugPort = profile.IsOriginal ? 9220 : 9220 + int.Parse(profile.Id);

    var startInfo = new ProcessStartInfo(profile.Executable)
    {
        WorkingDirectory = Path.GetDirectoryName(profile.Executable),
        UseShellExecute = false,
        Arguments = "--remote-debugging-port=" + debugPort
    };

    using (Process.Start(startInfo)) { }

    for (int attempt = 0; attempt < 20; attempt++)
    {
        System.Threading.Thread.Sleep(200);
        if (WindowActivation.Restore(profile)) break;
    }

    // Kích hoạt watcher lắng nghe sự kiện
    CdpMessageWatcher.StartWatching(profile, debugPort);
}
```

#### Bước 2: Tạo Module `src/CdpMessageWatcher.cs` (C# / .NET Framework 4.x)
Module này chịu trách nhiệm:
1. Thử kết nối vào `http://127.0.0.1:{port}/json` để lấy URL debug của cửa sổ chính (`chat.zalo.me` hoặc main window).
2. Kết nối tới URL đó bằng `System.Net.WebSockets.ClientWebSocket`.
3. Gửi lệnh kích hoạt Network event: `{"id": 1, "method": "Network.enable"}`.
4. Đọc gói tin qua vòng lặp nhận frame `Network.webSocketFrameReceived` và `Network.webSocketFrameSent`.

```csharp
using System;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZaloManagerPoc
{
    public class ZaloMessageEventArgs : EventArgs
    {
        public string ProfileId { get; set; }
        public bool IsOutgoing { get; set; } // true = gửi đi, false = nhận về
        public string RawPayload { get; set; }
    }

    public static class CdpMessageWatcher
    {
        public static event EventHandler<ZaloMessageEventArgs> OnMessageReceived;

        public static void StartWatching(Profile profile, int port)
        {
            Task.Run(async () =>
            {
                string wsUrl = null;
                for (int i = 0; i < 20; i++)
                {
                    try
                    {
                        var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/json");
                        req.Timeout = 1000;
                        using (var resp = req.GetResponse())
                        using (var reader = new StreamReader(resp.GetResponseStream()))
                        {
                            string json = reader.ReadToEnd();
                            int idx = json.IndexOf("\"webSocketDebuggerUrl\": \"");
                            if (idx >= 0)
                            {
                                int start = idx + 25;
                                int end = json.IndexOf("\"", start);
                                wsUrl = json.Substring(start, end - start);
                                break;
                            }
                        }
                    }
                    catch { await Task.Delay(500); }
                }

                if (string.IsNullOrEmpty(wsUrl)) return;

                using (var ws = new ClientWebSocket())
                {
                    await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);

                    string enableCmd = "{\"id\":1,\"method\":\"Network.enable\"}";
                    byte[] cmdBytes = Encoding.UTF8.GetBytes(enableCmd);
                    await ws.SendAsync(new ArraySegment<byte>(cmdBytes), WebSocketMessageType.Text, true, CancellationToken.None);

                    byte[] buffer = new byte[65536];
                    while (ws.State == WebSocketState.Open && Instance.IsRunning(profile))
                    {
                        var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        if (result.MessageType == WebSocketMessageType.Close) break;

                        string msg = Encoding.UTF8.GetString(buffer, 0, result.Count);

                        if (msg.Contains("Network.webSocketFrameReceived") || msg.Contains("Network.webSocketFrameSent"))
                        {
                            bool outgoing = msg.Contains("Network.webSocketFrameSent");
                            OnMessageReceived?.Invoke(null, new ZaloMessageEventArgs
                            {
                                ProfileId = profile.Id,
                                IsOutgoing = outgoing,
                                RawPayload = msg
                            });
                        }
                    }
                }
            });
        }
    }
}
```

---

## 3. Chi tiết Phương án 2: ASAR Hooking & Windows Named Pipe IPC

### 3.1. Kiến trúc Hoạt động

```text
+-----------------------+     Tạo Named Pipe Server: \\.\pipe\ZMC_EVENT_{id}
|    ZaloManager (C#)   | <-----------------------------------------+
+-----------------------+                                           |
                                                                    | Gửi JSON
+--------------------------------------------------------------+    | qua Named Pipe
| Zalo PC (Electron)                                           |    |
|   +-------------------------------------------------------+  |    |
|   | app.asar -> hook.js (Monkey-patch window.WebSocket)  | --+----+
|   |   - Gửi tin: ws.send() -> ghi vào Pipe                |  |
|   |   - Nhận tin: ws.addEventListener('message') -> Pipe  |  |
|   +-------------------------------------------------------+  |
+--------------------------------------------------------------+
```

### 3.2. Các bước triển khai trong ZaloManager

#### Bước 1: Tạo Script Hook (JavaScript) để inject vào Zalo PC
```javascript
// hook.js
(function() {
    const net = require('net');
    const pipeName = '\\\\.\\pipe\\ZMC_EVENT_' + (window.__ZMC_PROFILE_ID || '0');
    
    let pipeClient = null;
    function connectPipe() {
        pipeClient = net.connect(pipeName);
        pipeClient.on('error', () => { setTimeout(connectPipe, 2000); });
    }
    connectPipe();

    const OriginalWebSocket = window.WebSocket;
    window.WebSocket = function(...args) {
        const ws = new OriginalWebSocket(...args);
        
        const originalSend = ws.send;
        ws.send = function(data) {
            if (pipeClient && !pipeClient.destroyed) {
                pipeClient.write(JSON.stringify({ event: 'send', data: data }) + '\n');
            }
            return originalSend.apply(this, arguments);
        };

        ws.addEventListener('message', function(event) {
            if (pipeClient && !pipeClient.destroyed) {
                pipeClient.write(JSON.stringify({ event: 'receive', data: event.data }) + '\n');
            }
        });

        return ws;
    };
})();
```

#### Bước 2: Tích hợp vào hàm `Patch` trong `src/Manager.cs`
Trong `src/Manager.cs`, hàm `Instance.Patch(byte[] source, string id)` thay thế các offset an toàn trong file `app.asar`.
* Nhúng hook script vào file preload JS của Zalo Electron hoặc inject thông qua entry point JS.

#### Bước 3: Mở Named Pipe Server trong ZaloManager (C#)
```csharp
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;

namespace ZaloManagerPoc
{
    public static class NamedPipeMessageListener
    {
        public static void Listen(string profileId)
        {
            Task.Run(() =>
            {
                string pipeName = "ZMC_EVENT_" + profileId;
                while (true)
                {
                    using (var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In))
                    {
                        pipe.WaitForConnection();
                        using (var reader = new StreamReader(pipe, Encoding.UTF8))
                        {
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                ProcessEvent(profileId, line);
                            }
                        }
                    }
                }
            });
        }

        private static void ProcessEvent(string profileId, string jsonLine)
        {
            // Xử lý sự kiện tin nhắn nhận được từ pipe
        }
    }
}
```

---

## 4. Lộ trình Thực hiện Khuyến nghị

1. **Bước 1:** Bổ sung `src/CdpMessageWatcher.cs` vào dự án `ZaloManager`.
2. **Bước 2:** Cập nhật hàm `Instance.Open()` trong `src/Manager.cs` để truyền tham số `--remote-debugging-port`.
3. **Bước 3:** Chạy `.\build.ps1` để biên dịch kiểm tra.
4. **Bước 4:** Chạy thử nghiệm mở một Profile và theo dõi log frame tin nhắn gửi/nhận thời gian thực.
