using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MineirosRotinas;

public sealed class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication app)
    {
        const string tab = "BIM Rotinas";
        try { app.CreateRibbonTab(tab); } catch (Autodesk.Revit.Exceptions.ArgumentException) { }
        var panel = app.CreateRibbonPanel(tab, "Projeto e documentação");
        Add(panel, "MineirosRotinas.Open", "Rotinas de\nProjeto", typeof(OpenCommand), "Cadastro, cortes, pranchas, cotas, legendas e emissão.");
        Add(panel, "MineirosRotinas.Map", "Localização\nDWG", typeof(MapCommand), "Carregar mapa e recortar uma região retangular.");
        Add(panel, "MineirosRotinas.Copiloto", "Abrir BIM\nCopiloto", typeof(CopilotoCommand), "Abrir a conversa do BIM Copiloto já instalado.");
        return Result.Succeeded;
    }
    private static void Add(RibbonPanel panel, string id, string text, Type type, string hint)
    {
        var data = new PushButtonData(id, text, Assembly.GetExecutingAssembly().Location, type.FullName!) { ToolTip = hint };
        // Vector drawing is embedded in this independent assembly; no external icon files.
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(System.Windows.Media.Color.FromRgb(31, 95, 178)), null,
            new RectangleGeometry(new Rect(1, 1, 30, 30), 5, 5)));
        group.Children.Add(new GeometryDrawing(Brushes.White, null, Geometry.Parse("M7,8 L25,8 25,11 7,11 Z M7,15 L25,15 25,18 7,18 Z M7,22 L20,22 20,25 7,25 Z")));
        group.Freeze(); data.LargeImage = new DrawingImage(group); panel.AddItem(data);
    }
    public Result OnShutdown(UIControlledApplication app) { OpenCommand.Window?.Close(); ModelChanges.Detach(); return Result.Succeeded; }
}
[Transaction(TransactionMode.Manual)]
public sealed class OpenCommand : IExternalCommand
{
    internal static MainWindow? Window;
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) => Open(data.Application, -1);
    public static Result Open(UIApplication app, int page = -1)
    {
        if (app.ActiveUIDocument == null || app.ActiveUIDocument.Document.IsFamilyDocument)
        { TaskDialog.Show("BIM Rotinas", "Abra um projeto ou modelo de projeto no Revit."); return Result.Cancelled; }
        if (Window != null) { Window.Show(); Window.Activate(); Window.GoTo(page); return Result.Succeeded; }
        var bridge = new RevitBridge();
        try
        {
            Window = new MainWindow(app.ActiveUIDocument.Document, bridge);
            new WindowInteropHelper(Window).Owner = app.MainWindowHandle;
            Window.Closed += (_, _) => { bridge.Dispose(); Window = null; };
            Window.Show(); Window.GoTo(page); return Result.Succeeded;
        }
        catch { bridge.Dispose(); Window = null; throw; }
    }
}
[Transaction(TransactionMode.Manual)]
public sealed class MapCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) => OpenCommand.Open(data.Application, 10);
}
[Transaction(TransactionMode.Manual)]
public sealed class CopilotoCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
    {
        try { CopilotoLauncher.Open(data.Application); return Result.Succeeded; }
        catch (Exception ex) { message = ex.Message; TaskDialog.Show("BIM Copiloto", message); return Result.Cancelled; }
    }
}
public static class CopilotoLauncher
{
    public static void Open(UIApplication app)
    {
        if (app.ActiveUIDocument == null) throw new InvalidOperationException("Abra um projeto primeiro.");
        var assembly = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a => a.GetName().Name == "BIMCopiloto")
            ?? throw new InvalidOperationException("O BIM Copiloto existente não está carregado. Reinicie o Revit com seu plugin habilitado.");
        var commandType = assembly.GetType("ChatGPTRevit.Command", true)!;
        var field = commandType.GetField("Window", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new InvalidOperationException("Versão do BIM Copiloto sem a interface esperada. Use seu botão original.");
        if (field.GetValue(null) is Window existing) { existing.Show(); existing.Activate(); return; }
        var handlerType = assembly.GetType("ChatGPTRevit.RevitRequestHandler", true)!;
        var windowType = assembly.GetType("ChatGPTRevit.ChatGPTWindow", true)!;
        var handler = Activator.CreateInstance(handlerType)!;
        try
        {
            var window = (Window)Activator.CreateInstance(windowType, app, handler)!;
            new WindowInteropHelper(window).Owner = app.MainWindowHandle;
            field.SetValue(null, window);
            window.Closed += (_, _) => { (handler as IDisposable)?.Dispose(); field.SetValue(null, null); };
            window.Show(); window.Activate();
        }
        catch { (handler as IDisposable)?.Dispose(); throw; }
    }
}
public sealed class RevitBridge : IExternalEventHandler, IDisposable
{
    private readonly ExternalEvent _event;
    private Action<UIApplication>? _pending;
    public RevitBridge() => _event = ExternalEvent.Create(this);
    public Task<T> Run<T>(Func<UIApplication, T> action)
    {
        if (_pending != null) throw new InvalidOperationException("Aguarde a operação atual no Revit.");
        var task = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = app => { try { task.TrySetResult(action(app)); } catch (Exception ex) { task.TrySetException(ex); } };
        try
        {
            var status = _event.Raise();
            if (status != ExternalEventRequest.Accepted && status != ExternalEventRequest.Pending) throw new InvalidOperationException("Termine o comando ativo do Revit e tente novamente.");
        }
        catch (Exception ex) { _pending = null; task.TrySetException(ex); }
        return task.Task;
    }
    public void Execute(UIApplication app) { var pending = _pending; _pending = null; pending?.Invoke(app); }
    public string GetName() => "BIM Rotinas — documentação";
    public void Dispose() => _event.Dispose();
}
