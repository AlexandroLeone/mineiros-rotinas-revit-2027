using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MineirosRotinas;

public sealed class ClientData
{
    public string Client { get; set; } = "";
    public string Code { get; set; } = "";
    public string Project { get; set; } = "";
    public string Address { get; set; } = "";
    public string District { get; set; } = "";
    public string Block { get; set; } = "";
    public string Lot { get; set; } = "";
    public string Registry { get; set; } = "";
    public string Technician { get; set; } = "";
    public string ArtRrt { get; set; } = "";
    public string Revision { get; set; } = "R00";
    public double LandArea { get; set; }
    public string BaseFolder { get; set; } = @"C:\Users\Digimax\Documents\Projeto Em Andamento\#Projetos\2026";
    public string ReviewStamp { get; set; } = "";
    public string ReviewBy { get; set; } = "";
    public string ReviewDate { get; set; } = "";
    public List<long> ReviewedSheets { get; set; } = new();
    public List<Emission> Emissions { get; set; } = new();
}
public sealed class Emission
{
    public string Id { get; set; } = "";
    public string Client { get; set; } = "";
    public string Code { get; set; } = "";
    public string Revision { get; set; } = "";
    public string Date { get; set; } = "";
    public string Pdf { get; set; } = "";
    public string Hash { get; set; } = "";
    public string ModelPath { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string Reviewer { get; set; } = "";
    public string ReviewDate { get; set; } = "";
    public List<string> Sheets { get; set; } = new();
    public string Label => $"{Revision} · {Date} · {Code}";
}
public sealed class Choice
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public bool Include { get; set; }
    public string Label => string.IsNullOrEmpty(Kind) ? Name : $"{Name} [{Kind}]";
}
public sealed class CheckRow
{
    public string Status { get; set; } = "";
    public string Item { get; set; } = "";
    public string Detail { get; set; } = "";
}
public sealed class Proposal
{
    public bool Include { get; set; } = true;
    public string Kind { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "Proposto";
    public string Signature { get; set; } = "";
    public string[] References { get; set; } = [];
    public long ElementId { get; set; }
    public long TypeId { get; set; }
    public double[] Start { get; set; } = [];
    public double[] End { get; set; } = [];
}
public sealed class ScanResult
{
    public long ViewId { get; set; }
    public string Stamp { get; set; } = "";
    public List<Proposal> Proposals { get; set; } = new();
    public List<string> Notes { get; set; } = new();
}
public sealed class Catalog
{
    public string Title { get; set; } = "";
    public long ActiveViewId { get; set; }
    public List<Choice> Views { get; set; } = new();
    public List<Choice> Sheets { get; set; } = new();
    public List<Choice> TitleBlocks { get; set; } = new();
    public ClientData Client { get; set; } = new();
}
public static class Files
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static string SafeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Preencha o nome do cliente e o código do projeto.");
        var text = new string(value.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).TrimEnd('.', ' ');
        if (text is "" or "." or "..") throw new InvalidOperationException("Nome de pasta inválido.");
        string stem = text.Split('.')[0].ToUpperInvariant();
        if (new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(stem) ||
            (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3]))) text = "_" + text;
        return text.Length > 70 ? text[..70] : text;
    }
    public static string ProjectFolder(ClientData data)
    {
        if (!Path.IsPathFullyQualified(data.BaseFolder)) throw new InvalidOperationException("Escolha uma pasta-base absoluta.");
        var root = Path.GetFullPath(data.BaseFolder).TrimEnd(Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(root, SafeName(data.Client), SafeName(data.Code)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Pasta fora do destino escolhido.");
        return path;
    }
    public static string CreateFolders(ClientData data)
    {
        var root = ProjectFolder(data);
        foreach (var sub in new[] { "00_Cadastro", "01_Modelo", "02_Pranchas", "03_PDF", "04_Protocolo", "05_Aprovado" })
            Directory.CreateDirectory(Path.Combine(root, sub));
        return root;
    }
    public static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    public static string HashText(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static void WriteNew(string path, object value)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, value, Json);
    }
    public static string NewId() => DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
}
