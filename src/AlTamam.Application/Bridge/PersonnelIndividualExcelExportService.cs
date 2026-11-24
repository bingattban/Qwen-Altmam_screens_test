using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AlTamam.Contracts.Bridge;
using AlTamam.Infrastructure.Excel.Reports;
using AlTamam.Infrastructure.Persistence;

namespace AlTamam.Application.Bridge;

/// <summary>
/// Export-only original screen_025 individual history: six INDEPENDENT
/// canonical pagers, status/month aggregates and immutable PersonId.
/// No new SQL, update/insert, reinterpreted identity or UI-owned path.
/// </summary>
public sealed class PersonnelIndividualExcelExportService(AlTamamDbContext database)
{
    private const int MaxRowsPerStream = 20000;
    private readonly PersonnelReportService _source = new(database);
    private static readonly string[] Streams =
        ["attendance", "movements", "identityHistory", "siteHistory", "revisions", "audit"];
    private static readonly string[] Totals =
        ["attendanceTotal", "movementTotal", "identityTotal",
         "siteTotal", "revisionTotal", "auditTotal"];
    private static readonly string[] Pages =
        ["attendancePage", "movementPage", "identityPage",
         "sitePage", "revisionPage", "auditPage"];
    private static readonly string[] More =
        ["attendanceHasNext", "movementHasNext", "identityHasNext",
         "siteHasNext", "revisionHasNext", "auditHasNext"];
    private static JsonObject Obj(object? node) =>
        node as JsonObject ?? throw new InvalidDataException("Invalid canonical individual history object.");
    private static JsonArray Arr(JsonObject o, string key) =>
        o[key] as JsonArray ?? throw new InvalidDataException("Canonical individual history array missing: "+key);
    private static string Txt(JsonObject o,string key) =>
        o[key]?.GetValue<string>() ?? throw new InvalidDataException("Original history text missing: "+key);
    private static long Num(JsonObject o,string key)
    {
        if(o[key] is not JsonValue node)
            throw new InvalidDataException("Original history numeric field missing: "+key);
        if(node.TryGetValue<long>(out long v))return v;
        if(node.TryGetValue<int>(out int v32))return v32;
        throw new InvalidDataException("Invalid original history numeric field "+key);
    }
    private static bool Flag(JsonObject o,string key) =>
        o[key]?.GetValue<bool>() ?? throw new InvalidDataException("Original history paging flag missing "+key);

    public static bool ValidRequest(JsonObject? input)
    {
        try
        {
            if(input is null)return false;
            string id=Txt(input,"personId");
            string from=Txt(input,"dateFrom"),to=Txt(input,"dateTo");
            string site=Txt(input,"siteId"),status=Txt(input,"statusId");
            int movement=checked((int)Num(input,"movementType"));
            static bool Iso(string value) => value.Length==0 ||
                DateOnly.TryParseExact(value,"yyyy-MM-dd",
                    CultureInfo.InvariantCulture,DateTimeStyles.None,out _);
            return id.Length is >=3 and <=128 && !id.Any(char.IsControl) &&
                site.Length<=128 && status.Length<=128 &&
                !site.Any(char.IsControl) && !status.Any(char.IsControl) &&
                Iso(from) && Iso(to) && (from=="" || to=="" ||
                    string.CompareOrdinal(from,to)<=0) &&
                movement is >=0 and <=4;
        }
        catch { return false; }
    }

    private static JsonObject Page(JsonObject input,int page)
    {
        var payload=(JsonObject)input.DeepClone();
        foreach(string name in Pages)payload[name]=page;
        return payload;
    }

    private async Task<BridgeResponse> Read(JsonObject request,int page,CancellationToken ct) =>
        await _source.ReportAsync(Page(request,page),ct).ConfigureAwait(false);

    public static string Fingerprint(JsonObject result)
    {
        var proof = new JsonObject
        {
            ["person"]=result["person"]?.DeepClone(),
            ["dateFrom"]=result["dateFrom"]?.DeepClone(),
            ["dateTo"]=result["dateTo"]?.DeepClone(),
            ["siteId"]=result["siteId"]?.DeepClone(),
            ["statusId"]=result["statusId"]?.DeepClone(),
            ["movementType"]=result["movementType"]?.DeepClone(),
            ["statusTotals"]=result["statusTotals"]?.DeepClone(),
            ["monthlyTotals"]=result["monthlyTotals"]?.DeepClone(),
            ["filterSites"]=result["filterSites"]?.DeepClone(),
            ["filterStatuses"]=result["filterStatuses"]?.DeepClone()
        };
        foreach(string key in Totals)proof[key]=result[key]?.DeepClone();
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(proof.ToJsonString()))).ToLowerInvariant();
    }

    private static void Verify(JsonObject source,JsonObject request,int page)
    {
        JsonObject person=Obj(source["person"]);
        if(Txt(person,"personId")!=Txt(request,"personId") ||
            Txt(source,"dateFrom")!=Txt(request,"dateFrom") ||
            Txt(source,"dateTo")!=Txt(request,"dateTo") ||
            Txt(source,"siteId")!=Txt(request,"siteId") ||
            Txt(source,"statusId")!=Txt(request,"statusId") ||
            Num(source,"movementType")!=Num(request,"movementType") ||
            Num(source,"pageSize")!=100 ||
            Arr(source,"statusTotals").Count>5000 ||
            Arr(source,"monthlyTotals").Count>5000 ||
            Arr(source,"filterSites").Count>500 ||
            Arr(source,"filterStatuses").Count>500)
            throw new InvalidDataException("Original individual report echo, selectors or totals invalid.");

        for(int i=0;i<Streams.Length;i++)
        {
            long total=Num(source,Totals[i]);
            if(total is <0 or >MaxRowsPerStream ||
                Num(source,Pages[i])!=page)
                throw new InvalidDataException("Unbounded original history or wrong page number.");
            var collection=Arr(source,Streams[i]);
            long expected=Math.Min(100,Math.Max(0,total-(long)(page-1)*100));
            if(collection.Count!=expected ||
               Flag(source,More[i])!=((long)page*100<total))
                throw new InvalidDataException("Canonical source history page incomplete.");
        }
        long attendanceTotal=Num(source,"attendanceTotal");
        var statuses=Arr(source,"statusTotals").Select(Obj);
        var monthly=Arr(source,"monthlyTotals").Select(Obj);
        if(statuses.Sum(row=>Num(row,"count"))!=attendanceTotal ||
           monthly.Sum(row=>Num(row,"count"))!=attendanceTotal)
            throw new InvalidDataException("Canonical historical totals do not reconcile.");
    }

    private static JsonObject Preview(JsonObject source)
    {
        var person=Obj(source["person"]);
        var result=new JsonObject
        {
            ["personId"]=Txt(person,"personId"),
            ["name"]=Txt(person,"name"),
            ["serviceNumberA"]=Txt(person,"serviceNumberA"),
            ["dateFrom"]=Txt(source,"dateFrom"),
            ["dateTo"]=Txt(source,"dateTo"),
            ["siteId"]=Txt(source,"siteId"),
            ["statusId"]=Txt(source,"statusId"),
            ["movementType"]=Num(source,"movementType"),
            ["fingerprint"]=Fingerprint(source),
            ["format"]="xlsx",
            ["suggestedFileName"]="تقرير_فردي_"+
                DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss",CultureInfo.InvariantCulture)+".xlsx"
        };
        foreach(string key in Totals)result[key]=Num(source,key);
        return result;
    }

    public async Task<BridgeResponse> PreviewAsync(JsonObject? request,CancellationToken ct)
    {
        if(!ValidRequest(request))
            return BridgeHelpers.Error(BridgeErrorCodes.ValidationError,
                "معرف الفرد أو مرشحات التقرير التاريخي غير صالحة.");
        var result=await Read(request!,1,ct).ConfigureAwait(false);
        if(!result.Success)return result;
        try
        {
            var data=Obj(result.Data);
            Verify(data,request!,1);
            return BridgeHelpers.Ok(Preview(data));
        }
        catch(Exception ex)when(ex is InvalidDataException or OverflowException or FormatException)
        {
            return BridgeHelpers.Error(BridgeErrorCodes.ValidationError,
                "تعذر اعتماد بيانات التقرير الفردي الأصلية؛ لا يمكن معاينة Excel.");
        }
    }

    /// <summary>
    /// Reads all six canonical history streams (not merely the displayed
    /// 100 rows) and rechecks every page byte-for-byte before commit.
    /// Never publishes an incomplete or changed source as a final .xlsx.
    /// </summary>
    public async Task<BridgeResponse> ExportAsync(
        JsonObject? request,string expectedHash,
        Func<CancellationToken,Task> authorize,CancellationToken ct)
    {
        if(!ValidRequest(request) || expectedHash is not {Length:64} ||
            expectedHash.Any(ch=>!char.IsAsciiHexDigit(ch)))
            return BridgeHelpers.Error(BridgeErrorCodes.ValidationError,
                "معاينة Excel الفردية غير صالحة أو منتهية.");

        var all = Streams.ToDictionary(k=>k,k=>new List<JsonObject>(),
            StringComparer.Ordinal);
        var pageSignatures=new List<string>();
        JsonObject first;
        long[] totals;
        string fingerprint;
        try
        {
            await authorize(ct).ConfigureAwait(false);
            var initial=await Read(request!,1,ct).ConfigureAwait(false);
            await authorize(ct).ConfigureAwait(false);
            if(!initial.Success)return initial;
            first=Obj(initial.Data);
            Verify(first,request!,1);
            fingerprint=Fingerprint(first);
            if(fingerprint!=expectedHash.ToLowerInvariant())
                throw new InvalidDataException("The report has changed after preview.");
            totals=Totals.Select(k=>Num(first,k)).ToArray();
            int pages=Math.Max(1,(int)((totals.Max()+99)/100));
            for(int page=1;page<=pages;page++)
            {
                ct.ThrowIfCancellationRequested();
                await authorize(ct).ConfigureAwait(false);
                JsonObject current;
                if(page==1)current=first;
                else
                {
                    var answer=await Read(request!,page,ct).ConfigureAwait(false);
                    if(!answer.Success)throw new InvalidDataException("Original history page missing.");
                    current=Obj(answer.Data);
                }
                await authorize(ct).ConfigureAwait(false);
                Verify(current,request!,page);
                if(Fingerprint(current)!=fingerprint)
                    throw new InvalidDataException("Person history changed during export.");
                pageSignatures.Add(PageSignature(current));
                foreach(string stream in Streams)
                    foreach(var node in Arr(current,stream))
                        all[stream].Add(Obj(node).DeepClone().AsObject());
            }
            for(int i=0;i<Streams.Length;i++)
                if(all[Streams[i]].Count!=totals[i])
                    throw new InvalidDataException("A historical stream was truncated.");
            // Re-read full page evidence before file publication to detect
            // corrections between the first and last concurrent queries.
            for(int page=1;page<=pages;page++)
            {
                ct.ThrowIfCancellationRequested();
                await authorize(ct).ConfigureAwait(false);
                var answer=await Read(request!,page,ct).ConfigureAwait(false);
                await authorize(ct).ConfigureAwait(false);
                if(!answer.Success)throw new InvalidDataException("History changed during final verification.");
                var current=Obj(answer.Data);
                Verify(current,request!,page);
                if(Fingerprint(current)!=fingerprint ||
                   PageSignature(current)!=pageSignatures[page-1])
                    throw new InvalidDataException("History changed after the original page read.");
            }
        }
        catch(Exception ex)when(ex is InvalidDataException or OverflowException or FormatException)
        {
            return BridgeHelpers.Error(BridgeErrorCodes.ExcelExportFailed,
                "تغير التقرير الفردي أو تجاوز حدود القراءة الآمنة؛ لم يتم إنشاء ملف ناقص.");
        }

        string folderInput;
        try { folderInput=await new OutputDirectorySettings(database).GetAsync(ct).ConfigureAwait(false); }
        catch(InvalidOperationException)
        {
            return BridgeHelpers.Error(BridgeErrorCodes.ExcelExportFailed,
                "مجلد تقارير Excel المعتمد غير متاح.");
        }
        if(!OutputDirectorySettings.IsSafeDirectory(folderInput,database.DatabasePath,out string folder) ||
           !OutputDirectorySettings.IsUsableDirectory(folderInput,database.DatabasePath))
            return BridgeHelpers.Error(BridgeErrorCodes.ExcelExportFailed,
                "اختر مجلد تقارير محليا متاحا خارج قاعدة البيانات.");
        string name="تقرير_فردي_"+
            DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss",CultureInfo.InvariantCulture)+
            "_"+Guid.NewGuid().ToString("N")[..10]+".xlsx";
        string file=Path.Combine(folder,name);
        string partial=file+".part";
        bool published=false,completed=false;
        try
        {
            ct.ThrowIfCancellationRequested();
            using(var output=new FileStream(partial,FileMode.CreateNew,
                FileAccess.Write,FileShare.None))
                PersonnelIndividualExcelWorkbook.Write(output,first,all);
            await authorize(ct).ConfigureAwait(false);
            ReportWorkbookCommit.VerifyFlushAndMove(partial,file,ct);
            published=true;
            await authorize(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            completed=true;
            var receipt=new JsonObject
            {
                ["filePath"]=file,
                ["format"]="xlsx",
                ["fingerprint"]=fingerprint,
                ["sheetCount"]=8
            };
            foreach(int i in Enumerable.Range(0,Totals.Length))
                receipt[Streams[i]+"Rows"]=totals[i];
            return BridgeHelpers.Ok(receipt);
        }
        catch(OperationCanceledException){throw;}
        catch(UnauthorizedAccessException){throw;}
        catch(Exception ex)when(ex is IOException or InvalidDataException
            or ArgumentException or System.Security.SecurityException)
        {
            return BridgeHelpers.Error(BridgeErrorCodes.ExcelExportFailed,
                "تعذر إنشاء Excel الفردي أو لم يجتز فحص السلامة؛ لم ينشر ملف ناقص.");
        }
        finally
        {
            if(published && !completed)
            {
                try{if(File.Exists(file))File.Delete(file);}catch(IOException){}
                catch(UnauthorizedAccessException){}
            }
            try{if(File.Exists(partial))File.Delete(partial);}catch(IOException){}
            catch(UnauthorizedAccessException){}
        }
    }

    private static string PageSignature(JsonObject page)
    {
        var proof=new JsonObject();
        foreach(string stream in Streams)
            proof[stream]=page[stream]?.DeepClone();
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(proof.ToJsonString()))).ToLowerInvariant();
    }
}
