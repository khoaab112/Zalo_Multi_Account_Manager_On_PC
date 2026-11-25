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
            public long Dropped;
            public Task Completion;
            private const int MaxQueuedEvents = 256;
            private const int MaxQueuedBytes = 16 * 1024 * 1024;
            private readonly object intakeGate = new object();
            private readonly Queue<string> events = new Queue<string>();
            private readonly SemaphoreSlim available = new SemaphoreSlim(0);
            private int queuedBytes;
            private bool completed;
            public volatile bool DiscardQueuedEvents;

            public void Post(string payload)
            {
                lock (intakeGate)
                {
                    if (completed) return;
                    int bytes = payload.Length * sizeof(char);
                    if (events.Count >= MaxQueuedEvents || queuedBytes + bytes > MaxQueuedBytes)
                    {
                        if (payload.Length != 0) Interlocked.Increment(ref Dropped);
                        return;
                    }
                    events.Enqueue(payload);
                    queuedBytes += bytes;
                    available.Release();
                }
            }

            public void Complete()
            {
                lock (intakeGate)
                {
                    completed = true;
                    available.Release();
                }
            }

            public bool PostDownload(Action publish)
            {
                lock (intakeGate)
                {
                    if (completed || DiscardQueuedEvents) return false;
                    publish();
                    Post("");
                    return true;
                }
            }

            public async Task Process(Profile profile)
            {
                while (true)
                {
                    if (await available.WaitAsync(Messages.PendingTimeout).ConfigureAwait(false))
                    {
                        string payload = null;
                        lock (intakeGate)
                        {
                            if (events.Count != 0)
                            {
                                payload = events.Dequeue();
                                queuedBytes -= payload.Length * sizeof(char);
                            }
                            else if (completed) break;
                        }
                        if (!String.IsNullOrEmpty(payload) && !DiscardQueuedEvents)
                        {
                            try { Messages.AcceptBindingPayload(profile, payload, Cancellation.Token); }
                            catch (Exception error) { System.Diagnostics.Debug.WriteLine(error.GetType().Name); }
                        }
                    }
                    try { Messages.Maintain(profile); }
                    catch (Exception error) { Store.Log(error); }
                }
                Messages.Maintain(profile, true);
            }

            public long Sent;
            public long Received;
            public readonly VisibleMessages Messages = new VisibleMessages();
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Session> Sessions = new Dictionary<string, Session>();
        public static event EventHandler<ZaloFrameEventArgs> FrameReceived;
        private static bool shuttingDown;

        public static int AllocatePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            try { listener.Start(); return ((IPEndPoint)listener.LocalEndpoint).Port; }
            finally { listener.Stop(); }
        }

        public static string GetStatus(Profile profile)
        {
            if (!IsRecallEnabled(profile.Id)) return "Đã tắt theo dõi tin thu hồi";
            lock (Gate)
            {
                Session session;
                if (!Sessions.TryGetValue(profile.Id, out session)) return "CDP chưa kết nối · mở hồ sơ từ Manager";
                return session.Status + " · gửi " + Interlocked.Read(ref session.Sent) + " / nhận " + Interlocked.Read(ref session.Received) + " frame"
                    + " · sự kiện bỏ: " + Interlocked.Read(ref session.Dropped) + Environment.NewLine + session.Messages.Status;
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
                return Sessions.TryGetValue(profile.Id, out session) && session.Connected && IsRecallEnabled(profile.Id);
            }
        }

        // Also adopts sessions started by a previous Manager process.
        public static void Start(Profile profile, int port)
        {
            lock (Gate)
            {
                if (shuttingDown || Sessions.ContainsKey(profile.Id) || !IsRecallEnabled(profile.Id)) return;
                var session = new Session();
                Sessions.Add(profile.Id, session);
                session.Messages.PostDownload = session.PostDownload;
                session.Completion = Task.Run(async delegate
                {
                    var processor = session.Process(profile);
                    try { await Watch(profile, port, session); }
                    catch (OperationCanceledException) { }
                    catch (Exception exception) { Store.Log(exception); }
                    session.Cancellation.Cancel();
                    session.Complete();
                    try { await processor; }
                    catch (Exception error) { Store.Log(error); }
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
            StopRecallProfile(profile.Id);
        }

        private static void StopRecallProfile(string profileId)
        {
            lock (Gate)
            {
                Session session;
                if (Sessions.TryGetValue(profileId, out session))
                {
                    session.DiscardQueuedEvents = true;
                    session.Cancellation.Cancel();
                }
            }
        }

        public static void StopAll()
        {
            lock (Gate)
            {
                foreach (var session in Sessions.Values)
                {
                    session.DiscardQueuedEvents = true;
                    session.Cancellation.Cancel();
                }
            }
        }

        internal static Task StopForShutdown()
        {
            lock (Gate)
            {
                shuttingDown = true;
                var tasks = new List<Task>();
                foreach (var session in Sessions.Values)
                {
                    session.Cancellation.Cancel();
                    tasks.Add(session.Completion);
                }
                return Task.WhenAll(tasks);
            }
        }

        internal static void ResumeAfterFailedShutdown()
        {
            lock (Gate) shuttingDown = false;
        }

        private static readonly object PortDiscoveryGate = new object();
        private static readonly System.Diagnostics.Stopwatch PortDiscoveryClock = System.Diagnostics.Stopwatch.StartNew();
        private static List<KeyValuePair<string, int>> discoveredPorts = new List<KeyValuePair<string, int>>();
        private static long portDiscoveryExpires;

        internal static int FindPort(Profile profile)
        {
            var directory = Path.GetDirectoryName(profile.Executable);
            var prefix = String.IsNullOrEmpty(directory) ? null : directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            lock (PortDiscoveryGate)
            {
                if (PortDiscoveryClock.ElapsedMilliseconds >= portDiscoveryExpires)
                {
                    var found = new List<KeyValuePair<string, int>>();
                    using (var search = new ManagementObjectSearcher("SELECT ExecutablePath, CommandLine FROM Win32_Process WHERE Name='Zalo.exe'"))
                    using (var results = search.Get())
                        foreach (ManagementObject process in results)
                        using (process)
                        {
                            string executable = process["ExecutablePath"] as string;
                            string commandLine = process["CommandLine"] as string ?? "";
                            if (String.IsNullOrEmpty(executable) || Regex.IsMatch(commandLine, @"\s--type=")) continue;
                            Match match = Regex.Match(commandLine, @"\s--remote-debugging-port=(\d+)\b");
                            int port;
                            if (match.Success && Int32.TryParse(match.Groups[1].Value, out port) && port > 0 && port <= 65535)
                                found.Add(new KeyValuePair<string, int>(executable, port));
                        }
                    discoveredPorts = found;
                    portDiscoveryExpires = PortDiscoveryClock.ElapsedMilliseconds + 1500;
                }
                foreach (var entry in discoveredPorts)
                    if (String.Equals(entry.Key, profile.Executable, StringComparison.OrdinalIgnoreCase) ||
                        (prefix != null && entry.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) return entry.Value;
                return 0;
            }
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
                        session.Status = profile.IsOriginal ? "Zalo gốc đang mở nhưng chưa có cổng CDP"
                            : "CDP cần dừng rồi mở lại hồ sơ";
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
                        string bindingSession = Guid.NewGuid().ToString("N");
                        string hookScript = session.Messages.CreateHookScript(json, bindingSession);
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
                        await SendCdpCommand(socket, "{\"id\":7,\"method\":\"Page.getFrameTree\"}", token);
                        await SendCdpCommand(socket, evalCmd, token);

                        try { await Receive(profile, socket, session, json, bindingSession, token); }
                        catch (Exception)
                        {
                            if (!token.IsCancellationRequested) throw;
                        }
                        if (token.IsCancellationRequested)
                            await StopRendererHook(socketUri, bindingSession, json);
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

        private static async Task StopRendererHook(Uri endpoint, string bindingSession, JavaScriptSerializer json)
        {
            using (var cleanup = new ClientWebSocket())
            using (var timeout = new CancellationTokenSource(1500))
            {
                try
                {
                    await cleanup.ConnectAsync(endpoint, timeout.Token);
                    string expression = "if (window.__zaloRecallHook && window.__zaloRecallHook.session === " +
                        json.Serialize(bindingSession) + ") window.__zaloRecallHook.stop()";
                    await SendCdpCommand(cleanup, json.Serialize(new
                    {
                        id = 1, method = "Runtime.evaluate", @params = new { expression = expression }
                    }), timeout.Token);
                    var reply = new byte[1024];
                    await cleanup.ReceiveAsync(new ArraySegment<byte>(reply), timeout.Token);
                }
                catch (Exception error) { System.Diagnostics.Debug.WriteLine(error.GetType().Name); }
            }
        }

        private static async Task SendCdpCommand(ClientWebSocket socket, string command, CancellationToken token)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(command);
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
        }

        private static async Task Receive(Profile profile, ClientWebSocket socket, Session session, JavaScriptSerializer json, string bindingSession, CancellationToken token)
        {
            byte[] buffer = new byte[16384];
            string mainFrame = null;
            int mainContext = 0;
            int readyContext = 0;
            var defaultContexts = new Dictionary<string, int>();
            var pendingCommands = new HashSet<int> { 1, 2, 3, 4, 5, 6, 7 };
            using (var setup = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var abort = setup.Token.Register(socket.Abort))
            using (var message = new MemoryStream())
            {
                setup.CancelAfter(8000);
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
                        var resultData = Dict(data, "result");
                        if (data.ContainsKey("error") || (resultData != null && resultData.ContainsKey("exceptionDetails")))
                            throw new IOException("Lệnh CDP " + id + " thất bại.");
                        pendingCommands.Remove(id);
                        if (id == 7)
                        {
                            var frameTree = Dict(resultData, "frameTree");
                            var main = Dict(frameTree, "frame");
                            mainFrame = main == null ? null : Text(main, "id");
                            if (mainFrame == null) throw new IOException("Không tìm thấy frame chính của Zalo.");
                            defaultContexts.TryGetValue(mainFrame, out mainContext);
                        }
                        if (pendingCommands.Count == 0)
                        {
                            setup.CancelAfter(Timeout.Infinite);
                            SetHookStatus(session, mainContext != 0 && readyContext == mainContext);
                        }
                        continue;
                    }

                    string method = Text(data, "method");
                    var parameters = Dict(data, "params");
                    if (method == "Page.frameNavigated")
                    {
                        var navigatedFrame = Dict(parameters, "frame");
                        string frameId = navigatedFrame == null ? null : Text(navigatedFrame, "id");
                        if (frameId != null && !navigatedFrame.ContainsKey("parentId") && frameId != mainFrame)
                        {
                            mainFrame = frameId;
                            defaultContexts.TryGetValue(mainFrame, out mainContext);
                            SetHookStatus(session, pendingCommands.Count == 0 && mainContext != 0 && mainContext == readyContext);
                        }
                        continue;
                    }
                    if (method == "Runtime.executionContextCreated")
                    {
                        var context = Dict(parameters, "context");
                        var aux = Dict(context, "auxData");
                        object isDefault, contextId;
                        if (aux != null && aux.TryGetValue("isDefault", out isDefault) && true.Equals(isDefault)
                            && context.TryGetValue("id", out contextId))
                        {
                            string frameId = Text(aux, "frameId");
                            if (!String.IsNullOrEmpty(frameId))
                            {
                                defaultContexts[frameId] = Convert.ToInt32(contextId);
                                if (frameId == mainFrame)
                                {
                                    mainContext = defaultContexts[frameId];
                                    SetHookStatus(session, false);
                                }
                            }
                        }
                        continue;
                    }
                    if (method == "Runtime.executionContextsCleared" ||
                        (method == "Runtime.executionContextDestroyed" && parameters != null &&
                         parameters.ContainsKey("executionContextId") && Convert.ToInt32(parameters["executionContextId"]) == mainContext))
                    {
                        defaultContexts.Clear();
                        mainContext = readyContext = 0;
                        SetHookStatus(session, false);
                        continue;
                    }
                    if (method == "Runtime.bindingCalled")
                    {
                        if (parameters == null || Text(parameters, "name") != "__zaloManagerDispatch" ||
                            !parameters.ContainsKey("executionContextId")) continue;
                        int eventContext = Convert.ToInt32(parameters["executionContextId"]);

                        string payload = Text(parameters, "payload");
                        if (payload == null || !payload.StartsWith(bindingSession + ":", StringComparison.Ordinal)) continue;
                        payload = payload.Substring(bindingSession.Length + 1);
                        if (payload == "ready" || payload == "waiting")
                        {
                            if (mainContext != 0 && eventContext != mainContext) continue;
                            readyContext = payload == "ready" ? eventContext : 0;
                            if (pendingCommands.Count == 0) SetHookStatus(session, mainContext != 0 && readyContext == mainContext);
                        }
                        else if (pendingCommands.Count == 0 && session.Connected && eventContext == mainContext)
                        {
                            session.Post(payload);
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

        private static void SetHookStatus(Session session, bool ready)
        {
            session.Connected = ready;
            session.Status = ready ? "Đang theo dõi sự kiện Zalo" : "CDP đang chờ bộ phát sự kiện Zalo";
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
