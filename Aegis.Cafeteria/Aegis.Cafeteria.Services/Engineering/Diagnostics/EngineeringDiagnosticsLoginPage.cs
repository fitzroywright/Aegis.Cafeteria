namespace Aegis.Engineering.Diagnostics;

public static class EngineeringDiagnosticsLoginPage
{
    public const string Html = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width,initial-scale=1" />
<title>Engineering Diagnostics - Sign In</title>
<style>
*{box-sizing:border-box}body{margin:0;min-height:100vh;display:grid;place-items:center;background:#05050a;color:#f5f2ff;font-family:Arial,Helvetica,sans-serif}.card{width:min(500px,92vw);background:#11111b;border:1px solid #29263a;border-radius:22px;overflow:hidden;box-shadow:0 20px 70px rgba(0,0,0,.45)}.stripe{height:18px;background:#f4a261}.head{padding:28px 32px 20px;border-bottom:9px solid #9d8cff}.head h1{margin:0;text-transform:uppercase;font-size:24px}.head p{margin:8px 0 0;color:#bbb6ce}.body{padding:30px 32px}.body h2{margin:0 0 10px;font-size:18px;text-transform:uppercase}.body p{line-height:1.55;color:#c9c4d8}.signin{display:block;margin-top:24px;padding:14px 18px;border-radius:24px 5px 5px 24px;background:#7dc4ff;color:#101018;text-decoration:none;text-align:center;font-weight:800;text-transform:uppercase}.note{margin-top:18px;color:#928da5;font-size:12px}.footer{height:14px;background:#ffb4a2}
</style>
</head>
<body>
<div class="card">
  <div class="stripe"></div>
  <div class="head"><h1>Engineering Diagnostics</h1><p>Cafeteria Engineering Console</p></div>
  <div class="body">
    <h2>Engineer Sign In</h2>
    <p>Use your FFP Windows account. Access is controlled by the Engineering Diagnostics authorization policy and diagnostic activity is auditable.</p>
    <a class="signin" href="/engineering/signin">Sign in with FFP Windows Account</a>
    <div class="note">Level 2 and Level 1 operations remain controlled intervention gates after authentication.</div>
  </div>
  <div class="footer"></div>
</div>
</body>
</html>
""";
}
