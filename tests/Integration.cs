using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.IO;
using System.Text.Json;

namespace MineirosRotinas.Tests;

// Run only inside Revit, against a NEW, background document. Never mutate the active document.
public static class Integration
{
    public static string Run(UIApplication app, string template, string testRoot, string? dwg = null)
    {
        Directory.CreateDirectory(testRoot);
        var results = new List<object>();
        void Ok(string name, object detail) => results.Add(new { Name = name, Passed = true, Detail = detail });
        void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        void Reject(Action action, string name) { try { action(); } catch (InvalidOperationException) { Ok(name, "Blocked as expected"); return; } throw new Exception(name + ": should have blocked"); }
        var original = app.ActiveUIDocument.Document;
        var originalModified = original.IsModified;
        Document? test = null;
        try
        {
            Assert(Files.SafeName("CON") == "_CON", "Reserved device name");
            Assert(!Files.SafeName("A/B:Test").Contains('/'), "Invalid characters");
            Reject(() => Files.SafeName(".."), "Reject parent traversal");
            Reject(() => Files.ProjectFolder(new() { Client = "Teste", Code = "QA", BaseFolder = "relative" }), "Reject relative base");
            var data = new ClientData { Client = "TESTE AUTOMATIZADO", Code = "QA", Project = "Projeto de teste descartável", Address = "Endereço fictício", Technician = "Teste de software", ArtRrt = "TESTE — não válido", BaseFolder = testRoot, LandArea = 300 };
            var root = Files.CreateFolders(data);
            Assert(Directory.GetDirectories(root).Length == 6, "Six client directories");
            var immutable = Path.Combine(root, "00_Cadastro", Files.NewId()+".json");
            Files.WriteNew(immutable, new { Value = 1 }); var immutableHash = Files.Hash(immutable);
            try { Files.WriteNew(immutable, new { Value = 2 }); throw new Exception("WriteNew should fail"); } catch (IOException) { }
            Assert(Files.Hash(immutable) == immutableHash, "Immutable record overwritten"); Ok("Files and immutable records", root);
            test = app.Application.NewProjectDocument(template);
            var plan = new FilteredElementCollector(test).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().First(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan && v.Name == "01. Planta Baixa");
            var level = plan.GenLevel;
            ProjectService.Change(test, "QA geometry", () =>
            {
                plan=(ViewPlan)test.GetElement(plan.Duplicate(ViewDuplicateOption.Duplicate));plan.Name="QA Planta";
                plan.ViewTemplateId = ElementId.InvalidElementId; plan.Scale = 100; plan.CropBoxActive = false;
                var wt = new FilteredElementCollector(test).OfClass(typeof(WallType)).Cast<WallType>().First(t => t.Kind == WallKind.Basic);
                var points = new[] { new XYZ(0,0,level.Elevation),new XYZ(30,0,level.Elevation),new XYZ(30,20,level.Elevation),new XYZ(0,20,level.Elevation) };
                for (int i=0;i<4;i++) Wall.Create(test, Line.CreateBound(points[i],points[(i+1)%4]), wt.Id, level.Id, 10, 0, false, false);
                Wall.Create(test,Line.CreateBound(new XYZ(15,0,level.Elevation),new XYZ(15,20,level.Elevation)),wt.Id,level.Id,10,0,false,false);
                test.Regenerate(); test.Create.NewRoom(level,new UV(7,10)); test.Create.NewRoom(level,new UV(22,10));
                return true;
            });
            ProjectService.SaveClient(test,data);
            Assert(ProjectService.Read(test).Client == data.Client, "Client persistence"); Ok("Client metadata", test.ProjectInformation.ClientName);
            var sections = DrawingService.AutomaticSections(test,plan.Id.Value,6,30,100);
            Assert(new FilteredElementCollector(test).OfClass(typeof(ViewSection)).Count(v=>ProjectService.MarkOf(v).StartsWith("section:"))==3,"Exactly three cuts");
            DrawingService.AutomaticSections(test,plan.Id.Value,6,30,100);
            Assert(new FilteredElementCollector(test).OfClass(typeof(ViewSection)).Count(v=>ProjectService.MarkOf(v).StartsWith("section:"))==3,"Cuts duplicate"); Ok("Sections A B C and idempotency",sections);
            var facades=DrawingService.Facades(test,plan.Id.Value,100);
            Assert(new FilteredElementCollector(test).OfClass(typeof(View)).Count(v=>ProjectService.MarkOf(v).StartsWith("facade:"))==4,"Four facades"); Ok("Facades",facades);
            var dimensionsBefore=new FilteredElementCollector(test,plan.Id).OfClass(typeof(Dimension)).GetElementCount();
            var scan=AnnotationService.Dimensions(test,plan.Id.Value,true,true,true,8);
            Assert(new FilteredElementCollector(test,plan.Id).OfClass(typeof(Dimension)).GetElementCount()==dimensionsBefore,"Simulation must roll back");
            Assert(scan.Proposals.Any(p=>p.Include),"No valid dimension proposals: "+JsonSerializer.Serialize(scan));
            var applied=AnnotationService.Apply(test,scan); Ok("Dimension scan, rollback, apply",new{applied,scan.Proposals});
            var second=AnnotationService.Dimensions(test,plan.Id.Value,true,true,true,8);
            Assert(second.Proposals.All(p=>!p.Include),"Duplicate dimensions offered"); Ok("Dimension deduplication",second.Proposals.Count);
            var setbacks=AnnotationService.Dimensions(test,plan.Id.Value,false,false,false,8,true);
            Assert(setbacks.Proposals.Any(p=>p.Kind=="manual-review"&&!p.Include),"Missing lot boundary must remain a manual item");
            Ok("Setbacks require an actual boundary",setbacks.Proposals);
            var stale=AnnotationService.Dimensions(test,plan.Id.Value,true,true,false,8);
            var before=ProjectService.Fingerprint(test);
            ProjectService.Change(test,"QA edit after scan",()=> { plan.Scale=75;return true;});
            Assert(before != ProjectService.Fingerprint(test),"Fingerprint did not track view change");
            Reject(()=>AnnotationService.Apply(test,stale),"Reject stale annotation scan");
            var tags=AnnotationService.Tags(test,plan.Id.Value,true,true,true);
            Assert(tags.Proposals.Any(p=>p.Include),"No valid room tag proposals: "+JsonSerializer.Serialize(tags));
            Ok("Room identifiers",AnnotationService.Apply(test,tags));
            ProjectService.Change(test,"QA crop",()=>
            {
                var crop=plan.CropBox;crop.Min=new XYZ(-10,-10,crop.Min.Z);crop.Max=new XYZ(40,30,crop.Max.Z);
                plan.CropBox=crop;plan.CropBoxActive=true;plan.CropBoxVisible=false;
                plan.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE)?.Set(1);
                return true;
            });
            var type = new FilteredElementCollector(test).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType().OfType<FamilySymbol>().First(t=>t.FamilyName=="Prancha A01"&&t.Name=="Autor");
            var sheetsResult=DrawingService.Sheets(test,new List<long>{plan.Id.Value},type.Id.Value,0);
            var sheet=new FilteredElementCollector(test).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().FirstOrDefault(s=>ProjectService.MarkOf(s).StartsWith("sheet:"));
            Assert(sheet!=null,"Sheet placement failed: "+sheetsResult); Ok("Sheet and viewport",sheetsResult);
            var count=new FilteredElementCollector(test).OfClass(typeof(Viewport)).GetElementCount();
            DrawingService.Sheets(test,new List<long>{plan.Id.Value},type.Id.Value,0);
            Assert(count==new FilteredElementCollector(test).OfClass(typeof(Viewport)).GetElementCount(),"Viewport duplication");
            var checklist=ProjectService.Checklist(test); Assert(!checklist.Any(c=>c.Status=="Pendente"),"Blocking checklist"); Ok("Checklist",checklist);
            var backgroundBefore=test.Application.BackgroundColor;
            var preview=ProjectService.Preview(test,plan.Id.Value);
            var backgroundAfter=test.Application.BackgroundColor;
            Assert(File.Exists(preview),"Native preview missing");
            Assert(backgroundBefore.Red==backgroundAfter.Red&&backgroundBefore.Green==backgroundAfter.Green&&backgroundBefore.Blue==backgroundAfter.Blue,"Canvas preference changed");
            Ok("Native preview with restored canvas color",preview);
            if (dwg != null)
            {
                var originalDwg=Files.Hash(dwg);
                var map=DrawingService.ImportMapDocument(test,plan.Id.Value,dwg,ImportUnit.Meter,2000);
                var imported=new FilteredElementCollector(test).OfClass(typeof(ImportInstance)).First(e=>ProjectService.MarkOf(e).StartsWith("map-import:"));
                Assert(imported.IsHidden(plan),"DWG leaks into existing plan");
                var bb=imported.get_BoundingBox(null); var center=(bb.Min+bb.Max)/2;
                DrawingService.SetMapCrop(test,map,center-new XYZ(100,100,0),center+new XYZ(100,100,0),2000);
                Assert(ProjectService.View(test,map).CropBoxActive,"Map not cropped");
                Assert(originalDwg==Files.Hash(dwg),"Source DWG changed"); Ok("Real DWG import and rectangular crop",new{map,dwg});
            }
            var ids=new List<long>{sheet!.Id.Value};
            Reject(()=>ProjectService.Export(test,ids),"Reject PDF without review");
            ProjectService.Review(test,"Teste automatizado — não revisão técnica",ids);
            Assert(ProjectService.Read(test).ReviewStamp==ProjectService.Fingerprint(test),"Saving review invalidates stamp");
            var emission=ProjectService.Export(test,ids);
            Assert(File.Exists(emission.Pdf),"PDF export missing"); Ok("Reviewed PDF emission",new{emission.Pdf,emission.Hash});
            Ok("Local protocol",ProjectService.Protocol(test,emission,"TESTE-NÃO-PROTOCOLADO",DateTime.Today,"Teste automático. Sem envio."));
            var changed=ProjectService.Read(test); changed.Client="Outro cliente";
            Reject(()=>ProjectService.SaveClient(test,changed),"Reject client reuse with emissions");
            ProjectService.Change(test,"QA after review",()=>{plan.Scale=100;return true;});
            Reject(()=>ProjectService.Export(test,ids),"Reject PDF after model change");
            test.SaveAs(Path.Combine(testRoot,"Modelo-QA.rvt"),new SaveAsOptions{OverwriteExistingFile=false});
            Ok("Temporary model saved",test.PathName);
        }
        catch(Exception ex){results.Add(new{Name="FAILED",Passed=false,Detail=ex.ToString()});}
        finally
        {
            ModelChanges.Detach();
            if(test!=null&&test.IsValidObject)test.Close(false);
            results.Add(new{Name="Active user model preserved",Passed=original.IsModified==originalModified&&app.ActiveUIDocument.Document.Equals(original),Detail=original.PathName});
        }
        var json=JsonSerializer.Serialize(results,Files.Json);
        Files.WriteNew(Path.Combine(testRoot,"test-results.json"),results);
        return json;
    }
}
