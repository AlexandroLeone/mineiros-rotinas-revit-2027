using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;

namespace MineirosRotinas;

public partial class MainWindow : Window
{
    private readonly Document _document;
    private readonly RevitBridge _bridge;
    private Catalog? _catalog;
    private ScanResult? _dimensionScan, _tagScan;
    private bool _busy, _loaded, _navigating, _binding;
    private System.Windows.Point? _dragOrigin;
    private readonly Dictionary<long,BitmapImage> _previews=new();
    private List<DashboardView> _visuals=new();
    private readonly List<RoutineStep> _steps=new[]{"Cadastro do imóvel","Cortes A, B e C","Atualizar pranchas","Checklist automático","Cotas por vista","Legendas por vista","Revisão profissional","Emitir PDF","Pasta do cliente","Versão protocolada"}.Select((t,i)=>new RoutineStep{Number=(i+1).ToString("00"),Title=t}).ToList();
    public MainWindow(Document document, RevitBridge bridge)
    {
        _document = document; _bridge = bridge;
        InitializeComponent();
        Width = Math.Min(1580, SystemParameters.WorkArea.Width - 12); Height = Math.Min(960, SystemParameters.WorkArea.Height - 12);
        Navigation.ItemsSource=_steps;_navigating=true;Navigation.SelectedIndex=4;_navigating=false;
        ProtocolDate.SelectedDate = DateTime.Today;
        Loaded += async (_, _) => { if (_loaded) return; _loaded = true; await Work(async()=>{await Refresh();await LoadDashboardPreviews();}); };
    }
    private Document Checked(UIApplication app)
    {
        if (!_document.IsValidObject || app.ActiveUIDocument == null || !_document.Equals(app.ActiveUIDocument.Document))
            throw new InvalidOperationException("Volte ao modelo desta janela ou feche e reabra Rotinas no projeto desejado.");
        return _document;
    }
    public void GoTo(int page)
    {
        if (Pages == null || RoutineOverlay==null) return;
        _navigating=true;
        try
        {
            if(page<0||page is 4 or 5){RoutineOverlay.Visibility=System.Windows.Visibility.Collapsed;RoutineTab.IsChecked=true;if(page>=0)Navigation.SelectedIndex=page;return;}
            ShowPage(page);
            if(page<10)Navigation.SelectedIndex=page;
            if(page==10)LocationTab.IsChecked=true;else if(page==2)SheetTab.IsChecked=true;else if(page==9)HistoryTab.IsChecked=true;else RoutineTab.IsChecked=true;
        }
        finally{_navigating=false;}
    }
    private void ShowPage(int page)
    {
        Pages.SelectedIndex=Math.Clamp(page,0,11);RoutineOverlay.Visibility=System.Windows.Visibility.Visible;
        OverlayTitle.Text=page<10?_steps[page].Title:page==10?"Configurar localização DWG":"Resultado da rotina";
    }
    private void Navigate(object sender, SelectionChangedEventArgs e) { if (!_navigating&&Pages!=null&&Navigation.SelectedIndex>=0)GoTo(Navigation.SelectedIndex); }
    private void DashboardClick(object sender,RoutedEventArgs e)=>GoTo(-1);
    private void SheetsNavClick(object sender,RoutedEventArgs e)=>GoTo(2);
    private void HistoryNavClick(object sender,RoutedEventArgs e)=>GoTo(9);
    private void ReviewDimensionsClick(object sender,RoutedEventArgs e)=>ShowPage(4);
    private void ReportClick(object sender,RoutedEventArgs e)=>ShowPage(11);
    private async Task Work(Func<Task> action, bool pick = false, bool report = true, bool leaveHidden = false)
    {
        if (_busy) return; _busy = true; Pages.IsEnabled = false; Navigation.IsEnabled = false;RightTools.IsEnabled=false; StatusLabel.Text = "Executando…";
        if (pick) Hide();
        try { await action(); StatusLabel.Text = "Concluído. Confira o resultado da rotina."; }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException) { StatusLabel.Text = "Seleção cancelada."; ReportBox.Text = "Operação cancelada pelo usuário."; }
        catch (Exception ex) { StatusLabel.Text = "Operação não concluída."; ReportBox.Text = ex.Message; if (report) ShowPage(11); else if (MapWorkflowStatus != null) MapWorkflowStatus.Text = ex.Message; }
        finally { _busy = false; Pages.IsEnabled = true; Navigation.IsEnabled = true;RightTools.IsEnabled=true; if (pick && !leaveHidden) { Show(); Activate(); } }
    }
    private async Task Refresh()
    {
        _binding=true;
        try{
        var catalog = await _bridge.Run(app => { Checked(app); return ProjectService.Catalog(app); });
        _catalog = catalog; var data = catalog.Client;
        ProjectLabel.Text = "Modelo aberto: " + catalog.Title;
        ClientBox.Text = data.Client; CodeBox.Text = data.Code; ProjectBox.Text = data.Project; AddressBox.Text = data.Address;
        DistrictBox.Text = data.District; BlockBox.Text = data.Block; LotBox.Text = data.Lot; RegistryBox.Text = data.Registry;
        TechnicianBox.Text = data.Technician; ArtBox.Text = data.ArtRrt; RevisionBox.Text = data.Revision;
        AreaBox.Text = data.LandArea.ToString(CultureInfo.GetCultureInfo("pt-BR")); BaseFolderBox.Text = data.BaseFolder;
        ClientLabel.Text = "Cliente: "+(string.IsNullOrEmpty(data.Client)?"não cadastrado":data.Client);RevisionLabel.Text="Revisão: "+data.Revision;
        _steps[0].Complete=!string.IsNullOrWhiteSpace(data.Client)&&!string.IsNullOrWhiteSpace(data.Code);
        _steps[7].Complete=data.Emissions.Count>0;
        ReviewerBox.Text = data.ReviewBy.Length > 0 ? data.ReviewBy : data.Technician;
        ReviewLabel.Text = data.ReviewStamp.Length > 0 ? "Última revisão: " + data.ReviewBy + " · " + data.ReviewDate + "\nA validade será conferida ao emitir." : "Revisão ainda não registrada.";
        try { FolderLabel.Text = Files.ProjectFolder(data); } catch { FolderLabel.Text = "Preencha cliente, código e pasta-base no cadastro."; }
        DestinationLabel.Text=string.IsNullOrWhiteSpace(data.Client)?data.BaseFolder+"\\Nome do Cliente":FolderLabel.Text;
        Bind(CutView,catalog.Views.Where(v=>v.Kind=="FloorPlan").ToList(),catalog.ActiveViewId);
        Bind(MapPlan,catalog.Views.Where(v=>v.Kind=="FloorPlan").ToList(),catalog.ActiveViewId);
        Bind(DimensionView,catalog.Views.Where(v=>v.Kind is "FloorPlan" or "CeilingPlan" or "Section" or "Elevation").ToList(),catalog.ActiveViewId);
        Bind(TagView,catalog.Views.Where(v=>v.Kind is "FloorPlan" or "CeilingPlan" or "Section" or "Elevation").ToList(),catalog.ActiveViewId);
        Bind(PreviewView,catalog.Views.Concat(catalog.Sheets).ToList(),catalog.ActiveViewId);
        _visuals=await _bridge.Run(app=>Dashboard.Collect(Checked(app)));ThumbnailList.ItemsSource=_visuals;
        if(!catalog.Views.Any(v=>v.Id==catalog.ActiveViewId)&&PreviewImage.Source==null&&_visuals[0].Id>0)PreviewView.SelectedItem=((IList<Choice>)PreviewView.ItemsSource).FirstOrDefault(v=>v.Id==_visuals[0].Id);
        foreach(var visual in _visuals)if(_previews.TryGetValue(visual.Id,out var cached))visual.Preview=cached;
        Bind(MapView,catalog.Views.Where(v=>v.Name.StartsWith("LOC — ")).ToList());
        Bind(TitleBlockBox,catalog.TitleBlocks);
        Bind(DestinationSheet,new List<Choice>{new(){Id=0,Name="Criar novas pranchas (uma por vista)"}}.Concat(catalog.Sheets).ToList());
        ViewsGrid.ItemsSource = catalog.Views; PdfGrid.ItemsSource = catalog.Sheets;
        EmissionBox.ItemsSource = data.Emissions; if (data.Emissions.Count > 0) EmissionBox.SelectedIndex = data.Emissions.Count-1;
        _dimensionScan = null; _tagScan = null; ApplyDimensionsButton.IsEnabled = false; ApplyTagsButton.IsEnabled = false;
        DimensionSummary.Text="Prévia: faça a varredura da vista";
        }finally{_binding=false;}
    }
    private static void Bind(ComboBox combo, IList<Choice> choices, long fallback = -1)
    {
        var old = (combo.SelectedItem as Choice)?.Id; combo.ItemsSource = choices;
        combo.SelectedItem = choices.FirstOrDefault(c=>c.Id==old) ?? choices.FirstOrDefault(c=>c.Id==fallback) ?? choices.FirstOrDefault();
    }
    private static long Id(ComboBox combo) => (combo.SelectedItem as Choice)?.Id ?? throw new InvalidOperationException("Escolha uma vista ou opção na lista.");
    private static double Number(TextBox box)
    {
        if (!double.TryParse(box.Text,NumberStyles.Float,CultureInfo.GetCultureInfo("pt-BR"),out var number) && !double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out number)) throw new InvalidOperationException("Número inválido: " + box.Text);
        if (!double.IsFinite(number)) throw new InvalidOperationException("Número inválido."); return number;
    }
    private static int Scale(TextBox box) { var number = Number(box); if (number < 1 || number > 10000 || number != Math.Truncate(number)) throw new InvalidOperationException("Escala deve ser um inteiro entre 1 e 10000."); return (int)number; }
    private ClientData Form() => new() { Client=ClientBox.Text.Trim(),Code=CodeBox.Text.Trim(),Project=ProjectBox.Text.Trim(),Address=AddressBox.Text.Trim(),District=DistrictBox.Text.Trim(),Block=BlockBox.Text.Trim(),Lot=LotBox.Text.Trim(),Registry=RegistryBox.Text.Trim(),Technician=TechnicianBox.Text.Trim(),ArtRrt=ArtBox.Text.Trim(),Revision=RevisionBox.Text.Trim(),LandArea=Number(AreaBox),BaseFolder=BaseFolderBox.Text.Trim() };
    private void CommitGrids() { foreach(var grid in new[]{ViewsGrid,PdfGrid,DimensionGrid,TagGrid}) { grid.CommitEdit(DataGridEditingUnit.Cell,true); grid.CommitEdit(DataGridEditingUnit.Row,true); } }
    private List<long> SelectedSheets() { CommitGrids(); return (_catalog?.Sheets ?? new()).Where(s=>s.Include).Select(s=>s.Id).ToList(); }
    private async void RefreshClick(object sender,RoutedEventArgs e) => await Work(async()=>{_previews.Clear();await Refresh();await LoadDashboardPreviews();});
    private async void SaveClientClick(object sender,RoutedEventArgs e) => await Work(async()=> { var data=Form(); ReportBox.Text=await _bridge.Run(app=>ProjectService.SaveClient(Checked(app),data)); await Refresh(); });
    private async void CopilotoClick(object sender,RoutedEventArgs e) => await Work(async()=> { await _bridge.Run(app=>{Checked(app);CopilotoLauncher.Open(app);return true;}); ReportBox.Text="BIM Copiloto existente aberto. Instalação original preservada."; });
    private void MapNavClick(object sender,RoutedEventArgs e)=>GoTo(10);
    private async void OpenMapWorkflowClick(object sender,RoutedEventArgs e)
    {
        GoTo(10);
        MapWorkflowStatus.Text = "Confira o arquivo e a unidade. Depois clique em “1 · Carregar mapa no projeto”.";
        if(_catalog?.Views.FirstOrDefault(v=>v.Name.StartsWith("LOC",StringComparison.OrdinalIgnoreCase)) is Choice map)
        {
            MapView.SelectedItem=map;
            try{await ShowPreview(map.Id);}catch(Exception ex){ReportBox.Text=ex.Message;}
        }
    }
    private void PdfNavClick(object sender,RoutedEventArgs e)=>GoTo(7);
    private void ReviewNavClick(object sender,RoutedEventArgs e)=>GoTo(6);
    private async Task Cuts(bool manual)
    {
        var id=Id(CutView);var h=Number(CutHeight);var d=Number(CutDepth);var s=Scale(CutScale);
        ReportBox.Text=await _bridge.Run(app=>{Checked(app);return DrawingService.Sections(app,id,manual,h,d,s);}); await Refresh();
    }
    private async void ManualCutsClick(object sender,RoutedEventArgs e)=>await Work(()=>Cuts(true),true);
    private async void AutoCutsClick(object sender,RoutedEventArgs e)=>await Work(()=>Cuts(false));
    private async void FacadesClick(object sender,RoutedEventArgs e)=>await Work(async()=> {var id=Id(CutView);var scale=Scale(CutScale);ReportBox.Text=await _bridge.Run(app=>DrawingService.Facades(Checked(app),id,scale));await Refresh();});
    private async void SheetsClick(object sender,RoutedEventArgs e)=>await Work(async()=> {CommitGrids();var ids=(_catalog?.Views??new()).Where(v=>v.Include).Select(v=>v.Id).ToList();var type=Id(TitleBlockBox);var sheet=Id(DestinationSheet);ReportBox.Text=await _bridge.Run(app=>DrawingService.Sheets(Checked(app),ids,type,sheet));await Refresh();});
    private async void ChecklistClick(object sender,RoutedEventArgs e)=>await Work(async()=> {var rows=await _bridge.Run(app=>ProjectService.Checklist(Checked(app)));ChecklistGrid.ItemsSource=rows;ReportBox.Text=$"{rows.Count} verificações. {rows.Count(r=>r.Status=="Pendente")} pendências documentais; {rows.Count(r=>r.Status is "Revisar" or "Manual")} itens para revisão.";});
    private async void AnnotationViewChanged(object sender,SelectionChangedEventArgs e)
    {
        if (ApplyDimensionsButton==null || ApplyTagsButton==null) return;
        if (ReferenceEquals(sender,DimensionView)) { _dimensionScan=null;ApplyDimensionsButton.IsEnabled=false;if(DimensionSummary!=null)DimensionSummary.Text="Prévia: faça a varredura da vista"; }
        if (ReferenceEquals(sender,TagView)) { _tagScan=null;ApplyTagsButton.IsEnabled=false; }
        if(_loaded&&!_binding&&!_busy&&sender is ComboBox combo&&combo.SelectedItem is Choice selected)await Work(()=>ShowPreview(selected.Id));
    }
    private async void ScanDimensionsClick(object sender,RoutedEventArgs e)=>await Work(async()=>
    {
        var id=Id(DimensionView);var outer=OuterCheck.IsChecked==true;var inner=InnerCheck.IsChecked==true;var opening=OpeningCheck.IsChecked==true;var offset=Number(OffsetBox);var setbacks=SetbackCheck.IsChecked==true;
        _dimensionScan=null;ApplyDimensionsButton.IsEnabled=false;
        _dimensionScan=await _bridge.Run(app=>AnnotationService.Dimensions(Checked(app),id,outer,inner,opening,offset,setbacks));
        DimensionGrid.ItemsSource=_dimensionScan.Proposals;ReportBox.Text=string.Join("\n",_dimensionScan.Notes)+$"\n{_dimensionScan.Proposals.Count(p=>p.Include)} propostas validadas. Simulação revertida.";
        ApplyDimensionsButton.IsEnabled=_dimensionScan.Proposals.Any(p=>p.Include);
        DimensionSummary.Text=$"Prévia: {_dimensionScan.Proposals.Count} cotas · {_dimensionScan.Proposals.Count(p=>p.Status.StartsWith("Revisar"))} para revisar";
        GoTo(4);await ShowPreview(id);
    });
    private async void ScanTagsClick(object sender,RoutedEventArgs e)=>await Work(async()=>
    {
        var id=Id(TagView);var rooms=RoomCheck.IsChecked==true;var openings=WindowDoorCheck.IsChecked==true;var materials=MaterialCheck.IsChecked==true;
        _tagScan=null;ApplyTagsButton.IsEnabled=false;
        _tagScan=await _bridge.Run(app=>AnnotationService.Tags(Checked(app),id,rooms,openings,materials));
        TagGrid.ItemsSource=_tagScan.Proposals;ReportBox.Text=string.Join("\n",_tagScan.Notes.Distinct())+$"\n{_tagScan.Proposals.Count(p=>p.Include)} propostas validadas. Simulação revertida.";
        ApplyTagsButton.IsEnabled=_tagScan.Proposals.Any(p=>p.Include);
        ShowPage(5);
    });
    private async void ApplyDimensionsClick(object sender,RoutedEventArgs e)=>await Work(async()=>{CommitGrids();var scan=_dimensionScan??throw new InvalidOperationException("Faça a varredura primeiro.");ReportBox.Text=await _bridge.Run(app=>AnnotationService.Apply(Checked(app),scan));ApplyDimensionsButton.IsEnabled=false;_steps[4].Complete=true;GoTo(4);_previews.Remove(scan.ViewId);await ShowPreview(scan.ViewId);});
    private async void ApplyTagsClick(object sender,RoutedEventArgs e)=>await Work(async()=>{CommitGrids();var scan=_tagScan??throw new InvalidOperationException("Faça a varredura primeiro.");ReportBox.Text=await _bridge.Run(app=>AnnotationService.Apply(Checked(app),scan));ApplyTagsButton.IsEnabled=false;_steps[5].Complete=true;GoTo(5);_previews.Remove(scan.ViewId);await ShowPreview(scan.ViewId);});
    private async void ReviewClick(object sender,RoutedEventArgs e)=>await Work(async()=>
    {
        if(ReviewConfirm.IsChecked!=true)throw new InvalidOperationException("Marque a confirmação após conferir o projeto.");
        var reviewer=ReviewerBox.Text;var ids=SelectedSheets();ReportBox.Text=await _bridge.Run(app=>ProjectService.Review(Checked(app),reviewer,ids));
        ReviewLabel.Text=ReportBox.Text;
        _steps[6].Complete=true;
    });
    private void SelectAllSheetsClick(object sender,RoutedEventArgs e){if(_catalog==null)return;foreach(var sheet in _catalog.Sheets)sheet.Include=true;PdfGrid.Items.Refresh();}
    private async void ExportClick(object sender,RoutedEventArgs e)=>await Work(async()=>{var ids=SelectedSheets();var emission=await _bridge.Run(app=>ProjectService.Export(Checked(app),ids));ReportBox.Text="PDF emitido:\n"+emission.Pdf+"\nRegistro de emissão e cadastro gravados junto ao PDF.";await Refresh();});
    private async void FoldersClick(object sender,RoutedEventArgs e)=>await Work(async()=>{var data=await _bridge.Run(app=>ProjectService.Read(Checked(app)));ReportBox.Text="Estrutura criada:\n"+Files.CreateFolders(data);});
    private async void OpenFolderClick(object sender,RoutedEventArgs e)=>await Work(async()=>{var data=await _bridge.Run(app=>ProjectService.Read(Checked(app)));var folder=Files.ProjectFolder(data);if(!Directory.Exists(folder))throw new InvalidOperationException("Crie a estrutura de pastas primeiro.");Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});});
    private async void ProtocolClick(object sender,RoutedEventArgs e)=>await Work(async()=>{var emission=EmissionBox.SelectedItem as Emission??throw new InvalidOperationException("Selecione uma emissão.");var number=ProtocolBox.Text;var date=ProtocolDate.SelectedDate??throw new InvalidOperationException("Informe a data do protocolo.");var notes=ProtocolNote.Text;ReportBox.Text=await _bridge.Run(app=>ProjectService.Protocol(Checked(app),emission,number,date,notes));});
    private void BrowseDwgClick(object sender,RoutedEventArgs e){var dialog=new OpenFileDialog{Filter="Mapa AutoCAD (*.dwg)|*.dwg",CheckFileExists=true};if(dialog.ShowDialog(this)==true){DwgBox.Text=dialog.FileName;UnitConfirmed.IsChecked=false;}}
    private async void ImportMapClick(object sender,RoutedEventArgs e)=>await Work(async()=>
    {
        if(UnitConfirmed.IsChecked!=true)throw new InvalidOperationException("Confira e confirme a unidade do DWG.");
        var plan=Id(MapPlan);var path=DwgBox.Text.Trim();var scale=Scale(MapScale);var unit=DwgUnit.SelectedIndex switch{1=>ImportUnit.Millimeter,2=>ImportUnit.Centimeter,_=>ImportUnit.Meter};
        var id=await _bridge.Run(app=>{Checked(app);return DrawingService.ImportMap(app,plan,path,unit,scale);});await Refresh();MapView.SelectedItem=_catalog!.Views.FirstOrDefault(v=>v.Id==id);
        MapWorkflowStatus.Text="Mapa carregado e aberto no Revit. Clique em “2 · Marcar retângulo no Revit” e arraste sobre o trecho desejado.";
        await ShowPreview(id);
    }, report:false);
    private long MapId()
    {
        if(MapView.SelectedItem is Choice selected&&selected.Id>0)return selected.Id;
        var fallback=_catalog?.Views.FirstOrDefault(v=>v.Name.StartsWith("LOC",StringComparison.OrdinalIgnoreCase));
        if(fallback!=null){MapView.SelectedItem=fallback;return fallback.Id;}
        throw new InvalidOperationException("Carregue o DWG primeiro. Depois a vista de localização será selecionada automaticamente.");
    }
    private async void OpenMapViewClick(object sender,RoutedEventArgs e)=>await Work(async()=>{var id=MapId();await _bridge.Run(app=>{Checked(app);return DrawingService.FocusMap(app,id);});MapWorkflowStatus.Text="Vista de localização aberta no Revit. Confira o norte e a escala antes de marcar o recorte.";}, report:false);
    private async void CropMapClick(object sender,RoutedEventArgs e)=>await Work(async()=>{var id=MapId();var scale=Scale(MapScale);var message=await _bridge.Run(app=>{Checked(app);return DrawingService.CropMap(app,id,scale);});MapWorkflowStatus.Text=message+" A prévia foi atualizada.";_previews.Remove(id);await ShowPreview(id);},true, false);
    private void MapToSheetClick(object sender,RoutedEventArgs e)
    {
        if(_catalog==null){ReportBox.Text="Atualize o modelo e carregue a localização.";return;}
        Choice choice;
        try{choice=_catalog.Views.First(v=>v.Id==MapId());}catch(Exception ex){MapWorkflowStatus.Text=ex.Message;GoTo(10);return;}
        foreach(var view in _catalog.Views)view.Include=view.Id==choice.Id;ViewsGrid.Items.Refresh();GoTo(2);
    }
    private async void OpenViewClick(object sender,RoutedEventArgs e)=>await Work(async()=>{var id=Id(PreviewView);await _bridge.Run(app=>{var doc=Checked(app);DrawingService.Activate(app,ProjectService.View(doc,id));return true;});},true,true,true);
    private static BitmapImage Bitmap(string path)
    {
        var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.UriSource=new Uri(path);bitmap.EndInit();bitmap.Freeze();return bitmap;
    }
    private async Task<BitmapImage> PreviewBitmap(long id)
    {
        if(_previews.TryGetValue(id,out var cached))return cached;
        var path=await _bridge.Run(app=>ProjectService.Preview(Checked(app),id));return _previews[id]=Bitmap(path);
    }
    private async Task ShowPreview(long id)
    {
        var bitmap=await PreviewBitmap(id);PreviewImage.Source=bitmap;PreviewHint.Visibility=System.Windows.Visibility.Collapsed;
        ResetZoom();
        var scale=await _bridge.Run(app=>ProjectService.View(Checked(app),id).Scale);ScaleLabel.Text=scale>0?"Escala: 1:"+scale:"Escala: —";
        _binding=true;try{if(PreviewView.ItemsSource is IList<Choice> choices){var choice=choices.FirstOrDefault(c=>c.Id==id);if(choice==null){choice=new Choice{Id=id,Name=_visuals.FirstOrDefault(v=>v.Id==id)?.ViewName??"Vista selecionada"};var expanded=choices.ToList();expanded.Add(choice);PreviewView.ItemsSource=expanded;}PreviewView.SelectedItem=choice;}}finally{_binding=false;}
        foreach(var item in _visuals){item.Selected=item.Id==id;if(item.Id==id)item.Preview=bitmap;}
        var map=_visuals.FirstOrDefault(v=>v.Title=="Localização");if(map?.Id==id){MapPreviewImage.Source=bitmap;MapPreviewHint.Visibility=System.Windows.Visibility.Collapsed;}
    }
    private async Task LoadDashboardPreviews()
    {
        if(PreviewView.SelectedItem is Choice selected)
        {try{await ShowPreview(selected.Id);}catch(Exception ex){PreviewHint.Text="Prévia indisponível: "+ex.Message;PreviewHint.Visibility=System.Windows.Visibility.Visible;}}
        var notes=new List<string>();
        foreach(var visual in _visuals.Where(v=>v.Id>0))
        {
            StatusLabel.Text="Gerando miniatura: "+visual.Title+"…";
            try{visual.Preview=await PreviewBitmap(visual.Id);if(visual.Title=="Localização"){MapPreviewImage.Source=visual.Preview;MapPreviewHint.Visibility=System.Windows.Visibility.Collapsed;}}
            catch(Exception ex){notes.Add(visual.Title+": "+ex.Message);}
        }
        if(notes.Count>0)ReportBox.Text="Algumas miniaturas não puderam ser geradas. As vistas continuam acessíveis no Revit.\n"+string.Join("\n",notes);
    }
    private async void ThumbnailClick(object sender,RoutedEventArgs e)=>await Work(async()=>
    {
        var visual=(sender as Button)?.Tag as DashboardView??throw new InvalidOperationException("Escolha uma miniatura.");
        if(visual.Id<=0)throw new InvalidOperationException("Ainda não existe uma vista de "+visual.Title+" neste projeto.");
        GoTo(-1);await ShowPreview(visual.Id);
    });
    private async void PreviewSelectionChanged(object sender,SelectionChangedEventArgs e)
    {if(_loaded&&!_binding&&!_busy&&PreviewView.SelectedItem is Choice selected)await Work(()=>ShowPreview(selected.Id));}
    private async void PreviewClick(object sender,RoutedEventArgs e)=>await Work(async()=>{var id=Id(PreviewView);_previews.Remove(id);await ShowPreview(id);});
    private void ResetZoom(){PreviewZoom.ScaleX=1;PreviewZoom.ScaleY=1;PreviewPan.X=0;PreviewPan.Y=0;}
    private void ZoomClick(object sender,RoutedEventArgs e)
    {
        if((sender as Button)?.Tag is string value&&double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var factor))
        {if(factor==0)ResetZoom();else{var zoom=Math.Clamp(PreviewZoom.ScaleX*factor,1,8);PreviewZoom.ScaleX=zoom;PreviewZoom.ScaleY=zoom;}}
    }
    private void PreviewZoomWheel(object sender,MouseWheelEventArgs e){var zoom=Math.Clamp(PreviewZoom.ScaleX*(e.Delta>0?1.2:1/1.2),1,8);PreviewZoom.ScaleX=zoom;PreviewZoom.ScaleY=zoom;e.Handled=true;}
    private void PreviewPanStart(object sender,MouseButtonEventArgs e){if(PreviewImage.Source==null)return;_dragOrigin=e.GetPosition(PreviewFrame);PreviewImage.CaptureMouse();e.Handled=true;}
    private void PreviewPanMove(object sender,MouseEventArgs e){if(_dragOrigin is not System.Windows.Point last||e.LeftButton!=MouseButtonState.Pressed)return;var next=e.GetPosition(PreviewFrame);PreviewPan.X+=next.X-last.X;PreviewPan.Y+=next.Y-last.Y;_dragOrigin=next;}
    private void PreviewPanEnd(object sender,MouseButtonEventArgs e){_dragOrigin=null;PreviewImage.ReleaseMouseCapture();}
    private void OnClosing(object? sender,CancelEventArgs e){if(_busy){e.Cancel=true;StatusLabel.Text="Aguarde a operação terminar para fechar.";}}
}
