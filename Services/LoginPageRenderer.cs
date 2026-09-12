using System.Net;
using Microsoft.AspNetCore.Antiforgery;

namespace TexTrack.Web.Services;

public static class LoginPageRenderer
{
    public static string Render(HttpContext context, IAntiforgery antiforgery,
        DatabaseStatus database, bool setupRequired)
    {
        var returnUrl = context.Request.Query["returnUrl"].FirstOrDefault();
        var safeReturnUrl = SecurityRequestGuard.IsLocalReturnUrl(returnUrl) ? returnUrl! : "/";
        var tokens = antiforgery.GetAndStoreTokens(context);
        var token = $"<input type=\"hidden\" name=\"{WebUtility.HtmlEncode(tokens.FormFieldName)}\" value=\"{WebUtility.HtmlEncode(tokens.RequestToken)}\">";
        var error = context.Request.Query.ContainsKey("error")
            ? "<p class=\"error\">Invalid User ID or password.</p>" : string.Empty;
        var setupError = context.Request.Query.ContainsKey("setupError")
            ? "<p class=\"error\">Initial setup failed. Check the recovery code and password requirements.</p>" : string.Empty;
        var action = setupRequired ? "/initial-setup" : "/login";
        var fields = setupRequired
            ? """
              <p class="notice">No users exist. Enter the one-time recovery code shown in the TexTrack server window.</p>
              <label>Recovery Code</label><input name="recoveryCode" autocomplete="one-time-code" required>
              <label>New Developer User ID</label><input name="userName" autocomplete="username" required>
              <label>New Password</label><input name="password" type="password" minlength="12" autocomplete="new-password" required>
              <button type="submit">Create Developer Account</button>
              """
            : """
              <label>User ID</label><input name="userName" autocomplete="username" autofocus required>
              <label>Password</label><input name="password" type="password" autocomplete="current-password" required>
              <button type="submit">Login</button>
              """;
        var dbClass = database.IsConnected ? "good" : "warn";
        var dbText = database.IsConnected ? "PostgreSQL Connected" : "Database Attention Required";
        return $$$"""
            <!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>TexTrack ERP Login</title><style>
            :root{font-family:Arial,sans-serif;color:#07162b;background:#eef3f9}*{box-sizing:border-box}body{margin:0;min-height:100vh;display:grid;place-items:center;padding:32px}
            .window{width:min(1120px,96vw);border:1px solid #17304f;border-radius:12px;background:white;box-shadow:0 18px 50px #17304f22;overflow:hidden}.title{height:58px;background:#132f53;color:white;display:flex;align-items:center;justify-content:space-between;padding:0 24px;font-weight:700}.product{color:#ffe38b}.body{display:grid;grid-template-columns:1fr 1fr;min-height:500px}.brand{background:#f2f6fc;padding:54px 48px;border-right:1px solid #ced9e7}.mark{width:58px;height:58px;border-radius:10px;background:#092544;color:white;display:grid;place-items:center;font-weight:800;font-size:20px}.eyebrow{letter-spacing:.12em;font-weight:700;margin-top:32px}.brand h1{font-size:40px;margin:8px 0}.facts{margin-top:32px;border-top:1px solid #ced9e7}.fact{display:flex;justify-content:space-between;padding:16px 0;border-bottom:1px solid #ced9e7}.good{color:#087443}.warn,.error{color:#b42318}.login{padding:54px 48px;display:flex;align-items:center}.login form{width:100%;display:grid;gap:10px}.login h2{font-size:28px;margin:0 0 8px}.login p{color:#53657b}label{font-weight:700;margin-top:8px}input{height:42px;padding:8px 10px;border:1px solid #9babc0;border-radius:6px;font-size:16px}input:focus{outline:2px solid #2f6fed;outline-offset:1px}button{height:44px;margin-top:16px;border:0;border-radius:6px;background:#132f53;color:white;font-weight:800;font-size:16px}.notice{padding:12px;border-left:4px solid #2f6fed;background:#edf4ff}.foot{padding:14px 24px;background:#071c36;color:#dce7f5;display:flex;justify-content:space-between;font-size:13px}@media(max-width:760px){.body{grid-template-columns:1fr}.brand{border-right:0;padding:28px}.login{padding:32px}.brand h1{font-size:30px}}
            </style></head><body><main class="window"><header class="title"><span>Gateway of TexTrack</span><span class="product">TexTrack ERP</span></header><div class="body"><section class="brand"><div class="mark">TT</div><p class="eyebrow">GARMENT · ACCOUNTS · INVENTORY</p><h1>TexTrack ERP</h1><p>Keyboard-first production and inventory control.</p><div class="facts"><div class="fact"><span>Server</span><strong>Local Server Ready</strong></div><div class="fact"><span>Database</span><strong class="{{{dbClass}}}">{{{dbText}}}</strong></div><div class="fact"><span>Build</span><strong>v0.6 Build 3.1</strong></div></div></section><section class="login"><form method="post" action="{{{action}}}">{{{token}}}<input type="hidden" name="returnUrl" value="{{{WebUtility.HtmlEncode(safeReturnUrl)}}}"><h2>{{{(setupRequired ? "Initial Developer Setup" : "Sign in")}}}</h2><p>{{{(setupRequired ? "Secure recovery is required before TexTrack can be used." : "Use your TexTrack User ID and password.")}}}</p>{{{error}}}{{{setupError}}}{{{fields}}}</form></section></div><footer class="foot"><span>Authorized users only</span><span>PostgreSQL · Multi-user security</span></footer></main></body></html>
            """;
    }
}
