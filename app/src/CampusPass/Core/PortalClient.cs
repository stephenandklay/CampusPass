using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CampusPass.Core
{
    static class PortalClient
    {
        static readonly object Gate = new object();
        static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
        const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) CampusPass/1.0";

        internal sealed class DetectedForm
        {
            internal string SubmitUrl;
            internal string Method;
            internal string UsernameField;
            internal string PasswordField;
            internal string ExtraFields;
        }

        /// <summary>
        /// A handler per login flow rather than a pooled static client: the legacy
        /// build created a fresh CookieContainer per flow, and portal sessions are
        /// short-lived enough that connection reuse buys nothing worth the
        /// cross-session cookie risk.
        /// </summary>
        static HttpClient NewClient(CookieContainer cookies, bool followRedirects)
        {
            var handler = new HttpClientHandler
            {
                CookieContainer = cookies,
                UseCookies = true,
                AllowAutoRedirect = followRedirects,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            var client = new HttpClient(handler)
            {
                Timeout = RequestTimeout,
                // Portals here are HTTP/1.1 era; pinning the version keeps a protocol
                // difference from surfacing as a login failure.
                DefaultRequestVersion = HttpVersion.Version11,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            return client;
        }

        internal static DetectedForm DetectForm(string portalUrl)
        {
            return DetectForm(portalUrl, new CookieContainer());
        }

        internal static DetectedForm DetectForm(string portalUrl, CookieContainer cookies)
        {
            string expanded = Expand(portalUrl);
            string html = Request(expanded, null, "GET", cookies);
            string formHtml = html;
            Match formMatch = Regex.Match(html, "<form\\b[^>]*>([\\s\\S]*?)</form>", RegexOptions.IgnoreCase);
            if (formMatch.Success) formHtml = formMatch.Value;

            Match actionMatch = Regex.Match(formHtml, "\\baction\\s*=\\s*([\\\"'])(.*?)\\1", RegexOptions.IgnoreCase);
            string action = actionMatch.Success ? WebUtility.HtmlDecode(actionMatch.Groups[2].Value.Trim()) : "";
            string submit = string.IsNullOrEmpty(action) ? expanded : new Uri(new Uri(expanded), action).AbsoluteUri;
            Match methodMatch = Regex.Match(formHtml, "\\bmethod\\s*=\\s*([\\\"'])(.*?)\\1", RegexOptions.IgnoreCase);
            string method = methodMatch.Success && string.Equals(methodMatch.Groups[2].Value.Trim(), "get", StringComparison.OrdinalIgnoreCase) ? "GET" : "POST";

            var hidden = new Dictionary<string, string>();
            foreach (Match input in Regex.Matches(formHtml, "<input\\b[^>]*>", RegexOptions.IgnoreCase))
            {
                string tag = input.Value;
                string name = HtmlAttribute(tag, "name");
                if (string.IsNullOrEmpty(name)) continue;
                string type = HtmlAttribute(tag, "type");
                string value = HtmlAttribute(tag, "value");
                if (string.Equals(type, "password", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(type, "hidden", StringComparison.OrdinalIgnoreCase)) hidden[name] = value;
            }

            string usernameField = FindField(formHtml, false);
            string passwordField = FindField(formHtml, true);
            if (string.IsNullOrEmpty(usernameField) || string.IsNullOrEmpty(passwordField)) return null;
            var extras = new List<string>();
            foreach (var pair in hidden) extras.Add(pair.Key + "=" + pair.Value);
            return new DetectedForm
            {
                SubmitUrl = submit,
                Method = method,
                UsernameField = usernameField,
                PasswordField = passwordField,
                ExtraFields = string.Join("\r\n", extras.ToArray())
            };
        }

        static string FindField(string html, bool password)
        {
            string fallback = null;
            foreach (Match input in Regex.Matches(html, "<input\\b[^>]*>", RegexOptions.IgnoreCase))
            {
                string tag = input.Value;
                string type = HtmlAttribute(tag, "type");
                string name = HtmlAttribute(tag, "name");
                string id = HtmlAttribute(tag, "id");
                string hint = (name + " " + id).ToLowerInvariant();
                if (password && string.Equals(type, "password", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(name)) return name;
                if (!password && !string.Equals(type, "password", StringComparison.OrdinalIgnoreCase) && !string.Equals(type, "hidden", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(name) && (hint.Contains("user") || hint.Contains("account") || hint.Contains("login") || hint.Contains("name") || hint.Contains("账号") || hint.Contains("用户名"))) return name;
                if (!password && string.IsNullOrEmpty(fallback) && !string.Equals(type, "password", StringComparison.OrdinalIgnoreCase) && !string.Equals(type, "hidden", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(name) && !hint.Contains("captcha") && !hint.Contains("verify") && !hint.Contains("code")) fallback = name;
            }
            return password ? null : fallback;
        }

        static string HtmlAttribute(string tag, string attribute)
        {
            Match match = Regex.Match(tag, "\\b" + Regex.Escape(attribute) + "\\s*=\\s*([\\\"'])(.*?)\\1", RegexOptions.IgnoreCase);
            return match.Success ? WebUtility.HtmlDecode(match.Groups[2].Value) : "";
        }

        internal static string TryLogin(PortalSettings settings)
        {
            if (!Monitor.TryEnter(Gate)) return "已有登录检测正在运行";
            try
            {
                if (!NetworkInterface.GetIsNetworkAvailable()) return "未检测到可用网络";
                if (HasInternet(settings)) return "网络已经可以正常访问";
                string[] credential = AppData.LoadCredential();
                if (credential == null) return "尚未保存账号密码";

                var cookies = new CookieContainer();
                string portalUrl = ResolvePortalUrl(settings);
                Request(portalUrl, null, "GET", cookies);

                if (settings.AutoDetect)
                {
                    try
                    {
                        DetectedForm detected = DetectForm(portalUrl, cookies);
                        if (detected != null)
                        {
                            settings.SubmitUrl = detected.SubmitUrl;
                            settings.Method = detected.Method;
                            settings.UsernameField = detected.UsernameField;
                            settings.PasswordField = detected.PasswordField;
                            settings.ExtraFields = detected.ExtraFields;
                            AppData.Log("已自动识别登录表单：" + detected.Method + " " + detected.SubmitUrl);
                        }
                    }
                    catch (Exception detectEx) { AppData.Log("自动识别表单失败，使用已保存配置：" + detectEx.Message); }
                }

                var fields = ParseExtraFields(settings.ExtraFields);
                fields[settings.UsernameField] = credential[0];
                fields[settings.PasswordField] = credential[1];
                string form = BuildForm(fields);
                string response = Request(Expand(settings.SubmitUrl), form, settings.Method, cookies);
                Thread.Sleep(1800);

                bool keywordMatched = false;
                foreach (string keyword in (settings.SuccessKeywords ?? "").Split('|'))
                    if (keyword.Trim().Length > 0 && response.Contains(keyword.Trim())) keywordMatched = true;

                if (keywordMatched || HasInternet(settings))
                {
                    AppData.Log("认证成功。入口：" + portalUrl);
                    return "认证成功";
                }
                AppData.Log("认证请求已提交，但未确认联网。请检查字段、验证码或终端限制。");
                return "已提交，但未确认联网";
            }
            catch (Exception ex)
            {
                AppData.Log("认证失败：" + ex.Message);
                return "认证失败：" + ex.Message;
            }
            finally { Monitor.Exit(Gate); }
        }

        internal static bool HasInternet(PortalSettings settings)
        {
            try
            {
                string content = Request(Expand(settings.ConnectivityUrl), null, "GET", new CookieContainer());
                return content.Trim().Contains((settings.ConnectivityExpected ?? "").Trim());
            }
            catch { return false; }
        }

        static string ResolvePortalUrl(PortalSettings settings)
        {
            if (settings.DiscoverRedirect)
            {
                try
                {
                    using (var client = NewClient(new CookieContainer(), followRedirects: false))
                    using (var request = new HttpRequestMessage(HttpMethod.Get, Expand(settings.ConnectivityUrl)))
                    using (var response = client.Send(request, HttpCompletionOption.ResponseHeadersRead))
                    {
                        Uri location = response.Headers.Location;
                        if (location != null && Uri.IsWellFormedUriString(location.AbsoluteUri, UriKind.Absolute))
                            return location.AbsoluteUri;
                    }
                }
                catch { }
            }
            return Expand(settings.PortalUrl);
        }

        static Dictionary<string, string> ParseExtraFields(string text)
        {
            var result = new Dictionary<string, string>();
            foreach (string raw in (text ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = raw.IndexOf('=');
                if (separator > 0) result[raw.Substring(0, separator).Trim()] = Expand(raw.Substring(separator + 1).Trim());
            }
            return result;
        }

        static string BuildForm(Dictionary<string, string> fields)
        {
            var parts = new List<string>();
            foreach (var field in fields)
                parts.Add(Uri.EscapeDataString(field.Key) + "=" + Uri.EscapeDataString(field.Value ?? ""));
            return string.Join("&", parts.ToArray());
        }

        static string Request(string url, string form, string method, CookieContainer cookies)
        {
            if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException("请求地址不能为空");
            string normalizedMethod = string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) ? "GET" : "POST";
            if (normalizedMethod == "GET" && !string.IsNullOrEmpty(form)) url += (url.Contains("?") ? "&" : "?") + form;

            using (var client = NewClient(cookies, followRedirects: true))
            using (var request = new HttpRequestMessage(
                normalizedMethod == "GET" ? HttpMethod.Get : HttpMethod.Post, url))
            {
                if (normalizedMethod == "POST")
                {
                    byte[] data = Encoding.UTF8.GetBytes(form ?? "");
                    request.Content = new ByteArrayContent(data);
                    // Set literally rather than via StringContent/MediaTypeHeaderValue so
                    // the header stays byte-identical to what the legacy build sent.
                    request.Content.Headers.TryAddWithoutValidation(
                        "Content-Type", "application/x-www-form-urlencoded; charset=UTF-8");
                    request.Content.Headers.ContentLength = data.Length;
                }
                using (var response = client.Send(request, HttpCompletionOption.ResponseHeadersRead))
                {
                    // HttpWebRequest.GetResponse() threw on non-2xx; HttpClient does not.
                    // Without this, a 500 error page would flow on into the keyword
                    // check and could be misread as a successful login.
                    if (!response.IsSuccessStatusCode)
                    {
                        int code = (int)response.StatusCode;
                        throw new HttpRequestException("HTTP " + code + " " + response.ReasonPhrase);
                    }
                    using (var stream = response.Content.ReadAsStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd();
                }
            }
        }

        static string Expand(string value)
        {
            return (value ?? "").Replace("{local_ip}", GetLocalIPv4());
        }

        static string GetLocalIPv4()
        {
            string fallback = "";
            try
            {
                foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (UnicastIPAddressInformation address in adapter.GetIPProperties().UnicastAddresses)
                    {
                        if (address.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string ip = address.Address.ToString();
                        if (ip.StartsWith("10.") || ip.StartsWith("192.168.") || IsPrivate172(ip)) return ip;
                        if (!ip.StartsWith("169.254.")) fallback = ip;
                    }
                }
            }
            catch { }
            return fallback;
        }

        static bool IsPrivate172(string ip)
        {
            string[] parts = ip.Split('.');
            int second;
            return parts.Length == 4 && parts[0] == "172" && int.TryParse(parts[1], out second) && second >= 16 && second <= 31;
        }
    }
}
