using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;

namespace MineirosRotinas;

// VersionGuid alone only reflects persisted versions. Track unsaved changes as well.
public static class ModelChanges
{
    private sealed class State { public string Session { get; } = Guid.NewGuid().ToString("N"); public long Revision; }
    private static Application? _application;
    private static readonly Dictionary<Document,State> States = new();
    public static string Token(Document doc)
    {
        if (_application == null) { _application=doc.Application; _application.DocumentChanged+=Changed; }
        foreach(var old in States.Keys.Where(d=>!d.IsValidObject).ToList()) States.Remove(old);
        if (!States.TryGetValue(doc,out var state)) States[doc]=state=new State();
        return state.Session+":"+state.Revision;
    }
    private static void Changed(object? sender,DocumentChangedEventArgs e)
    {
        var doc=e.GetDocument();
        if(!States.TryGetValue(doc,out var state))return;
        // Our review/emission metadata changes ProjectInformation only. Native fields are separately hashed.
        if(e.GetAddedElementIds().Count>0||e.GetDeletedElementIds().Count>0||e.GetModifiedElementIds().Any(id=>id!=doc.ProjectInformation.Id))state.Revision++;
    }
    public static void Detach()
    {
        if(_application!=null)_application.DocumentChanged-=Changed;
        _application=null;States.Clear();
    }
}
