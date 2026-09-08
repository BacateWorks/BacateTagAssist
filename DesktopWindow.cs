using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Drawing;
using System.Windows.Forms;

internal sealed class DesktopWindow : Form
{
    private static DesktopWindow? current;
    private readonly WebView2 view = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(11, 19, 15) };
    private readonly string address;
    private readonly string stateDir;
    private bool choosing;
    private string? lastFolder;

    public DesktopWindow(string address, string stateDir)
    {
        this.address = address;
        this.stateDir = stateDir;
        Text = "BacateTagAssist";
        Size = new Size(1360, 940);
        MinimumSize = new Size(800, 600);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(11, 19, 15);
        Controls.Add(view);
        current = this;
        Shown += Initialize;
        FormClosed += (_, _) => { current = null; view.Dispose(); };
    }

    private async void Initialize(object? sender, EventArgs args)
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: Path.Combine(stateDir, "WebView2"));
            await view.EnsureCoreWebView2Async(environment);
            view.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            view.CoreWebView2.Settings.IsStatusBarEnabled = false;
            view.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
            view.CoreWebView2.NavigationStarting += (_, e) => {
                if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) ||
                    uri.GetLeftPart(UriPartial.Authority) != address)
                    e.Cancel = true;
            };
            view.Source = new Uri(address);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Não foi possível carregar a interface. Verifique se o Microsoft Edge WebView2 Runtime está instalado.\n\n" + ex.Message,
                "BacateTagAssist", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    public static Task<string?> ChooseFolderAsync()
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = current;
        if (window is null || window.IsDisposed) { completion.SetResult(null); return completion.Task; }
        try {
            window.BeginInvoke(new Action(() => {
                if (window.choosing) { completion.TrySetResult(null); return; }
                window.choosing = true;
                try {
                    using var dialog = new FolderBrowserDialog {
                        Description = "Escolha a pasta",
                        UseDescriptionForTitle = true,
                        ShowNewFolderButton = false,
                        SelectedPath = window.lastFolder ?? ""
                    };
                    if (window.WindowState == FormWindowState.Minimized) window.WindowState = FormWindowState.Normal;
                    window.Activate();
                    var result = dialog.ShowDialog(window);
                    if (result == DialogResult.OK) {
                        window.lastFolder = dialog.SelectedPath;
                        completion.TrySetResult(dialog.SelectedPath);
                    } else completion.TrySetResult(null);
                } catch (Exception ex) { completion.TrySetException(ex); }
                finally { window.choosing = false; window.Activate(); }
            }));
        } catch (Exception ex) { completion.TrySetException(ex); }
        return completion.Task;
    }
}
