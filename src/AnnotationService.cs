using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace MineirosRotinas;

public static class AnnotationService
{
    private sealed record FaceRef(string Stable, XYZ Point, XYZ Normal);
    private static XYZ Point(double[] a) => new(a[0], a[1], a[2]);
    private static double[] Array(XYZ p) => [p.X, p.Y, p.Z];
    private static IEnumerable<FaceRef> Faces(Document doc, Element element, View view)
    {
        using var options = new Options { ComputeReferences = true, IncludeNonVisibleObjects = false, View = view };
        var geometry = element.get_Geometry(options); if (geometry == null) yield break;
        // Direct solids provide actual native references. Transformed copies of geometry are intentionally not used for dimensions.
        foreach (var solid in geometry.OfType<Solid>()) foreach (var face in solid.Faces.OfType<PlanarFace>())
        {
            if (face.Reference == null || face.Area < ProjectService.M(.01) * ProjectService.M(.01)) continue;
            var uv = face.GetBoundingBox(); var point = face.Evaluate((uv.Min + uv.Max) * .5);
            yield return new FaceRef(face.Reference.ConvertToStableRepresentation(doc), point, face.FaceNormal);
        }
    }
    private static bool InCrop(Element e, View view)
    {
        if (!view.CropBoxActive) return true;
        var b = e.get_BoundingBox(view); if (b == null) return false;
        var crop = view.CropBox; var points = DrawingService.Corners(b).Select(crop.Transform.Inverse.OfPoint).ToList();
        return points.Max(p => p.X) >= crop.Min.X && points.Min(p => p.X) <= crop.Max.X && points.Max(p => p.Y) >= crop.Min.Y && points.Min(p => p.Y) <= crop.Max.Y;
    }
    private static XYZ OnPlane(View view, XYZ p) => p - view.ViewDirection * ((p - view.Origin).DotProduct(view.ViewDirection));
    private static View Annotatable(Document doc, long id)
    {
        var view = ProjectService.View(doc, id);
        if (view.IsTemplate || view.ViewType is not (ViewType.FloorPlan or ViewType.CeilingPlan or ViewType.Section or ViewType.Elevation))
            throw new InvalidOperationException("Escolha uma planta, corte ou fachada para anotar.");
        return view;
    }
    public static ScanResult Dimensions(Document doc, long viewId, bool outer, bool inner, bool openings, double paperOffsetMm, bool setbacks=false)
    {
        var view = Annotatable(doc, viewId); var result = new ScanResult { ViewId = viewId, Stamp = ProjectService.Fingerprint(doc) };
        if (!outer && !inner && !openings && !setbacks) throw new InvalidOperationException("Marque ao menos um tipo de cota.");
        if (paperOffsetMm < 2 || paperOffsetMm > 100) throw new InvalidOperationException("Use afastamento entre 2 e 100 mm no papel.");
        var elements = DrawingService.Building(doc, view).Where(e => InCrop(e,view)).ToList();
        if (elements.Count == 0) { result.Notes.Add("Nenhum elemento arquitetônico visível para cotar."); return result; }
        var points = elements.Select(e => e.get_BoundingBox(view)).Where(b => b != null).SelectMany(b => DrawingService.Corners(b!)).ToList();
        var offset = ProjectService.Paper(paperOffsetMm) * view.Scale;
        var nativeFaces = new List<FaceRef>();
        foreach (var element in elements.Where(e => e is Wall || (view.ViewType is ViewType.Section or ViewType.Elevation && e is Floor or RoofBase)))
        {
            try { nativeFaces.AddRange(Faces(doc, element, view)); }
            catch (Exception ex) { result.Notes.Add($"Elemento {element.Id.Value}: referências não disponíveis ({ex.Message})."); }
        }
        var axes = new[] { view.RightDirection, view.UpDirection };
        for (int axisIndex = 0; axisIndex < 2; axisIndex++)
        {
            var axis = axes[axisIndex]; var normal = axes[1 - axisIndex];
            var candidates = nativeFaces.Where(f => Math.Abs(f.Normal.DotProduct(axis)) > .9999)
                .OrderBy(f => f.Point.DotProduct(axis)).GroupBy(f => Math.Round(f.Point.DotProduct(axis), 4)).Select(g => g.First()).ToList();
            if (candidates.Count < 2) { result.Notes.Add("Sem faces paralelas suficientes no eixo " + (axisIndex == 0 ? "horizontal" : "vertical") + "."); continue; }
            void Add(IList<FaceRef> refs, string name, double gap)
            {
                var start = OnPlane(view, axis * refs[0].Point.DotProduct(axis) + normal * (points.Min(p => p.DotProduct(normal)) - gap));
                var end = start + axis * (refs[^1].Point.DotProduct(axis) - refs[0].Point.DotProduct(axis));
                if (start.DistanceTo(end) < doc.Application.ShortCurveTolerance) return;
                var stable = refs.Select(r => r.Stable).ToArray();
                result.Proposals.Add(new Proposal { Kind = "dimension", Description = name + " · " + (axisIndex == 0 ? "horizontal" : "vertical") + $" · {refs.Count} referências",
                    References = stable, Start = Array(start), End = Array(end), Signature = Signature(view, "dimension", stable) });
            }
            if (outer) Add(new[] { candidates[0], candidates[^1] }, "Cota total entre faces extremas", offset * 2);
            if (inner && candidates.Count > 2) Add(candidates.Take(100).ToList(), "Cadeia de faces alinhadas (revisar trechos)", offset);
        }
        if (openings)
        {
            foreach (var family in elements.OfType<FamilyInstance>().Where(e => e.Category.Id.Value == (long)BuiltInCategory.OST_Doors || e.Category.Id.Value == (long)BuiltInCategory.OST_Windows))
            {
                var left = family.GetReferences(FamilyInstanceReferenceType.Left).FirstOrDefault();
                var right = family.GetReferences(FamilyInstanceReferenceType.Right).FirstOrDefault();
                if (left == null || right == null || family.Location is not LocationPoint location) { result.Notes.Add($"Esquadria {family.Id.Value}: família sem referências esquerda/direita utilizáveis."); continue; }
                var axis = family.HandOrientation.Normalize();
                if (Math.Abs(axis.DotProduct(view.ViewDirection)) > .01) { result.Notes.Add($"Esquadria {family.Id.Value}: largura fora do plano da vista."); continue; }
                var perpendicular = view.ViewDirection.CrossProduct(axis).Normalize();
                var center = OnPlane(view, location.Point) - perpendicular * offset;
                var stable = new[] { left.ConvertToStableRepresentation(doc), right.ConvertToStableRepresentation(doc) };
                result.Proposals.Add(new Proposal { Kind = "dimension", Description = "Largura da esquadria " + family.Id.Value, ElementId = family.Id.Value,
                    References = stable, Start = Array(center - axis * ProjectService.M(2)), End = Array(center + axis * ProjectService.M(2)), Signature = Signature(view, "dimension", stable) });
            }
        }
        if(setbacks) AddSetbacks(doc,view,nativeFaces,result);
        result.Notes.Add("Cotas propostas usam referências nativas. Paredes inclinadas/curvas, recuos legais e conflitos de texto exigem revisão manual.");
        ValidateProposals(doc, result); return result;
    }
    private static string Signature(View view, string kind, IEnumerable<string> refs) => "annotation:" + Files.HashText(view.UniqueId + "|" + kind + "|" + string.Join("|", refs.Order(StringComparer.Ordinal)));
    private static void AddSetbacks(Document doc,View view,List<FaceRef> faces,ScanResult scan)
    {
        var count=scan.Proposals.Count;
        if(view is ViewPlan)
        {
            foreach(var boundary in new FilteredElementCollector(doc,view.Id).OfClass(typeof(PropertyLine)))
            {
                using var options=new Options{ComputeReferences=true,View=view};
                var geometry=boundary.get_Geometry(options);if(geometry==null)continue;
                // Only actual straight native references are accepted; no inferred site boundary or copied geometry.
                foreach(var line in geometry.OfType<Line>().Where(l=>l.IsBound&&l.Reference!=null))
                {
                    var a=line.GetEndPoint(0);var b=line.GetEndPoint(1);var along=(b-a).Normalize();
                    if(Math.Abs(along.DotProduct(view.ViewDirection))>.001)continue;
                    var normal=view.ViewDirection.CrossProduct(along).Normalize();
                    var candidates=faces.Where(f=>Math.Abs(f.Normal.DotProduct(normal))>.9999)
                        .Where(f=>(f.Point-a).DotProduct(along)>=0&&(f.Point-a).DotProduct(along)<=line.Length)
                        .Where(f=>Math.Abs((f.Point-a).DotProduct(normal))>doc.Application.ShortCurveTolerance)
                        .OrderBy(f=>Math.Abs((f.Point-a).DotProduct(normal))).ToList();
                    if(candidates.Count==0)continue;
                    var nearest=candidates[0];var signed=(nearest.Point-a).DotProduct(normal);
                    var end=OnPlane(view,nearest.Point);var start=end-normal*signed;
                    var refs=new[]{line.Reference.ConvertToStableRepresentation(doc),nearest.Stable};
                    scan.Proposals.Add(new Proposal{Kind="dimension",Description=$"Recuo geométrico ao limite {boundary.Id.Value} · {Math.Abs(signed)*.3048:F2} m (revisar face)",References=refs,Start=Array(start),End=Array(end),Signature=Signature(view,"dimension",refs)});
                }
            }
        }
        if(scan.Proposals.Count==count)
            scan.Proposals.Add(new Proposal{Kind="manual-review",Include=false,Description="Recuos do lote",Status="Revisar: limite do lote sem referência nativa reta utilizável nesta vista."});
        scan.Notes.Add("Recuos: distância geométrica até a face paralela mais próxima; conferir face externa, lado do lote e regra legal. Linhas de detalhe e muros não são assumidos como divisa.");
    }
    public static ScanResult Tags(Document doc, long viewId, bool rooms, bool openings, bool materials)
    {
        var view = Annotatable(doc, viewId); var result = new ScanResult { ViewId = viewId, Stamp = ProjectService.Fingerprint(doc) };
        var existing = new FilteredElementCollector(doc, view.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>().SelectMany(t => t.GetTaggedLocalElementIds()).Select(id => id.Value).ToHashSet();
        var roomTags = new FilteredElementCollector(doc, view.Id).OfClass(typeof(SpatialElementTag)).OfType<RoomTag>().Where(t => t.Room != null).Select(t => t.Room.Id.Value).ToHashSet();
        var tagTypes = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().ToList();
        long FindType(BuiltInCategory category) => tagTypes.Where(t => t.Category.Id.Value == (long)category).OrderBy(t => t.Id.Value).Select(t => t.Id.Value).FirstOrDefault();
        if (rooms)
        {
            if (view is not ViewPlan) result.Notes.Add("Identificadores de ambientes: selecione uma planta.");
            else foreach (var room in new FilteredElementCollector(doc, view.Id).OfClass(typeof(SpatialElement)).OfType<Room>().Where(r => r.Area > 0 && r.Location is LocationPoint))
            {
                if (roomTags.Contains(room.Id.Value)) continue;
                var typeId = FindType(BuiltInCategory.OST_RoomTags);
                if (typeId == 0) { result.Notes.Add("Carregue uma família de identificador de ambiente."); break; }
                var point = ((LocationPoint)room.Location).Point;
                result.Proposals.Add(new Proposal { Kind = "room-tag", Description = "Ambiente · " + room.Name, ElementId = room.Id.Value, TypeId = typeId, Start = Array(point), Signature = Signature(view, "room-tag", [room.UniqueId]) });
            }
        }
        if (openings)
        {
            foreach (var e in DrawingService.Building(doc, view).OfType<FamilyInstance>().Where(e => InCrop(e,view) && (e.Category.Id.Value == (long)BuiltInCategory.OST_Doors || e.Category.Id.Value == (long)BuiltInCategory.OST_Windows)))
            {
                if (existing.Contains(e.Id.Value) || e.Location is not LocationPoint lp) continue;
                var typeId = FindType(e.Category.Id.Value == (long)BuiltInCategory.OST_Doors ? BuiltInCategory.OST_DoorTags : BuiltInCategory.OST_WindowTags);
                if (typeId == 0) { result.Notes.Add($"{e.Category.Name}: identificador compatível não carregado."); continue; }
                var point = OnPlane(view, lp.Point) + view.UpDirection * ProjectService.Paper(6) * view.Scale;
                result.Proposals.Add(new Proposal { Kind = "tag", Description = "Esquadria · " + e.Id.Value, ElementId = e.Id.Value, TypeId = typeId, Start = Array(point), Signature = Signature(view, "tag", [e.UniqueId]) });
            }
        }
        if (materials)
        {
            var typeId = FindType(BuiltInCategory.OST_MaterialTags);
            if (typeId == 0) result.Notes.Add("Carregue uma família de identificador de material.");
            else foreach (var e in DrawingService.Building(doc, view).Where(e => InCrop(e,view) && e is Wall or Floor or RoofBase).Take(200))
            {
                var face = Faces(doc,e,view).FirstOrDefault(f => f.Normal.DotProduct(view.ViewDirection) > .9);
                if (face == null) continue;
                result.Proposals.Add(new Proposal { Kind = "material-tag", Description = "Material · face do elemento " + e.Id.Value, ElementId = e.Id.Value, TypeId = typeId,
                    References = [face.Stable], Start = Array(OnPlane(view,face.Point)), Signature = Signature(view,"material-tag",[face.Stable]) });
            }
        }
        result.Notes.Add("Serão usadas as famílias de identificadores já carregadas. Revise conteúdo, posições e sobreposições; nenhuma família externa é baixada.");
        ValidateProposals(doc, result); return result;
    }
    private static bool Exists(Document doc, View view, Proposal proposal)
    {
        if (new FilteredElementCollector(doc, view.Id).Any(e => ProjectService.MarkOf(e) == proposal.Signature)) return true;
        if (proposal.Kind != "dimension") return false;
        var wanted = proposal.References.Order(StringComparer.Ordinal).ToArray();
        return new FilteredElementCollector(doc, view.Id).OfClass(typeof(Dimension)).Cast<Dimension>().Any(d =>
        {
            try { return d.References.Cast<Reference>().Select(r => r.ConvertToStableRepresentation(doc)).Order(StringComparer.Ordinal).SequenceEqual(wanted); }
            catch { return false; }
        });
    }
    private static Element Create(Document doc, View view, Proposal p)
    {
        Element element;
        switch (p.Kind)
        {
            case "dimension":
                var array = new ReferenceArray(); foreach (var s in p.References) array.Append(Reference.ParseFromStableRepresentation(doc,s));
                element = doc.Create.NewDimension(view, Line.CreateBound(Point(p.Start), Point(p.End)), array) ?? throw new InvalidOperationException("Cota recusada.");
                break;
            case "room-tag":
                var pos = Point(p.Start);
                var roomTag = doc.Create.NewRoomTag(new LinkElementId(new ElementId(p.ElementId)), new UV(pos.X,pos.Y), view.Id);
                roomTag.ChangeTypeId(new ElementId(p.TypeId)); element = roomTag; break;
            case "tag":
            case "material-tag":
                var reference = p.Kind == "material-tag" ? Reference.ParseFromStableRepresentation(doc,p.References[0]) : new Reference(doc.GetElement(new ElementId(p.ElementId)));
                element = IndependentTag.Create(doc,new ElementId(p.TypeId),view.Id,reference,true,TagOrientation.Horizontal,Point(p.Start)); break;
            default: throw new InvalidOperationException("Proposta desconhecida.");
        }
        ProjectService.Mark(element,p.Signature); return element;
    }
    private static void ValidateProposals(Document doc, ScanResult scan)
    {
        var view = ProjectService.View(doc,scan.ViewId);
        using var group = new TransactionGroup(doc,"BIM Rotinas · validar e reverter anotações"); group.Start();
        try
        {
            foreach (var p in scan.Proposals)
            {
                if(p.Kind=="manual-review")continue;
                if (Exists(doc,view,p)) { p.Include = false; p.Status = "Já existe"; continue; }
                try
                {
                    ProjectService.Change(doc,"simulação de anotação",()=> { Create(doc,view,p); doc.Regenerate(); return true; });
                    p.Status = "Referências validadas";
                }
                catch (Exception ex) { p.Include = false; p.Status = "Revisar: " + ex.Message; }
            }
        }
        finally
        {
            if (group.GetStatus() == TransactionStatus.Started && group.RollBack() != TransactionStatus.RolledBack) throw new InvalidOperationException("Reversão não confirmada pelo Revit.");
        }
        // Capture after rollback because native version identifiers may be refreshed by simulation.
        scan.Stamp = ProjectService.Fingerprint(doc);
    }
    public static string Apply(Document doc, ScanResult scan)
    {
        if (scan.Stamp != ProjectService.Fingerprint(doc)) throw new InvalidOperationException("O modelo mudou. Faça uma nova varredura antes de aplicar.");
        var chosen = scan.Proposals.Where(p => p.Include && p.Status == "Referências validadas").ToList();
        if (chosen.Count == 0) throw new InvalidOperationException("Não há propostas válidas selecionadas.");
        var view = ProjectService.View(doc,scan.ViewId);
        return ProjectService.Change(doc,"aplicar anotações revisadas",()=>
        {
            int count = 0;
            foreach (var p in chosen) { if (Exists(doc,view,p)) continue; Create(doc,view,p); count++; }
            doc.Regenerate(); return $"{count} anotações aplicadas em {view.Name}. Confira posições, textos e referências na vista.";
        });
    }
}
