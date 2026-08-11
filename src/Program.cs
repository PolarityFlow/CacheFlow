// ============================================================================
//  CacheFlow v1.41 — browser cache cleaner
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
using System.IO.Compression;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinForms = System.Windows.Forms;

[assembly: AssemblyTitle("CacheFlow")]
[assembly: AssemblyDescription("Browser cache cleaner")]
[assembly: AssemblyCompany("PolarityFlow")]
[assembly: AssemblyProduct("CacheFlow")]
[assembly: AssemblyCopyright("(c) 2026 PolarityFlow, Adrian Zingg")]
[assembly: AssemblyVersion("1.41.0.0")]
[assembly: AssemblyFileVersion("1.41.0.0")]

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

    class UpdateManifest
    {
        public string Version, Url, Sha256, Stage, Page;
    }

    public static class Program
    {
        // ── Update ──────────────────────────────────────────────────────────
        const string AppVersion        = "1.41";
        const string UpdateManifestUrl = "https://polarityflow.com/downloads/updates.json";
        const string UpdateAppKey      = "cacheflow";
        static UpdateManifest PendingUpdate;

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
        static bool IsExiting;
        static WinForms.NotifyIcon TrayIcon;

        // ── UI references ───────────────────────────────────────────────────
        static Window Win;
        static Grid TitleBar;
        static Image TitleIcon;
        static Button BtnMin, BtnClose, BtnRescan, BtnClear, BtnDonate;
        static StackPanel BrowserList;
        static TextBlock StatusText, GeckoNote;
        static CheckBox ChkPw, ChkHist, ChkCookies, ChkForm, ChkAutoUpdate;
        static Hyperlink SiteLink, UpdateLink;
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
    <Style TargetType='ScrollBar'>
      <Setter Property='Width' Value='6'/>
      <Setter Property='MinWidth' Value='6'/>
      <Setter Property='Background' Value='Transparent'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='ScrollBar'>
            <Grid Background='Transparent'>
              <Track x:Name='PART_Track' IsDirectionReversed='True'>
                <Track.DecreaseRepeatButton>
                  <RepeatButton Command='{x:Static ScrollBar.LineUpCommand}' Opacity='0' Height='0.001'/>
                </Track.DecreaseRepeatButton>
                <Track.IncreaseRepeatButton>
                  <RepeatButton Command='{x:Static ScrollBar.LineDownCommand}' Opacity='0' Height='0.001'/>
                </Track.IncreaseRepeatButton>
                <Track.Thumb>
                  <Thumb>
                    <Thumb.Template>
                      <ControlTemplate TargetType='Thumb'>
                        <Border CornerRadius='3' Background='#3A3F50' Margin='1,2'/>
                      </ControlTemplate>
                    </Thumb.Template>
                  </Thumb>
                </Track.Thumb>
              </Track>
            </Grid>
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
            <TextBlock x:Name='LblSubtitle' Text='browser cache cleaner' FontSize='12' Foreground='#8A91A3'/>
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
            <CheckBox x:Name='ChkAutoUpdate' Content='Auto-update' IsChecked='True'
                      Margin='14,0,0,0' FontSize='11' Foreground='#5A6072' VerticalAlignment='Center'
                      ToolTip='Checks polarityflow.com/downloads/updates.json at startup. No personal data sent.'/>
          </StackPanel>
          <Button x:Name='BtnClear' Grid.Column='2' Style='{StaticResource PrimaryBtn}' Content='Clear selected'/>
        </Grid>
        <TextBlock FontSize='11' Foreground='#5A6072' HorizontalAlignment='Center' Margin='0,12,0,0'>
          <Run Text='CacheFlow v1.41  &#xB7;  '/><Hyperlink x:Name='SiteLink' Foreground='#5BA3C4' TextDecorations='None' ToolTip='www.polarityflow.com'><Run Text='PolarityFlow'/></Hyperlink>
        </TextBlock>
        <TextBlock x:Name='UpdateBar' FontSize='11' HorizontalAlignment='Center' Margin='0,3,0,0' Visibility='Collapsed'>
          <Hyperlink x:Name='UpdateLink' Foreground='#E8B45A' TextDecorations='None'><Run Text=''/></Hyperlink>
        </TextBlock>
      </StackPanel>

      <Border DockPanel.Dock='Bottom' Margin='20,6,20,0' Padding='16,12,16,12'
              CornerRadius='10' Background='#1A1D26'>
        <StackPanel>
          <TextBlock x:Name='LblKeep' Text='KEEP WHEN CLEARING' FontSize='11' FontWeight='SemiBold'
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
          <TextBlock x:Name='DnTitleText' Text='Support CacheFlow' FontSize='15' FontWeight='Bold' Foreground='White' VerticalAlignment='Center'/>
        </StackPanel>
        <Button x:Name='DnClose' Style='{StaticResource DGhost}' Content='&#x2715;'
                HorizontalAlignment='Right' Padding='8,3'/>
      </Grid>
      <TextBlock x:Name='DnMsg' Margin='0,10,0,0' FontSize='13' Foreground='#8A91A3' TextWrapping='Wrap'
                 Text='CacheFlow is free. If it freed up a few gigabytes for you, a small donation helps keep the tools coming. Thank you!'/>
      <StackPanel x:Name='DnList' Margin='0,14,0,0'/>
      <TextBlock x:Name='DnEthNote' Margin='2,8,2,0' FontSize='11' Foreground='#5A6072' TextWrapping='Wrap'
                 Text='The ETH / EVM address also works on Base, BSC, Blast and Ink.'/>
      <TextBlock Margin='0,12,0,0' FontSize='11' Foreground='#5A6072' HorizontalAlignment='Center'
                 Text='PolarityFlow &#xB7; www.polarityflow.com'/>
    </StackPanel>
  </Border>
</Window>";

        const string UpdateXaml = @"
<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Title='CacheFlow Update' SizeToContent='Height' Width='440'
        WindowStyle='None' AllowsTransparency='True' Background='Transparent'
        WindowStartupLocation='CenterOwner' ResizeMode='NoResize'
        FontFamily='Segoe UI' ShowInTaskbar='False'>
  <Border CornerRadius='12' Background='#14161C' BorderBrush='#2A2E3A' BorderThickness='1' Padding='22,18,22,22'>
    <StackPanel>
      <Grid x:Name='UpdTitle' Background='Transparent'>
        <TextBlock x:Name='UpdTitleText' Text='Update available' FontSize='15' FontWeight='Bold' Foreground='White'/>
        <Button x:Name='UpdClose' Content='&#x2715;' HorizontalAlignment='Right'
                Background='Transparent' Foreground='#8A91A3' BorderThickness='0'
                FontSize='12' Cursor='Hand' Padding='4,2'/>
      </Grid>
      <TextBlock x:Name='UpdMsg' Margin='0,12,0,0' FontSize='13' Foreground='#C8CDDA' TextWrapping='Wrap'/>
      <TextBlock x:Name='UpdNote' Margin='0,8,0,0' FontSize='11' Foreground='#5A6072' TextWrapping='Wrap'
                 Text='The download is checksum-verified before being applied. Your settings are kept.'/>
      <TextBlock x:Name='UpdPageRow' Margin='0,6,0,0' FontSize='12' Visibility='Collapsed'>
        <Hyperlink x:Name='UpdPage' Foreground='#5BA3C4' TextDecorations='None'>See what changed</Hyperlink>
      </TextBlock>
      <StackPanel Orientation='Horizontal' HorizontalAlignment='Right' Margin='0,18,0,0'>
        <Button x:Name='UpdLater' Content='Later' FontSize='13' Padding='16,8' Cursor='Hand'
                Background='Transparent' Foreground='#C8CDDA' BorderBrush='#2A2E3A' BorderThickness='1'/>
        <Button x:Name='UpdNow' Content='Update now' Margin='8,0,0,0'
                Background='#488CAA' Foreground='White' BorderThickness='0'
                FontWeight='SemiBold' FontSize='13' Padding='16,8' Cursor='Hand'/>
      </StackPanel>
    </StackPanel>
  </Border>
</Window>";

        // ── Localization ─────────────────────────────────────────────────────
        static Dictionary<string, string> L = new Dictionary<string, string>();
        static string T(string key) { string v; return L.TryGetValue(key, out v) ? v : key; }

        static void InitLocale()
        {
            string lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLower();
            string saved;
            if (State.TryGetValue("lang", out saved) && !string.IsNullOrEmpty(saved)) lang = saved;
            switch (lang)
            {
                case "de": L = Loc_DE(); break;
                case "fr": L = Loc_FR(); break;
                case "it": L = Loc_IT(); break;
                case "es": L = Loc_ES(); break;
                case "pt": L = Loc_PT(); break;
                default:   L = Loc_EN(); break;
            }
        }

        static Dictionary<string, string> Loc_EN() { return new Dictionary<string, string> {
            { "subtitle",        "browser cache cleaner" },
            { "keep_section",    "KEEP WHEN CLEARING" },
            { "passwords",       "Passwords" },
            { "history",         "Visited sites (history)" },
            { "cookies",         "Cookies / logins" },
            { "autofill",        "Autofill data" },
            { "gecko_note",      "Firefox-family browsers: history is never touched (it is stored together with bookmarks)." },
            { "rescan",          "Rescan" },
            { "clear_selected",  "Clear selected" },
            { "auto_update",     "Auto-update" },
            { "auto_update_tip", "Checks polarityflow.com/downloads/updates.json at startup. No personal data sent." },
            { "scanning",        "Scanning {0}..." },
            { "checking_ver",    "Checking latest version: {0}..." },
            { "no_browsers",     "No supported browsers found on this system." },
            { "nothing_sel",     "Nothing selected — tick at least one browser." },
            { "clearing",        "Clearing {0}..." },
            { "done_freed",      "✓ Done — freed {0} across {1} browser(s)." },
            { "del_pw",          "saved PASSWORDS" },
            { "del_hist",        "browsing history" },
            { "del_cookies",     "cookies (logs you out of websites)" },
            { "del_autofill",    "autofill data" },
            { "del_pre",         "Besides the cache, this will PERMANENTLY delete:\n\n  - " },
            { "del_suf",         "\n\nfor the selected browsers. This cannot be undone.\n\nContinue?" },
            { "del_title",       "CacheFlow — confirm data deletion" },
            { "running_msg",     "{0} appears to be running.\n\nFiles in use will be skipped — close the browser first for a full clean.\n\nClear anyway?" },
            { "upd_none",        "Could not check for updates.\nPlease check your internet connection." },
            { "upd_latest",      "You already have the latest version (v{0})." },
            { "upd_checksum",    "Update failed: checksum did not match.\nThe file has not been installed." },
            { "upd_no_exe",      "Update package does not contain CacheFlow.exe." },
            { "upd_fail",        "Update failed: {0}" },
            { "tray_open",       "Open CacheFlow" },
            { "tray_exit",       "Exit" },
            { "upd_title_text",  "Update available" },
            { "upd_note",        "The download is checksum-verified before being applied. Your settings are kept." },
            { "upd_see_changes", "See what changed" },
            { "upd_later",       "Later" },
            { "upd_now",         "Update now" },
            { "upd_downloading", "Downloading update..." },
            { "upd_verifying",   "Verifying..." },
            { "upd_extracting",  "Extracting..." },
            { "donate_title",    "Support CacheFlow" },
            { "donate_msg",      "CacheFlow is free. If it freed up a few gigabytes for you, a small donation helps keep the tools coming. Thank you!" },
            { "donate_eth",      "The ETH / EVM address also works on Base, BSC, Blast and Ink." },
        }; }

        static Dictionary<string, string> Loc_DE() { return new Dictionary<string, string> {
            { "subtitle",        "Browser-Cache-Bereiniger" },
            { "keep_section",    "BEIM LEEREN BEHALTEN" },
            { "passwords",       "Passwörter" },
            { "history",         "Besuchte Seiten (Verlauf)" },
            { "cookies",         "Cookies / Anmeldungen" },
            { "autofill",        "Autofill-Daten" },
            { "gecko_note",      "Firefox-Browser: Der Verlauf wird nie gelöscht (er ist zusammen mit Lesezeichen gespeichert)." },
            { "rescan",          "Neu scannen" },
            { "clear_selected",  "Ausgewählte leeren" },
            { "auto_update",     "Autoupdate" },
            { "auto_update_tip", "Prüft polarityflow.com/downloads/updates.json beim Start. Keine persönlichen Daten." },
            { "scanning",        "{0} wird gescannt..." },
            { "checking_ver",    "Neueste Version wird geprüft: {0}..." },
            { "no_browsers",     "Keine unterstützten Browser auf diesem System gefunden." },
            { "nothing_sel",     "Nichts ausgewählt — mindestens einen Browser ankreuzen." },
            { "clearing",        "{0} wird geleert..." },
            { "done_freed",      "✓ Fertig — {0} bei {1} Browser(n) freigegeben." },
            { "del_pw",          "gespeicherte PASSWÖRTER" },
            { "del_hist",        "Browserverlauf" },
            { "del_cookies",     "Cookies (Sie werden von Websites abgemeldet)" },
            { "del_autofill",    "Autofill-Daten" },
            { "del_pre",         "Zusätzlich zum Cache werden folgende Daten DAUERHAFT gelöscht:\n\n  - " },
            { "del_suf",         "\n\nfür die ausgewählten Browser. Dies kann nicht rückgängig gemacht werden.\n\nFortfahren?" },
            { "del_title",       "CacheFlow — Löschung bestätigen" },
            { "running_msg",     "{0} scheint zu laufen.\n\nGenutzte Dateien werden übersprungen — Browser zuerst schließen für eine vollständige Bereinigung.\n\nTrotzdem leeren?" },
            { "upd_none",        "Update-Check fehlgeschlagen.\nBitte Internetverbindung prüfen." },
            { "upd_latest",      "Sie haben bereits die neueste Version (v{0})." },
            { "upd_checksum",    "Update fehlgeschlagen: Prüfsumme stimmt nicht überein.\nDie Datei wurde nicht installiert." },
            { "upd_no_exe",      "Das Update-Paket enthält keine CacheFlow.exe." },
            { "upd_fail",        "Update fehlgeschlagen: {0}" },
            { "tray_open",       "CacheFlow öffnen" },
            { "tray_exit",       "Beenden" },
            { "upd_title_text",  "Update verfügbar" },
            { "upd_note",        "Der Download wird vor der Installation per Prüfsumme verifiziert. Ihre Einstellungen bleiben erhalten." },
            { "upd_see_changes", "Neuigkeiten ansehen" },
            { "upd_later",       "Später" },
            { "upd_now",         "Jetzt aktualisieren" },
            { "upd_downloading", "Update wird heruntergeladen..." },
            { "upd_verifying",   "Prüfsumme wird verifiziert..." },
            { "upd_extracting",  "Wird entpackt..." },
            { "donate_title",    "CacheFlow unterstützen" },
            { "donate_msg",      "CacheFlow ist kostenlos. Falls du ein paar Gigabyte freigegeben hast, hilft eine kleine Spende, die Tools weiterzuentwickeln. Danke!" },
            { "donate_eth",      "Die ETH / EVM-Adresse funktioniert auch auf Base, BSC, Blast und Ink." },
        }; }

        static Dictionary<string, string> Loc_FR() { return new Dictionary<string, string> {
            { "subtitle",        "nettoyeur de cache navigateur" },
            { "keep_section",    "CONSERVER LORS DU NETTOYAGE" },
            { "passwords",       "Mots de passe" },
            { "history",         "Sites visités (historique)" },
            { "cookies",         "Cookies / connexions" },
            { "autofill",        "Données de saisie auto" },
            { "gecko_note",      "Navigateurs Firefox : l’historique n’est jamais supprimé (il est stocké avec les signets)." },
            { "rescan",          "Rescanner" },
            { "clear_selected",  "Effacer la sélection" },
            { "auto_update",     "Mise à jour auto" },
            { "auto_update_tip", "Vérifie polarityflow.com/downloads/updates.json au démarrage. Aucune donnée personnelle transmise." },
            { "scanning",        "Analyse de {0}..." },
            { "checking_ver",    "Vérification de la version : {0}..." },
            { "no_browsers",     "Aucun navigateur compatible trouvé sur ce système." },
            { "nothing_sel",     "Rien de sélectionné — cochez au moins un navigateur." },
            { "clearing",        "Nettoyage de {0}..." },
            { "done_freed",      "✓ Terminé — {0} libérés sur {1} navigateur(s)." },
            { "del_pw",          "MOTS DE PASSE enregistrés" },
            { "del_hist",        "historique de navigation" },
            { "del_cookies",     "cookies (vous déconnecte des sites)" },
            { "del_autofill",    "données de saisie auto" },
            { "del_pre",         "En plus du cache, ceci supprimera DÉFINITIVEMENT :\n\n  - " },
            { "del_suf",         "\n\npour les navigateurs sélectionnés. Cette action est irréversible.\n\nContinuer ?" },
            { "del_title",       "CacheFlow — confirmer la suppression" },
            { "running_msg",     "{0} semble en cours d’exécution.\n\nLes fichiers utilisés seront ignorés — fermez le navigateur d’abord pour un nettoyage complet.\n\nNettoyer quand même ?" },
            { "upd_none",        "Impossible de vérifier les mises à jour.\nVérifiez votre connexion internet." },
            { "upd_latest",      "Vous avez déjà la dernière version (v{0})." },
            { "upd_checksum",    "Échec de la mise à jour : la somme de contrôle ne correspond pas.\nLe fichier n’a pas été installé." },
            { "upd_no_exe",      "Le paquet de mise à jour ne contient pas CacheFlow.exe." },
            { "upd_fail",        "Échec de la mise à jour : {0}" },
            { "tray_open",       "Ouvrir CacheFlow" },
            { "tray_exit",       "Quitter" },
            { "upd_title_text",  "Mise à jour disponible" },
            { "upd_note",        "Le téléchargement est vérifié par somme de contrôle avant installation. Vos paramètres sont conservés." },
            { "upd_see_changes", "Voir les nouveautés" },
            { "upd_later",       "Plus tard" },
            { "upd_now",         "Mettre à jour" },
            { "upd_downloading", "Téléchargement..." },
            { "upd_verifying",   "Vérification..." },
            { "upd_extracting",  "Extraction..." },
            { "donate_title",    "Soutenir CacheFlow" },
            { "donate_msg",      "CacheFlow est gratuit. Si quelques gigaoctets ont été libérés, un petit don aide à maintenir les outils. Merci !" },
            { "donate_eth",      "L’adresse ETH / EVM fonctionne aussi sur Base, BSC, Blast et Ink." },
        }; }

        static Dictionary<string, string> Loc_IT() { return new Dictionary<string, string> {
            { "subtitle",        "pulizia cache browser" },
            { "keep_section",    "MANTIENI ALLA PULIZIA" },
            { "passwords",       "Password" },
            { "history",         "Siti visitati (cronologia)" },
            { "cookies",         "Cookie / accessi" },
            { "autofill",        "Dati compilazione automatica" },
            { "gecko_note",      "Browser Firefox: la cronologia non viene mai eliminata (archiviata insieme ai segnalibri)." },
            { "rescan",          "Ri-scansiona" },
            { "clear_selected",  "Cancella selezionati" },
            { "auto_update",     "Aggiornamento auto" },
            { "auto_update_tip", "Controlla polarityflow.com/downloads/updates.json all’avvio. Nessun dato personale trasmesso." },
            { "scanning",        "Scansione {0}..." },
            { "checking_ver",    "Controllo versione: {0}..." },
            { "no_browsers",     "Nessun browser supportato trovato sul sistema." },
            { "nothing_sel",     "Nessuna selezione — seleziona almeno un browser." },
            { "clearing",        "Pulizia {0}..." },
            { "done_freed",      "✓ Completato — {0} liberati su {1} browser." },
            { "del_pw",          "PASSWORD salvate" },
            { "del_hist",        "cronologia di navigazione" },
            { "del_cookies",     "cookie (ti disconnette dai siti)" },
            { "del_autofill",    "dati di compilazione automatica" },
            { "del_pre",         "Oltre alla cache, verranno eliminati DEFINITIVAMENTE:\n\n  - " },
            { "del_suf",         "\n\nper i browser selezionati. Questa azione è irreversibile.\n\nContinuare?" },
            { "del_title",       "CacheFlow — conferma eliminazione dati" },
            { "running_msg",     "{0} sembra in esecuzione.\n\nI file in uso verranno ignorati — chiudi il browser prima per una pulizia completa.\n\nPulire comunque?" },
            { "upd_none",        "Impossibile verificare gli aggiornamenti.\nControllare la connessione internet." },
            { "upd_latest",      "Hai già l’ultima versione (v{0})." },
            { "upd_checksum",    "Aggiornamento fallito: checksum non corrispondente.\nIl file non è stato installato." },
            { "upd_no_exe",      "Il pacchetto di aggiornamento non contiene CacheFlow.exe." },
            { "upd_fail",        "Aggiornamento fallito: {0}" },
            { "tray_open",       "Apri CacheFlow" },
            { "tray_exit",       "Esci" },
            { "upd_title_text",  "Aggiornamento disponibile" },
            { "upd_note",        "Il download viene verificato tramite checksum prima dell’installazione. Le impostazioni vengono mantenute." },
            { "upd_see_changes", "Cosa c’è di nuovo" },
            { "upd_later",       "Più tardi" },
            { "upd_now",         "Aggiorna ora" },
            { "upd_downloading", "Download aggiornamento..." },
            { "upd_verifying",   "Verifica..." },
            { "upd_extracting",  "Estrazione..." },
            { "donate_title",    "Supporta CacheFlow" },
            { "donate_msg",      "CacheFlow è gratuito. Se ha liberato qualche gigabyte, una piccola donazione aiuta a mantenere gli strumenti. Grazie!" },
            { "donate_eth",      "L’indirizzo ETH / EVM funziona anche su Base, BSC, Blast e Ink." },
        }; }

        static Dictionary<string, string> Loc_ES() { return new Dictionary<string, string> {
            { "subtitle",        "limpiador de caché del navegador" },
            { "keep_section",    "CONSERVAR AL LIMPIAR" },
            { "passwords",       "Contraseñas" },
            { "history",         "Sitios visitados (historial)" },
            { "cookies",         "Cookies / sesiones" },
            { "autofill",        "Datos de autocompletar" },
            { "gecko_note",      "Navegadores Firefox: el historial nunca se elimina (está almacenado junto a los marcadores)." },
            { "rescan",          "Volver a escanear" },
            { "clear_selected",  "Limpiar selección" },
            { "auto_update",     "Actualización auto" },
            { "auto_update_tip", "Comprueba polarityflow.com/downloads/updates.json al iniciar. No se envían datos personales." },
            { "scanning",        "Escaneando {0}..." },
            { "checking_ver",    "Comprobando versión: {0}..." },
            { "no_browsers",     "No se encontraron navegadores compatibles en este sistema." },
            { "nothing_sel",     "Sin selección — marca al menos un navegador." },
            { "clearing",        "Limpiando {0}..." },
            { "done_freed",      "✓ Listo — {0} liberados en {1} navegador(es)." },
            { "del_pw",          "CONTRASEÑAS guardadas" },
            { "del_hist",        "historial de navegación" },
            { "del_cookies",     "cookies (cierra sesión en los sitios)" },
            { "del_autofill",    "datos de autocompletar" },
            { "del_pre",         "Además de la caché, esto eliminará PERMANENTEMENTE:\n\n  - " },
            { "del_suf",         "\n\npara los navegadores seleccionados. Esta acción no se puede deshacer.\n\n¿Continuar?" },
            { "del_title",       "CacheFlow — confirmar eliminación de datos" },
            { "running_msg",     "{0} parece estar en ejecución.\n\nLos archivos en uso se omitirán — cierra el navegador primero para una limpieza completa.\n\n¿Limpiar de todos modos?" },
            { "upd_none",        "No se pudo verificar actualizaciones.\nCompruebe su conexión a internet." },
            { "upd_latest",      "Ya tiene la última versión (v{0})." },
            { "upd_checksum",    "Actualización fallida: la suma de verificación no coincide.\nEl archivo no se ha instalado." },
            { "upd_no_exe",      "El paquete de actualización no contiene CacheFlow.exe." },
            { "upd_fail",        "Actualización fallida: {0}" },
            { "tray_open",       "Abrir CacheFlow" },
            { "tray_exit",       "Salir" },
            { "upd_title_text",  "Actualización disponible" },
            { "upd_note",        "La descarga se verifica mediante suma de comprobación antes de aplicarse. Su configuración se conserva." },
            { "upd_see_changes", "Ver novedades" },
            { "upd_later",       "Más tarde" },
            { "upd_now",         "Actualizar ahora" },
            { "upd_downloading", "Descargando actualización..." },
            { "upd_verifying",   "Verificando..." },
            { "upd_extracting",  "Extrayendo..." },
            { "donate_title",    "Apoyar CacheFlow" },
            { "donate_msg",      "CacheFlow es gratuito. Si liberó unos gigabytes, una pequeña donación ayuda a mantener las herramientas. ¡Gracias!" },
            { "donate_eth",      "La dirección ETH / EVM también funciona en Base, BSC, Blast e Ink." },
        }; }

        static Dictionary<string, string> Loc_PT() { return new Dictionary<string, string> {
            { "subtitle",        "limpador de cache do navegador" },
            { "keep_section",    "MANTER AO LIMPAR" },
            { "passwords",       "Senhas" },
            { "history",         "Sites visitados (histórico)" },
            { "cookies",         "Cookies / sessões" },
            { "autofill",        "Dados de preenchimento automático" },
            { "gecko_note",      "Navegadores Firefox: o histórico nunca é apagado (está armazenado junto aos favoritos)." },
            { "rescan",          "Verificar novamente" },
            { "clear_selected",  "Limpar seleção" },
            { "auto_update",     "Atualização auto" },
            { "auto_update_tip", "Verifica polarityflow.com/downloads/updates.json ao iniciar. Nenhum dado pessoal enviado." },
            { "scanning",        "Verificando {0}..." },
            { "checking_ver",    "Verificando versão: {0}..." },
            { "no_browsers",     "Nenhum navegador compatível encontrado neste sistema." },
            { "nothing_sel",     "Nada selecionado — marque pelo menos um navegador." },
            { "clearing",        "Limpando {0}..." },
            { "done_freed",      "✓ Concluído — {0} liberados em {1} navegador(es)." },
            { "del_pw",          "SENHAS salvas" },
            { "del_hist",        "histórico de navegação" },
            { "del_cookies",     "cookies (fecha sua sessão nos sites)" },
            { "del_autofill",    "dados de preenchimento automático" },
            { "del_pre",         "Além do cache, isto irá EXCLUIR PERMANENTEMENTE:\n\n  - " },
            { "del_suf",         "\n\npara os navegadores selecionados. Esta ação não pode ser desfeita.\n\nContinuar?" },
            { "del_title",       "CacheFlow — confirmar exclusão de dados" },
            { "running_msg",     "{0} parece estar em execução.\n\nArquivos em uso serão ignorados — feche o navegador primeiro para uma limpeza completa.\n\nLimpar mesmo assim?" },
            { "upd_none",        "Não foi possível verificar atualizações.\nVerifique sua conexão com a internet." },
            { "upd_latest",      "Você já possui a versão mais recente (v{0})." },
            { "upd_checksum",    "Falha na atualização: soma de verificação não corresponde.\nO arquivo não foi instalado." },
            { "upd_no_exe",      "O pacote de atualização não contém CacheFlow.exe." },
            { "upd_fail",        "Falha na atualização: {0}" },
            { "tray_open",       "Abrir CacheFlow" },
            { "tray_exit",       "Sair" },
            { "upd_title_text",  "Atualização disponível" },
            { "upd_note",        "O download é verificado por checksum antes de ser aplicado. Suas configurações são mantidas." },
            { "upd_see_changes", "Ver novidades" },
            { "upd_later",       "Depois" },
            { "upd_now",         "Atualizar agora" },
            { "upd_downloading", "Baixando atualização..." },
            { "upd_verifying",   "Verificando..." },
            { "upd_extracting",  "Extraindo..." },
            { "donate_title",    "Apoiar CacheFlow" },
            { "donate_msg",      "CacheFlow é gratuito. Se liberou alguns gigabytes, uma pequena doação ajuda a manter as ferramentas. Obrigado!" },
            { "donate_eth",      "O endereço ETH / EVM também funciona no Base, BSC, Blast e Ink." },
        }; }

        // ── Entry point ─────────────────────────────────────────────────────
        [STAThread]
        public static void Main()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            InitDefs();
            BaseDir = AppDomain.CurrentDomain.BaseDirectory;
            StateFile = Path.Combine(BaseDir, "cacheflow-state.json");
            LoadState();
            InitLocale();

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
            ChkForm       = Find<CheckBox>(Win, "ChkForm");
            ChkAutoUpdate = Find<CheckBox>(Win, "ChkAutoUpdate");
            SiteLink    = (Hyperlink)Win.FindName("SiteLink");
            UpdateLink  = (Hyperlink)Win.FindName("UpdateLink");
            Progress    = Find<ProgressBar>(Win, "Progress");

            // Apply locale
            Find<TextBlock>(Win, "LblSubtitle").Text = T("subtitle");
            Find<TextBlock>(Win, "LblKeep").Text     = T("keep_section");
            BtnRescan.Content   = T("rescan");
            BtnClear.Content    = T("clear_selected");
            ChkAutoUpdate.Content = T("auto_update");
            ToolTipService.SetToolTip(ChkAutoUpdate, T("auto_update_tip"));
            ChkPw.Content       = T("passwords");
            ChkHist.Content     = T("history");
            ChkCookies.Content  = T("cookies");
            ChkForm.Content     = T("autofill");
            GeckoNote.Text      = T("gecko_note");

            BitmapImage appMark = LoadEmbedded("appicon.png");
            if (appMark != null)
            {
                TitleIcon.Source = appMark;
                Win.Icon = appMark;
            }

            BtnClose.Click  += delegate { SaveWindowPos(); Win.Hide(); };
            BtnMin.Click    += delegate { Win.WindowState = WindowState.Minimized; };
            TitleBar.MouseLeftButtonDown += delegate { try { Win.DragMove(); } catch { } };
            BtnRescan.Click += async delegate { await RefreshList(); };
            BtnClear.Click  += async delegate { await ClearSelected(); };
            BtnDonate.Click += delegate { ShowDonateDialog(); };
            SiteLink.Click  += delegate { OpenUrl("https://www.polarityflow.com"); };
            UpdateLink.Click += delegate(object s, RoutedEventArgs e)
            {
                if (PendingUpdate != null) ShowUpdateDialog(PendingUpdate);
            };

            // restore auto-update preference
            string autoUpd;
            if (State.TryGetValue("auto_check_updates", out autoUpd))
                ChkAutoUpdate.IsChecked = autoUpd != "false";
            ChkAutoUpdate.Checked   += delegate { State["auto_check_updates"] = "true";  SaveState(); };
            ChkAutoUpdate.Unchecked += delegate { State["auto_check_updates"] = "false"; SaveState(); };

            Win.Closing += delegate(object s, System.ComponentModel.CancelEventArgs e)
            {
                if (!IsExiting) { e.Cancel = true; Win.Hide(); }
            };

            Win.ContentRendered += async delegate
            {
                SetupTray();
                await RefreshList();
                await CheckForUpdatesAsync(false);
            };
            RestoreWindowPos();
        }

        static void SetupTray()
        {
            TrayIcon = new WinForms.NotifyIcon();
            try
            {
                TrayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
            }
            catch { }
            TrayIcon.Text = "CacheFlow — " + T("subtitle");
            TrayIcon.Visible = true;

            var menu = new WinForms.ContextMenuStrip();
            var openItem = new WinForms.ToolStripMenuItem(T("tray_open"));
            openItem.Font = new System.Drawing.Font(openItem.Font, System.Drawing.FontStyle.Bold);
            openItem.Click += delegate { RestoreWindow(); };
            var exitItem = new WinForms.ToolStripMenuItem(T("tray_exit"));
            exitItem.Click += delegate { ExitApp(); };
            menu.Items.Add(openItem);
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(exitItem);
            TrayIcon.ContextMenuStrip = menu;
            TrayIcon.DoubleClick += delegate { RestoreWindow(); };
        }

        static void RestoreWindow()
        {
            Win.Show();
            if (Win.WindowState == WindowState.Minimized)
                Win.WindowState = WindowState.Normal;
            Win.Activate();
        }

        static void ExitApp()
        {
            IsExiting = true;
            if (TrayIcon != null) { TrayIcon.Visible = false; TrayIcon.Dispose(); TrayIcon = null; }
            Application.Current.Shutdown();
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
                StatusText.Text = string.Format(T("scanning"), def.Name);
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
                    StatusText.Text = string.Format(T("checking_ver"), name);

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
                StatusText.Text = T("no_browsers");
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
                StatusText.Text = T("nothing_sel");
                return;
            }

            bool wipePw      = ChkPw.IsChecked      != true;
            bool wipeHist    = ChkHist.IsChecked    != true;
            bool wipeCookies = ChkCookies.IsChecked != true;
            bool wipeForm    = ChkForm.IsChecked    != true;

            var extras = new List<string>();
            if (wipePw)      extras.Add(T("del_pw"));
            if (wipeHist)    extras.Add(T("del_hist"));
            if (wipeCookies) extras.Add(T("del_cookies"));
            if (wipeForm)    extras.Add(T("del_autofill"));

            if (extras.Count > 0)
            {
                string msg = T("del_pre") + string.Join("\n  - ", extras) + T("del_suf");
                if (MessageBox.Show(Win, msg, T("del_title"),
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
                    string msg = string.Format(T("running_msg"), info.Def.Name);
                    if (MessageBox.Show(Win, msg, "CacheFlow",
                            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                        continue;
                }

                StatusText.Text = string.Format(T("clearing"), info.Def.Name);
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
                StatusText.Text = string.Format(T("done_freed"), FormatSize(freed), cleared);
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

            Find<TextBlock>(dn, "DnTitleText").Text = T("donate_title");
            Find<TextBlock>(dn, "DnMsg").Text       = T("donate_msg");
            Find<TextBlock>(dn, "DnEthNote").Text   = T("donate_eth");

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

        // ── Update check ────────────────────────────────────────────────────
        static async Task CheckForUpdatesAsync(bool manual)
        {
            if (!manual)
            {
                string autoUpd;
                if (State.TryGetValue("auto_check_updates", out autoUpd) && autoUpd == "false") return;
                string lastStr;
                if (State.TryGetValue("update_check_utc", out lastStr))
                {
                    DateTime last;
                    if (DateTime.TryParse(lastStr, null, DateTimeStyles.RoundtripKind, out last))
                        if (DateTime.UtcNow - last < TimeSpan.FromHours(20)) return;
                }
            }

            UpdateManifest m = null;
            await Task.Run(delegate { try { m = FetchManifest(); } catch { } });
            State["update_check_utc"] = DateTime.UtcNow.ToString("o");
            SaveState();

            if (m == null || string.IsNullOrEmpty(m.Version))
            {
                if (manual)
                    MessageBox.Show(Win, T("upd_none"), "CacheFlow", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (IsNewerVersion(m.Version, AppVersion))
                ShowUpdateDialog(m);
            else if (manual)
                MessageBox.Show(Win, string.Format(T("upd_latest"), AppVersion), "CacheFlow", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        static UpdateManifest FetchManifest()
        {
            using (var wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.UserAgent] = "CacheFlow/" + AppVersion + " (update check)";
                wc.Headers[HttpRequestHeader.CacheControl] = "no-cache";
                string json = wc.DownloadString(UpdateManifestUrl);
                var jss = new JavaScriptSerializer();
                var root = jss.Deserialize<Dictionary<string, object>>(json);
                object appObj;
                if (root == null || !root.TryGetValue(UpdateAppKey, out appObj)) return null;
                var app = appObj as Dictionary<string, object>;
                if (app == null) return null;
                return new UpdateManifest
                {
                    Version = JsonStr(app, "version"),
                    Url     = JsonStr(app, "download"),
                    Sha256  = JsonStr(app, "sha256"),
                    Stage   = JsonStr(app, "stage"),
                    Page    = JsonStr(app, "page")
                };
            }
        }

        static string JsonStr(Dictionary<string, object> d, string key)
        {
            object v;
            return (d.TryGetValue(key, out v) && v != null) ? v.ToString() : null;
        }

        static bool IsNewerVersion(string remote, string local)
        {
            try
            {
                System.Version rv, lv;
                if (!System.Version.TryParse(NumericVersion(remote), out rv)) return false;
                if (!System.Version.TryParse(NumericVersion(local),  out lv)) return false;
                return rv > lv;
            }
            catch { return false; }
        }

        static string NumericVersion(string v)
        {
            if (string.IsNullOrEmpty(v)) return v;
            int i = v.IndexOfAny(new char[] { '-', ' ', '+' });
            return i >= 0 ? v.Substring(0, i) : v;
        }

        static void ShowUpdateDialog(UpdateManifest m)
        {
            PendingUpdate = m;

            // show footer badge
            if (UpdateLink != null)
            {
                Run r = UpdateLink.Inlines.FirstInline as Run;
                if (r != null) r.Text = "CacheFlow " + m.Version + " is available — click to update";
                TextBlock bar = UpdateLink.Parent as TextBlock;
                if (bar != null) bar.Visibility = Visibility.Visible;
            }

            var uw = (Window)XamlReader.Parse(UpdateXaml);
            uw.Owner = Win;

            var updMsg     = Find<TextBlock>(uw, "UpdMsg");
            var updPageRow = Find<TextBlock>(uw, "UpdPageRow");
            var updPage    = (Hyperlink)uw.FindName("UpdPage");
            var updTitle   = Find<Grid>(uw, "UpdTitle");
            var updClose   = Find<Button>(uw, "UpdClose");
            var updLater   = Find<Button>(uw, "UpdLater");
            var updNow     = Find<Button>(uw, "UpdNow");

            Find<TextBlock>(uw, "UpdTitleText").Text = T("upd_title_text");
            Find<TextBlock>(uw, "UpdNote").Text      = T("upd_note");
            updLater.Content = T("upd_later");
            updNow.Content   = T("upd_now");
            var pr = updPage.Inlines.FirstInline as Run;
            if (pr != null) pr.Text = T("upd_see_changes");

            string label = m.Version;
            if (!string.IsNullOrEmpty(m.Stage)) label = label + " (" + m.Stage + ")";
            updMsg.Text = "CacheFlow " + label + " is available. You currently have v" + AppVersion + ".";

            if (!string.IsNullOrEmpty(m.Page))
            {
                updPageRow.Visibility = Visibility.Visible;
                updPage.Click += delegate(object s, RoutedEventArgs e) { OpenUrl(m.Page); };
            }

            updTitle.MouseLeftButtonDown += delegate { try { uw.DragMove(); } catch { } };
            updClose.Click += delegate { uw.Close(); };
            updLater.Click += delegate { uw.Close(); };
            updNow.Click   += delegate { uw.Close(); ApplyUpdate(m); };

            uw.ShowDialog();
        }

        static async void ApplyUpdate(UpdateManifest m)
        {
            // progress window
            var pw = new Window
            {
                Title = "CacheFlow Update", Width = 300, Height = 100,
                WindowStyle = WindowStyle.ToolWindow, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Win,
                ShowInTaskbar = false
            };
            var pb = new ProgressBar { IsIndeterminate = true, Margin = new Thickness(20, 20, 20, 10) };
            var lbl = new TextBlock { Text = T("upd_downloading"), HorizontalAlignment = HorizontalAlignment.Center,
                                      Foreground = Brush("#8A91A3"), FontSize = 12 };
            var sp = new StackPanel();
            sp.Children.Add(pb);
            sp.Children.Add(lbl);
            pw.Content = sp;
            pw.Show();

            string tempZip = null, tempDir = null;
            try
            {
                // 1. Download
                tempZip = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
                await Task.Run(delegate
                {
                    using (var wc = new WebClient())
                    {
                        wc.Headers[HttpRequestHeader.UserAgent] = "CacheFlow/" + AppVersion + " (update)";
                        wc.DownloadFile(m.Url, tempZip);
                    }
                });

                // 2. Verify SHA-256
                lbl.Text = T("upd_verifying");
                string hash = null;
                await Task.Run(delegate
                {
                    using (var sha = SHA256.Create())
                    using (var fs = File.OpenRead(tempZip))
                    {
                        byte[] bytes = sha.ComputeHash(fs);
                        var sb2 = new StringBuilder(64);
                        foreach (byte b in bytes) sb2.Append(b.ToString("x2"));
                        hash = sb2.ToString();
                    }
                });

                if (!string.Equals(hash, m.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    pw.Close();
                    try { File.Delete(tempZip); } catch { }
                    MessageBox.Show(Win, T("upd_checksum"), "CacheFlow", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 3. Extract
                lbl.Text = T("upd_extracting");
                tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
                await Task.Run(delegate { ZipFile.ExtractToDirectory(tempZip, tempDir); });

                // 4. Locate exe in staging folder
                string newExe = Path.Combine(tempDir, "CacheFlow.exe");
                if (!File.Exists(newExe))
                {
                    string[] found = Directory.GetFiles(tempDir, "CacheFlow.exe", SearchOption.AllDirectories);
                    newExe = found.Length > 0 ? found[0] : null;
                }
                if (newExe == null)
                {
                    pw.Close();
                    MessageBox.Show(Win, T("upd_no_exe"), "CacheFlow", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                string stageDir  = Path.GetDirectoryName(newExe);
                string installDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
                string exePath   = Path.Combine(installDir, "CacheFlow.exe");

                // 5. Write updater bat
                string batPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".bat");
                string bat =
                    "@echo off\r\n" +
                    ":wait\r\n" +
                    "tasklist /fi \"imagename eq CacheFlow.exe\" | find /i \"CacheFlow.exe\" >nul 2>&1\r\n" +
                    "if not errorlevel 1 (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
                    "xcopy /y /e /i \"" + stageDir + "\\*\" \"" + installDir + "\\\"\r\n" +
                    "start \"\" \"" + exePath + "\"\r\n" +
                    "del \"%~f0\"\r\n";
                File.WriteAllText(batPath, bat, Encoding.ASCII);

                // 6. Launch bat and shut down
                Process.Start(new ProcessStartInfo(batPath)
                {
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                pw.Close();
                ExitApp();
            }
            catch (Exception ex)
            {
                try { pw.Close(); } catch { }
                MessageBox.Show(Win, string.Format(T("upd_fail"), ex.Message), "CacheFlow", MessageBoxButton.OK, MessageBoxImage.Error);
                if (tempZip != null) try { File.Delete(tempZip); } catch { }
                if (tempDir != null) try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
