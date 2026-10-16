using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System.IO;

namespace MineirosRotinas;

public static class DrawingService
{
    public static List<Element> Building(Document doc, View? view = null)
    {
        var collector = view == null ? new FilteredElementCollector(doc) : new FilteredElementCollector(doc, view.Id);
        return collector.WherePasses(new ElementMulticategoryFilter(new[] { BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Roofs, BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows, BuiltInCategory.OST_StructuralColumns })).WhereElementIsNotElementType().ToList();
    }
    public static (XYZ Min, XYZ Max) Bounds(IEnumerable<Element> elements, View? view = null)
    {
        var points = elements.Select(e => e.get_BoundingBox(view)).Where(b => b != null).SelectMany(b => Corners(b!)).ToList();
        if (points.Count == 0) throw new InvalidOperationException("Não há geometria arquitetônica suficiente nesta vista. Escolha a planta que contém o projeto.");
        return (new XYZ(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z)), new XYZ(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z)));
    }
    public static IEnumerable<XYZ> Corners(BoundingBoxXYZ box)
    {
        foreach (var x in new[] { box.Min.X, box.Max.X }) foreach (var y in new[] { box.Min.Y, box.Max.Y }) foreach (var z in new[] { box.Min.Z, box.Max.Z }) yield return box.Transform.OfPoint(new XYZ(x, y, z));
    }
    public static ViewPlan Plan(Document doc, long id)
    {
        var plan = ProjectService.View(doc, id) as ViewPlan;
        if (plan == null || plan.ViewType != ViewType.FloorPlan || plan.GenLevel == null) throw new InvalidOperationException("Escolha uma planta baixa de piso.");
        return plan;
    }
    public static void Activate(UIApplication app, View view)
    {
        if (view.IsTemplate) throw new InvalidOperationException("Escolha uma vista, não um template.");
        app.ActiveUIDocument.ActiveView = view;
    }
    public static string Sections(UIApplication app, long planId, bool manual, double height, double depth, int scale)
    {
        var doc = app.ActiveUIDocument.Document; var plan = Plan(doc, planId);
        if (!manual) return AutomaticSections(doc, planId, height, depth, scale);
        var lines = new List<(XYZ A, XYZ B)>(); var z = plan.GenLevel.Elevation;
        Activate(app, plan);
        foreach (var name in new[] { "A", "B", "C" })
        {
            var a = app.ActiveUIDocument.Selection.PickPoint(ObjectSnapTypes.Endpoints | ObjectSnapTypes.Intersections, "Corte " + name + ": primeiro ponto. Esc cancela os três cortes.");
            var b = app.ActiveUIDocument.Selection.PickPoint(ObjectSnapTypes.Endpoints | ObjectSnapTypes.Intersections, "Corte " + name + ": segundo ponto. A direção depende da ordem dos pontos.");
            lines.Add((new XYZ(a.X, a.Y, z), new XYZ(b.X, b.Y, z)));
        }
        return CreateSections(doc, plan, lines, height, depth, scale);
    }
    public static string AutomaticSections(Document doc, long planId, double height, double depth, int scale)
    {
        var plan = Plan(doc, planId);
        if (height < 1 || height > 200 || depth < 0.1 || depth > 2000 || scale < 1) throw new InvalidOperationException("Confira altura, profundidade e escala dos cortes.");
        var bounds = Bounds(Building(doc, plan)); var z = plan.GenLevel.Elevation;
        var cx = (bounds.Min.X + bounds.Max.X) / 2; var cy = (bounds.Min.Y + bounds.Max.Y) / 2;
        var margin = ProjectService.M(1); var lines = new List<(XYZ A, XYZ B)>();
            lines.Add((new XYZ(cx, bounds.Min.Y - margin, z), new XYZ(cx, bounds.Max.Y + margin, z)));
            lines.Add((new XYZ(bounds.Min.X - margin, cy, z), new XYZ(bounds.Max.X + margin, cy, z)));
            var thirdY = bounds.Min.Y + (bounds.Max.Y - bounds.Min.Y) * .75;
            lines.Add((new XYZ(bounds.Min.X - margin, thirdY, z), new XYZ(bounds.Max.X + margin, thirdY, z)));
        return CreateSections(doc, plan, lines, height, depth, scale);
    }
    private static string CreateSections(Document doc, ViewPlan plan, List<(XYZ A, XYZ B)> lines, double height, double depth, int scale)
    {
        if (height < 1 || height > 200 || depth < 0.1 || depth > 2000 || scale < 1) throw new InvalidOperationException("Confira altura, profundidade e escala dos cortes.");
        var sectionTypes = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
            .Where(t => t.ViewFamily == ViewFamily.Section).ToList();
        var type = sectionTypes.FirstOrDefault(t => string.Equals(t.Name, "Corte XX", StringComparison.OrdinalIgnoreCase))
            ?? sectionTypes.FirstOrDefault(t => t.Name.Contains("Corte", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("O modelo não possui um tipo de corte. Carregue o tipo “Corte XX” e tente novamente.");
        return ProjectService.Change(doc, "cortes A/B/C", () =>
        {
            var result = new List<string>();
            for (var i = 0; i < 3; i++)
            {
                var letter = ((char)('A' + i)).ToString(); var key = "section:" + letter + ":" + plan.UniqueId;
                var existing = new FilteredElementCollector(doc).OfClass(typeof(ViewSection)).Cast<ViewSection>().FirstOrDefault(v => ProjectService.MarkOf(v) == key);
                if (existing != null)
                {
                    var shortName = SectionShortName(doc, letter, existing.Id);
                    if (!string.Equals(existing.Name, shortName, StringComparison.Ordinal)) existing.Name = shortName;
                }
                if (existing != null) { result.Add(existing.Name + ": já existe; preservado. Ajuste sua linha de corte no Revit."); continue; }
                var (a, b) = lines[i]; var vector = b - a; if (vector.GetLength() < ProjectService.M(.2)) throw new InvalidOperationException("Pontos do corte " + letter + " muito próximos.");
                var t = Transform.Identity; t.Origin = (a + b) * .5; t.BasisX = vector.Normalize(); t.BasisY = XYZ.BasisZ; t.BasisZ = t.BasisX.CrossProduct(t.BasisY);
                var box = new BoundingBoxXYZ { Transform = t, Min = new XYZ(-vector.GetLength() / 2, -ProjectService.M(1), 0), Max = new XYZ(vector.GetLength() / 2, ProjectService.M(height), ProjectService.M(depth)) };
                var section = ViewSection.CreateSection(doc, type.Id, box);
                section.Name = ProjectService.UniqueName(doc, "Corte " + letter + " — " + plan.Name); section.Scale = scale;
                ProjectService.Mark(section, key); HideMaps(doc, section); result.Add(section.Name + ": criado.");
            }
            return string.Join("\n", result) + "\nTipo aplicado: “" + type.Name + "”. A/B/C usam as posições inteligentes da planta; confira sentido, níveis e cotas nas vistas geradas.";
        });
    }
    private static string SectionShortName(Document doc, string letter, ElementId currentId)
    {
        var nameTaken = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
            .Any(v => v.Id != currentId && string.Equals(v.Name, letter, StringComparison.OrdinalIgnoreCase));
        return nameTaken ? ProjectService.UniqueName(doc, letter) : letter;
    }
    public static string Facades(Document doc, long planId, int scale)
    {
        var plan = Plan(doc, planId); var bounds = Bounds(Building(doc, plan));
        var type = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().FirstOrDefault(t => t.ViewFamily == ViewFamily.Elevation)
            ?? throw new InvalidOperationException("Tipo de elevação ausente.");
        return ProjectService.Change(doc, "fachadas", () =>
        {
            var created = new List<string>();
            // One marker outside each side points inward at the building, rather than looking out from its center.
            var center = (bounds.Min + bounds.Max) * .5; var offset = ProjectService.M(2);
            var origins = new[] { new XYZ(bounds.Min.X-offset, center.Y, plan.GenLevel.Elevation), new XYZ(center.X,bounds.Min.Y-offset,plan.GenLevel.Elevation),
                new XYZ(bounds.Max.X+offset,center.Y,plan.GenLevel.Elevation), new XYZ(center.X,bounds.Max.Y+offset,plan.GenLevel.Elevation) };
            for (var i = 0; i < 4; i++)
            {
                var key = "facade:" + i + ":" + plan.UniqueId;
                if (new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Any(v => ProjectService.MarkOf(v) == key)) { created.Add("Fachada " + (i + 1) + ": já existe."); continue; }
                var marker = ElevationMarker.CreateElevationMarker(doc, type.Id, origins[i], scale);
                var elevation = marker.CreateElevation(doc, plan.Id, 0);
                var desired = new XYZ(center.X - origins[i].X, center.Y - origins[i].Y, 0).Normalize();
                var current = elevation.ViewDirection;
                var angle = Math.Atan2(current.CrossProduct(desired).Z, current.DotProduct(desired));
                ElementTransformUtils.RotateElement(doc, marker.Id, Line.CreateBound(origins[i], origins[i] + XYZ.BasisZ), angle);
                doc.Regenerate();
                elevation.Name = ProjectService.UniqueName(doc, "Fachada " + (i + 1) + " — " + plan.Name);
                var inv = elevation.CropBox.Transform.Inverse;
                var allBounds = Bounds(Building(doc));
                var pts = new[] { allBounds.Min, allBounds.Max, new XYZ(allBounds.Min.X,allBounds.Max.Y,allBounds.Min.Z), new XYZ(allBounds.Max.X,allBounds.Min.Y,allBounds.Max.Z) }.Select(inv.OfPoint).ToList();
                var crop = elevation.CropBox; crop.Min = new XYZ(pts.Min(p=>p.X)-offset,pts.Min(p=>p.Y)-offset,crop.Min.Z); crop.Max = new XYZ(pts.Max(p=>p.X)+offset,pts.Max(p=>p.Y)+offset,crop.Max.Z);
                elevation.CropBox = crop; elevation.CropBoxActive = true;
                elevation.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR)?.Set((bounds.Max - bounds.Min).GetLength() + offset * 3);
                ProjectService.Mark(elevation, key); HideMaps(doc, elevation); created.Add(elevation.Name + ": criada.");
            }
            return string.Join("\n", created);
        });
    }
    public static void HideMaps(Document doc, View view)
    {
        var ids = new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).Where(e => ProjectService.MarkOf(e).StartsWith("map-import:") && e.CanBeHidden(view) && !e.IsHidden(view)).Select(e => e.Id).ToList();
        if (ids.Count > 0) view.HideElements(ids);
    }
    public static long ImportMap(UIApplication app, long planId, string path, ImportUnit unit, int scale)
    {
        var doc = app.ActiveUIDocument.Document;
        var mapId = ImportMapDocument(doc, planId, path, unit, scale);
        Activate(app, ProjectService.View(doc, mapId));
        app.ActiveUIDocument.GetOpenUIViews().FirstOrDefault(v => v.ViewId.Value == mapId)?.ZoomToFit();
        return mapId;
    }
    public static long ImportMapDocument(Document doc, long planId, string path, ImportUnit unit, int scale)
    {
        var plan = Plan(doc, planId); var data = ProjectService.Read(doc);
        if (!File.Exists(path) || !Path.GetExtension(path).Equals(".dwg", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Selecione um arquivo DWG existente.");
        var mapId = ProjectService.Change(doc, "carregar mapa DWG", () =>
        {
            // A localização pode ser preparada antes do cadastro completo; o checklist continua
            // exigindo cliente e código para a emissão. Use uma chave estável quando o cadastro
            // ainda estiver vazio para evitar duplicar a vista ao atualizar o painel.
            var projectKey = string.IsNullOrWhiteSpace(data.Code)
                ? Files.HashText((doc.PathName ?? "") + "|" + doc.Title).Substring(0, 12)
                : data.Code;
            var marker = "map:" + projectKey;
            var existing = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().FirstOrDefault(v => ProjectService.MarkOf(v) == marker);
            if (existing != null) throw new InvalidOperationException("Já existe uma localização para este projeto. Selecione-a na lista para ajustar o recorte.");
            var view = ViewPlan.Create(doc, plan.GetTypeId(), plan.GenLevel.Id);
            var label = string.IsNullOrWhiteSpace(data.Client) ? doc.Title : data.Client;
            var codeLabel = string.IsNullOrWhiteSpace(data.Code) ? "rascunho" : data.Code;
            view.Name = ProjectService.UniqueName(doc, "LOC — " + label + " — " + codeLabel); view.Scale = scale; view.ViewTemplateId = ElementId.InvalidElementId;
            view.CropBoxActive = false; ProjectService.Mark(view, marker);
            using var options = new DWGImportOptions { Unit = unit, ThisViewOnly = false, Placement = ImportPlacement.Centered, ColorMode = ImportColorMode.BlackAndWhite, OrientToView = false };
            if (!doc.Import(path, options, view, out var id)) throw new InvalidOperationException("Revit não conseguiu carregar o DWG. Verifique formato, unidades e coordenadas.");
            var imported = (ImportInstance)doc.GetElement(id); ProjectService.Mark(imported, "map-import:" + view.UniqueId); imported.Pinned = true; doc.Regenerate();
            // Model import is used so Revit's MODEL crop actually clips the CAD geometry.
            // Isolate it in its new map view and hide the import from all existing other model views.
            foreach (var other in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate && v.Id != view.Id))
                if (imported.CanBeHidden(other)) other.HideElements(new List<ElementId> { imported.Id });
            var hide = new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType().Where(e => e.Id != id && e.Id != view.Id && e.CanBeHidden(view)).Select(e => e.Id).ToList();
            if (hide.Count > 0) view.HideElements(hide);
            // Keep the view plane on the project's existing level; imported 2D geometry is placed on that level.
            return view.Id.Value;
        });
        return mapId;
    }
    public static string CropMap(UIApplication app, long viewId, int scale)
    {
        var doc = app.ActiveUIDocument.Document; var view = ProjectService.View(doc, viewId);
        if (!ProjectService.MarkOf(view).StartsWith("map:")) throw new InvalidOperationException("Escolha a vista LOC criada pelo módulo.");
        Activate(app, view);
        var rectangle = app.ActiveUIDocument.Selection.PickBox(PickBoxStyle.Crossing, "Arraste um retângulo ao redor do lote e das ruas desejadas. Esc cancela.");
        return SetMapCrop(doc, view.Id.Value, rectangle.Min, rectangle.Max, scale);
    }
    public static bool FocusMap(UIApplication app, long viewId)
    {
        var doc=app.ActiveUIDocument.Document;var view=ProjectService.View(doc,viewId);
        if(!ProjectService.MarkOf(view).StartsWith("map:") && !view.Name.StartsWith("LOC",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Escolha uma vista de localização criada pelo módulo.");
        Activate(app,view);
        var uiView=app.ActiveUIDocument.GetOpenUIViews().FirstOrDefault(v=>v.ViewId==view.Id);
        var import=new FilteredElementCollector(doc,view.Id).OfClass(typeof(ImportInstance)).Cast<ImportInstance>()
            .FirstOrDefault(i=>ProjectService.MarkOf(i)=="map-import:"+view.UniqueId);
        var bounds=import?.get_BoundingBox(view);
        if(uiView!=null && bounds!=null)
        {
            var dx=Math.Max(bounds.Max.X-bounds.Min.X,ProjectService.M(1));
            var dy=Math.Max(bounds.Max.Y-bounds.Min.Y,ProjectService.M(1));
            var margin=Math.Max(dx,dy)*.04;
            uiView.ZoomAndCenterRectangle(new XYZ(bounds.Min.X-margin,bounds.Min.Y-margin,bounds.Min.Z),new XYZ(bounds.Max.X+margin,bounds.Max.Y+margin,bounds.Max.Z));
        }
        else uiView?.ZoomToFit();
        return true;
    }
    public static string SetMapCrop(Document doc, long viewId, XYZ a, XYZ b, int scale)
    {
        var view = ProjectService.View(doc, viewId);
        if (!ProjectService.MarkOf(view).StartsWith("map:")) throw new InvalidOperationException("Vista não pertence à localização do módulo.");
        var inverse = view.CropBox.Transform.Inverse; var p = inverse.OfPoint(a); var q = inverse.OfPoint(b);
        if (Math.Abs(p.X - q.X) < ProjectService.M(.5) || Math.Abs(p.Y - q.Y) < ProjectService.M(.5)) throw new InvalidOperationException("Marque um retângulo maior.");
        return ProjectService.Change(doc, "recorte retangular DWG", () =>
        {
            var crop = view.CropBox; crop.Min = new XYZ(Math.Min(p.X,q.X),Math.Min(p.Y,q.Y),crop.Min.Z); crop.Max = new XYZ(Math.Max(p.X,q.X),Math.Max(p.Y,q.Y),crop.Max.Z);
            view.CropBox = crop; view.CropBoxActive = true; view.CropBoxVisible = false; view.Scale = scale;
            return "Recorte aplicado em " + view.Name + ". Insira esta vista na prancha. O arquivo DWG original foi preservado.";
        });
    }
    public static string Sheets(Document doc, IList<long> viewIds, long titleBlockId, long destinationId)
    {
        if (viewIds.Count == 0) throw new InvalidOperationException("Marque ao menos uma vista.");
        var titleBlock = doc.GetElement(new ElementId(titleBlockId)) as FamilySymbol;
        if (titleBlock == null || titleBlock.Category.Id.Value != (long)BuiltInCategory.OST_TitleBlocks) throw new InvalidOperationException("Escolha uma família de carimbo carregada.");
        return ProjectService.Change(doc, "atualizar pranchas", () =>
        {
            var reports = new List<string>();
            foreach (var viewId in viewIds)
            {
                var view = ProjectService.View(doc, viewId);
                var placed = new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>().FirstOrDefault(p => p.ViewId == view.Id);
                if (placed != null) { reports.Add(view.Name + ": já está em prancha; conteúdo sincronizado pelo Revit, posição preservada."); continue; }
                using var sub = new SubTransaction(doc); sub.Start();
                try
                {
                    ViewSheet sheet;
                    if (destinationId > 0) sheet = ProjectService.View(doc, destinationId) as ViewSheet ?? throw new InvalidOperationException("Prancha de destino inválida.");
                    else
                    {
                        sheet = ViewSheet.Create(doc, titleBlock.Id);
                        var existing = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Select(s => s.SheetNumber).ToHashSet();
                        int n = 1; while (existing.Contains("RT-" + n.ToString("00"))) n++;
                        sheet.SheetNumber = "RT-" + n.ToString("00"); sheet.Name = view.Name;
                        ProjectService.Mark(sheet, "sheet:" + view.UniqueId);
                    }
                    if (!Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id)) throw new InvalidOperationException("A vista não pode ser colocada nesta prancha.");
                    var viewport = Viewport.Create(doc, sheet.Id, view.Id, XYZ.Zero); doc.Regenerate();
                    var usable = sheet.Outline; var border = ProjectService.Paper(15); var bottomReserve = ProjectService.Paper(55);
                    var left = usable.Min.U + border; var right = usable.Max.U - border; var bottom = usable.Min.V + bottomReserve; var top = usable.Max.V - border;
                    var box = ViewportOutline(viewport); var center = viewport.GetBoxCenter();
                    var relMin=box.MinimumPoint-center;var relMax=box.MaximumPoint-center;
                    var oldBoxes = sheet.GetAllViewports().Where(id => id != viewport.Id).Select(id => ViewportOutline((Viewport)doc.GetElement(id))).ToList();
                    foreach(var item in new FilteredElementCollector(doc,sheet.Id).WhereElementIsNotElementType())
                    {
                        if(item is Viewport || item is View || item.Category?.Id.Value==(long)BuiltInCategory.OST_TitleBlocks)continue;
                        var occupied=item.get_BoundingBox(sheet);if(occupied!=null)oldBoxes.Add(new Outline(occupied.Min,occupied.Max));
                    }
                    bool fitted = false;
                    for (var row = 0; row < 12 && !fitted; row++) for (var col = 0; col < 12 && !fitted; col++)
                    {
                        double x = left - relMin.X + col * (right - left) / 12;
                        double y = top - relMax.Y - row * (top - bottom) / 12;
                        if (x + relMax.X > right || y + relMin.Y < bottom) continue;
                        var min = new XYZ(x+relMin.X-border/2,y+relMin.Y-border/2,0); var max = new XYZ(x+relMax.X+border/2,y+relMax.Y+border/2,0);
                        if (oldBoxes.Any(o => min.X < o.MaximumPoint.X && max.X > o.MinimumPoint.X && min.Y < o.MaximumPoint.Y && max.Y > o.MinimumPoint.Y)) continue;
                        viewport.SetBoxCenter(new XYZ(x,y,0)); fitted = true;
                    }
                    if (!fitted) throw new InvalidOperationException($"Sem espaço livre. Vista com título: {(relMax.X-relMin.X)*304.8:F0} × {(relMax.Y-relMin.Y)*304.8:F0} mm; área útil: {(right-left)*304.8:F0} × {(top-bottom)*304.8:F0} mm. Confira o recorte da vista, a escala e o tamanho da folha.");
                    ProjectService.Mark(viewport, "viewport:" + view.UniqueId); sub.Commit(); reports.Add(view.Name + " → " + sheet.SheetNumber);
                }
                catch (Exception ex) { if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack(); reports.Add(view.Name + ": não inserida — " + ex.Message); }
            }
            doc.Regenerate(); return string.Join("\n", reports) + "\nRevise títulos, carimbo e eventuais anotações sobrepostas antes de emitir.";
        });
    }
    private static Outline ViewportOutline(Viewport viewport)
    {
        var box=viewport.GetBoxOutline();var label=viewport.GetLabelOutline();
        return new Outline(new XYZ(Math.Min(box.MinimumPoint.X,label.MinimumPoint.X),Math.Min(box.MinimumPoint.Y,label.MinimumPoint.Y),0),
            new XYZ(Math.Max(box.MaximumPoint.X,label.MaximumPoint.X),Math.Max(box.MaximumPoint.Y,label.MaximumPoint.Y),0));
    }
}
