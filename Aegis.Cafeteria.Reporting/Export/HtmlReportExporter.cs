namespace Aegis.Cafeteria.Reporting.Export;

using System.Globalization;
using System.Net;
using System.Text;
using Aegis.Cafeteria.Reporting.Models;

/// <summary>
/// Dependency-free printable HTML exporter. Charts are inline SVG so reports remain portable,
/// printable to PDF from any browser and suitable for future WebView2/desktop preview.
/// </summary>
public sealed class HtmlReportExporter : IReportExporter
{
    public string Format => "HTML";
    public string FileExtension => ".html";

    public async Task ExportAsync(ReportDocument report, Stream output, CancellationToken cancellationToken = default)
    {
        await using var writer = new StreamWriter(output, new UTF8Encoding(false), leaveOpen: true);
        await writer.WriteAsync(Build(report));
        await writer.FlushAsync(cancellationToken);
    }

    internal static string Build(ReportDocument r)
    {
        if (r.Kind == CafeteriaReportKind.CanteenSummary) return BuildCanteenSummary(r);
        var b = new StringBuilder();
        b.Append("<!doctype html><html><head><meta charset='utf-8'><title>").Append(E(r.Title)).Append("</title><style>")
         .Append("body{font-family:Segoe UI,Arial,sans-serif;margin:32px;color:#18202a}h1{margin:0;font-size:26px}h2{margin-top:30px}.sub{color:#687384;margin:6px 0 24px}.metrics{display:flex;gap:12px;flex-wrap:wrap}.metric{border:1px solid #dde3ea;border-radius:10px;padding:14px 18px;min-width:150px}.metric b{display:block;font-size:21px;margin-top:5px}.chart{border:1px solid #dde3ea;border-radius:10px;padding:14px;margin:18px 0}.chart svg{width:100%;height:270px}table{width:100%;border-collapse:collapse;margin:12px 0 26px;font-size:13px}th{text-align:left;background:#f3f6f9}th,td{padding:8px;border-bottom:1px solid #e3e8ee}.num{text-align:right}.warn{background:#fff7df;padding:10px;border-radius:8px}@media print{body{margin:14mm}.chart{break-inside:avoid}table{break-inside:auto}tr{break-inside:avoid}}")
         .Append("</style></head><body>");
        b.Append("<h1>").Append(E(r.Title)).Append("</h1><div class='sub'>").Append(E(r.Subtitle)).Append(" · Generated ").Append(E(r.GeneratedAt.ToString("yyyy-MM-dd HH:mm"))).Append("</div>");
        if (r.Metrics.Count > 0)
        {
            b.Append("<div class='metrics'>");
            foreach (var m in r.Metrics) b.Append("<div class='metric'>").Append(E(m.Label)).Append("<b>").Append(E(F(m.Value,m.Format))).Append("</b>").Append(m.Note is null ? "" : $"<small>{E(m.Note)}</small>").Append("</div>");
            b.Append("</div>");
        }
        foreach (var c in r.Charts) b.Append("<div class='chart'><h2>").Append(E(c.Title)).Append("</h2>").Append(c.Subtitle is null ? "" : $"<div class='sub'>{E(c.Subtitle)}</div>").Append(RenderChart(c)).Append("</div>");
        if (r.Warnings is { Count: > 0 }) foreach (var w in r.Warnings) b.Append("<p class='warn'>").Append(E(w)).Append("</p>");
        foreach (var t in r.Tables)
        {
            b.Append("<h2>").Append(E(t.Title)).Append("</h2><table><thead><tr>");
            foreach (var c in t.Columns) b.Append("<th>").Append(E(c.Heading)).Append("</th>");
            b.Append("</tr></thead><tbody>");
            foreach (var row in t.Rows)
            {
                b.Append("<tr>");
                foreach (var c in t.Columns) b.Append("<td class='").Append(IsNumeric(c.Format)?"num":"").Append("'>").Append(E(FV(row.GetValueOrDefault(c.Key),c.Format))).Append("</td>");
                b.Append("</tr>");
            }
            b.Append("</tbody></table>");
        }
        return b.Append("</body></html>").ToString();
    }


    private static string BuildCanteenSummary(ReportDocument r)
    {
        var table = r.Tables.FirstOrDefault();
        var rows = table?.Rows ?? [];
        var b = new StringBuilder();
        b.Append("<!doctype html><html><head><meta charset='utf-8'><title>Canteen Summary</title><style>")
         .Append("@page{size:letter portrait;margin:12mm 13mm 14mm}body{font-family:Arial,Helvetica,sans-serif;color:#111;margin:0;font-size:10.5pt}.verse{text-align:center;font-style:italic;font-weight:600;margin:0 0 12px}.brand{display:flex;align-items:flex-end;justify-content:space-between;border-bottom:1.5px solid #111;padding:0 0 6px}.brand-name{font-size:21pt;font-weight:700}.report-name{font-size:20pt;font-weight:700}.address{text-align:center;font-size:8.5pt;margin:3px 0 20px}.range{text-align:center;font-size:14pt;font-weight:700;margin:0 0 16px}.range span{display:block}table{width:100%;border-collapse:collapse;font-size:9.5pt}thead{display:table-header-group}th{border:1px solid #333;padding:3px 4px;text-align:left;font-weight:700;white-space:nowrap}th.num,td.num{text-align:right}td{padding:3px 4px;border-bottom:1px dotted #777}.dept td{background:#e9e9e9;border-top:1px dotted #777;border-bottom:1px dotted #777;font-weight:400;padding:4px}.total td{font-weight:700;border-top:1.5px solid #111;border-bottom:0;padding-top:6px}.footer{display:flex;justify-content:space-between;margin-top:18px;border-top:1px solid #444;padding-top:5px;color:#243b73;font-family:Georgia,serif;font-size:9pt}.nowrap{white-space:nowrap}@media print{.dept{break-after:avoid}tr{break-inside:avoid}.footer{position:running(footer)}}")
         .Append("</style></head><body>");
        b.Append("<div class='verse'>&quot;As often as you did it for the least of my brothers and sisters, you did it for me.&quot; Matthew 25:40</div>")
         .Append("<div class='brand'><div class='brand-name'>Food For The Poor Jamaica</div><div class='report-name'>Canteen Summary</div></div>")
         .Append("<div class='address'>Ellerslie Pen - Spanish Town - St. Catherine - Jamaica</div>")
         .Append("<div class='range'>Report for Dates between ")
         .Append(E(r.Filter.From.ToDateTime(TimeOnly.MinValue).ToString("dddd, MMMM d, yyyy", CultureInfo.CurrentCulture)))
         .Append(" and ")
         .Append(E(r.Filter.To.ToDateTime(TimeOnly.MinValue).ToString("dddd, MMMM d, yyyy", CultureInfo.CurrentCulture)))
         .Append("</div>");

        b.Append("<table><thead><tr><th>Employee Name</th><th class='num'>Amt Used</th><th class='num'>Work Days</th><th class='num'>FFP Pmt</th><th class='num'>FFP Amt</th><th class='num'>Deduction</th></tr></thead><tbody>");
        string? department = null;
        decimal totalUsed = 0m, totalFfp = 0m, totalDeduction = 0m;
        foreach (var row in rows)
        {
            var nextDepartment = Convert.ToString(row.GetValueOrDefault("department"), CultureInfo.CurrentCulture) ?? "Unassigned";
            if (!string.Equals(department, nextDepartment, StringComparison.Ordinal))
            {
                department = nextDepartment;
                b.Append("<tr class='dept'><td colspan='6'>").Append(E(department)).Append("</td></tr>");
            }
            var amountUsed = Convert.ToDecimal(row.GetValueOrDefault("amountUsed") ?? 0m, CultureInfo.InvariantCulture);
            var ffpAmount = Convert.ToDecimal(row.GetValueOrDefault("ffpAmount") ?? 0m, CultureInfo.InvariantCulture);
            var deduction = Convert.ToDecimal(row.GetValueOrDefault("deduction") ?? 0m, CultureInfo.InvariantCulture);
            totalUsed += amountUsed; totalFfp += ffpAmount; totalDeduction += deduction;
            b.Append("<tr><td>").Append(E(Convert.ToString(row.GetValueOrDefault("employee"), CultureInfo.CurrentCulture) ?? ""))
             .Append("</td><td class='num'>").Append(E(amountUsed.ToString("N2", CultureInfo.CurrentCulture)))
             .Append("</td><td class='num'>").Append(E(FV(row.GetValueOrDefault("workDays"), "integer")))
             .Append("</td><td class='num'>").Append(E(Convert.ToDecimal(row.GetValueOrDefault("ffpPayment") ?? 0m, CultureInfo.InvariantCulture).ToString("N2", CultureInfo.CurrentCulture)))
             .Append("</td><td class='num'>").Append(E(ffpAmount.ToString("N2", CultureInfo.CurrentCulture)))
             .Append("</td><td class='num'>").Append(E(deduction.ToString("N2", CultureInfo.CurrentCulture))).Append("</td></tr>");
        }
        b.Append("<tr class='total'><td>Total :</td><td class='num'>").Append(E(totalUsed.ToString("N2", CultureInfo.CurrentCulture)))
         .Append("</td><td></td><td></td><td class='num'>").Append(E(totalFfp.ToString("N2", CultureInfo.CurrentCulture)))
         .Append("</td><td class='num'>").Append(E(totalDeduction.ToString("N2", CultureInfo.CurrentCulture))).Append("</td></tr></tbody></table>");
        b.Append("<div class='footer'><span>Date Printed : ").Append(E(r.GeneratedAt.LocalDateTime.ToString("dddd, MMMM d, yyyy h:mm tt", CultureInfo.CurrentCulture)))
         .Append("</span><span>Printed By ").Append(E(string.IsNullOrWhiteSpace(r.GeneratedBy) ? "Cafeteria" : r.GeneratedBy)).Append("</span></div></body></html>");
        return b.ToString();
    }

    private static string RenderChart(ReportChart c) => c.Type switch
    {
        ReportChartType.Pie => Pie(c),
        ReportChartType.Line => Cartesian(c,true),
        _ => Cartesian(c,false)
    };

    private static string Cartesian(ReportChart c, bool line)
    {
        const decimal W=900,H=250,L=55,R=15,T=10,B=45;
        var all=c.Series.SelectMany(s=>s.Values).ToArray(); var max=all.Length==0?1m:Math.Max(1m,all.Max());
        var plotW=W-L-R; var plotH=H-T-B; var n=Math.Max(1,c.Labels.Count);
        var b=new StringBuilder($"<svg viewBox='0 0 {W} {H}' role='img'>");
        for(var g=0;g<=4;g++){var y=T+plotH*g/4;b.Append($"<line x1='{L}' y1='{y}' x2='{W-R}' y2='{y}' stroke='#e7ebef'/>"); var val=max*(4-g)/4;b.Append($"<text x='{L-7}' y='{y+4}' text-anchor='end' font-size='10' fill='#677'>{E(F(val,c.Series.FirstOrDefault()?.Format??"number"))}</text>");}
        var palette=new[]{"#3867d6","#20bf6b","#f7b731","#eb3b5a","#8854d0"};
        for(var si=0;si<c.Series.Count;si++)
        {
            var s=c.Series[si]; var color=palette[si%palette.Length];
            if(line){var pts=new List<string>();for(var i=0;i<s.Values.Count;i++){var x=L+(n==1?plotW/2:plotW*i/(n-1));var y=T+plotH*(1-s.Values[i]/max);pts.Add($"{x:0.#},{y:0.#}");b.Append($"<circle cx='{x:0.#}' cy='{y:0.#}' r='3.5' fill='{color}'/>");}b.Append($"<polyline fill='none' stroke='{color}' stroke-width='3' points='{string.Join(' ',pts)}'/>");}
            else {var groupW=plotW/n;var barW=Math.Max(3m,groupW*.7m/Math.Max(1,c.Series.Count));for(var i=0;i<s.Values.Count;i++){var h=plotH*s.Values[i]/max;var x=L+i*groupW+groupW*.15m+si*barW;var y=T+plotH-h;b.Append($"<rect x='{x:0.#}' y='{y:0.#}' width='{barW:0.#}' height='{h:0.#}' rx='2' fill='{color}'/>");}}
        }
        for(var i=0;i<c.Labels.Count;i++){var x=L+(line?(n==1?plotW/2:plotW*i/(n-1)):plotW*(i+.5m)/n);b.Append($"<text x='{x:0.#}' y='{H-18}' text-anchor='middle' font-size='10' fill='#566'>{E(Short(c.Labels[i]))}</text>");}
        return b.Append("</svg>").ToString();
    }

    private static string Pie(ReportChart c)
    {
        var vals=c.Series.FirstOrDefault()?.Values??[];var total=vals.Sum();if(total<=0)return "<p>No data.</p>";
        const double cx=180,cy=125,r=95;var palette=new[]{"#3867d6","#20bf6b","#f7b731","#eb3b5a","#8854d0","#45aaf2"};var start=-Math.PI/2;var b=new StringBuilder("<svg viewBox='0 0 700 250'>");
        for(var i=0;i<vals.Count;i++){var frac=(double)(vals[i]/total);var end=start+frac*Math.PI*2;var x1=cx+r*Math.Cos(start);var y1=cy+r*Math.Sin(start);var x2=cx+r*Math.Cos(end);var y2=cy+r*Math.Sin(end);var large=frac>0.5?1:0;b.Append($"<path d='M {cx} {cy} L {x1:0.##} {y1:0.##} A {r} {r} 0 {large} 1 {x2:0.##} {y2:0.##} Z' fill='{palette[i%palette.Length]}'/>");start=end;var label=i<c.Labels.Count?c.Labels[i]:$"Item {i+1}";b.Append($"<rect x='390' y='{35+i*28}' width='12' height='12' fill='{palette[i%palette.Length]}'/><text x='410' y='{46+i*28}' font-size='12'>{E(label)} — {frac:P1}</text>");}
        return b.Append("</svg>").ToString();
    }

    private static bool IsNumeric(string f)=>f is "currency" or "integer" or "percent" or "number";
    private static string F(decimal v,string f)=>f switch{"currency"=>v.ToString("C2",CultureInfo.CurrentCulture),"integer"=>v.ToString("N0"),"percent"=>v.ToString("0.##")+"%",_=>v.ToString("N2")};
    private static string FV(object? v,string f)=>v switch{null=>"",decimal d=>F(d,f),double d=>F((decimal)d,f),int i=>f=="integer"?i.ToString("N0"):i.ToString(),DateTimeOffset d=>d.ToString("yyyy-MM-dd HH:mm"),DateOnly d=>d.ToString("yyyy-MM-dd"),_=>Convert.ToString(v,CultureInfo.CurrentCulture)??""};
    private static string Short(string s)=>s.Length<=14?s:s[..12]+"…";
    private static string E(string s)=>WebUtility.HtmlEncode(s);
}
