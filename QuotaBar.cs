using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

[assembly: TargetFramework(".NETFramework,Version=v4.8")]

namespace CodexQuotaBar
{
    internal sealed class Quota
    {
        public double? Remaining;
        public long? ResetsAt;
        public string AccountId;
        public static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        internal static object Get(object value, string key)
        {
            var dictionary = value as Dictionary<string, object>;
            object result;
            return dictionary != null && dictionary.TryGetValue(key, out result) ? result : null;
        }

        internal static Quota Parse(object result)
        {
            object bucket = Get(Get(result, "rateLimitsByLimitId"), "codex");
            if (bucket == null)
            {
                object legacy = Get(result, "rateLimits");
                if ((string)Get(legacy, "limitId") == "codex") bucket = legacy;
            }
            object week = new[] { Get(bucket, "primary"), Get(bucket, "secondary") }
                .FirstOrDefault(w => Get(w, "windowDurationMins") != null && Convert.ToInt32(Get(w, "windowDurationMins")) == 10080);
            var quota = new Quota { AccountId = Get(result, "accountId") as string };
            object used = Get(week, "usedPercent");
            if (used != null)
            {
                double percent = Convert.ToDouble(used, CultureInfo.InvariantCulture);
                if (!Double.IsNaN(percent) && !Double.IsInfinity(percent)) quota.Remaining = Math.Max(0, Math.Min(100, 100 - percent));
            }
            object reset = Get(week, "resetsAt");
            if (reset != null && Convert.ToInt64(reset) > 0) quota.ResetsAt = Convert.ToInt64(reset);
            return quota;
        }

        internal static string ResetTime(long? reset, bool chinese = true)
        {
            if (!reset.HasValue) return chinese ? "未提供" : "N/A";
            return Epoch.AddSeconds(reset.Value).ToLocalTime().ToString(chinese ? "yyyy年M月d日 HH:mm" : "MMM d, yyyy, HH:mm",
                CultureInfo.GetCultureInfo(chinese ? "zh-CN" : "en-US"));
        }
    }

    internal static class UiLanguage
    {
        internal static bool? ReadChinese()
        {
            string home = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (String.IsNullOrWhiteSpace(home)) home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            try
            {
                // Codex writes its resolved UI locale here and updates it when the language setting changes.
                using (var stream = new FileStream(Path.Combine(home, "computer-use", "config.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream))
                {
                    object config = new JavaScriptSerializer().DeserializeObject(reader.ReadToEnd());
                    string locale = Quota.Get(config, "locale") as string;
                    if (String.IsNullOrWhiteSpace(locale)) return null;
                    return locale.Trim().Replace('_', '-').Split('-')[0].Equals("zh", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }
    }

    internal sealed class TiboForecast
    {
        internal const string ForecastUrl = "https://nextreset.ai/forecast/";
        internal const string ApiUrl = "https://nextreset.ai/api/forecast";
        internal bool IsFresh;
        internal bool HasAnnouncement;
        internal int? Probability24h;
        internal DateTimeOffset? AnnouncementTimeUtc;
        internal DateTimeOffset? AsOf;
        internal DateTimeOffset? ExpiresAt;
        internal string SourceUrl;
        internal string Error;

        internal bool Usable { get { return IsFresh && (HasAnnouncement || Probability24h.HasValue); } }

        internal static TiboForecast Failure(string error)
        {
            return new TiboForecast { IsFresh = false, Error = String.IsNullOrWhiteSpace(error) ? "读取失败" : error };
        }

        private static IEnumerable<object> Items(object value)
        {
            var enumerable = value as System.Collections.IEnumerable;
            return enumerable == null || value is string ? Enumerable.Empty<object>() : enumerable.Cast<object>();
        }

        private static string Text(object value)
        {
            return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static DateTimeOffset? Timestamp(object value)
        {
            if (value == null) return null;
            double seconds;
            if (Double.TryParse(Text(value), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)
                && seconds > 1000000000 && seconds < 10000000000)
                return new DateTimeOffset(Quota.Epoch).AddSeconds(seconds).ToUniversalTime();
            DateTimeOffset parsed;
            if (DateTimeOffset.TryParse(Text(value), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
                return parsed.ToUniversalTime();
            return null;
        }

        private static double? Number(object value)
        {
            if (value == null) return null;
            double parsed;
            return Double.TryParse(Text(value), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                && !Double.IsNaN(parsed) && !Double.IsInfinity(parsed) ? (double?)parsed : null;
        }

        private static string FindUrl(object value)
        {
            string text = value as string;
            if (!String.IsNullOrWhiteSpace(text) && (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("http://", StringComparison.OrdinalIgnoreCase))) return text;
            var dictionary = value as Dictionary<string, object>;
            if (dictionary != null)
            {
                foreach (string key in new[] { "url", "href", "link", "sourceUrl" })
                {
                    string url = FindUrl(Quota.Get(dictionary, key));
                    if (!String.IsNullOrWhiteSpace(url)) return url;
                }
                foreach (object child in dictionary.Values)
                {
                    string url = FindUrl(child);
                    if (!String.IsNullOrWhiteSpace(url)) return url;
                }
            }
            foreach (object child in Items(value))
            {
                string url = FindUrl(child);
                if (!String.IsNullOrWhiteSpace(url)) return url;
            }
            return null;
        }

        private static string Flatten(object value)
        {
            if (value == null) return String.Empty;
            if (value is string) return (string)value;
            var dictionary = value as Dictionary<string, object>;
            if (dictionary != null) return String.Join(" ", dictionary.Values.Select(Flatten));
            return String.Join(" ", Items(value).Select(Flatten));
        }

        private static bool IsResetSignal(Dictionary<string, object> item)
        {
            string text = Flatten(item);
            if (text.IndexOf("reset", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("allowance", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("quota", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            bool hasTiboSource = text.IndexOf("tibo", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("thsottiaux", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("savemetibo", StringComparison.OrdinalIgnoreCase) >= 0;
            bool hasExplicitTime = new[] { "scheduledAt", "resetAt", "nextResetAt", "deadlineAt", "announcementAt" }
                .Any(key => Quota.Get(item, key) != null);
            return hasTiboSource && hasExplicitTime;
        }

        private static bool IsActive(Dictionary<string, object> item, DateTimeOffset now)
        {
            string status = Text(Quota.Get(item, "status"));
            if (status == "expired" || status == "inactive" || status == "dismissed" || status == "rejected") return false;
            DateTimeOffset? expires = Timestamp(Quota.Get(item, "expiresAt"));
            if (expires.HasValue && expires.Value <= now) return false;
            DateTimeOffset? published = Timestamp(Quota.Get(item, "publishedAt"));
            if (published.HasValue && published.Value > now.AddMinutes(5)) return false;
            return true;
        }

        private static DateTimeOffset? FindExactTime(Dictionary<string, object> item, DateTimeOffset now)
        {
            // Only absolute timestamps with an explicit field name are accepted. In particular,
            // expiresAt describes signal validity and is never treated as a reset deadline.
            foreach (string key in new[] { "scheduledAt", "resetAt", "nextResetAt", "deadlineAt", "announcementAt" })
            {
                DateTimeOffset? candidate = Timestamp(Quota.Get(item, key));
                if (candidate.HasValue && candidate.Value > now) return candidate;
            }
            return null;
        }

        internal static TiboForecast Parse(string payload, DateTimeOffset now)
        {
            object root = new JavaScriptSerializer().DeserializeObject(payload);
            var document = root as Dictionary<string, object>;
            if (document == null) throw new InvalidOperationException("预测接口返回的 JSON 不是对象。");
            var forecast = new TiboForecast {
                AsOf = Timestamp(Quota.Get(document, "asOf")),
                ExpiresAt = Timestamp(Quota.Get(document, "expiresAt")),
                SourceUrl = ForecastUrl
            };
            string state = Text(Quota.Get(document, "state"));
            bool degradedValue;
            bool degraded = Boolean.TryParse(Text(Quota.Get(document, "degraded")), out degradedValue) && degradedValue;
            forecast.IsFresh = forecast.AsOf.HasValue && forecast.ExpiresAt.HasValue && forecast.ExpiresAt.Value > now
                && !degraded && !String.Equals(state, "error", StringComparison.OrdinalIgnoreCase)
                && !String.Equals(state, "stale", StringComparison.OrdinalIgnoreCase);

            foreach (object entry in Items(Quota.Get(document, "windows")))
            {
                var window = entry as Dictionary<string, object>;
                if (window == null) continue;
                double? hours = Number(Quota.Get(window, "hours"));
                if (!hours.HasValue || Math.Abs(hours.Value - 24) > 0.01) continue;
                double? probability = Number(Quota.Get(window, "probability"));
                if (probability.HasValue && probability.Value >= 0 && probability.Value <= 1)
                    forecast.Probability24h = (int)Math.Round(probability.Value * 100, MidpointRounding.AwayFromZero);
                break;
            }

            foreach (object entry in Items(Quota.Get(Quota.Get(document, "news"), "events")))
            {
                var item = entry as Dictionary<string, object>;
                if (item == null || !IsResetSignal(item) || !IsActive(item, now)) continue;
                forecast.HasAnnouncement = true;
                forecast.AnnouncementTimeUtc = FindExactTime(item, now)
                    ?? FindExactTime(document, now);
                string url = FindUrl(Quota.Get(item, "sources"));
                if (!String.IsNullOrWhiteSpace(url)) forecast.SourceUrl = url;
                break;
            }
            if (!forecast.HasAnnouncement)
            {
                DateTimeOffset? rootTime = FindExactTime(document, now);
                if (rootTime.HasValue)
                {
                    forecast.HasAnnouncement = true;
                    forecast.AnnouncementTimeUtc = rootTime;
                }
            }
            if (!forecast.Usable) forecast.IsFresh = false;
            return forecast;
        }
    }

    internal sealed class TiboForecastClient : IDisposable
    {
        private readonly HttpClient client = new HttpClient();

        internal TiboForecastClient()
        {
            client.Timeout = TimeSpan.FromSeconds(8);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CodexQuotaBar/1.1 (+https://github.com/Useless-Craft/codex-quota-bar)");
        }

        internal async Task<TiboForecast> Read()
        {
            using (HttpResponseMessage response = await client.GetAsync(TiboForecast.ApiUrl))
            {
                response.EnsureSuccessStatusCode();
                string payload = await response.Content.ReadAsStringAsync();
                return TiboForecast.Parse(payload, DateTimeOffset.UtcNow);
            }
        }

        public void Dispose() { client.Dispose(); }
    }

    internal sealed class QuotaClient : IDisposable
    {
        private Process process;
        private int nextId;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();

        internal static string FindCodex()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
            foreach (Process candidate in Process.GetProcessesByName("codex"))
            {
                using (candidate)
                {
                    try
                    {
                        string path = candidate.MainModule.FileName;
                        if (path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return path;
                    }
                    catch (System.ComponentModel.Win32Exception) { }
                    catch (InvalidOperationException) { }
                }
            }
            string latest = Directory.Exists(root) ? Directory.GetFiles(root, "codex.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
            if (latest == null) throw new InvalidOperationException("未找到本机 Codex 程序。请先打开 Codex。");
            return latest;
        }

        private async Task<object> Request(string method, object parameters)
        {
            int id = ++nextId;
            process.StandardInput.WriteLine(json.Serialize(new { id = id, method = method, @params = parameters }));
            DateTime deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                Task<string> read = process.StandardOutput.ReadLineAsync();
                TimeSpan remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero || await Task.WhenAny(read, Task.Delay(remaining)) != read) throw new TimeoutException("额度读取超时。");
                string line = await read;
                if (line == null) throw new IOException("Codex 额度连接已关闭。");
                object reply = json.DeserializeObject(line);
                object responseId = Quota.Get(reply, "id");
                if (responseId == null || Convert.ToString(responseId, CultureInfo.InvariantCulture) != id.ToString(CultureInfo.InvariantCulture)) continue;
                object error = Quota.Get(reply, "error");
                if (error != null) throw new InvalidOperationException(Convert.ToString(Quota.Get(error, "message"), CultureInfo.InvariantCulture));
                return Quota.Get(reply, "result");
            }
            throw new TimeoutException("额度读取超时。");
        }

        internal async Task<Quota> Read()
        {
            try
            {
                if (process == null || process.HasExited)
                {
                    Dispose();
                    process = new Process { StartInfo = new ProcessStartInfo(FindCodex(), "app-server --stdio") {
                        UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                        RedirectStandardOutput = true, RedirectStandardError = true
                    } };
                    process.ErrorDataReceived += delegate { };
                    process.Start();
                    process.BeginErrorReadLine();
                    await Request("initialize", new { clientInfo = new { name = "codex_quota_bar", title = "Codex Quota Bar", version = "1.1.0" } });
                    process.StandardInput.WriteLine("{\"method\":\"initialized\",\"params\":{}}");
                }
                return Quota.Parse(await Request("account/rateLimits/read", null));
            }
            catch { Dispose(); throw; }
        }

        public void Dispose()
        {
            Process owned = process;
            process = null;
            if (owned == null) return;
            try
            {
                if (!owned.HasExited)
                {
                    owned.StandardInput.Close();
                    if (!owned.WaitForExit(1500)) owned.Kill();
                }
            }
            catch (InvalidOperationException) { }
            finally { owned.Dispose(); }
        }
    }

    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct Rectangle { public int Left, Top, Right, Bottom; }
        internal delegate bool EnumCallback(IntPtr handle, IntPtr parameter);
        internal delegate void WinEventCallback(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint threadId, uint eventTime);
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWinEventHook(uint firstEvent, uint lastEvent, IntPtr module, WinEventCallback callback, uint processId, uint threadId, uint flags);
        [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern uint GetPixel(IntPtr dc, int x, int y);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr window, out Rectangle rectangle);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out Rectangle rectangle);
        [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr window, ref Point point);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern IntPtr GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern IntPtr SetWindowLong(IntPtr window, int index, IntPtr value);
        [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
        [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumCallback callback, IntPtr parameter);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr window, StringBuilder text, int size);
        [DllImport("user32.dll")] internal static extern IntPtr GetMenu(IntPtr window);
        [DllImport("user32.dll")] internal static extern int GetMenuItemCount(IntPtr menu);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetMenuString(IntPtr menu, uint item, StringBuilder text, int size, uint flags);
        [DllImport("user32.dll")] internal static extern bool GetMenuItemRect(IntPtr window, IntPtr menu, uint item, out Rectangle rectangle);

        internal static bool IsCodex(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return false;
            uint id;
            GetWindowThreadProcessId(handle, out id);
            try
            {
                using (Process process = Process.GetProcessById((int)id))
                {
                    if (process.ProcessName != "ChatGPT" && process.ProcessName != "Codex") return false;
                    string path = process.MainModule.FileName;
                    return path.IndexOf("\\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) >= 0
                        || path.IndexOf("\\OpenAI\\Codex\\", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (System.ComponentModel.Win32Exception) { return false; }
        }

        internal static IntPtr FindCodexWindow()
        {
            IntPtr foreground = GetForegroundWindow();
            if (IsCodex(foreground)) return foreground;
            return FindCodexWindows().FirstOrDefault();
        }

        internal static List<IntPtr> FindCodexWindows()
        {
            var windows = new List<IntPtr>();
            EnumWindows(delegate(IntPtr window, IntPtr unused) {
                if (IsWindowVisible(window) && IsCodex(window)) windows.Add(window);
                return true;
            }, IntPtr.Zero);
            return windows;
        }
    }

    internal sealed class Anchor
    {
        internal IntPtr Window;
        internal double X, CenterY, Dpi;
        internal Rect Help;

        internal static Anchor Find(IntPtr window)
        {
            if (window == IntPtr.Zero) throw new InvalidOperationException("请先打开 Codex 桌面窗口。");
            var origin = new Native.Point();
            Native.ClientToScreen(window, ref origin);
            Native.Rectangle bounds;
            Native.GetWindowRect(window, out bounds);
            double scale = Native.GetDpiForWindow(window) / 96.0;
            IntPtr menu = Native.GetMenu(window);
            if (menu != IntPtr.Zero)
            {
                for (uint i = 0; i < Native.GetMenuItemCount(menu); i++)
                {
                    var name = new StringBuilder(128);
                    Native.GetMenuString(menu, i, name, name.Capacity, 0x400);
                    string text = name.ToString().Replace("&", "");
                    Native.Rectangle item;
                    if ((text == "Help" || text.StartsWith("帮助", StringComparison.Ordinal)) && Native.GetMenuItemRect(window, menu, i, out item))
                        return new Anchor { Window = window, X = item.Right - origin.X + 24 * scale,
                            CenterY = (item.Top + item.Bottom) / 2.0 - origin.Y, Dpi = scale,
                            Help = new Rect(item.Left, item.Top, item.Right - item.Left, item.Bottom - item.Top) };
                }
            }
            AutomationElement root = AutomationElement.FromHandle(window);
            var names = new OrCondition(new PropertyCondition(AutomationElement.AutomationIdProperty, "application-menu-trigger-help-menu"),
                new PropertyCondition(AutomationElement.NameProperty, "Help"), new PropertyCondition(AutomationElement.NameProperty, "帮助"));
            AutomationElement menuBar = root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuBar));
            if (menuBar == null) throw new InvalidOperationException("顶部菜单暂不可用。");
            AutomationElementCollection matches = menuBar.FindAll(TreeScope.Descendants, names);
            foreach (AutomationElement element in matches)
            {
                Rect rect = element.Current.BoundingRectangle;
                if (!rect.IsEmpty && rect.Width > 0 && rect.Top >= bounds.Top && rect.Bottom <= bounds.Top + 90 * scale
                    && rect.Left < bounds.Left + 600 * scale)
                    return new Anchor { Window = window, X = rect.Right - origin.X + 24 * scale,
                        CenterY = rect.Top + rect.Height / 2 - origin.Y, Dpi = scale, Help = rect };
            }
            throw new InvalidOperationException("未找到顶部 Help 菜单。请保持截图中的顶部菜单栏可见。");
        }

        internal Native.Point Position(double height)
        {
            var origin = new Native.Point();
            Native.ClientToScreen(Window, ref origin);
            return new Native.Point { X = origin.X + (int)Math.Round(X), Y = origin.Y + (int)Math.Round(CenterY - height * Dpi / 2) };
        }

        internal Color? ReadBackground()
        {
            if (Native.IsIconic(Window) || !Native.IsWindowVisible(Window)) return null;
            Native.Point point = Position(0);
            point.X -= (int)Math.Round(12 * Dpi);
            // Sample the empty gap after Help only when this window is not covered there.
            if (Native.GetAncestor(Native.WindowFromPoint(point), 2) != Window) return null;
            IntPtr dc = Native.GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return null;
            try
            {
                uint rgb = Native.GetPixel(dc, point.X, point.Y);
                if (rgb == 0xFFFFFFFF) return null;
                return Color.FromRgb((byte)rgb, (byte)(rgb >> 8), (byte)(rgb >> 16));
            }
            finally { Native.ReleaseDC(IntPtr.Zero, dc); }
        }
    }

    // UI Automation can block or fail inside native RPC. Keep it outside the display process.
    internal sealed class MenuProbe : IDisposable
    {
        private readonly object gate = new object();
        private Process process;
        private bool disposed;

        [DllImport("kernel32.dll")] private static extern uint SetErrorMode(uint mode);

        internal Task<List<Anchor>> Read(List<IntPtr> windows)
        {
            return Task.Run(delegate {
                var anchors = new List<Anchor>();
                Process owned = null;
                try
                {
                    lock (gate)
                    {
                        if (disposed) return anchors;
                        owned = new Process { StartInfo = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,
                            "--probe-menus " + String.Join(" ", windows.Select(w => w.ToInt64().ToString(CultureInfo.InvariantCulture)))) {
                            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                            RedirectStandardOutput = true, RedirectStandardError = true
                        } };
                        process = owned;
                        owned.Start();
                    }
                    Task<string> output = owned.StandardOutput.ReadToEndAsync();
                    owned.ErrorDataReceived += delegate { };
                    owned.BeginErrorReadLine();
                    if (!owned.WaitForExit(5000)) Stop(owned);
                    string text = output.GetAwaiter().GetResult();
                    var json = new JavaScriptSerializer();
                    foreach (string line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        object row = json.DeserializeObject(line);
                        var anchor = new Anchor {
                            Window = new IntPtr(Convert.ToInt64(Quota.Get(row, "window"))),
                            X = Convert.ToDouble(Quota.Get(row, "x")), CenterY = Convert.ToDouble(Quota.Get(row, "centerY")),
                            Dpi = Convert.ToDouble(Quota.Get(row, "dpi")),
                            Help = new Rect(Convert.ToDouble(Quota.Get(row, "helpX")), Convert.ToDouble(Quota.Get(row, "helpY")),
                                Convert.ToDouble(Quota.Get(row, "helpWidth")), Convert.ToDouble(Quota.Get(row, "helpHeight")))
                        };
                        if (anchor.Dpi > 0 && windows.Contains(anchor.Window)) anchors.Add(anchor);
                    }
                }
                catch (Exception) { /* Keep the last valid position and try again on the next scheduled probe. */ }
                finally
                {
                    lock (gate)
                    {
                        if (owned != null) { Stop(owned); owned.Dispose(); }
                        if (process == owned) process = null;
                    }
                }
                return anchors;
            });
        }

        private static void Stop(Process owned)
        {
            try { if (!owned.HasExited) { owned.Kill(); owned.WaitForExit(500); } }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        public void Dispose()
        {
            lock (gate) { disposed = true; if (process != null) Stop(process); }
        }

        internal static int Run(string[] windows)
        {
            SetErrorMode(3);
            int result = 0;
            var worker = new Thread(delegate() {
                var json = new JavaScriptSerializer();
                foreach (string value in windows)
                {
                    try
                    {
                        var window = new IntPtr(Int64.Parse(value, CultureInfo.InvariantCulture));
                        if (!Native.IsCodex(window) || !Native.IsWindowVisible(window) || Native.IsIconic(window)) continue;
                        Anchor anchor = Anchor.Find(window);
                        Console.WriteLine(json.Serialize(new { window = window.ToInt64(), x = anchor.X, centerY = anchor.CenterY,
                            dpi = anchor.Dpi, helpX = anchor.Help.X, helpY = anchor.Help.Y, helpWidth = anchor.Help.Width, helpHeight = anchor.Help.Height }));
                        Console.Out.Flush();
                    }
                    catch (Exception) { result = 1; }
                }
            }) { IsBackground = true };
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
            worker.Join();
            return result;
        }
    }

    internal sealed class Bar : Window
    {
        private Anchor anchor;
        private readonly DispatcherTimer tracking = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        private readonly TextBlock label = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        private readonly Border surface = new Border { CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(1), Padding = new Thickness(10, 0, 10, 0) };
        private readonly ContextMenu menu = new ContextMenu();
        private readonly Native.WinEventCallback windowMoved;
        private IntPtr movementHook;
        private IntPtr handle;
        private Quota quota;
        private TiboForecast forecast;
        private string error;
        private string display;
        private bool closed, lightTheme, forecastInline = true;
        private bool chinese = true;

        internal Bar(Anchor initial, Func<Task> refreshQuota, Action quitAll)
        {
            anchor = initial;
            Title = "Codex Quota Bar";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            Height = 26;
            SizeToContent = SizeToContent.Width;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(label, TextFormattingMode.Display);
            Typography.SetNumeralAlignment(label, FontNumeralAlignment.Tabular);
            surface.Child = label;
            Content = surface;
            var refresh = new MenuItem { Header = "刷新额度" };
            refresh.Click += async delegate { await refreshQuota(); };
            var openForecast = new MenuItem { Header = "打开 Tibo 预测" };
            openForecast.Click += delegate {
                try { Process.Start(new ProcessStartInfo(TiboForecast.ForecastUrl) { UseShellExecute = true }); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            };
            var quit = new MenuItem { Header = "退出额度显示" };
            quit.Click += delegate { quitAll(); };
            menu.Items.Add(refresh);
            menu.Items.Add(openForecast);
            menu.Items.Add(quit);
            surface.ContextMenu = menu;
            surface.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) {
                if (e.ClickCount == 1) { Native.ReleaseCapture(); Native.SendMessage(anchor.Window, 0xA1, new IntPtr(2), IntPtr.Zero); }
            };
            windowMoved = delegate(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint threadId, uint eventTime) {
                if (closed || window != anchor.Window || objectId != 0 || childId != 0 || !IsVisible) return;
                Native.Point point = anchor.Position(Height);
                // Move the existing rendered surface immediately; keep its size and z-order.
                Native.SetWindowPos(handle, IntPtr.Zero, point.X, point.Y, 0, 0, 0x15);
            };
            SourceInitialized += delegate {
                handle = new WindowInteropHelper(this).Handle;
                Native.SetWindowLong(handle, -20, new IntPtr(Native.GetWindowLong(handle, -20).ToInt64() | 0x08000080));
                HwndSource.FromHwnd(handle).AddHook(delegate(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled) {
                    if (message == 0x21) { handled = true; return new IntPtr(3); }
                    return IntPtr.Zero;
                });
                uint targetProcess;
                Native.GetWindowThreadProcessId(anchor.Window, out targetProcess);
                movementHook = Native.SetWinEventHook(0x800B, 0x800B, IntPtr.Zero, windowMoved, targetProcess, 0, 2);
                if (movementHook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "无法监听 Codex 窗口移动。");
            };
            tracking.Tick += delegate { UpdatePlacement(); };
            Closed += delegate {
                closed = true;
                if (movementHook != IntPtr.Zero) Native.UnhookWinEvent(movementHook);
                tracking.Stop();
            };
        }

        internal void Start()
        {
            UpdateTheme();
            UpdateText();
            var interop = new WindowInteropHelper(this) { Owner = anchor.Window };
            interop.EnsureHandle();
            UpdatePlacement();
            tracking.Start();
        }

        internal void SetQuota(Quota current, string problem)
        {
            if (closed) return;
            quota = current;
            error = problem;
            UpdateText();
            if (handle != IntPtr.Zero) UpdatePlacement();
        }

        internal void SetForecast(TiboForecast current)
        {
            if (closed) return;
            forecast = current;
            display = null;
            UpdateText();
            if (handle != IntPtr.Zero) UpdatePlacement();
        }

        internal void UpdateTheme()
        {
            if (closed) return;
            Color? background = anchor.ReadBackground();
            if (!background.HasValue) return;
            Color color = background.Value;
            bool light = color.R * 299 + color.G * 587 + color.B * 114 >= 128000;
            if (light == lightTheme) return;
            lightTheme = light;
            display = null;
            UpdateText();
        }

        internal void UpdateLanguage(bool useChinese)
        {
            if (closed) return;
            if (chinese == useChinese) return;
            chinese = useChinese;
            display = null;
            UpdateText();
            if (handle != IntPtr.Zero) UpdatePlacement();
        }

        internal void UpdateAnchor(Anchor current)
        {
            if (closed || current.Window != anchor.Window) return;
            anchor = current;
            if (handle != IntPtr.Zero) UpdatePlacement();
        }

        private string ForecastLabel()
        {
            if (forecast == null || !forecast.Usable) return chinese ? "Tibo未更新" : "Tibo not updated";
            if (forecast.HasAnnouncement)
            {
                if (forecast.AnnouncementTimeUtc.HasValue)
                {
                    DateTime local = forecast.AnnouncementTimeUtc.Value.ToLocalTime().DateTime;
                    return chinese ? "Tibo预告 " + local.ToString("M月d日 HH:mm", CultureInfo.GetCultureInfo("zh-CN"))
                        : "Tibo ETA " + local.ToString("MMM d, HH:mm", CultureInfo.GetCultureInfo("en-US"));
                }
                return chinese ? "Tibo预告：时间未定" : "Tibo signal · time unknown";
            }
            return chinese ? "Tibo概率 " + forecast.Probability24h.Value.ToString(CultureInfo.InvariantCulture) + "%"
                : "Tibo 24h " + forecast.Probability24h.Value.ToString(CultureInfo.InvariantCulture) + "%";
        }

        private string ForecastTimestamp(DateTimeOffset? value)
        {
            if (!value.HasValue) return chinese ? "未提供" : "N/A";
            return value.Value.ToLocalTime().ToString(chinese ? "yyyy年M月d日 HH:mm" : "MMM d, yyyy, HH:mm",
                CultureInfo.GetCultureInfo(chinese ? "zh-CN" : "en-US"));
        }

        private string ForecastTooltip()
        {
            if (forecast == null)
                return chinese ? "Tibo 实验性预测 · 等待 NextReset 数据" : "Tibo experimental forecast · Waiting for NextReset data";
            string status = forecast.Usable ? ForecastLabel() : (chinese ? "Tibo未更新" : "Tibo not updated");
            string source = String.IsNullOrWhiteSpace(forecast.SourceUrl) ? TiboForecast.ForecastUrl : forecast.SourceUrl;
            string details = chinese
                ? "Tibo 实验性预测 · 来源 NextReset\n来源链接：" + source + "\n状态：" + status + "\n更新时间：" + ForecastTimestamp(forecast.AsOf)
                    + "\n有效期至：" + ForecastTimestamp(forecast.ExpiresAt)
                : "Tibo experimental forecast · Source: NextReset\nSource link: " + source + "\nStatus: " + status + "\nUpdated: " + ForecastTimestamp(forecast.AsOf)
                    + "\nValid until: " + ForecastTimestamp(forecast.ExpiresAt);
            if (!String.IsNullOrWhiteSpace(forecast.Error)) details += chinese ? "\n读取失败：" + forecast.Error : "\nRead error: " + forecast.Error;
            return details + (chinese ? "\n预测仅供参考，不代表 OpenAI 承诺。" : "\nExperimental estimate; not an OpenAI commitment.");
        }

        private void UpdateText()
        {
            DateTime now = DateTime.UtcNow;
            bool expired = quota != null && quota.ResetsAt.HasValue && Quota.Epoch.AddSeconds(quota.ResetsAt.Value) <= now;
            string unavailable = chinese ? "未更新" : "Not updated", loading = chinese ? "读取中" : "Loading";
            string amount = error != null ? unavailable : quota == null ? loading : expired ? (chinese ? "待更新" : "Pending")
                : quota.Remaining.HasValue ? quota.Remaining.Value.ToString("0.#", CultureInfo.InvariantCulture) + "%" : (chinese ? "未提供" : "N/A");
            bool hasRemaining = quota != null && quota.Remaining.HasValue && error == null && !expired;
            string remainingSuffix = !chinese && hasRemaining ? " left" : "";
            string resetTime = error != null ? unavailable : quota == null ? loading : Quota.ResetTime(quota.ResetsAt, chinese);
            string weeklyLabel = chinese ? "每周额度剩余 " : "Weekly usage limit ", resetLabel = chinese ? "重置时间 " : "Resets ";
            bool showForecast = forecast != null && forecastInline;
            string forecastLabel = showForecast ? ForecastLabel() : null;
            string next = weeklyLabel + amount + remainingSuffix + "  |  " + resetLabel + resetTime
                + (showForecast ? "  |  " + forecastLabel : "");
            if (next == display) return;
            display = next;
            surface.Background = new SolidColorBrush(lightTheme ? Color.FromRgb(238, 243, 239) : Color.FromRgb(42, 53, 46));
            surface.BorderBrush = new SolidColorBrush(lightTheme ? Color.FromRgb(206, 218, 209) : Color.FromRgb(67, 83, 73));
            label.Foreground = new SolidColorBrush(lightTheme ? Color.FromRgb(92, 111, 98) : Color.FromRgb(174, 189, 180));
            var ink = new SolidColorBrush(lightTheme ? Color.FromRgb(38, 56, 45) : Color.FromRgb(227, 237, 230));
            var forecastInk = new SolidColorBrush(lightTheme ? Color.FromRgb(47, 85, 112) : Color.FromRgb(157, 201, 226));
            label.Inlines.Clear();
            label.Inlines.Add(new Run(weeklyLabel) { FontSize = 13, FontWeight = FontWeights.SemiBold });
            var value = new Run(amount) { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = ink };
            if (hasRemaining)
                value.Foreground = quota.Remaining.Value <= 10
                    ? new SolidColorBrush(lightTheme ? Color.FromRgb(180, 35, 24) : Color.FromRgb(250, 132, 132))
                    : quota.Remaining.Value <= 20
                    ? new SolidColorBrush(lightTheme ? Color.FromRgb(138, 75, 0) : Color.FromRgb(229, 184, 110))
                    : new SolidColorBrush(lightTheme ? Color.FromRgb(40, 97, 69) : Color.FromRgb(165, 217, 183));
            label.Inlines.Add(value);
            if (remainingSuffix.Length > 0) label.Inlines.Add(new Run(remainingSuffix) { FontSize = 13, FontWeight = FontWeights.SemiBold });
            label.Inlines.Add(new InlineUIContainer(new Border { Width = 1, Height = 12, Margin = new Thickness(9, 0, 9, 0), Background = surface.BorderBrush }) { BaselineAlignment = BaselineAlignment.Center });
            label.Inlines.Add(new Run(resetLabel));
            label.Inlines.Add(new Run(resetTime) { FontSize = 13, Foreground = ink });
            if (showForecast)
            {
                label.Inlines.Add(new InlineUIContainer(new Border { Width = 1, Height = 12, Margin = new Thickness(9, 0, 9, 0), Background = surface.BorderBrush }) { BaselineAlignment = BaselineAlignment.Center });
                label.Inlines.Add(new Run(forecastLabel) { FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = forecastInk });
            }
            ((MenuItem)menu.Items[0]).Header = chinese ? "刷新额度" : "Refresh quota";
            ((MenuItem)menu.Items[1]).Header = chinese ? "打开 Tibo 预测" : "Open Tibo forecast";
            ((MenuItem)menu.Items[2]).Header = chinese ? "退出额度显示" : "Exit quota display";
            string quotaTooltip = chinese
                ? (error == null ? "Codex 账户周额度 · 每 60 秒刷新 · 右键退出" : "额度读取失败：" + error)
                : (error == null ? "Codex weekly quota · Refreshes every 60s · Right-click for options" : "Unable to refresh quota. Retrying every 60s. Right-click for options.");
            label.ToolTip = quotaTooltip + "\n\n" + ForecastTooltip();
            Title = "Codex Quota Bar — " + display;
            AutomationProperties.SetName(label, display);
        }

        private void UpdatePlacement()
        {
            if (closed) return;
            if (!Native.IsWindow(anchor.Window)) { Close(); return; }
            UpdateText();
            if (Native.IsIconic(anchor.Window) || !Native.IsWindowVisible(anchor.Window))
            {
                if (IsVisible) Hide();
                return;
            }
            double scale = Native.GetDpiForWindow(anchor.Window) / 96.0;
            if (scale <= 0) return;
            if (scale != anchor.Dpi)
            {
                // Keep moving smoothly while the background probe checks the new DPI layout.
                anchor.X *= scale / anchor.Dpi;
                anchor.CenterY *= scale / anchor.Dpi;
                anchor.Dpi = scale;
            }
            Native.Rectangle clientRect;
            Native.GetClientRect(anchor.Window, out clientRect);
            surface.Measure(new Size(Double.PositiveInfinity, Height));
            int width = (int)Math.Ceiling(surface.DesiredSize.Width * scale) + 2;
            bool enough = anchor.X + width <= clientRect.Right - 140 * scale;
            if (!enough && forecastInline && forecast != null)
            {
                // Keep the original two quota fields visible when the third segment would
                // crowd the menu. Its full status remains available in the hover tooltip.
                forecastInline = false;
                display = null;
                UpdateText();
                surface.Measure(new Size(Double.PositiveInfinity, Height));
                width = (int)Math.Ceiling(surface.DesiredSize.Width * scale) + 2;
                enough = anchor.X + width <= clientRect.Right - 140 * scale;
            }
            else if (enough && !forecastInline && forecast != null)
            {
                // Re-enable the third segment after a resize or a wider menu becomes available.
                forecastInline = true;
                display = null;
                UpdateText();
                surface.Measure(new Size(Double.PositiveInfinity, Height));
                width = (int)Math.Ceiling(surface.DesiredSize.Width * scale) + 2;
                enough = anchor.X + width <= clientRect.Right - 140 * scale;
                if (!enough)
                {
                    forecastInline = false;
                    display = null;
                    UpdateText();
                    surface.Measure(new Size(Double.PositiveInfinity, Height));
                    width = (int)Math.Ceiling(surface.DesiredSize.Width * scale) + 2;
                }
            }
            if (anchor.X + width > clientRect.Right - 140 * scale)
            {
                if (IsVisible) Hide();
                return;
            }
            Native.Point point = anchor.Position(Height);
            Native.SetWindowPos(handle, IntPtr.Zero, point.X, point.Y, width, (int)Math.Ceiling(Height * scale), 0x14);
            if (!IsVisible)
            {
                Show();
            }
            // Keep this top-level overlay immediately above its own Codex window.
            IntPtr aboveOwner = Native.GetWindow(anchor.Window, 3);
            if (aboveOwner != handle)
                Native.SetWindowPos(handle, aboveOwner, 0, 0, 0, 0, 0x13);
        }
    }

    internal sealed class QuotaDisplay
    {
        private readonly Application app;
        private readonly EventWaitHandle exit;
        private readonly Dictionary<IntPtr, Bar> bars = new Dictionary<IntPtr, Bar>();
        private readonly QuotaClient client = new QuotaClient();
        private readonly TiboForecastClient forecastClient = new TiboForecastClient();
        private readonly MenuProbe menuProbe = new MenuProbe();
        private readonly DispatcherTimer windows = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private readonly DispatcherTimer polling = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        private readonly DispatcherTimer forecastPolling = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        private Quota quota;
        private TiboForecast forecast;
        private string error;
        private bool busy, forecastBusy, closed, chinese, probing;
        private string layout;
        private DateTime nextProbe;

        internal QuotaDisplay(Application application, EventWaitHandle exitEvent)
        {
            app = application;
            exit = exitEvent;
            windows.Tick += delegate { UpdateWindows(); };
            polling.Tick += async delegate { await RefreshQuota(); };
            forecastPolling.Tick += async delegate { await RefreshForecast(); };
            app.Exit += delegate {
                closed = true;
                windows.Stop(); polling.Stop(); forecastPolling.Stop(); menuProbe.Dispose(); client.Dispose(); forecastClient.Dispose();
            };
        }

        internal async void Start()
        {
            UpdateWindows();
            if (closed) return;
            windows.Start();
            polling.Start();
            forecastPolling.Start();
            await Task.WhenAll(RefreshQuota(), RefreshForecast());
        }

        private void UpdateWindows()
        {
            if (closed) return;
            if (exit.WaitOne(0)) { app.Shutdown(); return; }
            chinese = UiLanguage.ReadChinese() ?? chinese;
            foreach (IntPtr window in bars.Keys.Where(w => !Native.IsWindow(w)).ToArray())
            {
                bars[window].Close();
                bars.Remove(window);
            }
            List<IntPtr> targets = Native.FindCodexWindows();
            if (targets.Count == 0 && bars.Count == 0) { app.Shutdown(); return; }
            foreach (Bar bar in bars.Values) { bar.UpdateLanguage(chinese); bar.UpdateTheme(); }
            string currentLayout = chinese + ";" + String.Join(";", targets.OrderBy(w => w.ToInt64())
                .Select(w => w.ToInt64() + ":" + Native.GetDpiForWindow(w) + ":" + Native.IsIconic(w)));
            if (layout != currentLayout) { layout = currentLayout; nextProbe = DateTime.MinValue; }
            if (!probing && targets.Count > 0 && DateTime.UtcNow >= nextProbe) RefreshAnchors(targets, currentLayout);
        }

        private async void RefreshAnchors(List<IntPtr> targets, string requestedLayout)
        {
            probing = true;
            nextProbe = DateTime.UtcNow.AddSeconds(10);
            try
            {
                List<Anchor> anchors = await menuProbe.Read(targets);
                if (closed || requestedLayout != layout) return;
                foreach (Anchor anchor in anchors)
                {
                    if (!Native.IsWindow(anchor.Window) || !Native.IsCodex(anchor.Window)) continue;
                    Bar bar;
                    if (bars.TryGetValue(anchor.Window, out bar)) { bar.UpdateAnchor(anchor); continue; }
                    bar = new Bar(anchor, RefreshQuota, delegate { app.Shutdown(); });
                    bars.Add(anchor.Window, bar);
                    try { bar.UpdateLanguage(chinese); bar.SetQuota(quota, error); bar.SetForecast(forecast); bar.Start(); }
                    catch (InvalidOperationException) { bar.Close(); bars.Remove(anchor.Window); }
                    catch (System.ComponentModel.Win32Exception) { bar.Close(); bars.Remove(anchor.Window); }
                }
                nextProbe = DateTime.UtcNow.AddSeconds(anchors.Count == targets.Count ? 30 : 10);
            }
            finally { probing = false; }
        }

        private async Task RefreshQuota()
        {
            if (busy || closed) return;
            busy = true;
            try { quota = await client.Read(); error = null; }
            catch (Exception failure) { error = failure.Message; }
            finally { busy = false; }
            if (!closed) foreach (Bar bar in bars.Values) bar.SetQuota(quota, error);
        }

        private async Task RefreshForecast()
        {
            if (forecastBusy || closed) return;
            forecastBusy = true;
            try { forecast = await forecastClient.Read(); }
            catch (Exception failure) { forecast = TiboForecast.Failure(failure.Message); }
            finally { forecastBusy = false; }
            if (!closed) foreach (Bar bar in bars.Values) bar.SetForecast(forecast);
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--probe-menus") return MenuProbe.Run(args.Skip(1).ToArray());
            string suffix = WindowsIdentity.GetCurrent().User.Value;
            string eventName = "Local\\CodexQuotaBar.Exit." + suffix;
            if (args.Length == 1 && args[0] == "--exit")
            {
                try { using (var signal = EventWaitHandle.OpenExisting(eventName)) signal.Set(); }
                catch (WaitHandleCannotBeOpenedException) { }
                return 0;
            }
            if (args.Length == 2 && args[0] == "--check") return Check(args[1]);
            bool launchCodex = args.Length == 1 && args[0] == "--launch";
            bool acquired;
            using (var mutex = new Mutex(true, "Local\\CodexQuotaBar.Instance." + suffix, out acquired))
            {
                using (var exit = new EventWaitHandle(false, EventResetMode.ManualReset, eventName))
                {
                    string stage = "launch_codex";
                    try
                    {
                        if (launchCodex)
                        {
                            using (Process activation = Process.Start(new ProcessStartInfo(
                                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                                @"shell:AppsFolder\OpenAI.Codex_2p2nqsd0c76g0!App") { UseShellExecute = true })) { }
                        }
                        if (!acquired) return 0;
                        AppContext.SetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges", false);
                        stage = "wait_for_window";
                        if (launchCodex)
                        {
                            var startup = Stopwatch.StartNew();
                            while (Native.FindCodexWindows().Count == 0 && startup.Elapsed < TimeSpan.FromSeconds(120))
                                if (exit.WaitOne(250)) return 0;
                        }
                        if (Native.FindCodexWindows().Count == 0)
                        {
                            bool chinese = UiLanguage.ReadChinese() == true;
                            throw new InvalidOperationException(launchCodex
                                ? (chinese ? "等待 120 秒后仍未发现 Codex 窗口。请待 Codex 打开后再次启动额度显示。"
                                    : "Codex did not show a window within 120 seconds. Once Codex opens, start the quota display again.")
                                : (chinese ? "请先打开 Codex，或使用‘Codex＋额度条’快捷方式一起启动。"
                                    : "Open Codex first, or use the Codex + quota bar shortcut to start both."));
                        }
                        stage = "display";
                        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                        var display = new QuotaDisplay(app, exit);
                        app.Dispatcher.BeginInvoke(new Action(display.Start));
                        return app.Run();
                    }
                    catch (Exception error)
                    {
                        Console.Error.WriteLine(error.ToString());
                        bool saved = false;
                        try
                        {
                            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last-error.txt"),
                                DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture) + "\r\nStage: " + stage + "\r\n" + error,
                                new UTF8Encoding(false));
                            saved = true;
                        }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                        bool chinese = UiLanguage.ReadChinese() == true;
                        MessageBox.Show(error.Message + (saved ? (chinese ? "\n\n详细原因已保存到工具目录的 last-error.txt。"
                            : "\n\nDetails were saved to last-error.txt in the quota tool folder.") : ""),
                            chinese ? "Codex 顶部额度显示" : "Codex quota display", MessageBoxButton.OK, MessageBoxImage.Information);
                        return 1;
                    }
                    finally { if (acquired) mutex.ReleaseMutex(); }
                }
            }
        }

        private static int Check(string reportPath)
        {
            var report = new Dictionary<string, object>();
            try
            {
                bool chinese = UiLanguage.ReadChinese() == true;
                report["displayLanguage"] = chinese ? "zh" : "en";
                using (var client = new QuotaClient())
                {
                    Quota quota = client.Read().GetAwaiter().GetResult();
                    report["remainingPercent"] = quota.Remaining;
                    report["resetsAt"] = quota.ResetsAt;
                    report["resetTime"] = Quota.ResetTime(quota.ResetsAt, chinese);
                    report["accountId"] = quota.AccountId;
                }
                IntPtr target = Native.FindCodexWindow();
                report["window"] = target.ToInt64();
                report["foregroundWindow"] = Native.GetForegroundWindow().ToInt64();
                report["targetVisible"] = Native.IsWindowVisible(target);
                report["targetMinimized"] = Native.IsIconic(target);
                Native.Rectangle targetClient;
                Native.GetClientRect(target, out targetClient);
                report["targetClientWidth"] = targetClient.Right;
                var titleText = new StringBuilder(512);
                Native.GetWindowText(target, titleText, titleText.Capacity);
                report["windowTitle"] = titleText.ToString();
                report["nativeMenu"] = Native.GetMenu(target).ToInt64();
                List<Anchor> snapshots;
                using (var probe = new MenuProbe()) snapshots = probe.Read(Native.FindCodexWindows()).GetAwaiter().GetResult();
                Anchor anchor = snapshots.FirstOrDefault(a => a.Window == target);
                if (anchor == null) throw new InvalidOperationException("顶部菜单读取暂不可用。");
                report["window"] = anchor.Window.ToInt64();
                report["helpBounds"] = anchor.Help.ToString(CultureInfo.InvariantCulture);
                report["dpiScale"] = anchor.Dpi;
                Native.Point point = anchor.Position(26);
                report["position"] = new { x = point.X, y = point.Y };
                var targets = new List<object>();
                foreach (Anchor other in snapshots)
                {
                    Native.Point position = other.Position(26);
                    Color? background = other.ReadBackground();
                    targets.Add(new { window = other.Window.ToInt64(), x = position.X, y = position.Y, dpiScale = other.Dpi,
                        background = background.HasValue ? background.Value.ToString() : null });
                }
                report["windows"] = targets;
                var overlays = new List<object>();
                Native.EnumWindows(delegate(IntPtr window, IntPtr unused) {
                    var title = new StringBuilder(512);
                    Native.GetWindowText(window, title, title.Capacity);
                    if (title.ToString().StartsWith("Codex Quota Bar — ", StringComparison.Ordinal))
                    {
                        Native.Rectangle rect;
                        Native.GetWindowRect(window, out rect);
                        overlays.Add(new { title = title.ToString(), owner = Native.GetWindow(window, 4).ToInt64(), visible = Native.IsWindowVisible(window), x = rect.Left, y = rect.Top, width = rect.Right - rect.Left, height = rect.Bottom - rect.Top });
                    }
                    return true;
                }, IntPtr.Zero);
                report["overlays"] = overlays;
                DateTime at = new DateTime(2026, 9, 6, 19, 29, 36, DateTimeKind.Local);
                long reset = (long)(at.ToUniversalTime() - Quota.Epoch).TotalSeconds;
                bool timing = Quota.ResetTime(reset, false) == "Sep 6, 2026, 19:29"
                    && Quota.ResetTime(reset, true) == "2026年9月6日 19:29"
                    && Quota.ResetTime(null, false) == "N/A"
                    && Quota.ResetTime(null, true) == "未提供";
                report["resetTimeChecksPassed"] = timing;
                if (!timing) throw new InvalidOperationException("重置日期格式检查失败。");
                report["ok"] = true;
            }
            catch (Exception failure) { report["ok"] = false; report["error"] = failure.ToString(); }
            File.WriteAllText(reportPath, new JavaScriptSerializer().Serialize(report), new UTF8Encoding(false));
            return (bool)report["ok"] ? 0 : 1;
        }
    }
}
