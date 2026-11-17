using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace ArubaMacViewer;

/// <summary>Where the switch gets the image from.</summary>
public enum ImageSource
{
    /// <summary>This PC pushes the image: SFTP to the switch's own SSH server (ProVision), REST over HTTPS (AOS-CX).
    /// TCP, windowed, fast over a WAN; works wherever SSH already works (ZPA included). No TFTP anywhere.</summary>
    SshPush = 0,
    /// <summary>The switch downloads from the TFTP server built into this app.</summary>
    BuiltInTftp = 1,
    /// <summary>The switch downloads from an existing TFTP server (e.g. SolarWinds).</summary>
    ExternalTftp = 2
}

public static partial class SwitchClient
{
    /// <summary>
    /// Pushes the image from this PC into the chosen flash slot. TFTP sends 512 bytes per round trip, so a
    /// 15 MB image over a WAN with a 50 ms RTT takes ~25 min; SFTP/HTTPS run at line speed (seconds to a
    /// minute). Progress is reported with the real byte count. Throws with the switch's own message on failure.
    /// </summary>
    private static void PushImage(SshClient ssh, ShellStream shell, SwitchEntry sw, FirmwareInfo info, FirmwareImage image,
                                  UpgradeOptions opt, UpgradeProgress progress, CancellationToken ct, Action<string> log)
    {
        if (!File.Exists(image.Path)) throw new FileNotFoundException("image file not found on this PC: " + image.Path);
        if (info.IsCx) PushCxRest(shell, sw, info, image, opt, progress, ct, log);
        else PushProVisionSftp(ssh, shell, sw, info, image, opt, progress, ct, log);
    }

    // ---------------------------------------------------------------- ProVision: SFTP into /os/<slot>

    private static void PushProVisionSftp(SshClient ssh, ShellStream shell, SwitchEntry sw, FirmwareInfo info, FirmwareImage image,
                                          UpgradeOptions opt, UpgradeProgress progress, CancellationToken ct, Action<string> log)
    {
        // 1. the switch's SSH server must have file transfer (SFTP) enabled: "ip ssh filetransfer".
        //    Enabling it turns TFTP off on the switch, which is fine - it is no longer needed. It stays in the
        //    running config and is saved by the "Save config" step.
        var sshInfo = SendCommand(shell, "show ip ssh", ct);
        var scp = Match1(sshInfo, @"Secure Copy Enabled\s*:\s*(\w+)");
        if (!scp.Equals("Yes", StringComparison.OrdinalIgnoreCase))
        {
            progress(UpgradeStage.Copying, 0, "Enabling SSH file transfer on the switch");
            log($"[{sw.DisplayName}] SSH file transfer is {(scp.Length > 0 ? scp : "not enabled")} - running 'ip ssh filetransfer' (this disables TFTP on the switch).");
            SendCommand(shell, "configure", ct);
            var r = SendCommandAnswering(shell, "ip ssh filetransfer", ct);
            SendCommand(shell, "exit", ct);
            // The switch's normal reply is "TFTP and auto-TFTP are now disabled because they cannot be secured with
            // SSH ..." - a success message that contains "cannot", so the reply is only logged; the re-check decides.
            var reply = Squash(StripEcho(r, "ip ssh filetransfer"));
            if (reply.Length > 0) log($"[{sw.DisplayName}] switch: {reply}");
            sshInfo = SendCommand(shell, "show ip ssh", ct);
            scp = Match1(sshInfo, @"Secure Copy Enabled\s*:\s*(\w+)");
            if (!scp.Equals("Yes", StringComparison.OrdinalIgnoreCase))
                throw new Exception($"'ip ssh filetransfer' did not take: 'show ip ssh' still says Secure Copy Enabled: {Or(scp)}" +
                                    (reply.Length > 0 ? $" (switch said: {reply})" : "") + " - use a TFTP source for this switch");
            log($"[{sw.DisplayName}] SSH file transfer enabled.");
        }

        // 2. second SSH connection (same login) with the SFTP subsystem, upload to /os/primary or /os/secondary.
        var remote = "/os/" + opt.Slot.ToLowerInvariant();
        var total = new FileInfo(image.Path).Length;
        log($"[{sw.DisplayName}] SFTP upload {image.FileName} ({total / 1048576.0:F1} MB) -> {remote}");
        progress(UpgradeStage.Copying, 0, "Opening SFTP session");
        var started = DateTime.UtcNow;
        using var sftp = new SftpClient(ssh.ConnectionInfo);
        sftp.OperationTimeout = TimeSpan.FromMinutes(10);   // the Close after the last byte is when the switch writes flash
        sftp.BufferSize = 32 * 1024;
        try { sftp.Connect(); }
        catch (Exception ex)
        {
            throw new Exception("could not open an SFTP session to the switch (the shell login works, so this is the SFTP " +
                                "subsystem itself): " + ex.Message + ". On very old firmware SFTP is not available - use a TFTP source", ex);
        }

        long sent = 0;
        var lastTick = DateTime.UtcNow;
        void Report(ulong n)
        {
            sent = (long)n;
            var now = DateTime.UtcNow;
            if ((now - lastTick).TotalMilliseconds < 400 && sent < total) return;
            var el = now - started;
            var rate = el.TotalSeconds > 0 ? sent / el.TotalSeconds : 0;
            int pct = total > 0 ? (int)(sent * 100 / total) : 0;
            progress(UpgradeStage.Copying, pct, $"SFTP push {pct}% - {sent / 1048576.0:F1} of {total / 1048576.0:F1} MB, {rate / 1048576.0:F2} MB/s, {(int)el.TotalMinutes}:{el.Seconds:00}");
            lastTick = now;
        }

        try
        {
            using var fs = new FileStream(image.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
            // UploadFile blocks until the switch acknowledges the Close, which is when it validates the image and
            // writes it to flash (can take 20-60 s on a 2530 after the last byte).
            progress(UpgradeStage.Copying, 0, "SFTP push starting");
            // The CLI session sits idle the whole time - on a slow link a push takes 10+ minutes and the switch's
            // console idle-timeout logs the shell out ("Cannot access a disposed object: ShellStream" afterwards).
            // So the upload runs on its own thread while this one taps Enter on the shell every 45 s.
            Exception? uploadError = null;
            var upload = Task.Run(() =>
            {
                try { sftp.UploadFile(fs, remote, canOverride: true, Report); }
                catch (Exception ex) { uploadError = ex; }
            }, CancellationToken.None);
            var nextKeepAlive = DateTime.UtcNow + TimeSpan.FromSeconds(45);
            while (!upload.Wait(500))
            {
                ct.ThrowIfCancellationRequested();
                if (DateTime.UtcNow >= nextKeepAlive)
                {
                    nextKeepAlive = DateTime.UtcNow + TimeSpan.FromSeconds(45);
                    try
                    {
                        shell.Write("\n");
                        Thread.Sleep(200);
                        while (shell.DataAvailable) shell.Read();   // swallow the prompt echo
                    }
                    catch { /* session already gone: the next real command will say so */ }
                }
            }
            if (uploadError != null) throw uploadError;
            ct.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) { throw; }
        catch (SftpPermissionDeniedException ex)
        {
            throw new Exception($"the switch refused the SFTP write to {remote} (permission denied): {ex.Message}. The login needs manager " +
                                "level, and 'ip ssh filetransfer' must be enabled", ex);
        }
        catch (SshException ex)
        {
            throw new Exception($"SFTP upload to {remote} failed after {sent / 1048576.0:F1} MB: {ex.Message}" +
                                (sent >= total ? " (all bytes were sent - the switch rejected the image when closing the file; wrong platform/corrupt .swi?)" : ""), ex);
        }
        var took = DateTime.UtcNow - started;
        log($"[{sw.DisplayName}] SFTP push done: {total / 1048576.0:F1} MB in {(int)took.TotalMinutes}:{took.Seconds:00} " +
            $"({total / Math.Max(1, took.TotalSeconds) / 1048576.0:F2} MB/s). Waiting for the switch to validate and write {opt.Slot} ...");
        try { sftp.Disconnect(); } catch { }

        // 3. The SFTP close returns before the switch has finished "Validating and Writing System Software to the
        //    Filesystem" - on a 2530 that runs on for a minute or more. Poll "show flash" until the slot shows the
        //    new version (v1.2.8 checked once, 24 s after the upload, and called a still-busy switch a failure).
        var target = image.TargetVersionFor(info.Version);
        bool primary = opt.Slot.Equals("primary", StringComparison.OrdinalIgnoreCase);
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(6);
        string slotVer = "";
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var flash = SendCommand(shell, "show flash", ct);
            slotVer = primary ? Match1(flash, @"Primary Image[^\n]*?([A-Z]{2}\.\d{2}\.\d{2}\.\d{4})")
                              : Match1(flash, @"Secondary Image[^\n]*?([A-Z]{2}\.\d{2}\.\d{2}\.\d{4})");
            if (slotVer.Length > 0 && FirmwareImage.CompareVersions(slotVer, target) == 0) break;
            var el = DateTime.UtcNow - started;
            if (DateTime.UtcNow > deadline)
                throw new Exception($"the SFTP upload completed but 'show flash' still shows {Or(slotVer)} in {opt.Slot} 6 min later (expected {target}). " +
                                    "The switch did not accept the image: check 'show logging -r' on the switch for the SFTP/validation message " +
                                    "(wrong platform, corrupt .swi, or an older firmware whose SFTP server does not write /os/) - use a TFTP source for this switch");
            progress(UpgradeStage.Copying, 100, $"Switch validating and writing {opt.Slot} ({(int)el.TotalMinutes}:{el.Seconds:00})");
            Thread.Sleep(5000);
        }
        var all = DateTime.UtcNow - started;
        log($"[{sw.DisplayName}] {opt.Slot} now holds {slotVer} ({(int)all.TotalMinutes}:{all.Seconds:00} from the start of the push).");
        progress(UpgradeStage.Copying, 100, "SFTP push complete");
    }

    // ---------------------------------------------------------------- AOS-CX: REST API firmware upload over HTTPS

    private static void PushCxRest(ShellStream shell, SwitchEntry sw, FirmwareInfo info, FirmwareImage image,
                                   UpgradeOptions opt, UpgradeProgress progress, CancellationToken ct, Action<string> log)
    {
        // 1. the REST API must be reachable in read-write mode on the VRF we come in on.
        string vrf = ResolveVrf(opt, info);
        var https = SendCommand(shell, "show https-server", ct);
        bool rw = Regex.IsMatch(https, @"rest.*read-write|read-write", RegexOptions.IgnoreCase);
        bool onVrf = Regex.IsMatch(https, @"\b" + Regex.Escape(vrf) + @"\b", RegexOptions.IgnoreCase);
        if (!rw || !onVrf)
        {
            progress(UpgradeStage.Copying, 0, "Enabling the REST API on the switch");
            log($"[{sw.DisplayName}] HTTPS/REST server: {(rw ? "read-write" : "not read-write")}, VRF {vrf} {(onVrf ? "enabled" : "not enabled")} - configuring 'https-server rest access-mode read-write' / 'https-server vrf {vrf}'.");
            SendCommand(shell, "configure terminal", ct);
            if (!rw) SendCommand(shell, "https-server rest access-mode read-write", ct);
            if (!onVrf) SendCommand(shell, $"https-server vrf {vrf}", ct);
            SendCommand(shell, "exit", ct);
            Thread.Sleep(3000);
        }

        // 2. login (cookie), PUT the image, logout.
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,   // switches use self-signed certificates
            CookieContainer = new CookieContainer(),
            UseCookies = true
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(15) };
        var cred = CredentialForConnection(shell);
        string baseUrl = $"https://{sw.Ip}/rest/v10.04";
        var started = DateTime.UtcNow;
        progress(UpgradeStage.Copying, 0, "Logging in to the REST API");
        HttpResponseMessage login;
        try
        {
            // the documented form: credentials as query parameters on POST .../login (TLS, so not in the clear)
            login = http.PostAsync($"{baseUrl}/login?username={Uri.EscapeDataString(cred.User)}&password={Uri.EscapeDataString(cred.Pass)}", null, ct).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new Exception($"could not reach the REST API at https://{sw.Ip}/ : {ex.Message}. TCP 443 to the switch must be allowed " +
                                "from this PC (firewall / ZPA application segment) and 'https-server vrf' must include the VRF you come in on. " +
                                "Use a TFTP source for this switch if 443 can't be opened", ex);
        }
        if (!login.IsSuccessStatusCode)
            throw new Exception($"REST login failed: HTTP {(int)login.StatusCode} {login.ReasonPhrase} - the same user/password as SSH is used; " +
                                "check 'https-server rest access-mode read-write' on the switch");
        log($"[{sw.DisplayName}] REST login ok ({cred.User}); uploading {image.FileName} to {opt.Slot} ...");

        var total = new FileInfo(image.Path).Length;
        try
        {
            using var fs = new FileStream(image.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
            var ps = new ProgressStream(fs, sent =>
            {
                var el = DateTime.UtcNow - started;
                int pct = total > 0 ? (int)(sent * 100 / total) : 0;
                var rate = el.TotalSeconds > 0 ? sent / el.TotalSeconds : 0;
                progress(UpgradeStage.Copying, pct, $"HTTPS push {pct}% - {sent / 1048576.0:F1} of {total / 1048576.0:F1} MB, {rate / 1048576.0:F2} MB/s, {(int)el.TotalMinutes}:{el.Seconds:00}");
            });
            using var content = new MultipartFormDataContent();
            var file = new StreamContent(ps, 64 * 1024);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(file, "fileupload", image.FileName);
            var put = http.PutAsync($"{baseUrl}/firmware?image={opt.Slot.ToLowerInvariant()}", content, ct).GetAwaiter().GetResult();
            var body = put.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult();
            if (!put.IsSuccessStatusCode)
                throw new Exception($"REST firmware upload rejected: HTTP {(int)put.StatusCode} {put.ReasonPhrase} {Shorten(body.Trim(), 200)}");
        }
        finally
        {
            try { http.PostAsync($"{baseUrl}/logout", null, CancellationToken.None).GetAwaiter().GetResult(); } catch { }
        }
        var took = DateTime.UtcNow - started;
        log($"[{sw.DisplayName}] HTTPS push done: {total / 1048576.0:F1} MB in {(int)took.TotalMinutes}:{took.Seconds:00}. Waiting for the switch to write {opt.Slot} ...");

        // 3. the switch validates and writes the image after the upload; wait until "show images" shows the new version.
        var target = image.TargetVersionFor(info.Version);
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(10);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var now = SendCommand(shell, "show images", ct);
            var slotVer = opt.Slot.Equals("primary", StringComparison.OrdinalIgnoreCase)
                ? Match1(now, @"Primary Image[\s\S]*?Version\s*:\s*(\S+)")
                : Match1(now, @"Secondary Image[\s\S]*?Version\s*:\s*(\S+)");
            if (slotVer.Length > 0 && FirmwareImage.CompareVersions(slotVer, target) == 0) break;
            if (DateTime.UtcNow > deadline)
                throw new Exception($"the upload finished but 'show images' still shows {Or(slotVer)} in {opt.Slot} after 10 min (expected {target})");
            var el = DateTime.UtcNow - started;
            progress(UpgradeStage.Copying, 100, $"Switch writing {opt.Slot} ({(int)el.TotalMinutes}:{el.Seconds:00})");
            Thread.Sleep(5000);
        }
        progress(UpgradeStage.Copying, 100, "HTTPS push complete");
    }

    /// <summary>Like SendCommand, but answers a y/n question the command may ask first.</summary>
    private static string SendCommandAnswering(ShellStream shell, string command, CancellationToken ct)
    {
        shell.WriteLine(command);
        var sb = new System.Text.StringBuilder();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(CommandTimeoutSeconds);
        var promptRx = PromptRegexFor(shell);
        bool answered = false;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            while (shell.DataAvailable) sb.Append(shell.Read());
            var text = AnsiRegex.Replace(sb.ToString(), "");
            if (!answered && Regex.IsMatch(text, @"[yY]\s*/\s*[nN]")) { shell.Write("y\n"); answered = true; }
            int nl = text.LastIndexOf('\n');
            var lastLine = (nl >= 0 ? text[(nl + 1)..] : text).Trim();
            if (lastLine.Length > 0 && !lastLine.Equals(command) && IsPromptLine(lastLine, promptRx)) return text;
            Thread.Sleep(50);
        }
        return AnsiRegex.Replace(sb.ToString(), "");
    }

    /// <summary>The username/password the open shell session logged in with (remembered by Connect/OpenShell).</summary>
    private static (string User, string Pass) CredentialForConnection(ShellStream shell)
    {
        if (shellLogins.TryGetValue(shell, out var c)) return (c.User, c.Pass);
        throw new Exception("the login used for the SSH session is not known (internal) - cannot log in to the REST API");
    }
    private sealed class LoginInfo { public string User = ""; public string Pass = ""; }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SshClient, LoginInfo> connectionLogins = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ShellStream, LoginInfo> shellLogins = new();
    /// <summary>Called by ConnectWith on a successful login.</summary>
    private static void RememberLogin(SshClient ssh, string user, string pass) => connectionLogins.AddOrUpdate(ssh, new LoginInfo { User = user, Pass = pass });
    /// <summary>Called by OpenShell so the push code can reuse the shell's login for the REST API.</summary>
    private static void RememberLogin(ShellStream shell, SshClient ssh)
    {
        if (connectionLogins.TryGetValue(ssh, out var c)) shellLogins.AddOrUpdate(shell, c);
    }

    /// <summary>A read-only stream wrapper that reports how many bytes have been read (for upload progress).</summary>
    private sealed class ProgressStream : Stream
    {
        private readonly Stream inner; private readonly Action<long> report; private long read; private DateTime last = DateTime.MinValue;
        public ProgressStream(Stream inner, Action<long> report) { this.inner = inner; this.report = report; }
        // seekable (when the file is) so HttpClient sends a Content-Length instead of chunked encoding, which the switch's web server may refuse
        public override bool CanRead => true; public override bool CanSeek => inner.CanSeek; public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set { inner.Position = value; read = value; } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = inner.Read(buffer, offset, count);
            read += n;
            if (n == 0 || (DateTime.UtcNow - last).TotalMilliseconds >= 400) { report(read); last = DateTime.UtcNow; }
            return n;
        }
        public override long Seek(long offset, SeekOrigin origin) { var p = inner.Seek(offset, origin); read = p; return p; }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
