using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});
builder.WebHost.UseUrls("http://127.0.0.1:0");
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

var stateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BacateTagAssist");
Directory.CreateDirectory(stateDir);
var historyFile = Path.Combine(stateDir, "history.json");

app.MapGet("/api/status", () => Results.Ok(new { ready = true, historyAvailable = File.Exists(historyFile) }));

app.MapPost("/api/select-folder", async () => {
    var selected = await DesktopWindow.ChooseFolderAsync();
    return selected is null ? Results.NoContent() : Results.Ok(new { path = selected });
});

app.MapPost("/api/scan", (ScanRequest request) => {
    if (string.IsNullOrWhiteSpace(request.Path) || !Directory.Exists(request.Path))
        return Results.BadRequest(new { error = "A pasta informada não existe." });

    var root = Path.GetFullPath(request.Path);
    var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        ".mkv", ".mp4", ".avi", ".mov", ".m4v", ".ts", ".m2ts", ".webm",
        ".srt", ".ass", ".ssa", ".vtt"
    };
    var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Where(p => allowed.Contains(Path.GetExtension(p)))
        .Where(p => !Path.GetRelativePath(root, p).Split(Path.DirectorySeparatorChar)
            .Any(part => part.Equals("BDMV", StringComparison.OrdinalIgnoreCase)
                || part.Equals("CERTIFICATE", StringComparison.OrdinalIgnoreCase)
                || part.Equals("VIDEO_TS", StringComparison.OrdinalIgnoreCase)))
        .Take(5000)
        .Select(p => new {
            source = Path.GetRelativePath(root, p),
            originalName = Path.GetFileName(p),
            extension = Path.GetExtension(p),
            seasonEpisode = FindSeasonEpisode(Path.GetFileNameWithoutExtension(p)),
            isSubtitle = new[] { ".srt", ".ass", ".ssa", ".vtt" }.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)
        }).ToArray();
    return Results.Ok(new { root, count = files.Length, files });
});

app.MapPost("/api/apply", (ApplyRequest request) => {
    try {
        if (string.IsNullOrWhiteSpace(request.Root) || !Directory.Exists(request.Root))
            return Results.BadRequest(new { error = "A pasta raiz não existe." });
        request = request with { Changes = request.Changes ?? [] };

        var root = Path.GetFullPath(request.Root);
        var parent = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Não é possível renomear esta pasta raiz.");
        var cleanRootName = string.IsNullOrWhiteSpace(request.NewRootName) ? Path.GetFileName(root) : SanitizeFileName(request.NewRootName);
        var finalRoot = Path.Combine(parent, cleanRootName);
        if (!root.Equals(finalRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(finalRoot))
            return Results.Conflict(new { error = $"Já existe uma pasta chamada {cleanRootName}." });
        var changes = new List<RenameRecord>();
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in request.Changes) {
            var source = SafeInside(root, item.Source);
            if (!File.Exists(source)) return Results.BadRequest(new { error = $"Arquivo não encontrado: {item.Source}" });
            var extension = Path.GetExtension(source);
            var baseName = Path.GetFileNameWithoutExtension(item.NewName);
            var cleanName = SanitizeFileName(baseName) + extension;
            if (string.IsNullOrWhiteSpace(baseName)) return Results.BadRequest(new { error = "Um dos novos nomes ficou vazio." });
            var destination = Path.Combine(Path.GetDirectoryName(source)!, cleanName);
            if (!destinations.Add(destination)) return Results.BadRequest(new { error = $"Nome duplicado: {cleanName}" });
            if (!source.Equals(destination, StringComparison.OrdinalIgnoreCase) && File.Exists(destination))
                return Results.Conflict(new { error = $"Já existe um arquivo chamado {cleanName}." });
            if (!source.Equals(destination, StringComparison.Ordinal)) changes.Add(new(source, destination, ""));
        }

        for (var i = 0; i < changes.Count; i++) {
            var c = changes[i];
            var temp = Path.Combine(Path.GetDirectoryName(c.Source)!, $".__rename_{Guid.NewGuid():N}{Path.GetExtension(c.Source)}");
            File.Move(c.Source, temp);
            changes[i] = c with { Temp = temp };
        }
        foreach (var c in changes) File.Move(c.Temp, c.Destination);

        var history = LoadHistory(historyFile);
        if (!root.Equals(finalRoot, StringComparison.Ordinal)) Directory.Move(root, finalRoot);
        history.Add(new HistoryEntry(DateTimeOffset.Now, root, finalRoot, changes.Select(c => new HistoryChange(Path.GetRelativePath(root, c.Source), Path.GetRelativePath(root, c.Destination))).ToList()));
        if (history.Count > 50) history.RemoveAt(0);
        File.WriteAllText(historyFile, JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true }));
        return Results.Ok(new { renamed = changes.Count, root = finalRoot });
    } catch (Exception ex) {
        return Results.Problem($"Não foi possível concluir: {ex.Message}");
    }
});

app.MapPost("/api/undo", () => {
    var history = LoadHistory(historyFile);
    if (history.Count == 0) return Results.BadRequest(new { error = "Não há operação para desfazer." });
    var last = history[^1];
    if (!last.RootBefore.Equals(last.RootAfter, StringComparison.OrdinalIgnoreCase)) {
        if (!Directory.Exists(last.RootAfter)) return Results.Conflict(new { error = "A pasta renomeada não foi encontrada." });
        if (Directory.Exists(last.RootBefore)) return Results.Conflict(new { error = "O nome anterior da pasta já está ocupado." });
        Directory.Move(last.RootAfter, last.RootBefore);
    }
    foreach (var change in last.Changes.AsEnumerable().Reverse()) {
        var from = Path.Combine(last.RootBefore, change.From);
        var to = Path.Combine(last.RootBefore, change.To);
        if (!File.Exists(to)) return Results.Conflict(new { error = $"O arquivo atual não foi encontrado: {change.To}" });
        if (File.Exists(from)) return Results.Conflict(new { error = $"O nome anterior já está ocupado: {change.From}" });
        File.Move(to, from);
    }
    history.RemoveAt(history.Count - 1);
    File.WriteAllText(historyFile, JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true }));
    return Results.Ok(new { restored = last.Changes.Count, root = last.RootBefore });
});

try {
    app.StartAsync().GetAwaiter().GetResult();
    var address = app.Urls.Single();
    var windowThread = new Thread(() => {
        System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        System.Windows.Forms.Application.Run(new DesktopWindow(address, stateDir));
    });
    windowThread.SetApartmentState(ApartmentState.STA);
    windowThread.Start();
    windowThread.Join();
} catch (Exception ex) {
    MessageBox.Show("Não foi possível abrir o BacateTagAssist.\n\n" + ex.Message, "BacateTagAssist");
} finally {
    app.StopAsync().GetAwaiter().GetResult();
    app.DisposeAsync().AsTask().GetAwaiter().GetResult();
}

static string? FindSeasonEpisode(string name) {
    var full = Regex.Match(name, @"(?<![A-Za-z0-9])S(?<s>\d{1,2})[ ._-]*E(?<e1>\d{1,3})(?:[ ._-]*E(?<e2>\d{1,3}))?(?!\d)", RegexOptions.IgnoreCase);
    if (full.Success) {
        var result = $"S{int.Parse(full.Groups["s"].Value):00}E{int.Parse(full.Groups["e1"].Value):00}";
        if (full.Groups["e2"].Success) result += $"-E{int.Parse(full.Groups["e2"].Value):00}";
        return result;
    }
    var labeled = Regex.Match(name, @"(?<![A-Za-z0-9])(?:EP(?:ISODE|ISODIO)?|E)[ ._-]*(?<e1>\d{1,3})(?:[ ._]*-[ ._]*(?:E|EP)?[ ._-]*(?<e2>\d{1,3}))?(?!\d)", RegexOptions.IgnoreCase);
    if (labeled.Success) {
        var result = $"E{int.Parse(labeled.Groups["e1"].Value):00}";
        if (labeled.Groups["e2"].Success) result += $"-E{int.Parse(labeled.Groups["e2"].Value):00}";
        return result;
    }
    var numeric = Regex.Match(name.Trim(), @"^(?<e1>\d{1,3})(?:[ ._-]+(?<e2>\d{1,3}))?(?:[ ._-].*)?$", RegexOptions.IgnoreCase);
    if (numeric.Success) {
        var result = $"E{int.Parse(numeric.Groups["e1"].Value):00}";
        if (numeric.Groups["e2"].Success) result += $"-E{int.Parse(numeric.Groups["e2"].Value):00}";
        return result;
    }
    return null;
}

static string SafeInside(string root, string relative) {
    var full = Path.GetFullPath(Path.Combine(root, relative));
    var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Caminho fora da pasta selecionada.");
    return full;
}

static string SanitizeFileName(string name) {
    foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, ' ');
    return Regex.Replace(name.Trim().TrimEnd('.'), @"\s+", " ");
}

static List<HistoryEntry> LoadHistory(string file) {
    try { return File.Exists(file) ? JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(file)) ?? [] : []; }
    catch { return []; }
}

record ScanRequest(string Path);
record ApplyRequest(string Root, string? NewRootName, List<ChangeRequest>? Changes);
record ChangeRequest(string Source, string NewName);
record RenameRecord(string Source, string Destination, string Temp);
record HistoryEntry(DateTimeOffset At, string RootBefore, string RootAfter, List<HistoryChange> Changes);
record HistoryChange(string From, string To);
