using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;
using System.IO;
using System.Text.Json;

namespace MineirosRotinas;

public static class ProjectService
{
    private static readonly Guid DataGuid = new("23924792-75C6-488B-B234-B0620839855B");
    private static readonly Guid MarkGuid = new("A916060C-75D6-4550-8ABA-B50292483A50");
    private static Schema GetSchema(Guid guid, string name)
    {
        var schema = Schema.Lookup(guid);
        if (schema != null) return schema;
        var builder = new SchemaBuilder(guid);
        builder.SetSchemaName(name); builder.SetReadAccessLevel(AccessLevel.Public); builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField("Json", typeof(string)); return builder.Finish();
    }
    public static ClientData Read(Document doc)
    {
        var schema = Schema.Lookup(DataGuid);
        if (schema == null) return new ClientData();
        var entity = doc.ProjectInformation.GetEntity(schema);
        if (!entity.IsValid()) return new ClientData();
        return JsonSerializer.Deserialize<ClientData>(entity.Get<string>(schema.GetField("Json"))) ?? new ClientData();
    }
    public static void Store(Document doc, ClientData data)
    {
        var schema = GetSchema(DataGuid, "MineirosRotinasProjectV1");
        var entity = new Entity(schema); entity.Set(schema.GetField("Json"), JsonSerializer.Serialize(data));
        doc.ProjectInformation.SetEntity(entity);
    }
    public static void Mark(Element element, string text)
    {
        var schema = GetSchema(MarkGuid, "MineirosRotinasElementV1");
        var entity = new Entity(schema); entity.Set(schema.GetField("Json"), text); element.SetEntity(entity);
    }
    public static string MarkOf(Element element)
    {
        var schema = Schema.Lookup(MarkGuid); if (schema == null) return "";
        var entity = element.GetEntity(schema); return entity.IsValid() ? entity.Get<string>(schema.GetField("Json")) : "";
    }
    public static double M(double meters) => UnitUtils.ConvertToInternalUnits(meters, UnitTypeId.Meters);
    public static double Paper(double millimeters) => UnitUtils.ConvertToInternalUnits(millimeters, UnitTypeId.Millimeters);
    public static void Editable(Document doc)
    {
        if (doc.IsReadOnly || doc.IsLinked || doc.IsFamilyDocument) throw new InvalidOperationException("Escolha um projeto editável.");
    }
    public static T Change<T>(Document doc, string title, Func<T> action)
    {
        Editable(doc);
        using var tx = new Transaction(doc, "BIM Rotinas · " + title);
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Não foi possível iniciar a operação.");
        var failures = new Failures();
        tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(failures).SetClearAfterRollback(true));
        try
        {
            var result = action();
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("O Revit reverteu a operação: " + string.Join("; ", failures.Errors));
            return result;
        }
        catch { if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack(); throw; }
    }
    public sealed class Failures : IFailuresPreprocessor
    {
        public List<string> Errors { get; } = new();
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            foreach (var failure in accessor.GetFailureMessages())
            {
                if (failure.GetSeverity() == FailureSeverity.Error) Errors.Add(failure.GetDescriptionText());
                // Warnings stay visible in Revit; the module never suppresses model warnings.
            }
            return Errors.Count > 0 ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }
    public static Catalog Catalog(UIApplication app)
    {
        var doc = app.ActiveUIDocument.Document;
        var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate).ToList();
        return new Catalog
        {
            Title = doc.Title, ActiveViewId = doc.ActiveView.Id.Value, Client = Read(doc),
            Views = views.Where(v => v.ViewType is ViewType.FloorPlan or ViewType.CeilingPlan or ViewType.Section or ViewType.Elevation or ViewType.DraftingView or ViewType.AreaPlan)
                .OrderBy(v => v.Name).Select(v => new Choice { Id = v.Id.Value, Name = v.Name, Kind = v.ViewType.ToString(), Include = v.Id == doc.ActiveView.Id }).ToList(),
            Sheets = views.OfType<ViewSheet>().Where(s => !s.IsPlaceholder && s.CanBePrinted).OrderBy(s => s.SheetNumber)
                .Select(s => new Choice { Id = s.Id.Value, Name = s.SheetNumber + " — " + s.Name, Include = true }).ToList(),
            TitleBlocks = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType().OfType<FamilySymbol>()
                .Select(t => new Choice { Id = t.Id.Value, Name = t.FamilyName + " · " + t.Name }).ToList()
        };
    }
    public static View View(Document doc, long id) => doc.GetElement(new ElementId(id)) as View ?? throw new InvalidOperationException("Vista removida. Atualize o projeto.");
    public static string UniqueName(Document doc, string name)
    {
        // Cortes gerados pela rotina usam somente o identificador A/B/C como nome da vista.
        if (name.StartsWith("Corte ", StringComparison.OrdinalIgnoreCase) && name.Length > 6 && char.IsLetter(name[6]))
            name = name.Substring(6, 1).ToUpperInvariant();
        var names = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string candidate = name; int i = 2; while (names.Contains(candidate)) candidate = name + " (" + i++ + ")"; return candidate;
    }
    public static string Fingerprint(Document doc)
    {
        var data = Read(doc);
        var details = JsonSerializer.Serialize(new { data.Client, data.Code, data.Project, data.Address, data.District, data.Block, data.Lot, data.Registry, data.Technician, data.ArtRrt, data.LandArea, data.Revision });
        // Revit VersionGuid also tracks changes to native annotations, views, sheets and types.
        var versions = new FilteredElementCollector(doc).WherePasses(new LogicalOrFilter(new ElementIsElementTypeFilter(), new ElementIsElementTypeFilter(true)))
            .Where(e => e.Id != doc.ProjectInformation.Id).OrderBy(e => e.Id.Value).Select(e => $"{e.Id.Value}:{e.VersionGuid}");
        var info = doc.ProjectInformation;
        var nativeInfo=string.Join(";",info.Parameters.Cast<Parameter>().OrderBy(p=>p.Id.Value).Select(p=>p.Id.Value+":"+(p.StorageType switch
        { StorageType.String=>p.AsString(),StorageType.Integer=>p.AsInteger().ToString(),StorageType.Double=>p.AsDouble().ToString("R",System.Globalization.CultureInfo.InvariantCulture),StorageType.ElementId=>p.AsElementId().Value.ToString(),_=>"" })));
        return Files.HashText(ModelChanges.Token(doc) + "|" + details + "|" + nativeInfo + "|" + string.Join(";", versions));
    }
    public static string SaveClient(Document doc, ClientData data)
    {
        _ = Files.ProjectFolder(data);
        if (data.LandArea < 0 || !double.IsFinite(data.LandArea)) throw new InvalidOperationException("Área do terreno inválida.");
        var previous = Read(doc);
        if (previous.Emissions.Count > 0 && (data.Client != previous.Client || data.Code != previous.Code))
            throw new InvalidOperationException("Este cadastro já possui emissões. Para outro cliente, crie um novo projeto a partir do modelo de projeto.");
        data.Emissions = previous.Emissions; data.ReviewStamp = ""; data.ReviewedSheets = new();
        return Change(doc, "cadastro", () =>
        {
            doc.ProjectInformation.ClientName = data.Client;
            doc.ProjectInformation.Name = data.Project;
            doc.ProjectInformation.Number = data.Code;
            doc.ProjectInformation.Address = data.Address;
            Store(doc, data); return "Cadastro gravado no modelo. Carimbos com parâmetros de projeto serão atualizados pelo Revit.";
        });
    }
    public static List<CheckRow> Checklist(Document doc)
    {
        var rows = new List<CheckRow>(); var data = Read(doc);
        void Add(bool ok, string item, string detail, bool blocking = false) => rows.Add(new CheckRow { Status = ok ? "Conferido" : blocking ? "Pendente" : "Revisar", Item = item, Detail = detail });
        Add(!string.IsNullOrWhiteSpace(data.Client) && !string.IsNullOrWhiteSpace(data.Code), "Cadastro", "Cliente e código do projeto obrigatórios.", true);
        Add(!string.IsNullOrWhiteSpace(data.Address), "Endereço", "Conferir também bairro, quadra e lote.");
        Add(!string.IsNullOrWhiteSpace(data.Technician) && !string.IsNullOrWhiteSpace(data.ArtRrt), "Responsável técnico", "Dados informados; autenticidade de ART/RRT exige conferência profissional.");
        var rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().OfType<Room>().ToList();
        Add(rooms.Count > 0 && rooms.All(r => r.Area > 0 && r.Location != null), "Ambientes fechados", $"{rooms.Count} ambientes; {rooms.Count(r => r.Area <= 0 || r.Location == null)} sem área/posição.");
        Add(rooms.All(r => !string.IsNullOrWhiteSpace(r.Name)), "Identificação de ambientes", "Verificação dos nomes cadastrados; identificação em cada vista deve ser revisada.");
        var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate).ToList();
        foreach (var label in new[] { "A", "B", "C" }) Add(views.Any(v => v.ViewType == ViewType.Section && (MarkOf(v).StartsWith("section:" + label + ":") || v.Name.Contains("Corte " + label, StringComparison.OrdinalIgnoreCase))), "Corte " + label, "Confirmar posição, direção, profundidade e cotas.");
        Add(views.Any(v => v.ViewType == ViewType.Elevation), "Fachadas", "Vistas existentes; conferir materiais, níveis e enquadramento.");
        Add(new FilteredElementCollector(doc).OfClass(typeof(RoofBase)).Any(), "Cobertura modelada", "Uma vista chamada Telhado não comprova cobertura modelada.");
        Add(views.Any(v => MarkOf(v).StartsWith("map:") && v.CropBoxActive), "Localização DWG recortada", "Recorte retangular aplicado à vista de localização criada pelo módulo.");
        var sheets = views.OfType<ViewSheet>().Where(s => !s.IsPlaceholder).ToList();
        Add(sheets.Count > 0, "Pranchas", $"{sheets.Count} pranchas disponíveis.", true);
        foreach (var sheet in sheets)
        {
            Add(new FilteredElementCollector(doc, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).Any(), "Carimbo · " + sheet.SheetNumber, sheet.Name);
            Add(sheet.GetAllPlacedViews().Count > 0 || new FilteredElementCollector(doc, sheet.Id).OfClass(typeof(ScheduleSheetInstance)).Any(), "Conteúdo · " + sheet.SheetNumber, "Conferir legibilidade, escala, margens e sobreposições.");
        }
        Add(doc.GetWarnings().Count == 0, "Avisos do modelo", $"{doc.GetWarnings().Count} avisos do Revit. Conferir em Gerenciar → Revisar avisos.");
        rows.Add(new CheckRow { Status = "Manual", Item = "Revisão técnica", Detail = "Recuos, índices urbanísticos, ventilação, norte, validade do mapa e conformidade municipal não são certificados pelo plugin." });
        return rows;
    }
    public static string Preview(Document doc, long viewId)
    {
        var view = View(doc, viewId); if (!view.CanBePrinted) throw new InvalidOperationException("Essa vista não admite prévia.");
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MineirosRotinas", "preview");
        Directory.CreateDirectory(root); var stem = Path.Combine(root, Files.NewId());
        using var options = new ImageExportOptions { FilePath = stem, ExportRange = ExportRange.SetOfViews, HLRandWFViewsFileType = ImageFileType.PNG,
            ShadowViewsFileType = ImageFileType.PNG, ZoomType = ZoomFitType.FitToPage, PixelSize = 1800, FitDirection = FitDirectionType.Horizontal };
        options.SetViewsAndSheets(new List<ElementId> { view.Id });
        // White paper preview, independent of the user's dark canvas. Restore the exact preference even on failure.
        var background=doc.Application.BackgroundColor;
        try{doc.Application.BackgroundColor=new Autodesk.Revit.DB.Color(255,255,255);doc.ExportImage(options);}
        finally{doc.Application.BackgroundColor=background;}
        return Directory.GetFiles(root, Path.GetFileName(stem) + "*.png").FirstOrDefault() ?? throw new IOException("A prévia não foi produzida pelo Revit.");
    }
    public static string Review(Document doc, string reviewer, IList<long> sheets)
    {
        if (string.IsNullOrWhiteSpace(reviewer) || sheets.Count == 0) throw new InvalidOperationException("Informe quem revisou e selecione as pranchas para emissão.");
        if (Checklist(doc).Any(r => r.Status == "Pendente")) throw new InvalidOperationException("Complete o cadastro e as pranchas antes de registrar a revisão.");
        foreach (var id in sheets) if (View(doc, id) is not ViewSheet s || !s.CanBePrinted) throw new InvalidOperationException("Seleção contém uma prancha inválida.");
        var data = Read(doc); data.ReviewStamp = Fingerprint(doc); data.ReviewBy = reviewer.Trim(); data.ReviewDate = DateTimeOffset.Now.ToString("O"); data.ReviewedSheets = sheets.Order().ToList();
        return Change(doc, "revisão profissional", () => { Store(doc, data); return "Revisão registrada para as pranchas selecionadas. Qualquer alteração posterior exige nova revisão."; });
    }
    public static Emission Export(Document doc, IList<long> sheetIds)
    {
        var data = Read(doc); var stamp = Fingerprint(doc);
        if (string.IsNullOrEmpty(data.ReviewStamp) || data.ReviewStamp != stamp || !data.ReviewedSheets.SequenceEqual(sheetIds.Order()))
            throw new InvalidOperationException("O modelo ou a seleção de pranchas mudou. Registre novamente a revisão profissional.");
        var sheets = sheetIds.Select(id => View(doc, id) as ViewSheet ?? throw new InvalidOperationException("Prancha inválida.")).OrderBy(s => s.SheetNumber).ToList();
        if (sheets.Count == 0 || sheets.Any(s => !s.CanBePrinted)) throw new InvalidOperationException("Selecione pranchas imprimíveis.");
        var root = Files.CreateFolders(data); var id = Files.NewId(); var directory = Path.Combine(root, "03_PDF", data.Revision.Length > 0 ? Files.SafeName(data.Revision) + "_" + id : id);
        Directory.CreateDirectory(directory); var fileName = Files.SafeName(data.Code) + "_" + Files.SafeName(data.Revision);
        using var options = new PDFExportOptions { Combine = true, FileName = fileName, StopOnError = true };
        if (!doc.Export(directory, sheets.Select(s => s.Id).ToList(), options)) throw new IOException("O Revit não confirmou a emissão. Verifique a pasta de saída antes de repetir.");
        var pdf = Path.Combine(directory, fileName + ".pdf");
        if (!File.Exists(pdf) || new FileInfo(pdf).Length == 0) throw new IOException("PDF ausente ou vazio. Emissão não registrada.");
        var emission = new Emission { Id = id, Client = data.Client, Code = data.Code, Revision = data.Revision, Date = DateTimeOffset.Now.ToString("O"), Pdf = pdf, Hash = Files.Hash(pdf),
            ModelPath = doc.PathName, Fingerprint = stamp, Reviewer = data.ReviewBy, ReviewDate = data.ReviewDate, Sheets = sheets.Select(s => s.SheetNumber + " — " + s.Name).ToList() };
        // Persist the external audit record before associating it with the model, for recoverability.
        Files.WriteNew(Path.Combine(directory, "emissao.json"), emission);
        Files.WriteNew(Path.Combine(directory, "cadastro.json"), new { data.Client, data.Code, data.Project, data.Address, data.District, data.Block, data.Lot, data.Registry, data.Technician, data.ArtRrt, data.LandArea, data.Revision });
        data.Emissions.Add(emission);
        try { Change(doc, "registrar emissão", () => { Store(doc, data); return true; }); }
        catch (Exception ex) { throw new IOException("PDF e registro foram gravados em " + directory + ", mas não foi possível vincular ao modelo: " + ex.Message, ex); }
        return emission;
    }
    public static string Protocol(Document doc, Emission emission, string number, DateTime date, string note)
    {
        var data = Read(doc);
        if (string.IsNullOrWhiteSpace(number)) throw new InvalidOperationException("Informe o número do protocolo recebido da Prefeitura.");
        if (!data.Emissions.Any(e => e.Id == emission.Id && e.Hash == emission.Hash)) throw new InvalidOperationException("Emissão não pertence a este projeto.");
        if (!File.Exists(emission.Pdf) || Files.Hash(emission.Pdf) != emission.Hash) throw new InvalidOperationException("O PDF emitido foi removido ou alterado. Selecione a versão original.");
        var destination = Path.Combine(Files.CreateFolders(data), "04_Protocolo", emission.Id);
        Directory.CreateDirectory(destination); var copy = Path.Combine(destination, Path.GetFileName(emission.Pdf));
        if (!File.Exists(copy)) File.Copy(emission.Pdf, copy, false);
        else if (Files.Hash(copy) != emission.Hash) throw new IOException("Já existe uma cópia de protocolo diferente. Nada foi sobrescrito.");
        var record = Path.Combine(destination, "protocolo_" + Files.NewId() + ".json");
        Files.WriteNew(record, new { Protocol = number.Trim(), ProtocolDate = date.ToString("yyyy-MM-dd"), RegisteredAt = DateTimeOffset.Now, Notes = note, PdfCopy = copy, Emission = emission });
        return "Registro local de protocolo: " + record + "\nNenhum documento foi enviado à Prefeitura.";
    }
}
