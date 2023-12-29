using System.Globalization;
using System.Net;
using System.Text;

namespace StatusApp;

/// <summary>Server-rendered status page. No scripts, so the strict CSP in Program.cs holds.</summary>
internal static class StatusPage
{
    public static string Render(StatusReport report)
    {
        var d = report.Deployment;
        var sb = new StringBuilder();
        sb.Append($$"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{E(d.AppName)}} · {{E(d.Environment)}}</title>
            <style>
            :root { --bg:#f6f7f9; --card:#fff; --ink:#1b1f24; --muted:#5b6470; --line:#e3e6ea;
                    --pass:#1a7f37; --pass-bg:#dafbe1; --warn:#9a6700; --warn-bg:#fff4d4; --fail:#cf222e; --fail-bg:#ffebe9; }
            @media (prefers-color-scheme: dark) {
              :root { --bg:#0d1117; --card:#161b22; --ink:#e6edf3; --muted:#8d96a0; --line:#30363d;
                      --pass:#3fb950; --pass-bg:#12261a; --warn:#d29922; --warn-bg:#2b2111; --fail:#f85149; --fail-bg:#2d1214; }
            }
            * { box-sizing:border-box }
            body { margin:0; background:var(--bg); color:var(--ink); font:15px/1.5 system-ui,-apple-system,"Segoe UI",sans-serif }
            main { max-width:860px; margin:0 auto; padding:32px 16px 48px }
            header { display:flex; flex-wrap:wrap; align-items:baseline; gap:12px; margin-bottom:20px }
            h1 { font-size:24px; margin:0 }
            h2 { font-size:15px; margin:0 0 12px; color:var(--muted); font-weight:600; text-transform:uppercase; letter-spacing:.04em }
            .env { font-size:13px; font-weight:600; padding:2px 10px; border-radius:999px; background:var(--ink); color:var(--bg) }
            .overall { margin-left:auto; font-size:13px; font-weight:700; padding:3px 12px; border-radius:999px }
            .card { background:var(--card); border:1px solid var(--line); border-radius:10px; padding:18px 20px; margin-bottom:16px }
            dl { display:grid; grid-template-columns:repeat(auto-fit,minmax(160px,1fr)); gap:12px 20px; margin:0 }
            dt { color:var(--muted); font-size:12px } dd { margin:0; font-family:ui-monospace,Consolas,monospace; font-size:14px; overflow-wrap:anywhere }
            ul { list-style:none; margin:0; padding:0 }
            li { display:grid; grid-template-columns:72px 180px 1fr; gap:12px; align-items:center; padding:9px 0; border-top:1px solid var(--line) }
            li:first-child { border-top:0 }
            .pill { font-size:12px; font-weight:700; text-align:center; border-radius:6px; padding:2px 0 }
            .pass { color:var(--pass); background:var(--pass-bg) } .warn { color:var(--warn); background:var(--warn-bg) } .fail { color:var(--fail); background:var(--fail-bg) }
            .detail { color:var(--muted); font-family:ui-monospace,Consolas,monospace; font-size:13px; overflow-wrap:anywhere }
            table { width:100%; border-collapse:collapse; font-size:14px }
            th { text-align:left; color:var(--muted); font-weight:600; font-size:12px; padding:6px 0 }
            td { padding:7px 0; border-top:1px solid var(--line); font-family:ui-monospace,Consolas,monospace; font-size:13px }
            footer { color:var(--muted); font-size:13px; margin-top:24px }
            code { font-family:ui-monospace,Consolas,monospace }
            @media (max-width:560px) { li { grid-template-columns:64px 1fr } .detail { grid-column:1 / -1 } }
            </style>
            </head>
            <body>
            <main>
            <header>
              <h1>{{E(d.AppName)}}</h1><span class="env">{{E(d.Environment)}}</span>
              <span class="overall {{Css(report.Overall)}}">{{Label(report.Overall)}}</span>
            </header>
            <section class="card">
              <h2>Deployment</h2>
              <dl>
                <div><dt>Version</dt><dd>{{E(d.Version)}}</dd></div>
                <div><dt>Slot</dt><dd>{{E(d.Slot)}}</dd></div>
                <div><dt>Instance</dt><dd>{{E(d.InstanceId)}}</dd></div>
                <div><dt>Region</dt><dd>{{E(d.Region)}}</dd></div>
                <div><dt>Host</dt><dd>{{E(d.Host)}}</dd></div>
              </dl>
            </section>
            <section class="card">
              <h2>Wiring checks</h2>
              <ul>
            """);

        foreach (var c in report.Checks)
        {
            sb.Append($"""
                    <li><span class="pill {Css(c.State)}">{Label(c.State)}</span><strong>{E(c.Name)}</strong><span class="detail">{E(c.Detail)}</span></li>

                """);
        }

        sb.Append("""
              </ul>
            </section>
            """);

        if (report.Visits is { } v)
        {
            sb.Append($"""

                <section class="card">
                  <h2>Visits · {v.Total.ToString("N0", CultureInfo.InvariantCulture)} rows in dbo.Visits</h2>
                  <table>
                    <tr><th>UTC</th><th>Instance</th><th>Slot</th><th>Version</th></tr>

                """);
            foreach (var r in v.Recent)
            {
                sb.Append($"""
                        <tr><td>{r.VisitedAtUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}</td><td>{E(r.InstanceId)}</td><td>{E(r.Slot)}</td><td>{E(r.AppVersion)}</td></tr>

                    """);
            }
            sb.Append("""
                  </table>
                </section>
                """);
        }

        sb.Append("""

            <footer>Deployed by <code>azure-pipelines.yml</code> from <code>infra/main.bicep</code>. Machine-readable: <code>/healthz</code>.</footer>
            </main>
            </body>
            </html>
            """);
        return sb.ToString();
    }

    internal static string Css(CheckState s) => s switch
    {
        CheckState.Pass => "pass",
        CheckState.Warn => "warn",
        _ => "fail",
    };

    internal static string Label(CheckState s) => s switch
    {
        CheckState.Pass => "OK",
        CheckState.Warn => "WARN",
        _ => "FAIL",
    };

    private static string E(string s) => WebUtility.HtmlEncode(s);
}

    public static object DetectLanguage(object data = null)
    {
        if (data == null) return null;
        return data;
    }

    public static object DetectLanguage(object data = null)
    {
        if (data == null) return null;
        return data;
    }

    public static object DetectLanguage(object data = null)
    {
        if (data == null) return null;
        return data;
    }