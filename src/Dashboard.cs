using Autodesk.Revit.DB;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace MineirosRotinas;

public sealed class RoutineStep : INotifyPropertyChanged
{
    public string Number { get; init; }="";
    public string Title { get; init; }="";
    private bool _complete;
    public bool Complete { get=>_complete; set { _complete=value; PropertyChanged?.Invoke(this,new(nameof(Mark)));PropertyChanged?.Invoke(this,new(nameof(MarkColor))); } }
    public string Mark=>Complete?"✓":"○";
    public string MarkColor=>Complete?"#21A44F":"#9AA7B5";
    public event PropertyChangedEventHandler? PropertyChanged;
}
public sealed class DashboardView : INotifyPropertyChanged
{
    public string Title { get; init; }="";
    public string ViewName { get; init; }="Sem vista correspondente";
    public long Id { get; init; }
    public int Scale { get; init; }
    private ImageSource? _preview;
    private bool _selected;
    public ImageSource? Preview { get=>_preview;set{_preview=value;Notify();Notify(nameof(Placeholder));} }
    public bool Selected { get=>_selected;set{_selected=value;Notify();} }
    public string Placeholder=>Preview!=null?"":Id>0?"Carregar prévia":"Sem vista";
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
}
public static class Dashboard
{
    public static List<DashboardView> Collect(Document doc)
    {
        var views=new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v=>!v.IsTemplate&&v.CanBePrinted).OrderBy(v=>v.Name).ToList();
        bool Has(View v,params string[] words)=>words.Any(w=>v.Name.Contains(w,StringComparison.OrdinalIgnoreCase));
        var plan=views.FirstOrDefault(v=>v.ViewType==ViewType.FloorPlan&&Has(v,"planta baixa")&&!Has(v,"copiar"," - "))??views.FirstOrDefault(v=>v.ViewType==ViewType.FloorPlan);
        var cuts=views.FirstOrDefault(v=>v.ViewType==ViewType.Section&&!Has(v,"fachada","render"));
        var facade=views.FirstOrDefault(v=>v.ViewType is ViewType.Section or ViewType.Elevation&&Has(v,"fachada"))??views.FirstOrDefault(v=>v.ViewType==ViewType.Elevation);
        var roof=views.FirstOrDefault(v=>v.ViewType==ViewType.FloorPlan&&Has(v,"cobertura","telhado"));
        var location=views.FirstOrDefault(v=>ProjectService.MarkOf(v).StartsWith("map:"))??views.FirstOrDefault(v=>v.ViewType==ViewType.FloorPlan&&Has(v,"localiza","situa"));
        // Prefer a printable sheet containing the actual project tables: schedule image export is not supported by every Revit setup.
        var tables=views.FirstOrDefault(v=>v is ViewSheet&&Has(v,"planilha","quadro","tabela"))??views.FirstOrDefault(v=>v is ViewSchedule s&&!s.IsTitleblockRevisionSchedule&&Has(v,"área","area","quadro"));
        DashboardView Item(string title,View? view)=>new(){Title=title,Id=view?.Id.Value??0,ViewName=view?.Name??"Sem vista correspondente",Scale=view?.Scale??0};
        return [Item("Planta baixa",plan),Item("Cortes A/B/C",cuts),Item("Fachadas",facade),Item("Cobertura",roof),Item("Localização",location),Item("Quadros",tables)];
    }
}
