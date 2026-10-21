using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Markdig;
using Microsoft.Web.WebView2.Core;

namespace MarkdownViewerCS;

public partial class MainWindow : Window
{
    const int DefaultFontSize = 16, MinFontSize = 8, MaxFontSize = 48;
    static readonly string[] MarkdownExtensions = [".md", ".markdown", ".mdown", ".mkd"];
    readonly MarkdownPipeline pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();
    string? currentFilePath;
    int fontSize = DefaultFontSize;
    bool darkMode = true;

    public MainWindow()
    {
        InitializeComponent();
        // Sin esto, WebView2 consume el arrastre externo y navega al archivo,
        // que Edge muestra como texto plano en una ventana aparte.
        // Desactivado, el drop llega a los manejadores WPF de la ventana.
        webView.AllowExternalDrop = false;
        Loaded += async (_, _) => await InitializeAsync();
    }

    async Task InitializeAsync()
    {
        await webView.EnsureCoreWebView2Async();
        webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        string[] args = Environment.GetCommandLineArgs();
        if (args.Length > 1) OpenFile(args[1]);
        else ShowHome();
    }

    /// <summary>Muestra la pantalla inicial (botón de abrir y atajos).</summary>
    void ShowHome()
    {
        currentFilePath = null;
        Title = "Visor Markdown";
        // WebView2 es una ventana nativa que siempre se dibuja encima del contenido WPF,
        // así que hay que ocultarla para que el panel inicial sea visible y clickeable.
        webView.Visibility = Visibility.Collapsed;
        homePanel.Visibility = Visibility.Visible;
        Render("");
    }

    void OpenFile(string path)
    {
        if (!IsMarkdownFile(path)) return;
        string markdown = ReadTextOrShowError(path);
        currentFilePath = path;
        Title = Path.GetFileName(path) + " - Visor Markdown";
        homePanel.Visibility = Visibility.Collapsed;
        webView.Visibility = Visibility.Visible;
        Render(markdown);
    }

    void OpenFile_Click(object sender, RoutedEventArgs e) => ShowOpenFileDialog();

    void OpenCommand_Executed(object sender, ExecutedRoutedEventArgs e) => ShowOpenFileDialog();

    void ShowOpenFileDialog()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Abrir archivo Markdown",
            Filter = "Archivos Markdown (*.md;*.markdown;*.mdown;*.mkd)|*.md;*.markdown;*.mdown;*.mkd|Todos los archivos (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true) OpenFile(dialog.FileName);
    }

    void Render(string markdown)
    {
        string article = Markdown.ToHtml(markdown, pipeline);
        string theme = darkMode ? "dark" : "light";
        string html = """
<!doctype html><html class="__THEME__" lang="es"><head><meta charset="utf-8"><style>
:root{--size:__SIZE__px;--bg:#fff;--surface:#f6f8fa;--text:#24292f;--border:#d0d7de;--code:#afb8c133;--link:#0969da;color-scheme:light}
:root.dark{--bg:#0d1117;--surface:#161b22;--text:#c9d1d9;--border:#30363d;--code:#6e768166;--link:#58a6ff;color-scheme:dark}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif}
main{max-width:960px;min-height:100vh;margin:auto;padding:24px;font-size:var(--size);line-height:1.6}
h1,h2{border-bottom:1px solid var(--border);padding-bottom:.3em}a{color:var(--link)}code{padding:.15em .35em;background:var(--code);border-radius:4px}pre{padding:16px;overflow:auto;background:var(--surface);border-radius:8px}pre code{padding:0;background:none}blockquote{margin-left:0;padding-left:1em;border-left:4px solid var(--border)}table{border-collapse:collapse}th,td{padding:6px 13px;border:1px solid var(--border)}img{max-width:100%}
</style></head><body><main><article>__ARTICLE__</article></main><script>
document.addEventListener('keydown',e=>{let a=null;if(e.ctrlKey&&(e.key==='+'||e.key==='='))a='in';else if(e.ctrlKey&&e.key==='-')a='out';else if(e.ctrlKey&&e.key.toLowerCase()==='o')a='open';else if(e.key==='F5')a='reload';else if(e.key==='F6')a='theme';if(a){e.preventDefault();chrome.webview.postMessage(a)}});
</script></body></html>
""";
        html = html.Replace("__THEME__", theme).Replace("__SIZE__", fontSize.ToString()).Replace("__ARTICLE__", article);
        webView.CoreWebView2.NavigateToString(html);
    }

    void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        switch (e.TryGetWebMessageAsString())
        {
            case "in": if (fontSize < MaxFontSize) fontSize += 2; break;
            case "out": if (fontSize > MinFontSize) fontSize -= 2; break;
            case "theme": darkMode = !darkMode; break;
            case "reload": if (currentFilePath is not null) OpenFile(currentFilePath); return;
            case "open": ShowOpenFileDialog(); return;
            default: return;
        }
        Render(currentFilePath is null ? "" : ReadTextOrShowError(currentFilePath));
    }

    string ReadTextOrShowError(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "No se pudo abrir el archivo.\n\n" + ex.Message,
                "Visor Markdown", MessageBoxButton.OK, MessageBoxImage.Error);
            return "";
        }
    }

    void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetDroppedMarkdown(e.Data, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void Window_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (TryGetDroppedMarkdown(e.Data, out string? path) && path is not null) OpenFile(path);
    }

    static bool TryGetDroppedMarkdown(IDataObject data, out string? path)
    {
        path = null;
        if (!data.GetDataPresent(DataFormats.FileDrop)) return false;
        if (data.GetData(DataFormats.FileDrop) is not string[] files) return false;
        path = files.FirstOrDefault(IsMarkdownFile);
        return path is not null;
    }

    static bool IsMarkdownFile(string path) =>
        MarkdownExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
