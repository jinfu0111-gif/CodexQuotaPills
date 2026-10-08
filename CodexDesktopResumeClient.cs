using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace CodexUsageOverlay
{
    // Port of progressrdx/codex-auto-resume's framed desktop follower transport
    // (MIT). Windows profile verified against the installed app, not guessed.
    internal interface IResumeDesktop : IDisposable
    {
        IDictionary<string, object> Snapshot(string id);
        string Resume(string id, string fingerprint, string messageId, Func<bool> guard);
    }

    internal sealed class CodexDesktopResumeClient : IResumeDesktop
    {
        internal const string VerifiedPackageVersion = "26.930.7945.0";
        private const int Timeout = 5000;
        private readonly NamedPipeClientStream pipe;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024, RecursionLimit = 300 };
        private string clientId = "initializing-client";
        private string owner;
        private string threadId;
        private bool stateChanged;

#if AUTO_RESUME_TESTS
        internal CodexDesktopResumeClient(NamedPipeClientStream isolatedTestPipe)
        {
            pipe = isolatedTestPipe;
            object init = Request("initialize", ResumeJson.Obj("clientType", "codex-quota-pills-test"), 0, null);
            clientId = ResumeJson.Text(ResumeJson.Get(init, "result"), "clientId");
        }
#endif

        internal CodexDesktopResumeClient()
        {
            pipe = new NamedPipeClientStream(".", "codex-ipc", PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                pipe.Connect(Timeout);
                uint serverPid;
                if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out serverPid))
                    throw new IOException("无法验证 Codex 本地通道");
                using (Process server = Process.GetProcessById((int)serverPid))
                {
                    string path = server.MainModule.FileName;
                    if (!IsSupportedServer(path)) throw new IOException("Codex 本地通道不是官方 WindowsApps 客户端");
                    // Inspect each running package, including future versions. Never
                    // trust the version number alone or negotiate a write blindly.
                    CodexDesktopContract.ValidateArchive(Path.Combine(Path.GetDirectoryName(path), "resources", "app.asar"));
                }
                object init = Request("initialize", ResumeJson.Obj("clientType", "codex-quota-pills"), 0, null);
                clientId = ResumeJson.Text(ResumeJson.Get(init, "result"), "clientId");
                if (String.IsNullOrWhiteSpace(clientId)) throw new IOException("Codex 握手结果不完整");
            }
            catch { pipe.Dispose(); throw; }
        }

        internal static bool IsSupportedServer(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return false;
            try
            {
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps") + Path.DirectorySeparatorChar;
                return Regex.IsMatch(Path.GetFullPath(path), "^" + Regex.Escape(root) +
                    @"OpenAI\.Codex_\d+\.\d+\.\d+\.\d+_x64__2p2nqsd0c76g0\\app\\ChatGPT\.exe$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch { return false; }
        }

        public IDictionary<string, object> Snapshot(string id)
        {
            Guid parsed;
            if (!Guid.TryParse(id, out parsed)) throw new IOException("无效的对话标识");
            threadId = id;
            object discovery = Request("thread-owner-discovery", ResumeJson.Obj("hostId", "local", "conversationId", id), 1, null);
            owner = ResumeJson.Text(discovery, "handledByClientId");
            if (String.IsNullOrWhiteSpace(owner)) throw new IOException("请先在 Codex 打开此对话");
            if (!Object.Equals(ResumeJson.Get(ResumeJson.Get(discovery, "result"), "supportsUntrustedAppInput"), true))
                throw new IOException("Codex owner 能力未确认，停止续跑");
            Follow(false);
            Follow(true);
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(Timeout);
            while (DateTime.UtcNow < deadline)
            {
                object message = Read(deadline);
                if (ResumeJson.Text(message, "type") != "broadcast" || ResumeJson.Text(message, "method") != "thread-stream-state-changed") continue;
                object p = ResumeJson.Get(message, "params");
                if (ResumeJson.Text(p, "conversationId") != id || ResumeJson.Text(p, "hostId") != "local" ||
                    ResumeJson.Text(message, "sourceClientId") != owner) continue;
                if (ResumeJson.Number(ResumeJson.Get(message, "version")) != 11) throw new IOException("Codex 状态协议已改变，停止续跑");
                object change = ResumeJson.Get(p, "change");
                if (ResumeJson.Text(change, "type") != "snapshot") continue;
                IDictionary<string, object> state = ResumeJson.Map(ResumeJson.Get(change, "conversationState"));
                if (state == null || ResumeJson.Text(state, "id") != id) throw new IOException("返回的对话状态不匹配");
                stateChanged = false;
                return state;
            }
            throw new IOException("原对话状态同步超时");
        }

        public string Resume(string id, string fingerprint, string messageId, Func<bool> guard)
        {
            ResumeDecision current = AutoResumePolicy.AssessDesktop(Snapshot(id), id);
            if (!current.Eligible || current.Fingerprint != fingerprint) throw new IOException("任务状态已改变，本次未续跑");
            // Consume queued state changes before checking cancellation and writing.
            uint available;
            for (int i = 0; i < 128; i++)
            {
                if (!PeekNamedPipe(pipe.SafePipeHandle.DangerousGetHandle(), IntPtr.Zero, 0, IntPtr.Zero, out available, IntPtr.Zero))
                    throw new IOException("Codex 本地通道已关闭");
                if (available == 0) break;
                Read(DateTime.UtcNow.AddMilliseconds(Timeout));
                if (i == 127) throw new IOException("任务状态持续变化，本次未续跑");
            }
            if (stateChanged || !guard()) throw new IOException("续跑已取消或状态已改变");
            object request = ResumeJson.Obj("threadId", id, "clientUserMessageId", messageId,
                "input", new[] { ResumeJson.Obj("type", "text", "text", AutoResumePolicy.Continuation, "text_elements", new object[0]) });
            object reply = Request("thread-follower-start-turn", ResumeJson.Obj("conversationId", id,
                "turnStart", ResumeJson.Obj("request", request, "context", ResumeJson.Obj("inheritThreadSettings", true))), 2, owner);
            string turn = ResumeJson.Text(ResumeJson.Get(ResumeJson.Get(ResumeJson.Get(reply, "result"), "result"), "turn"), "id");
            if (String.IsNullOrWhiteSpace(turn)) throw new IOException("发送结果未确认，不会自动重发");
            return turn;
        }

        private void Follow(bool enabled)
        {
            Write(ResumeJson.Obj("type", "broadcast", "method", "thread-stream-following-changed",
                "sourceClientId", clientId, "targetClientIds", new[] { owner }, "version", 1,
                "params", ResumeJson.Obj("hostId", "local", "conversationId", threadId, "following", enabled)));
        }

        private object Request(string method, object parameters, int version, string target)
        {
            string requestId = Guid.NewGuid().ToString();
            Dictionary<string, object> message = ResumeJson.Obj("type", "request", "requestId", requestId,
                "sourceClientId", clientId, "method", method, "params", parameters, "version", version, "timeoutMs", Timeout);
            if (target != null) message["targetClientId"] = target;
            Write(message);
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(Timeout);
            while (DateTime.UtcNow < deadline)
            {
                object reply = Read(deadline);
                if (ResumeJson.Text(reply, "type") != "response" || ResumeJson.Text(reply, "requestId") != requestId) continue;
                if (ResumeJson.Text(reply, "resultType") != "success")
                    throw new IOException(ResumeJson.Text(reply, "error") == "no-client-found" ? "请先在 Codex 打开此对话" : "Codex 拒绝请求或接口不兼容");
                if (ResumeJson.Text(reply, "method") != method) throw new IOException("Codex 响应方法不匹配");
                if (target != null && ResumeJson.Text(reply, "handledByClientId") != target)
                    throw new IOException("Codex 响应 owner 不匹配");
                return reply;
            }
            throw new IOException("Codex 响应超时");
        }

        private object Read(DateTime deadline)
        {
            int size = BitConverter.ToInt32(Exact(4, deadline), 0);
            if (size <= 0 || size > 32 * 1024 * 1024) throw new IOException("Codex 消息过大");
            object message = json.DeserializeObject(Encoding.UTF8.GetString(Exact(size, deadline)));
            string type = ResumeJson.Text(message, "type");
            if (type == "client-discovery-request")
                Write(ResumeJson.Obj("type", "client-discovery-response", "requestId", ResumeJson.Get(message, "requestId"), "response", ResumeJson.Obj("canHandle", false)));
            else if (type == "request")
                Write(ResumeJson.Obj("type", "response", "requestId", ResumeJson.Get(message, "requestId"), "resultType", "error", "error", "unsupported-by-quota-pills"));
            else if (type == "broadcast" && ResumeJson.Text(message, "method") == "thread-stream-state-changed" &&
                ResumeJson.Text(ResumeJson.Get(message, "params"), "conversationId") == threadId &&
                ResumeJson.Text(message, "sourceClientId") == owner) stateChanged = true;
            else if (type == "broadcast" && (ResumeJson.Text(message, "method") == "thread-archived" ||
                ResumeJson.Text(message, "method") == "thread-queued-followups-changed") &&
                (ResumeJson.Text(ResumeJson.Get(message, "params"), "conversationId") == threadId ||
                 ResumeJson.Text(ResumeJson.Get(message, "params"), "threadId") == threadId)) stateChanged = true;
            return message;
        }

        private byte[] Exact(int size, DateTime deadline)
        {
            byte[] data = new byte[size];
            int position = 0;
            while (position < size)
            {
                IAsyncResult result = pipe.BeginRead(data, position, size - position, null, null);
                int count;
                using (System.Threading.WaitHandle handle = result.AsyncWaitHandle)
                {
                    if (!handle.WaitOne(Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds)))
                    { pipe.Dispose(); throw new IOException("Codex 本地读取超时"); }
                    count = pipe.EndRead(result);
                }
                if (count <= 0) throw new IOException("Codex 本地通道已退出");
                position += count;
            }
            return data;
        }

        private void Write(object message)
        {
            byte[] body = Encoding.UTF8.GetBytes(json.Serialize(message));
            if (body.Length > 32 * 1024 * 1024) throw new IOException("消息过大");
            byte[] header = BitConverter.GetBytes(body.Length);
            byte[] frame = new byte[header.Length + body.Length];
            Buffer.BlockCopy(header, 0, frame, 0, header.Length);
            Buffer.BlockCopy(body, 0, frame, header.Length, body.Length);
            IAsyncResult result = pipe.BeginWrite(frame, 0, frame.Length, null, null);
            using (System.Threading.WaitHandle handle = result.AsyncWaitHandle)
            {
                if (!handle.WaitOne(Timeout)) { pipe.Dispose(); throw new IOException("Codex 本地发送超时"); }
                pipe.EndWrite(result);
            }
        }

        public void Dispose() { pipe.Dispose(); }
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint pid);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool PeekNamedPipe(IntPtr pipe, IntPtr buffer, uint size, IntPtr read, out uint available, IntPtr left);
    }
}
