// 用途：极简 HTTP 接口 —— POST /jump（跳歌）、GET /status、GET /save、GET /wait-select。
//       wait-select 是长轮询：选歌界面可点歌时才返回，避免网页端盲推。没有页面/曲绘/SSE。
// 状态：现行。无鉴权，默认只监听 127.0.0.1；配置「局域网」打开才多绑局域网 IP。
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SongRequestMod
{
    internal static class Web
    {
        private static HttpListener _listener;
        private static volatile bool _running;
        private static readonly List<string> _urls = new List<string>();

        internal static void Start(int port)
        {
            if (_running)
            {
                return;
            }
            try
            {
                _listener = Listen(port);
                _listener.Start();
                _running = true;
                var t = new Thread(Loop) { IsBackground = true, Name = "SongRequestLite.Web" };
                t.Start();
                foreach (var u in _urls)
                {
                    ModLog.Always("[点歌] 接口: " + u + "  ·  POST /jump   GET /status   GET /save   GET /wait-select");
                }
                ModLog.Always("[点歌] 成绩回传: " + (string.IsNullOrEmpty(Config.ScoreCallback)
                    ? ("自动 → 最近一次发 /jump 的那台机器, 端口 " + Config.CallbackPort)
                    : ("手动 → " + Config.ScoreCallback)));
            }
            catch (Exception e)
            {
                MelonLoader.MelonLogger.Error("启动接口失败(端口 " + port + "): " + e.Message);
            }
        }

        internal static void Stop()
        {
            _running = false;
            try
            {
                if (_listener != null)
                {
                    _listener.Stop();
                    _listener.Close();
                }
            }
            catch
            {
            }
        }

        private static HttpListener Listen(int port)
        {
            _urls.Clear();
            var l = new HttpListener();
            _urls.Add("http://127.0.0.1:" + port + "/");
            if (Config.LanAccess)
            {
                foreach (var ip in LanIps())
                {
                    _urls.Add("http://" + ip + ":" + port + "/");
                }
            }
            foreach (var u in _urls)
            {
                l.Prefixes.Add(u);
            }
            return l;
        }

        /// <summary>本机可用的 IPv4（滤掉虚拟机/容器那些虚拟网卡）</summary>
        private static IEnumerable<string> LanIps()
        {
            var list = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up)
                    {
                        continue;
                    }
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }
                    var name = (ni.Name + " " + ni.Description).ToLowerInvariant();
                    if (name.Contains("vmware") || name.Contains("virtualbox") || name.Contains("hyper-v")
                        || name.Contains("vethernet") || name.Contains("zerotier") || name.Contains("radmin")
                        || name.Contains("tap-") || name.Contains("wsl") || name.Contains("docker"))
                    {
                        continue;
                    }
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork)
                        {
                            continue;
                        }
                        var s = ua.Address.ToString();
                        if (s.StartsWith("127.") || s.StartsWith("169.254."))
                        {
                            continue;
                        }
                        list.Add(s);
                    }
                }
            }
            catch
            {
            }
            return list;
        }

        private static void Loop()
        {
            while (_running)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = _listener.GetContext();
                }
                catch
                {
                    break;
                }
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        Handle(ctx);
                    }
                    catch (Exception e)
                    {
                        ModLog.WarnOnce("处理请求异常: " + e.Message);
                        try { ctx.Response.Abort(); } catch { }
                    }
                });
            }
        }

        private static void Handle(HttpListenerContext ctx)
        {
            var path = ctx.Request.Url == null ? "/" : ctx.Request.Url.AbsolutePath.ToLowerInvariant();
            var method = ctx.Request.HttpMethod;

            if (path == "/status")
            {
                Reply(ctx, 200, StatusJson());
                return;
            }
            if (path == "/score" || path == "/api/score")
            {
                int sid, sdiff;
                if (!int.TryParse(ctx.Request.QueryString["internalId"], out sid)
                    || !int.TryParse(ctx.Request.QueryString["chartId"], out sdiff))
                {
                    Reply(ctx, 400, "{\"ok\":false,\"error\":\"缺 internalId/chartId\"}");
                    return;
                }
                string one = MainThread.Run(
                    () => SaveReader.ScoreJson(sid, sdiff),
                    5000,
                    "{\"ok\":false,\"error\":\"主线程读取单曲成绩超时\"}");
                Reply(ctx, 200, one);
                return;
            }
            if (path == "/wait-select" || path == "/api/wait-select")
            {
                WaitSelect(ctx);
                return;
            }
            if (path == "/probe")
            {
                Reply(ctx, 200, SelectDriver.ProbeJson());
                return;
            }
            if (path == "/save" || path == "/api/save")
            {
                string json = MainThread.Run(
                    SaveReader.CaptureJson,
                    5000,
                    "{\"ok\":false,\"error\":\"主线程读取存档超时\"}");
                Reply(ctx, 200, json);
                return;
            }
            if (path == "/callback")
            {
                if (method != "POST")
                {
                    Reply(ctx, 405, "{\"ok\":false,\"error\":\"用 POST\"}");
                    return;
                }
                var callbackForm = ReadForm(ctx.Request);
                var callbackUrl = (Get(callbackForm, "url") ?? "").Trim();
                if (callbackUrl.Length == 0)
                {
                    Reply(ctx, 400, "{\"ok\":false,\"error\":\"缺 url\"}");
                    return;
                }
                Mod.CallbackOverride = callbackUrl;
                ModLog.Always("[点歌] 成绩回传改为: " + callbackUrl);
                Reply(ctx, 200, "{\"ok\":true}");
                return;
            }
            if (path == "/jump")
            {
                if (method != "POST")
                {
                    Reply(ctx, 405, "{\"ok\":false,\"error\":\"用 POST\"}");
                    return;
                }
                Jump(ctx);
                return;
            }
            Reply(ctx, 404, "{\"ok\":false,\"error\":\"只有 POST /jump、GET /status、GET /save、GET /wait-select\"}");
        }

        /// <summary>
        /// 长轮询：等到玩家真的能点歌（在选曲界面且跳曲队列空闲）再返回。
        /// 网页端拿到这个响应后才发 /jump，不需要自己反复盲试。
        /// </summary>
        private static void WaitSelect(HttpListenerContext ctx)
        {
            int timeoutMs;
            if (!int.TryParse(ctx.Request.QueryString["timeout"], out timeoutMs))
            {
                timeoutMs = 30000;
            }
            if (timeoutMs < 100)
            {
                timeoutMs = 100;
            }
            if (timeoutMs > 120000)
            {
                timeoutMs = 120000;
            }

            int start = Environment.TickCount;
            while (true)
            {
                if (Mod.Ready && SelectDriver.InSelectCached && !SelectDriver.Busy)
                {
                    Reply(ctx, 200, "{\"ok\":true,\"ready\":true,\"inSelect\":true,\"busy\":false,\"waitedMs\":"
                        + unchecked(Environment.TickCount - start) + "}");
                    return;
                }
                int elapsed = unchecked(Environment.TickCount - start);
                if (elapsed >= timeoutMs)
                {
                    Reply(ctx, 200, "{\"ok\":true,\"ready\":false,\"inSelect\":"
                        + (SelectDriver.InSelectCached ? "true" : "false")
                        + ",\"busy\":" + (SelectDriver.Busy ? "true" : "false")
                        + ",\"timeout\":true,\"waitedMs\":" + elapsed + "}");
                    return;
                }
                Thread.Sleep(100);
            }
        }

        private static void Jump(HttpListenerContext ctx)
        {
            if (!Mod.Ready)
            {
                Reply(ctx, 503, "{\"ok\":false,\"error\":\"游戏数据还在加载\"}");
                return;
            }

            // 记下是谁在指挥 → 结算成绩就自动回传到这台机器
            try
            {
                var ep = ctx.Request.RemoteEndPoint;
                if (ep != null)
                {
                    var ip = ep.Address.ToString();
                    if (ip.StartsWith("::ffff:", StringComparison.Ordinal))
                    {
                        ip = ip.Substring(7);
                    }
                    if (ip.Length > 0)
                    {
                        Mod.LastCallerIp = ip;
                    }
                }
            }
            catch
            {
            }

            var form = ReadForm(ctx.Request);

            int id;
            if (!int.TryParse(Get(form, "id"), out id) || id <= 0)
            {
                Reply(ctx, 400, "{\"ok\":false,\"error\":\"缺 id（游戏曲目 id，DX 行 ≥10000）\"}");
                return;
            }

            int diff = -1;
            var ds = Get(form, "diff");
            if (!string.IsNullOrEmpty(ds) && !int.TryParse(ds, out diff))
            {
                Reply(ctx, 400, "{\"ok\":false,\"error\":\"diff 要是 0..4（0=BASIC … 4=Re:MASTER）\"}");
                return;
            }
            if (diff < -1 || diff > 4)
            {
                Reply(ctx, 400, "{\"ok\":false,\"error\":\"diff 超出范围\"}");
                return;
            }

            // scoreKind: 0=标准 1=DX -1=自动（按 id 判断）
            int kind = -1;
            var ts = (Get(form, "type") ?? "").Trim().ToLowerInvariant();
            if (ts == "std")
            {
                kind = 0;
            }
            else if (ts == "dx")
            {
                kind = 1;
            }
            else if (ts.Length > 0)
            {
                Reply(ctx, 400, "{\"ok\":false,\"error\":\"type 只能是 dx 或 std\"}");
                return;
            }

            ModLog.Info("[点歌] 请求 id=" + id + " diff=" + diff + " type=" + (ts.Length == 0 ? "auto" : ts) + " kind=" + kind);
            if (SongTable.Rev <= 0)
            {
                Reply(ctx, 409, "{\"ok\":false,\"error\":\"曲名表还没建立，已拒绝跳转（等进入选曲界面后再试）\"}");
                return;
            }
            var result = SelectDriver.Enqueue(id, diff, kind);
            var superseded = result != null && result.StartsWith(SelectDriver.SupersededPrefix, StringComparison.Ordinal);
            Reply(ctx, 200, "{\"ok\":" + (superseded ? "false" : "true")
                + ",\"superseded\":" + (superseded ? "true" : "false")
                + ",\"id\":" + id + ",\"diff\":" + diff
                + ",\"msg\":\"" + SongTable.Escape(result ?? "") + "\""
                + ",\"callbackTo\":\"" + SongTable.Escape(CallbackTo()) + "\"}");
        }

        private static string StatusJson()
        {
            bool inSelect;
            try
            {
                inSelect = SelectDriver.InSelectCached;
            }
            catch
            {
                inSelect = false;
            }
            bool busy;
            try
            {
                busy = SelectDriver.Busy;
            }
            catch
            {
                busy = false;
            }
            return "{\"ok\":true,\"ready\":" + (Mod.Ready ? "true" : "false")
                + ",\"version\":\"" + typeof(Web).Assembly.GetName().Version + "\""
                + ",\"songs\":" + SongTable.Rev
                + ",\"inSelect\":" + (inSelect ? "true" : "false")
                + ",\"resultSerial\":" + ResultWatcher.ResultSerial
                + ",\"loginSerial\":" + ResultWatcher.LoginSerial
                + ",\"isEntry\":" + (ResultWatcher.IsEntry ? "true" : "false")
                + ",\"userName\":\"" + SongTable.Escape(ResultWatcher.UserName ?? "") + "\""
                + ",\"busy\":" + (busy ? "true" : "false")
                + ",\"callbackTo\":\"" + SongTable.Escape(CallbackTo()) + "\""
                + ",\"lastCaller\":\"" + SongTable.Escape(Mod.LastCallerIp ?? "") + "\""
                + ",\"lastJump\":\"" + SongTable.Escape(SelectDriver.LastJumpResult ?? "") + "\""
                + ",\"lastJumpAt\":\"" + SongTable.Escape(SelectDriver.LastJumpAt ?? "") + "\"}";
        }

        /// <summary>成绩回传地址：手动配置优先；否则回到最近一次发 /jump 的那台机器的 回传端口。</summary>
        internal static string CallbackTo()
        {
            if (!string.IsNullOrEmpty(Mod.CallbackOverride))
            {
                return Mod.CallbackOverride;
            }
            if (!string.IsNullOrEmpty(Config.ScoreCallback))
            {
                return Config.ScoreCallback;
            }
            if (string.IsNullOrEmpty(Mod.LastCallerIp))
            {
                return "";
            }
            return "http://" + Mod.LastCallerIp + ":" + Config.CallbackPort + "/api/score";
        }

        // ── HTTP 小工具 ────────────────────────────────────────────────
        private static string Get(Dictionary<string, string> form, string key)
        {
            string v;
            return form.TryGetValue(key, out v) ? v : null;
        }

        /// <summary>只认 application/x-www-form-urlencoded（id=1&amp;diff=3&amp;type=dx）</summary>
        private static Dictionary<string, string> ReadForm(HttpListenerRequest req)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string body;
            try
            {
                var enc = req.ContentEncoding ?? Encoding.UTF8;
                using (var r = new StreamReader(req.InputStream, enc))
                {
                    body = r.ReadToEnd();
                }
            }
            catch
            {
                return d;
            }
            if (string.IsNullOrEmpty(body))
            {
                return d;
            }
            foreach (var pair in body.Split('&'))
            {
                if (pair.Length == 0)
                {
                    continue;
                }
                int eq = pair.IndexOf('=');
                var k = eq < 0 ? pair : pair.Substring(0, eq);
                var v = eq < 0 ? "" : pair.Substring(eq + 1);
                d[WebUtility.UrlDecode(k)] = WebUtility.UrlDecode(v);
            }
            return d;
        }

        private static void Reply(HttpListenerContext ctx, int status, string json)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = "application/json; charset=utf-8";
                ctx.Response.Headers["Cache-Control"] = "no-store";
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            }
            catch
            {
            }
            finally
            {
                try { ctx.Response.Close(); } catch { }
            }
        }
    }
}
