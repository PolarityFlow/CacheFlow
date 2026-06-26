// ============================================================================
//  CacheFlow v1.3 — browser cache cleaner
//  PolarityFlow · Adrian Zingg
//  https://www.polarityflow.com  ·  MIT License
//
//  Compiles with the in-box C# compiler (.NET Framework 4.x):
//  run build.bat — no SDK or extra tooling required.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

[assembly: AssemblyTitle("CacheFlow")]
[assembly: AssemblyDescription("Browser cache cleaner")]
[assembly: AssemblyCompany("PolarityFlow")]
[assembly: AssemblyProduct("CacheFlow")]
[assembly: AssemblyCopyright("(c) 2026 PolarityFlow, Adrian Zingg")]
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]

namespace CacheFlow
{
    public class BrowserDef
    {
        public string Name, Family, Root, CacheRoot, Proc, Color, Mono, ExeName;
        public string[] ExePaths;
        public Func<string, string> VersionTransform;
    }

    public class BrowserInfo
    {
        public BrowserDef Def;
        public List<string> CacheDirs;
        public List<string> Passwords, History, Cookies, Autofill;
        public int Profiles;
        public bool SupportsHistory;
        public string Version;
        public string ExePath;
    }

    public class RowUi
    {
        public Border Border;
        public CheckBox Check;
        public BrowserInfo Info;
        public long Size;
        public bool Running;
        public TextBlock Sub;
    }

    public static class Program
    {
        // ── Donations ───────────────────────────────────────────────────────
        // Set any value to "" to hide it in the donate dialog.
        static readonly string[][] Donate = new string[][] {
            new string[] { "PayPal",            "https://www.paypal.com/ncp/payment/FEZJVK7BHFBSG" },
            new string[] { "P2P (Sentinel)",    "sent10gk4xefa2x542636q54jef2wrv3jaz04p6aens" },
            new string[] { "Bitcoin (SegWit)",  "bc1qyxs5rmjgu98xpgl3puystkgq3068dh7qudr9dz" },
            new string[] { "Bitcoin (Taproot)", "bc1papvjkahdtw9ssc8q0vx9ddw8kvksvcyc32qle5tq2smhw879xkzqvec9ws" },
            new string[] { "ETH / EVM",         "0xCef2f3E90e1b24c85c7269eea7C970D1062436ae" },
            new string[] { "Cosmos (ATOM)",     "cosmos10gk4xefa2x542636q54jef2wrv3jaz046ptqhl" },
            new string[] { "Injective (INJ)",   "" },
            new string[] { "XRPL EVM",          "" }
        };

        // ── Browser definitions ─────────────────────────────────────────────
        static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        static readonly string AppData      = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        static readonly string ProgFiles    = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        static readonly string ProgFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        static BrowserDef[] Defs;

        static readonly string[] ChromiumProfileCache = new string[] {
            "Cache", "Code Cache", "GPUCache", "Media Cache", "DawnCache",
            "DawnGraphiteCache", "DawnWebGPUCache",
            "Service Worker\\CacheStorage", "Service Worker\\ScriptCache"
        };
        static readonly string[] ChromiumRootCache = new string[] { "ShaderCache", "GrShaderCache", "GraphiteDawnCache" };
        static readonly string[] GeckoProfileCache = new string[] { "cache2", "startupCache", "shader-cache", "jumpListCache", "thumbnails" };

        static void InitDefs()
        {
            // DuckDuckGo browser is an MSIX (Microsoft Store) app: data lives under
            // %LOCALAPPDATA%\Packages\..., the exe under WindowsApps (resolved via registry).
            string duckData = null, duckExe = null;
            try
            {
                string pkgs = Path.Combine(LocalAppData, "Packages");
                if (Directory.Exists(pkgs))
                {
                    string[] dd = Directory.GetDirectories(pkgs, "DuckDuckGo.DesktopBrowser_*");
                    if (dd.Length > 0) duckData = Path.Combine(dd[0], "LocalState\\DDGWebView");
                }
            }
            catch { }
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    "Software\\Classes\\Local Settings\\Software\\Microsoft\\Windows\\CurrentVersion\\AppModel\\Repository\\Packages"))
                {
                    if (k != null)
                    {
                        foreach (string name in k.GetSubKeyNames())
                        {
                            if (!name.StartsWith("DuckDuckGo.DesktopBrowser_")) continue;
                            using (var sk = k.OpenSubKey(name))
                            {
                                if (sk == null) continue;
                                string root = sk.GetValue("PackageRootFolder") as string;
                                if (string.IsNullOrEmpty(root)) continue;
                                string exe = Path.Combine(root, "WindowsBrowser\\DuckDuckGo.exe");
                                if (File.Exists(exe)) { duckExe = exe; break; }
                            }
                        }
                    }
                }
            }
            catch { }

            Defs = new BrowserDef[] {
                new BrowserDef { Name="Google Chrome", Family="chromium", Root=LocalAppData+"\\Google\\Chrome\\User Data",
                    Proc="chrome", Color="#4E8CF5", Mono="Ch", ExeName="chrome.exe",
                    ExePaths=new string[]{ ProgFiles+"\\Google\\Chrome\\Application\\chrome.exe", ProgFilesX86+"\\Google\\Chrome\\Application\\chrome.exe", LocalAppData+"\\Google\\Chrome\\Application\\chrome.exe" } },
                new BrowserDef { Name="Microsoft Edge", Family="chromium", Root=LocalAppData+"\\Microsoft\\Edge\\User Data",
                    Proc="msedge", Color="#2EC4B6", Mono="Ed", ExeName="msedge.exe",
                    ExePaths=new string[]{ ProgFilesX86+"\\Microsoft\\Edge\\Application\\msedge.exe", ProgFiles+"\\Microsoft\\Edge\\Application\\msedge.exe" } },
                new BrowserDef { Name="Brave", Family="chromium", Root=LocalAppData+"\\BraveSoftware\\Brave-Browser\\User Data",
                    Proc="brave", Color="#FB542B", Mono="Br", ExeName="brave.exe",
                    ExePaths=new string[]{ ProgFiles+"\\BraveSoftware\\Brave-Browser\\Application\\brave.exe", ProgFilesX86+"\\BraveSoftware\\Brave-Browser\\Application\\brave.exe", LocalAppData+"\\BraveSoftware\\Brave-Browser\\Application\\brave.exe" },
                    // Brave exe reports <chromiumMajor>.<braveVersion> (e.g. 149.1.91.171) — strip the first segment
                    VersionTransform = delegate(string v) { var p = v.Split('.'); return p.Length == 4 ? string.Join(".", p, 1, 3) : v; } },
                new BrowserDef { Name="Vivaldi", Family="chromium", Root=LocalAppData+"\\Vivaldi\\User Data",
                    Proc="vivaldi", Color="#EF3939", Mono="Vi", ExeName="vivaldi.exe",
                    ExePaths=new string[]{ LocalAppData+"\\Vivaldi\\Application\\vivaldi.exe", ProgFiles+"\\Vivaldi\\Application\\vivaldi.exe" } },
                new BrowserDef { Name="Chromium", Family="chromium", Root=LocalAppData+"\\Chromium\\User Data",
                    Proc="chromium", Color="#7A8CA8", Mono="Cr", ExeName="chromium.exe",
                    ExePaths=new string[]{ LocalAppData+"\\Chromium\\Application\\chrome.exe" } },
                new BrowserDef { Name="Opera", Family="opera", Root=AppData+"\\Opera Software\\Opera Stable",
                    CacheRoot=LocalAppData+"\\Opera Software\\Opera Stable", Proc="opera", Color="#FF1B2D", Mono="Op", ExeName="opera.exe",
                    ExePaths=new string[]{ LocalAppData+"\\Programs\\Opera\\opera.exe", ProgFiles+"\\Opera\\opera.exe", ProgFilesX86+"\\Opera\\opera.exe" } },
                new BrowserDef { Name="Opera GX", Family="opera", Root=AppData+"\\Opera Software\\Opera GX Stable",
                    CacheRoot=LocalAppData+"\\Opera Software\\Opera GX Stable", Proc="opera", Color="#FA1E4E", Mono="GX",
                    ExePaths=new string[]{ LocalAppData+"\\Programs\\Opera GX\\opera.exe", ProgFiles+"\\Opera GX\\opera.exe" } },
                new BrowserDef { Name="Firefox", Family="gecko", Root=AppData+"\\Mozilla\\Firefox",
                    CacheRoot=LocalAppData+"\\Mozilla\\Firefox", Proc="firefox", Color="#FF7139", Mono="Fx", ExeName="firefox.exe",
                    ExePaths=new string[]{ ProgFiles+"\\Mozilla Firefox\\firefox.exe", ProgFilesX86+"\\Mozilla Firefox\\firefox.exe" } },
                new BrowserDef { Name="LibreWolf", Family="gecko", Root=AppData+"\\librewolf",
                    CacheRoot=LocalAppData+"\\librewolf", Proc="librewolf", Color="#2D8CCC", Mono="LW", ExeName="librewolf.exe",
                    ExePaths=new string[]{ ProgFiles+"\\LibreWolf\\librewolf.exe" } },
                new BrowserDef { Name="Waterfox", Family="gecko", Root=AppData+"\\Waterfox",
                    CacheRoot=LocalAppData+"\\Waterfox", Proc="waterfox", Color="#1FA3DD", Mono="Wf", ExeName="waterfox.exe",
                    ExePaths=new string[]{ ProgFiles+"\\Waterfox\\waterfox.exe" } },
                // DDGWebView is a standard chromium 'User Data' layout (Default profile,
                // ShaderCache, ...). Passwords/history are DDG-proprietary databases outside
                // the WebView profile, so the standard data files simply don't exist — safe.
                new BrowserDef { Name="DuckDuckGo", Family="chromium",
                    Root = duckData != null ? duckData : Path.Combine(LocalAppData, "__cacheflow_none__"),
                    Proc="DuckDuckGo", Color="#DE5833", Mono="Du",
                    ExePaths = duckExe != null ? new string[]{ duckExe } : null,
                    // ProductVersion looks like "0.158.7.0+<commit hash>" — keep the numeric part
                    VersionTransform = delegate(string v) { Match m = Regex.Match(v, @"^[\d.]+"); return m.Success ? m.Value.TrimEnd('.') : v; } }
            };
        }

        // ── State ───────────────────────────────────────────────────────────
        static string BaseDir;
        static string StateFile;
        static Dictionary<string, string> State = new Dictionary<string, string>();
        static Dictionary<string, string> LatestVersions = new Dictionary<string, string>();
        static Dictionary<string, BitmapSource> IconCache = new Dictionary<string, BitmapSource>();
        static List<RowUi> Rows = new List<RowUi>();
        static bool Busy;

        // ── UI references ───────────────────────────────────────────────────
        static Window Win;
        static Grid TitleBar;
        static Image TitleIcon;
        static Button BtnMin, BtnClose, BtnRescan, BtnClear, BtnDonate;
        static StackPanel BrowserList;
        static TextBlock StatusText, GeckoNote;
        static CheckBox ChkPw, ChkHist, ChkCookies, ChkForm;
        static Hyperlink SiteLink;
        static ProgressBar Progress;

        const string MainXaml = @"
<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Title='CacheFlow' Width='600' Height='700'
        WindowStartupLocation='CenterScreen' WindowStyle='None'
        AllowsTransparency='True' Background='Transparent'
        ResizeMode='CanMinimize' FontFamily='Segoe UI'
        TextOptions.TextFormattingMode='Display'>
  <Window.Resources>
    <Style TargetType='CheckBox'>
      <Setter Property='Foreground' Value='#C8CDDA'/>
      <Setter Property='FontSize' Value='13'/>
      <Setter Property='VerticalContentAlignment' Value='Center'/>
    </Style>
    <Style x:Key='PrimaryBtn' TargetType='Button'>
      <Setter Property='Background' Value='#488CAA'/>
      <Setter Property='Foreground' Value='White'/>
      <Setter Property='FontWeight' Value='SemiBold'/>
      <Setter Property='FontSize' Value='13'/>
      <Setter Property='Padding' Value='18,9'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='bd' CornerRadius='9' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='#5BA3C4'/>
              </Trigger>
              <Trigger Property='IsEnabled' Value='False'>
                <Setter TargetName='bd' Property='Background' Value='#333845'/>
                <Setter Property='Foreground' Value='#777D8C'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='GhostBtn' TargetType='Button'>
      <Setter Property='Background' Value='Transparent'/>
      <Setter Property='Foreground' Value='#C8CDDA'/>
      <Setter Property='FontSize' Value='13'/>
      <Setter Property='Padding' Value='16,9'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='bd' CornerRadius='9' Background='{TemplateBinding Background}'
                    BorderBrush='#2A2E3A' BorderThickness='1' Padding='{TemplateBinding Padding}'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='#232734'/>
              </Trigger>
              <Trigger Property='IsEnabled' Value='False'>
                <Setter Property='Foreground' Value='#777D8C'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='WinBtn' TargetType='Button'>
      <Setter Property='Background' Value='Transparent'/>
      <Setter Property='Foreground' Value='#8A91A3'/>
      <Setter Property='FontSize' Value='12'/>
      <Setter Property='Width' Value='34'/>
      <Setter Property='Height' Value='28'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='bd' CornerRadius='7' Background='{TemplateBinding Background}'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='#232734'/>
                <Setter Property='Foreground' Value='White'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
  </Window.Resources>

  <Border CornerRadius='14' Background='#14161C' BorderBrush='#2A2E3A' BorderThickness='1'>
    <DockPanel>

      <Grid x:Name='TitleBar' DockPanel.Dock='Top' Margin='20,14,12,6' Background='Transparent'>
        <StackPanel Orientation='Horizontal' HorizontalAlignment='Left'>
          <Image x:Name='TitleIcon' Width='36' Height='36' VerticalAlignment='Center'
                 RenderOptions.BitmapScalingMode='HighQuality'/>
          <StackPanel Margin='12,0,0,0' VerticalAlignment='Center'>
            <TextBlock Text='CacheFlow' FontSize='17' FontWeight='Bold' Foreground='White'/>
            <TextBlock Text='browser cache cleaner' FontSize='12' Foreground='#8A91A3'/>
          </StackPanel>
        </StackPanel>
        <StackPanel Orientation='Horizontal' HorizontalAlignment='Right' VerticalAlignment='Top'>
          <Button x:Name='BtnMin' Style='{StaticResource WinBtn}' Content='&#x2500;'/>
          <Button x:Name='BtnClose' Style='{StaticResource WinBtn}' Content='&#x2715;' Margin='2,0,0,0'/>
        </StackPanel>
      </Grid>

      <StackPanel DockPanel.Dock='Bottom' Margin='20,8,20,12'>
        <ProgressBar x:Name='Progress' Height='5' Minimum='0' Maximum='100' Value='0'
                     Visibility='Collapsed' Margin='2,0,2,8'
                     Background='#232734' Foreground='#488CAA' BorderThickness='0'/>
        <TextBlock x:Name='StatusText' Text='' FontSize='13' Foreground='#8A91A3'
                   TextWrapping='Wrap' Margin='2,0,2,8'/>
        <Grid>
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width='Auto'/>
            <ColumnDefinition Width='*'/>
            <ColumnDefinition Width='Auto'/>
          </Grid.ColumnDefinitions>
          <StackPanel Grid.Column='0' Orientation='Horizontal'>
            <Button x:Name='BtnRescan' Style='{StaticResource GhostBtn}' Content='Rescan'/>
            <Button x:Name='BtnDonate' Style='{StaticResource GhostBtn}' Foreground='#FF6A42'
                    Content='&#x2665; Donate' Margin='8,0,0,0'/>
          </StackPanel>
          <Button x:Name='BtnClear' Grid.Column='2' Style='{StaticResource PrimaryBtn}' Content='Clear selected'/>
        </Grid>
        <TextBlock FontSize='11' Foreground='#5A6072' HorizontalAlignment='Center' Margin='0,12,0,0'>
          <Run Text='CacheFlow v1.3  &#xB7;  '/><Hyperlink x:Name='SiteLink' Foreground='#5BA3C4' TextDecorations='None' ToolTip='www.polarityflow.com'><Run Text='PolarityFlow'/></Hyperlink><Run Text='  &#xB7;  Adrian Zingg'/>
        </TextBlock>
      </StackPanel>

      <Border DockPanel.Dock='Bottom' Margin='20,6,20,0' Padding='16,12,16,12'
              CornerRadius='10' Background='#1A1D26'>
        <StackPanel>
          <TextBlock Text='KEEP WHEN CLEARING' FontSize='11' FontWeight='SemiBold'
                     Foreground='#8A91A3' Margin='1,0,0,6'/>
          <WrapPanel>
            <CheckBox x:Name='ChkPw'      Content='Passwords'               IsChecked='True' Margin='0,2,20,2'/>
            <CheckBox x:Name='ChkHist'    Content='Visited sites (history)' IsChecked='True' Margin='0,2,20,2'/>
            <CheckBox x:Name='ChkCookies' Content='Cookies / logins'        IsChecked='True' Margin='0,2,20,2'/>
            <CheckBox x:Name='ChkForm'    Content='Autofill data'           IsChecked='True' Margin='0,2,0,2'/>
          </WrapPanel>
          <TextBlock x:Name='GeckoNote' FontSize='11' Foreground='#5A6072' Margin='1,6,0,0'
                     Text='Firefox-family browsers: history is never touched (it is stored together with bookmarks).'
                     Visibility='Collapsed' TextWrapping='Wrap'/>
        </StackPanel>
      </Border>

      <ScrollViewer VerticalScrollBarVisibility='Auto' Margin='20,6,20,0'>
        <StackPanel x:Name='BrowserList'/>
      </ScrollViewer>

    </DockPanel>
  </Border>
</Window>";

        const string DonateXaml = @"
<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Title='Support CacheFlow' SizeToContent='Height' Width='470'
        WindowStyle='None' AllowsTransparency='True' Background='Transparent'
        WindowStartupLocation='CenterOwner' ResizeMode='NoResize'
        FontFamily='Segoe UI' ShowInTaskbar='False'>
  <Window.Resources>
    <Style x:Key='DGhost' TargetType='Button'>
      <Setter Property='Background' Value='Transparent'/>
      <Setter Property='Foreground' Value='#C8CDDA'/>
      <Setter Property='FontSize' Value='12'/>
      <Setter Property='Padding' Value='12,6'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='bd' CornerRadius='8' Background='{TemplateBinding Background}'
                    BorderBrush='#2A2E3A' BorderThickness='1' Padding='{TemplateBinding Padding}'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='#232734'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='DPrim' TargetType='Button'>
      <Setter Property='Background' Value='#488CAA'/>
      <Setter Property='Foreground' Value='White'/>
      <Setter Property='FontWeight' Value='SemiBold'/>
      <Setter Property='FontSize' Value='12'/>
      <Setter Property='Padding' Value='12,6'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='bd' CornerRadius='8' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='#5BA3C4'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
  </Window.Resources>
  <Border CornerRadius='12' Background='#14161C' BorderBrush='#2A2E3A' BorderThickness='1' Padding='22,18,22,20'>
    <StackPanel>
      <Grid x:Name='DnTitle' Background='Transparent'>
        <StackPanel Orientation='Horizontal'>
          <TextBlock Text='&#x2665;' FontSize='15' Foreground='#FF6A42' Margin='0,0,8,0' VerticalAlignment='Center'/>
          <TextBlock Text='Support CacheFlow' FontSize='15' FontWeight='Bold' Foreground='White' VerticalAlignment='Center'/>
        </StackPanel>
        <Button x:Name='DnClose' Style='{StaticResource DGhost}' Content='&#x2715;'
                HorizontalAlignment='Right' Padding='8,3'/>
      </Grid>
      <TextBlock Margin='0,10,0,0' FontSize='13' Foreground='#8A91A3' TextWrapping='Wrap'
                 Text='CacheFlow is free. If it freed up a few gigabytes for you, a small donation helps keep the tools coming. Thank you!'/>
      <StackPanel x:Name='DnList' Margin='0,14,0,0'/>
      <TextBlock Margin='2,8,2,0' FontSize='11' Foreground='#5A6072' TextWrapping='Wrap'
                 Text='The ETH / EVM address also works on Base, BSC, Blast and Ink.'/>
      <TextBlock Margin='0,12,0,0' FontSize='11' Foreground='#5A6072' HorizontalAlignment='Center'
                 Text='PolarityFlow &#xB7; www.polarityflow.com'/>
    </StackPanel>
  </Border>
</Window>";

        // ── Entry point ─────────────────────────────────────────────────────
        [STAThread]
        public static void Main()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            InitDefs();
            BaseDir = AppDomain.CurrentDomain.BaseDirectory;
            StateFile = Path.Combine(BaseDir, "cacheflow-state.json");
            LoadState();

            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            BuildWindow();
            app.Run(Win);
        }

        static T Find<T>(FrameworkElement root, string name) where T : class
        {
            return root.FindName(name) as T;
        }

        static void BuildWindow()
        {
            Win = (Window)XamlReader.Parse(MainXaml);

            TitleBar    = Find<Grid>(Win, "TitleBar");
            TitleIcon   = Find<Image>(Win, "TitleIcon");
            BtnMin      = Find<Button>(Win, "BtnMin");
            BtnClose    = Find<Button>(Win, "BtnClose");
            BtnRescan   = Find<Button>(Win, "BtnRescan");
            BtnClear    = Find<Button>(Win, "BtnClear");
            BtnDonate   = Find<Button>(Win, "BtnDonate");
            BrowserList = Find<StackPanel>(Win, "BrowserList");
            StatusText  = Find<TextBlock>(Win, "StatusText");
            GeckoNote   = Find<TextBlock>(Win, "GeckoNote");
            ChkPw       = Find<CheckBox>(Win, "ChkPw");
            ChkHist     = Find<CheckBox>(Win, "ChkHist");
            ChkCookies  = Find<CheckBox>(Win, "ChkCookies");
            ChkForm     = Find<CheckBox>(Win, "ChkForm");
            SiteLink    = (Hyperlink)Win.FindName("SiteLink");
            Progress    = Find<ProgressBar>(Win, "Progress");

            BitmapImage appMark = LoadEmbedded("appicon.png");
            if (appMark != null)
            {
                TitleIcon.Source = appMark;
                Win.Icon = appMark;
            }

            BtnClose.Click  += delegate { SaveWindowPos(); Win.Close(); };
            BtnMin.Click    += delegate { Win.WindowState = WindowState.Minimized; };
            TitleBar.MouseLeftButtonDown += delegate { try { Win.DragMove(); } catch { } };
            BtnRescan.Click += async delegate { await RefreshList(); };
            BtnClear.Click  += async delegate { await ClearSelected(); };
            BtnDonate.Click += delegate { ShowDonateDialog(); };
            SiteLink.Click  += delegate { OpenUrl("https://www.polarityflow.com"); };

            Win.ContentRendered += async delegate { await RefreshList(); };
            RestoreWindowPos();
        }

        static void OpenUrl(string url)
        {
            try { Process.Start(url); } catch { }
        }

        static BitmapImage LoadEmbedded(string name)
        {
            try
            {
                Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
                if (s == null) return null;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = s;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        // ── Scan ────────────────────────────────────────────────────────────
        static async Task RefreshList()
        {
            if (Busy) return;
            Busy = true;
            BtnClear.IsEnabled = false;
            BtnRescan.IsEnabled = false;
            Progress.Value = 0;
            Progress.Visibility = Visibility.Visible;
            BrowserList.Children.Clear();
            Rows.Clear();
            bool anyGecko = false;
            long totalSize = 0;

            // phase 1: scan (progress 0-80)
            for (int i = 0; i < Defs.Length; i++)
            {
                BrowserDef def = Defs[i];
                StatusText.Text = "Scanning " + def.Name + "...";
                Progress.Value = Math.Round(i / (double)Defs.Length * 80);

                BrowserInfo info = null;
                long size = 0;
                await Task.Run(delegate
                {
                    info = ResolveBrowser(def);
                    if (info != null)
                        foreach (string d in info.CacheDirs) size += DirSize(d);
                });
                if (info == null) continue;
                if (def.Family == "gecko") anyGecko = true;
                totalSize += size;

                RowUi row = NewBrowserRow(info, size, IsRunning(def.Proc));
                BrowserList.Children.Add(row.Border);
                Rows.Add(row);
            }
            Progress.Value = 80;

            // phase 2: update check (progress 80-100, results cached per session)
            int updatesAvail = 0;
            for (int i = 0; i < Rows.Count; i++)
            {
                RowUi row = Rows[i];
                Progress.Value = 80 + Math.Round((i + 1) / (double)Rows.Count * 20);
                if (string.IsNullOrEmpty(row.Info.Version)) continue;
                string name = row.Info.Def.Name;
                if (!HasFeed(name)) continue;
                if (!LatestVersions.ContainsKey(name))
                    StatusText.Text = "Checking latest version: " + name + "...";

                string latest = null;
                await Task.Run(delegate { latest = GetLatestVersion(name); });
                if (string.IsNullOrEmpty(latest)) continue;

                Run note;
                if (IsNewer(row.Info.Version, latest))
                {
                    note = new Run("  ·  ↑ v" + latest + " available");
                    note.Foreground = Brush("#E8B45A");
                    updatesAvail++;
                }
                else
                {
                    note = new Run("  ·  ✓ up to date");
                    note.Foreground = Brush("#6BD490");
                }
                row.Sub.Inlines.Add(note);
            }

            Progress.Value = 100;
            Progress.Visibility = Visibility.Collapsed;
            GeckoNote.Visibility = anyGecko ? Visibility.Visible : Visibility.Collapsed;

            if (Rows.Count == 0)
            {
                StatusText.Text = "No supported browsers found on this system.";
            }
            else
            {
                string status = Rows.Count + (Rows.Count == 1 ? " browser" : " browsers") +
                                " found  ·  total cache: " + FormatSize(totalSize);
                if (updatesAvail > 0)
                    status += "  ·  " + updatesAvail + " browser " + (updatesAvail == 1 ? "update" : "updates") + " available";
                StatusText.Text = status;
                BtnClear.IsEnabled = true;
            }
            BtnRescan.IsEnabled = true;
            Busy = false;
        }

        // ── Clear ───────────────────────────────────────────────────────────
        static async Task ClearSelected()
        {
            if (Busy) return;
            var selected = new List<RowUi>();
            foreach (RowUi r in Rows) if (r.Check.IsChecked == true) selected.Add(r);
            if (selected.Count == 0)
            {
                StatusText.Text = "Nothing selected — tick at least one browser.";
                return;
            }

            bool wipePw      = ChkPw.IsChecked      != true;
            bool wipeHist    = ChkHist.IsChecked    != true;
            bool wipeCookies = ChkCookies.IsChecked != true;
            bool wipeForm    = ChkForm.IsChecked    != true;

            var extras = new List<string>();
            if (wipePw)      extras.Add("saved PASSWORDS");
            if (wipeHist)    extras.Add("browsing history");
            if (wipeCookies) extras.Add("cookies (logs you out of websites)");
            if (wipeForm)    extras.Add("autofill data");

            if (extras.Count > 0)
            {
                string msg = "Besides the cache, this will PERMANENTLY delete:\n\n  - " +
                             string.Join("\n  - ", extras) +
                             "\n\nfor the selected browsers. This cannot be undone.\n\nContinue?";
                if (MessageBox.Show(Win, msg, "CacheFlow — confirm data deletion",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
            }

            Busy = true;
            BtnClear.IsEnabled = false;
            BtnRescan.IsEnabled = false;
            Progress.Value = 0;
            Progress.Visibility = Visibility.Visible;
            long freed = 0;
            int cleared = 0;

            for (int i = 0; i < selected.Count; i++)
            {
                RowUi row = selected[i];
                BrowserInfo info = row.Info;
                Progress.Value = Math.Round(i / (double)selected.Count * 100);

                if (IsRunning(info.Def.Proc))
                {
                    string msg = info.Def.Name + " appears to be running.\n\n" +
                                 "Files in use will be skipped — close the browser first for a full clean.\n\nClear anyway?";
                    if (MessageBox.Show(Win, msg, "CacheFlow",
                            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                        continue;
                }

                StatusText.Text = "Clearing " + info.Def.Name + "...";
                long before = row.Size;
                long after = 0;
                await Task.Run(delegate
                {
                    foreach (string d in info.CacheDirs) DeleteDirBestEffort(d);
                    if (wipePw)                              DeleteFilesBestEffort(info.Passwords);
                    if (wipeHist && info.SupportsHistory)    DeleteFilesBestEffort(info.History);
                    if (wipeCookies)                         DeleteFilesBestEffort(info.Cookies);
                    if (wipeForm)                            DeleteFilesBestEffort(info.Autofill);
                    foreach (string d in info.CacheDirs) after += DirSize(d);
                });
                if (before > after) freed += before - after;

                State[info.Def.Name] = DateTime.Now.ToString("o");
                cleared++;
            }

            SaveState();
            Busy = false;
            await RefreshList();

            if (cleared > 0)
                StatusText.Text = "✓ Done — freed " + FormatSize(freed) + " across " +
                                  cleared + (cleared == 1 ? " browser." : " browsers.");
        }

        // ── Browser resolution ──────────────────────────────────────────────
        static BrowserInfo ResolveBrowser(BrowserDef def)
        {
            var info = new BrowserInfo
            {
                Def = def,
                CacheDirs = new List<string>(),
                Passwords = new List<string>(),
                History = new List<string>(),
                Cookies = new List<string>(),
                Autofill = new List<string>(),
                SupportsHistory = true
            };

            if (def.Family == "chromium")
            {
                if (!Directory.Exists(def.Root)) return null;
                var profiles = new List<string>();
                try
                {
                    foreach (string d in Directory.GetDirectories(def.Root))
                    {
                        string n = Path.GetFileName(d);
                        if (n == "Default" || n.StartsWith("Profile ")) profiles.Add(d);
                    }
                }
                catch { }
                if (profiles.Count == 0) return null;
                info.Profiles = profiles.Count;
                foreach (string p in profiles)
                {
                    foreach (string rel in ChromiumProfileCache) info.CacheDirs.Add(Path.Combine(p, rel));
                    AddFiles(info.Passwords, p, "Login Data", "Login Data-journal", "Login Data For Account", "Login Data For Account-journal");
                    AddFiles(info.History,   p, "History", "History-journal", "Visited Links", "Top Sites", "Top Sites-journal", "Shortcuts", "Shortcuts-journal");
                    AddFiles(info.Cookies,   p, "Network\\Cookies", "Network\\Cookies-journal", "Cookies", "Cookies-journal");
                    AddFiles(info.Autofill,  p, "Web Data", "Web Data-journal");
                }
                foreach (string rel in ChromiumRootCache) info.CacheDirs.Add(Path.Combine(def.Root, rel));
            }
            else if (def.Family == "opera")
            {
                if (!Directory.Exists(def.Root)) return null;
                info.Profiles = 1;
                // Opera keeps the profile in Roaming and the big caches in Local —
                // apply the chromium cache list to both roots.
                foreach (string root in new string[] { def.Root, def.CacheRoot })
                {
                    if (root == null || !Directory.Exists(root)) continue;
                    foreach (string rel in ChromiumProfileCache) info.CacheDirs.Add(Path.Combine(root, rel));
                    foreach (string rel in ChromiumRootCache)    info.CacheDirs.Add(Path.Combine(root, rel));
                }
                AddFiles(info.Passwords, def.Root, "Login Data", "Login Data-journal");
                AddFiles(info.History,   def.Root, "History", "History-journal", "Visited Links", "Top Sites", "Shortcuts");
                AddFiles(info.Cookies,   def.Root, "Network\\Cookies", "Network\\Cookies-journal", "Cookies", "Cookies-journal");
                AddFiles(info.Autofill,  def.Root, "Web Data", "Web Data-journal");
            }
            else // gecko
            {
                string roamingProfiles = Path.Combine(def.Root, "Profiles");
                if (!Directory.Exists(roamingProfiles)) return null;
                string[] profiles;
                try { profiles = Directory.GetDirectories(roamingProfiles); }
                catch { return null; }
                if (profiles.Length == 0) return null;
                info.Profiles = profiles.Length;
                // History lives in places.sqlite together with BOOKMARKS — never touch it.
                info.SupportsHistory = false;
                foreach (string p in profiles)
                {
                    string localProfile = Path.Combine(Path.Combine(def.CacheRoot, "Profiles"), Path.GetFileName(p));
                    foreach (string rel in GeckoProfileCache) info.CacheDirs.Add(Path.Combine(localProfile, rel));
                    AddFiles(info.Passwords, p, "logins.json", "logins-backup.json", "key4.db");
                    AddFiles(info.Cookies,   p, "cookies.sqlite", "cookies.sqlite-wal", "cookies.sqlite-shm");
                    AddFiles(info.Autofill,  p, "formhistory.sqlite", "autofill-profiles.json");
                }
            }

            info.ExePath = GetBrowserExePath(def);
            info.Version = GetBrowserVersion(def, info.ExePath);
            if (info.Version != null && def.VersionTransform != null)
                info.Version = def.VersionTransform(info.Version);
            return info;
        }

        static void AddFiles(List<string> list, string baseDir, params string[] rels)
        {
            foreach (string rel in rels) list.Add(Path.Combine(baseDir, rel));
        }

        // Locate the browser's exe: known install paths first, then App Paths registry.
        static string GetBrowserExePath(BrowserDef def)
        {
            if (def.ExePaths != null)
                foreach (string p in def.ExePaths)
                    if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
            if (!string.IsNullOrEmpty(def.ExeName))
            {
                foreach (string hive in new string[] { "HKEY_CURRENT_USER", "HKEY_LOCAL_MACHINE" })
                {
                    try
                    {
                        object v = Microsoft.Win32.Registry.GetValue(
                            hive + "\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\App Paths\\" + def.ExeName, "", null);
                        string exe = v as string;
                        if (!string.IsNullOrEmpty(exe) && File.Exists(exe)) return exe;
                    }
                    catch { }
                }
            }
            return null;
        }

        // Installed version: exe ProductVersion, falling back to chromium's
        // 'Last Version' file / gecko's compatibility.ini.
        static string GetBrowserVersion(BrowserDef def, string exePath)
        {
            if (exePath != null)
            {
                try
                {
                    string v = FileVersionInfo.GetVersionInfo(exePath).ProductVersion;
                    if (!string.IsNullOrEmpty(v)) return v.Trim();
                }
                catch { }
            }
            if (def.Family == "chromium")
            {
                try
                {
                    string lv = Path.Combine(def.Root, "Last Version");
                    if (File.Exists(lv)) return File.ReadAllText(lv).Trim();
                }
                catch { }
            }
            if (def.Family == "gecko")
            {
                try
                {
                    string profilesDir = Path.Combine(def.Root, "Profiles");
                    foreach (string p in Directory.GetDirectories(profilesDir))
                    {
                        string ini = Path.Combine(p, "compatibility.ini");
                        if (!File.Exists(ini)) continue;
                        Match m = Regex.Match(File.ReadAllText(ini), @"LastVersion=([\d\.]+)");
                        if (m.Success) return m.Groups[1].Value;
                    }
                }
                catch { }
            }
            return null;
        }

        static bool IsRunning(string procName)
        {
            try
            {
                Process[] ps = Process.GetProcessesByName(procName);
                bool any = ps.Length > 0;
                foreach (Process p in ps) p.Dispose();
                return any;
            }
            catch { return false; }
        }

        // ── Filesystem helpers ──────────────────────────────────────────────
        static long DirSize(string path)
        {
            long total = 0;
            var stack = new Stack<string>();
            try { if (!Directory.Exists(path)) return 0; } catch { return 0; }
            stack.Push(path);
            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                try
                {
                    foreach (string f in Directory.GetFiles(dir))
                    {
                        try { total += new FileInfo(f).Length; } catch { }
                    }
                    foreach (string d in Directory.GetDirectories(dir)) stack.Push(d);
                }
                catch { }
            }
            return total;
        }

        // Best-effort recursive delete: skips locked/in-use files instead of aborting.
        static void DeleteDirBestEffort(string path)
        {
            try { if (!Directory.Exists(path)) return; } catch { return; }
            try
            {
                foreach (string f in Directory.GetFiles(path))
                {
                    try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } catch { }
                }
            }
            catch { }
            try
            {
                foreach (string d in Directory.GetDirectories(path)) DeleteDirBestEffort(d);
            }
            catch { }
            try { Directory.Delete(path, false); } catch { }
        }

        static void DeleteFilesBestEffort(List<string> files)
        {
            foreach (string f in files)
            {
                try { if (File.Exists(f)) { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } } catch { }
            }
        }

        // ── Update feeds ────────────────────────────────────────────────────
        static bool HasFeed(string name)
        {
            return name == "Google Chrome" || name == "Microsoft Edge" || name == "Brave" ||
                   name == "Firefox" || name == "Opera" || name == "Opera GX";
        }

        static string Http(string url)
        {
            using (var wc = new TimeoutWebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.UserAgent] = "CacheFlow";
                return wc.DownloadString(url);
            }
        }

        class TimeoutWebClient : WebClient
        {
            protected override WebRequest GetWebRequest(Uri address)
            {
                WebRequest r = base.GetWebRequest(address);
                if (r != null) r.Timeout = 10000;
                return r;
            }
        }

        static string FetchLatest(string name)
        {
            if (name == "Google Chrome")
            {
                string json = Http("https://versionhistory.googleapis.com/v1/chrome/platforms/win64/channels/stable/versions");
                Match m = Regex.Match(json, "\"version\"\\s*:\\s*\"([\\d.]+)\"");
                return m.Success ? m.Groups[1].Value : null;
            }
            if (name == "Microsoft Edge")
            {
                string json = Http("https://edgeupdates.microsoft.com/api/products");
                int start = json.IndexOf("\"Product\":\"Stable\"", StringComparison.Ordinal);
                if (start < 0) return null;
                int end = json.IndexOf("\"Product\":\"", start + 20, StringComparison.Ordinal);
                string section = end > start ? json.Substring(start, end - start) : json.Substring(start);
                Version best = null;
                foreach (Match m in Regex.Matches(section,
                    "\"Platform\"\\s*:\\s*\"Windows\"\\s*,\\s*\"Architecture\"\\s*:\\s*\"x64\"[\\s\\S]{0,600}?\"ProductVersion\"\\s*:\\s*\"([\\d.]+)\""))
                {
                    Version v;
                    if (Version.TryParse(m.Groups[1].Value, out v) && (best == null || v > best)) best = v;
                }
                if (best == null) // fallback: any ProductVersion inside the Stable section
                {
                    foreach (Match m in Regex.Matches(section, "\"ProductVersion\"\\s*:\\s*\"([\\d.]+)\""))
                    {
                        Version v;
                        if (Version.TryParse(m.Groups[1].Value, out v) && (best == null || v > best)) best = v;
                    }
                }
                return best != null ? best.ToString() : null;
            }
            if (name == "Brave")
            {
                string json = Http("https://api.github.com/repos/brave/brave-browser/releases/latest");
                Match m = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([\\d.]+)\"");
                return m.Success ? m.Groups[1].Value : null;
            }
            if (name == "Firefox")
            {
                string json = Http("https://product-details.mozilla.org/1.0/firefox_versions.json");
                Match m = Regex.Match(json, "\"LATEST_FIREFOX_VERSION\"\\s*:\\s*\"([\\d.]+)");
                return m.Success ? m.Groups[1].Value : null;
            }
            if (name == "Opera" || name == "Opera GX")
            {
                string url = name == "Opera"
                    ? "https://get.geo.opera.com/pub/opera/desktop/"
                    : "https://get.geo.opera.com/pub/opera_gx/";
                string html = Http(url);
                Version best = null;
                foreach (Match m in Regex.Matches(html, "href=\"(\\d+(?:\\.\\d+){2,3})/\""))
                {
                    Version v;
                    if (Version.TryParse(m.Groups[1].Value, out v) && (best == null || v > best)) best = v;
                }
                return best != null ? best.ToString() : null;
            }
            return null;
        }

        static string GetLatestVersion(string name)
        {
            lock (LatestVersions)
            {
                if (LatestVersions.ContainsKey(name)) return LatestVersions[name];
            }
            string v = null;
            try { v = FetchLatest(name); } catch { }
            if (v != null)
            {
                Match m = Regex.Match(v.Trim(), @"^\d+(\.\d+)*");
                v = m.Success ? m.Value : null;
            }
            lock (LatestVersions) { LatestVersions[name] = v; }
            return v;
        }

        static bool IsNewer(string installed, string latest)
        {
            try
            {
                Match m = Regex.Match(installed.Trim(), @"^\d+(\.\d+)*");
                if (!m.Success) return false;
                Version vi, vl;
                if (!Version.TryParse(PadVersion(m.Value), out vi)) return false;
                if (!Version.TryParse(PadVersion(latest), out vl)) return false;
                return vl > vi;
            }
            catch { return false; }
        }

        static string PadVersion(string v)
        {
            // Version.TryParse needs at least two components ("151" -> "151.0")
            return v.IndexOf('.') < 0 ? v + ".0" : v;
        }

        // ── Row construction ────────────────────────────────────────────────
        static RowUi NewBrowserRow(BrowserInfo info, long size, bool running)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = Brush("#1C1F28"),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 4, 0, 4)
            };
            var dock = new DockPanel();
            border.Child = dock;

            var chk = new CheckBox
            {
                IsChecked = true,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            DockPanel.SetDock(chk, Dock.Left);
            dock.Children.Add(chk);

            BitmapSource iconSrc = GetBrowserIcon(info.ExePath);
            if (iconSrc != null)
            {
                var img = new Image
                {
                    Source = iconSrc,
                    Width = 30,
                    Height = 30,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(1, 0, 13, 0)
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                DockPanel.SetDock(img, Dock.Left);
                dock.Children.Add(img);
            }
            else
            {
                var dot = new Border
                {
                    Width = 32, Height = 32,
                    CornerRadius = new CornerRadius(16),
                    Background = Brush(info.Def.Color),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0)
                };
                var mono = new TextBlock
                {
                    Text = info.Def.Mono,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = System.Windows.Media.Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                dot.Child = mono;
                DockPanel.SetDock(dot, Dock.Left);
                dock.Children.Add(dot);
            }

            var right = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(right, Dock.Right);

            var sizeText = new TextBlock
            {
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Right
            };
            if (size < 5L * 1024 * 1024)
            {
                sizeText.Text = "✓ Clean";
                sizeText.Foreground = Brush("#6BD490");
            }
            else
            {
                sizeText.Text = FormatSize(size);
                sizeText.Foreground = Brush("#E8B45A");
            }
            right.Children.Add(sizeText);

            var clearedText = new TextBlock
            {
                FontSize = 11,
                Foreground = Brush("#5A6072"),
                TextAlignment = TextAlignment.Right
            };
            string iso;
            if (State.TryGetValue(info.Def.Name, out iso))
            {
                DateTime dt;
                clearedText.Text = DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out dt) ? FormatAgo(dt.ToLocalTime()) : "";
            }
            else clearedText.Text = "never cleared";
            right.Children.Add(clearedText);
            dock.Children.Add(right);

            var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var nameText = new TextBlock
            {
                Text = info.Def.Name,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.White
            };
            mid.Children.Add(nameText);

            var sub = new TextBlock { FontSize = 12, Foreground = Brush("#8A91A3") };
            string subBase = info.Profiles + (info.Profiles == 1 ? " profile" : " profiles");
            if (!string.IsNullOrEmpty(info.Version)) subBase = "v" + info.Version + "  ·  " + subBase;
            if (running)
            {
                sub.Inlines.Add(new Run(subBase + "  "));
                var runDot = new Run("● running");
                runDot.Foreground = Brush("#E8B45A");
                sub.Inlines.Add(runDot);
            }
            else sub.Text = subBase;
            mid.Children.Add(sub);
            dock.Children.Add(mid);

            return new RowUi { Border = border, Check = chk, Info = info, Size = size, Running = running, Sub = sub };
        }

        static BitmapSource GetBrowserIcon(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return null;
            BitmapSource cached;
            if (IconCache.TryGetValue(exePath, out cached)) return cached;
            BitmapSource src = null;
            try
            {
                using (System.Drawing.Icon ico = System.Drawing.Icon.ExtractAssociatedIcon(exePath))
                {
                    if (ico != null)
                    {
                        src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        if (src.CanFreeze) src.Freeze();
                    }
                }
            }
            catch { src = null; }
            IconCache[exePath] = src;
            return src;
        }

        // ── Donate dialog ───────────────────────────────────────────────────
        static void ShowDonateDialog()
        {
            var dn = (Window)XamlReader.Parse(DonateXaml);
            dn.Owner = Win;
            var dnList  = Find<StackPanel>(dn, "DnList");
            var dnTitle = Find<Grid>(dn, "DnTitle");
            var dnClose = Find<Button>(dn, "DnClose");

            foreach (string[] entry in Donate)
            {
                string key = entry[0];
                string val = entry[1];
                if (string.IsNullOrWhiteSpace(val)) continue;

                var row = new Border
                {
                    CornerRadius = new CornerRadius(9),
                    Background = Brush("#1A1D26"),
                    Padding = new Thickness(12, 9, 12, 9),
                    Margin = new Thickness(0, 4, 0, 4)
                };
                var dock = new DockPanel();
                row.Child = dock;

                var label = new TextBlock
                {
                    Text = key,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = System.Windows.Media.Brushes.White,
                    Width = 118,
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(label, Dock.Left);
                dock.Children.Add(label);

                var btn = new Button
                {
                    Tag = val,
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(btn, Dock.Right);
                if (val.StartsWith("http://") || val.StartsWith("https://"))
                {
                    btn.Content = "Open";
                    btn.Style = (Style)dn.FindResource("DPrim");
                    btn.Click += delegate(object s, RoutedEventArgs e) { OpenUrl((string)((Button)s).Tag); };
                }
                else
                {
                    btn.Content = "Copy";
                    btn.Style = (Style)dn.FindResource("DGhost");
                    btn.Click += delegate(object s, RoutedEventArgs e)
                    {
                        var b = (Button)s;
                        try { Clipboard.SetText((string)b.Tag); b.Content = "Copied"; } catch { }
                    };
                }
                dock.Children.Add(btn);

                var addr = new TextBox
                {
                    Text = val,
                    IsReadOnly = true,
                    BorderThickness = new Thickness(0),
                    Background = System.Windows.Media.Brushes.Transparent,
                    Foreground = Brush("#8A91A3"),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,   // long wallet addresses wrap to a second line
                    VerticalAlignment = VerticalAlignment.Center
                };
                dock.Children.Add(addr);

                dnList.Children.Add(row);
            }

            dnClose.Click += delegate { dn.Close(); };
            dnTitle.MouseLeftButtonDown += delegate { try { dn.DragMove(); } catch { } };
            dn.ShowDialog();
        }

        // ── Small helpers ───────────────────────────────────────────────────
        static SolidColorBrush Brush(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        static string FormatSize(long bytes)
        {
            if (bytes >= 1L << 30) return ((double)bytes / (1L << 30)).ToString("N1", CultureInfo.CurrentCulture) + " GB";
            if (bytes >= 1L << 20) return ((double)bytes / (1L << 20)).ToString("N0", CultureInfo.CurrentCulture) + " MB";
            if (bytes >= 1L << 10) return ((double)bytes / (1L << 10)).ToString("N0", CultureInfo.CurrentCulture) + " KB";
            return bytes + " B";
        }

        static string FormatAgo(DateTime dt)
        {
            int days = (int)(DateTime.Now.Date - dt.Date).TotalDays;
            if (days <= 0) return "cleared today";
            if (days == 1) return "cleared yesterday";
            if (days < 30) return "cleared " + days + " d ago";
            return "cleared " + dt.ToString("dd.MM.yyyy");
        }

        static void SaveWindowPos()
        {
            State["win_x"] = Win.Left.ToString("F0");
            State["win_y"] = Win.Top.ToString("F0");
            SaveState();
        }

        static void RestoreWindowPos()
        {
            try
            {
                if (!State.ContainsKey("win_x") || !State.ContainsKey("win_y")) return;
                double x = double.Parse(State["win_x"]);
                double y = double.Parse(State["win_y"]);
                double sw = SystemParameters.VirtualScreenWidth;
                double sh = SystemParameters.VirtualScreenHeight;
                double vx = SystemParameters.VirtualScreenLeft;
                double vy = SystemParameters.VirtualScreenTop;
                // Only restore if the position keeps the window at least 80px on screen
                if (x + Win.Width  - 80 < vx || x + 80 > vx + sw) return;
                if (y + Win.Height - 80 < vy || y + 80 > vy + sh) return;
                Win.WindowStartupLocation = WindowStartupLocation.Manual;
                Win.Left = x;
                Win.Top  = y;
            }
            catch { }
        }

        static void LoadState()
        {
            try
            {
                if (!File.Exists(StateFile)) return;
                string json = File.ReadAllText(StateFile);
                foreach (Match m in Regex.Matches(json, "\"([^\"]+)\"\\s*:\\s*\"([^\"]+)\""))
                    State[m.Groups[1].Value] = m.Groups[2].Value;
            }
            catch { }
        }

        static void SaveState()
        {
            try
            {
                var sb = new StringBuilder("{\n");
                bool first = true;
                foreach (KeyValuePair<string, string> kv in State)
                {
                    if (!first) sb.Append(",\n");
                    sb.Append("    \"").Append(kv.Key).Append("\": \"").Append(kv.Value).Append("\"");
                    first = false;
                }
                sb.Append("\n}\n");
                File.WriteAllText(StateFile, sb.ToString(), new UTF8Encoding(true));
            }
            catch { }
        }
    }
}
