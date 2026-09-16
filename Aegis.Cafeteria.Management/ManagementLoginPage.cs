using System.Net;

namespace Aegis.Cafeteria.Management;

internal static class ManagementLoginPage
{
    public static string Build(string? error = null)
    {
        string errorHtml = string.IsNullOrWhiteSpace(error)
            ? string.Empty
            : $"<div class=\"error\">{WebUtility.HtmlEncode(error)}</div>";

        return $$"""
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width,initial-scale=1" />
<title>Cafeteria Management - Sign In</title>
<style>
*{box-sizing:border-box}body{margin:0;min-height:100vh;display:grid;place-items:center;background:#eef3f7;font-family:Segoe UI,Arial,sans-serif;color:#263746}.card{width:min(470px,92vw);background:#fff;border-radius:16px;box-shadow:0 18px 50px rgba(23,48,70,.16);overflow:hidden}.head{background:#164269;color:white;padding:28px 32px;border-bottom:7px solid #eb8b28}.head h1{margin:0;font-size:25px}.head p{margin:8px 0 0;color:#dce8f1}.body{padding:32px}.body h2{margin:0 0 10px;font-size:20px}.body p{color:#60717f;line-height:1.5}label{display:block;margin-top:18px;font-weight:650;color:#334c60}input{width:100%;margin-top:7px;padding:12px 13px;border:1px solid #c8d2da;border-radius:8px;font:inherit}input:focus{outline:3px solid rgba(235,139,40,.22);border-color:#eb8b28}.signin{display:block;width:100%;margin-top:24px;padding:13px 18px;border:0;border-radius:8px;background:#164269;color:#fff;text-align:center;font-weight:700;font-size:15px;cursor:pointer}.note{font-size:12px;margin-top:18px;color:#7b8994}.error{margin:16px 0 0;padding:11px 13px;border-radius:8px;background:#fff0f0;color:#8a2929;border:1px solid #efc1c1}
</style>
</head>
<body>
<div class="card">
  <div class="head"><h1>Cafeteria Management</h1><p>Food For The Poor Jamaica</p></div>
  <div class="body">
    <h2>Management Sign In</h2>
    <p>Enter your FFP network credentials. Authentication is performed by Common.Security over the configured Active Directory LDAPS servers.</p>
    {{errorHtml}}
    <form method="post" action="/login">
      <label>Username<input name="username" autocomplete="username" autofocus required placeholder="username, FFPJM\\username or username@fftpj.local" /></label>
      <label>Password<input name="password" type="password" autocomplete="current-password" required /></label>
      <button class="signin" type="submit">Sign In</button>
    </form>
    <div class="note">Your password is used only for the Common.Security authentication exchange. After sign-in, the console stores only a short-lived opaque management token.</div>
  </div>
</div>
</body>
</html>
""";
    }
}
