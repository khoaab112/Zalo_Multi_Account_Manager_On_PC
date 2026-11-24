using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ZaloManagerPoc
{
    public class ZaloFrameEventArgs : EventArgs
    {
        public string ProfileId { get; internal set; }
        public bool IsOutgoing { get; internal set; }
        public int Opcode { get; internal set; }
        public string PayloadData { get; internal set; }
        public string RequestId { get; internal set; }
        public DateTime ReceivedUtc { get; internal set; }
    }

    // A frame is transport activity. Consumers must decode Zalo's protocol before
    // treating it as a chat message. Binary payloads use CDP's base64 representation.
    public static partial class CdpMessageWatcher
    {
        private sealed class Session
        {
            public readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            public volatile string Status = "CDP đang kết nối";
            public volatile bool Connected;
            public long Sent;
            public long Received;
            public readonly VisibleMessages Messages = new VisibleMessages();
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Session> Sessions = new Dictionary<string, Session>();
        public static event EventHandler<ZaloFrameEventArgs> FrameReceived;

        public static int AllocatePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            try { listener.Start(); return ((IPEndPoint)listener.LocalEndpoint).Port; }
            finally { listener.Stop(); }
        }

        public static string GetStatus(Profile profile)
        {
            lock (Gate)
            {
                Session session;
                if (!Sessions.TryGetValue(profile.Id, out session)) return "CDP chưa kết nối · mở hồ sơ từ Manager";
                return session.Status + " · gửi " + Interlocked.Read(ref session.Sent) + " / nhận " + Interlocked.Read(ref session.Received) + " frame"
                    + Environment.NewLine + session.Messages.Status;
            }
        }

        internal static bool IsTracking(Profile profile)
        {
            lock (Gate)
            {
                Session session;
                return Sessions.TryGetValue(profile.Id, out session) && session.Connected;
            }
        }

        internal static bool HasOpenConversation(Profile profile)
        {
            lock (Gate)
            {
                Session session;
                return Sessions.TryGetValue(profile.Id, out session) && session.Connected && session.Messages.ConversationOpen;
            }
        }

        // Also adopts sessions started by a previous Manager process.
        public static void Start(Profile profile, int port)
        {
            lock (Gate)
            {
                if (Sessions.ContainsKey(profile.Id)) return;
                var session = new Session();
                Sessions.Add(profile.Id, session);
                Task.Run(async delegate
                {
                    try { await Watch(profile, port, session); }
                    catch (OperationCanceledException) { }
                    catch (Exception exception) { Store.Log(exception); }
                    finally
                    {
                        lock (Gate)
                        {
                            Session current;
                            if (Sessions.TryGetValue(profile.Id, out current) && current == session) Sessions.Remove(profile.Id);
                        }
                        session.Cancellation.Dispose();
                    }
                });
            }
        }

        public static void Stop(Profile profile)
        {
            lock (Gate)
            {
                Session session;
                if (Sessions.TryGetValue(profile.Id, out session))
                {
                    Sessions.Remove(profile.Id);
                    session.Cancellation.Cancel();
                }
            }
        }

        public static void StopAll()
        {
            lock (Gate)
            {
                foreach (var session in new List<Session>(Sessions.Values)) session.Cancellation.Cancel();
                Sessions.Clear();
            }
        }

        internal static int FindPort(Profile profile)
        {
            using (var search = new ManagementObjectSearcher("SELECT ExecutablePath, CommandLine FROM Win32_Process WHERE Name='Zalo.exe'"))
            using (var results = search.Get())
                foreach (ManagementObject process in results)
                using (process)
                {
                    string exe = process["ExecutablePath"] as string;
                    if (String.IsNullOrEmpty(exe)) continue;
                    string dir = Path.GetDirectoryName(profile.Executable);
                    bool matchExe = String.Equals(exe, profile.Executable, StringComparison.OrdinalIgnoreCase)
                        || (!String.IsNullOrEmpty(dir) && exe.StartsWith(dir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
                    if (!matchExe) continue;
                    string commandLine = process["CommandLine"] as string ?? "";
                    if (Regex.IsMatch(commandLine, @"\s--type=")) continue;
                    var match = Regex.Match(commandLine, @"\s--remote-debugging-port=(\d+)\b");
                    int port;
                    if (match.Success && Int32.TryParse(match.Groups[1].Value, out port) && port > 0 && port <= 65535) return port;
                }
            return 0;
        }

        private static async Task Watch(Profile profile, int port, Session session)
        {
            var token = session.Cancellation.Token;
            var json = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024, RecursionLimit = 100 };
            while (!token.IsCancellationRequested && Instance.IsRunning(profile))
            {
                try
                {
                    if (port == 0) port = FindPort(profile);
                    if (port == 0)
                    {
                        session.Status = "CDP cần dừng rồi mở lại hồ sơ";
                        await Task.Delay(5000, token);
                        continue;
                    }
                    var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/json/list");
                    request.Proxy = null;
                    request.Timeout = 2000;
                    request.ReadWriteTimeout = 2000;
                    string discovery;
                    using (var discoveryTimeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        discoveryTimeout.CancelAfter(2500);
                        using (discoveryTimeout.Token.Register(request.Abort))
                        using (var response = await request.GetResponseAsync())
                        using (var reader = new StreamReader(response.GetResponseStream())) discovery = await reader.ReadToEndAsync();
                    }
                    var targets = json.DeserializeObject(discovery) as object[];
                    string endpoint = null;
                    foreach (var value in targets ?? new object[0])
                    {
                        var target = value as Dictionary<string, object>;
                        if (target == null || Text(target, "type") != "page") continue;
                        if (MatchesProfileTarget(profile, Text(target, "url"))) endpoint = Text(target, "webSocketDebuggerUrl");
                    }
                    Uri socketUri;
                    if (!Uri.TryCreate(endpoint, UriKind.Absolute, out socketUri) || socketUri.Scheme != "ws" || socketUri.Host != "127.0.0.1" || socketUri.Port != port)
                        throw new IOException("Chưa tìm thấy target CDP của hồ sơ.");
                    using (var socket = new ClientWebSocket())
                    using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        connectTimeout.CancelAfter(5000);
                        await socket.ConnectAsync(socketUri, connectTimeout.Token);
                        
                        // Cài đặt Endpoint trực tiếp vào Zalo:
                        // 1. Network.enable
                        await SendCdpCommand(socket, "{\"id\":1,\"method\":\"Network.enable\"}", token);
                        // 2. Runtime.enable
                        await SendCdpCommand(socket, "{\"id\":2,\"method\":\"Runtime.enable\"}", token);
                        // 3. Runtime.addBinding
                        await SendCdpCommand(socket, "{\"id\":3,\"method\":\"Runtime.addBinding\",\"params\":{\"name\":\"__zaloManagerDispatch\"}}", token);
                        // 4. Page.enable
                        await SendCdpCommand(socket, "{\"id\":4,\"method\":\"Page.enable\"}", token);
                        
                        // 5. Page.addScriptToEvaluateOnNewDocument (để sống qua reload)
                        string hookScript = session.Messages.CreateHookScript(json);
                        string addScriptCmd = json.Serialize(new
                        {
                            id = 5,
                            method = "Page.addScriptToEvaluateOnNewDocument",
                            @params = new { source = hookScript }
                        });
                        await SendCdpCommand(socket, addScriptCmd, token);
                        
                        // 6. Runtime.evaluate (để gài hook vào trang hiện tại ngay lập tức)
                        string evalCmd = json.Serialize(new
                        {
                            id = 6,
                            method = "Runtime.evaluate",
                            @params = new { expression = hookScript, silent = true }
                        });
                        await SendCdpCommand(socket, evalCmd, token);

                        await Receive(profile, socket, session, json, token);
                    }
                }
                catch (Exception exception)
                {
                    if (token.IsCancellationRequested) return;
                    port = 0;
                    session.Status = "CDP đang thử kết nối lại · " + exception.GetType().Name;
                    // Keep payloads and endpoint URLs out of persistent logs.
                    System.Diagnostics.Debug.WriteLine("CDP " + profile.Id + ": " + exception.GetType().Name);
                }
                finally { session.Connected = false; session.Messages.ConversationOpen = false; }
                await Task.Delay(2000, token);
            }
        }

        private static async Task SendCdpCommand(ClientWebSocket socket, string command, CancellationToken token)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(command);
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
        }

        private static async Task Receive(Profile profile, ClientWebSocket socket, Session session, JavaScriptSerializer json, CancellationToken token)
        {
            byte[] buffer = new byte[16384];
            using (var abort = token.Register(socket.Abort))
            using (var message = new MemoryStream())
            {
                while (socket.State == WebSocketState.Open)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    if (result.MessageType != WebSocketMessageType.Text) throw new IOException("Unexpected CDP message type.");
                    if (message.Length + result.Count > 8 * 1024 * 1024) throw new IOException("CDP message exceeds limit.");
                    message.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage) continue;
                    var data = json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(message.ToArray()));
                    message.SetLength(0);

                    object commandId;
                    if (data.TryGetValue("id", out commandId))
                    {
                        int id = Convert.ToInt32(commandId);
                        if (id == 2)
                        {
                            session.Messages.AcceptResponseWithDownloads(profile, data, token);
                            continue;
                        }
                        if (data.ContainsKey("error"))
                            System.Diagnostics.Debug.WriteLine("CDP command " + id + " error: " + Text(data, "error"));
                        session.Status = "CDP đã kết nối (Endpoint)";
                        session.Connected = true;
                    }

                    string method = Text(data, "method");
                    if (method == "Runtime.bindingCalled")
                    {
                        var parameters = Dict(data, "params");
                        if (parameters != null && Text(parameters, "name") == "__zaloManagerDispatch")
                        {
                            if (profile != null && !IsRecallEnabled(profile.Id)) continue;
                            string payload = Text(parameters, "payload");
                            ThreadPool.QueueUserWorkItem(delegate
                            {
                                try { session.Messages.AcceptBindingPayload(profile, payload, token); }
                                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("AcceptBindingPayload: " + ex.Message); }
                            });
                        }
                        continue;
                    }

                    if (method != "Network.webSocketFrameSent" && method != "Network.webSocketFrameReceived") continue;
                    var networkParams = Dict(data, "params");
                    if (networkParams == null) continue;
                    var frame = Dict(networkParams, "response");
                    if (frame == null) continue;
                    bool outgoing = method == "Network.webSocketFrameSent";
                    if (outgoing) Interlocked.Increment(ref session.Sent); else Interlocked.Increment(ref session.Received);
                    var args = new ZaloFrameEventArgs { ProfileId = profile.Id, IsOutgoing = outgoing,
                        Opcode = Convert.ToInt32(frame["opcode"]), PayloadData = Text(frame, "payloadData"),
                        RequestId = Text(networkParams, "requestId"), ReceivedUtc = DateTime.UtcNow };
                    var handlers = FrameReceived;
                    if (handlers != null)
                        foreach (EventHandler<ZaloFrameEventArgs> handler in handlers.GetInvocationList())
                            try { handler(null, args); }
                            catch (Exception exception) { System.Diagnostics.Debug.WriteLine(exception.GetType().Name); }
                }
            }
        }

        private static Dictionary<string, object> Dict(Dictionary<string, object> value, string key)
        {
            object item;
            return value != null && value.TryGetValue(key, out item) ? item as Dictionary<string, object> : null;
        }

        private static bool MatchesProfileTarget(Profile profile, string url)
        {
            if (String.IsNullOrEmpty(url)) return false;
            // Framework's file URI parser can include Chromium's query in LocalPath.
            int suffix = url.IndexOfAny(new[] { '?', '#' });
            if (suffix >= 0) url = url.Substring(0, suffix);
            Uri page;
            if (!Uri.TryCreate(url, UriKind.Absolute, out page) || !page.IsFile) return false;
            string expected = Path.Combine(Path.GetDirectoryName(profile.Executable), "resources", "app.asar", "pc-dist", "index.html");
            return String.Equals(page.LocalPath, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static string Text(Dictionary<string, object> value, string key)
        {
            object item;
            return value.TryGetValue(key, out item) ? item as string : null;
        }
    }
}
