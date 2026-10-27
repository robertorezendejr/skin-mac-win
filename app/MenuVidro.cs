// Mac Skin MenuVidro: menu do botão direito da dock em Liquid Glass, como o indicador de volume: janela acrylic atrás
// (que desfoca o fundo) + menu transparente com brilho e borda de luz por cima. Abre ACIMA da dock, onde dá para clicar.
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace MacSkin
{
    static class MenuVidro
    {
        const double Raio = 16, Recuo = 4, Acima = 42;
        const string Xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style TargetType='ContextMenu'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='HasDropShadow' Value='False'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ContextMenu'>
      <Grid>
        <Border CornerRadius='16' BorderThickness='4' BorderBrush='{DynamicResource MvAro}' IsHitTestVisible='False'/>
        <Border CornerRadius='16' BorderThickness='1' BorderBrush='{DynamicResource MvBorda}' Background='{DynamicResource MvFundo}'>
          <StackPanel IsItemsHost='True' Margin='8' KeyboardNavigation.DirectionalNavigation='Cycle'/>
        </Border>
      </Grid>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='MenuItem'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='FontFamily' Value='Inter, Segoe UI'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Foreground' Value='{DynamicResource MvTexto}'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='MenuItem'>
      <Border x:Name='b' CornerRadius='9' Padding='10,7,18,7' Background='Transparent' SnapsToDevicePixels='True'>
        <Grid>
          <Grid.ColumnDefinitions><ColumnDefinition Width='18'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
          <TextBlock x:Name='ok' Text='&#x2713;' FontWeight='Bold' Visibility='Collapsed' VerticalAlignment='Center' Foreground='{TemplateBinding Foreground}'/>
          <ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True' VerticalAlignment='Center' TextElement.Foreground='{TemplateBinding Foreground}'/>
        </Grid>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsChecked' Value='True'><Setter TargetName='ok' Property='Visibility' Value='Visible'/></Trigger>
        <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='b' Property='Background' Value='{DynamicResource MvHover}'/></Trigger>
        <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='{x:Static MenuItem.SeparatorStyleKey}' TargetType='Separator'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Separator'>
      <Border Height='1' Margin='10,5' Background='{DynamicResource MvSep}'/>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
</ResourceDictionary>";

        static ResourceDictionary recursos;
        static Window vidro; static IntPtr hVidro;
        static bool? claro;

        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr depois, int x, int y, int w, int hh, uint f);

        // aplica o visual de vidro e a posição acima do alvo (centrado no ícone, ou no cursor quando é o fundo da dock)
        public static void Aplicar(ContextMenu m, bool centrarNoAlvo)
        {
            m.Resources = Recursos();
            m.Placement = PlacementMode.Custom;
            m.CustomPopupPlacementCallback = (popup, alvo, desloc) =>
            {
                double x = centrarNoAlvo ? (alvo.Width - popup.Width) / 2 : Mouse.GetPosition(m.PlacementTarget).X - popup.Width / 2;
                return new[] { new CustomPopupPlacement(new Point(x, -popup.Height - Acima), PopupPrimaryAxis.None) };
            };
            m.Opened += delegate
            {
                Tema();
                m.Dispatcher.BeginInvoke(new Action(() => MostrarVidro(m)), DispatcherPriority.Loaded);
            };
            m.Closed += delegate { if (vidro != null) vidro.Hide(); };
        }

        static ResourceDictionary Recursos()
        {
            if (recursos == null) { recursos = (ResourceDictionary)XamlReader.Parse(Xaml); claro = null; Tema(); }
            return recursos;
        }

        // as cores seguem o tema do Windows; o acrylic é igual ao do volume
        static void Tema()
        {
            bool c = Comum.TemaClaro();
            if (c == claro) return;
            claro = c;
            recursos["MvAro"] = Comum.Cor(c ? "#8CEDEDEF" : "#8C3A3A3E");
            recursos["MvFundo"] = c ? Comum.Degrade("#55FFFFFF", "#14FFFFFF") : Comum.Degrade("#40FFFFFF", "#0EFFFFFF");
            recursos["MvBorda"] = c ? Comum.Degrade("#F2FFFFFF", "#66FFFFFF") : Comum.Degrade("#B3FFFFFF", "#33FFFFFF");
            recursos["MvTexto"] = Comum.Cor(c ? "#E6000000" : "#FFFFFFFF");
            recursos["MvHover"] = Comum.Cor(c ? "#26000000" : "#33FFFFFF");
            recursos["MvSep"] = Comum.Cor(c ? "#33000000" : "#40FFFFFF");
            if (hVidro != IntPtr.Zero) Win32.Vidro(hVidro, c ? 0x1EF8F6F6 : 0x24383030, true);
        }

        static void MostrarVidro(ContextMenu m)
        {
            try
            {
                if (!m.IsOpen) return;
                if (vidro == null)
                {
                    vidro = new Window
                    {
                        Title = "Mac Skin Menu vidro", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
                        Topmost = true, Background = Brushes.Transparent, Left = -10000, Top = -10000, Width = 10, Height = 10, ShowActivated = false
                    };
                    vidro.SourceInitialized += delegate
                    {
                        hVidro = new WindowInteropHelper(vidro).Handle;
                        HwndSource.FromHwnd(hVidro).CompositionTarget.BackgroundColor = Colors.Transparent;
                        Win32.Estilo(hVidro, Win32.EX_TOOL | Win32.EX_NOACTIVATE | Win32.EX_TRANSPARENT);
                    };
                }
                vidro.Show();
                claro = null; Tema();   // aplica o acrylic agora que a janela existe
                var fonte = (HwndSource)PresentationSource.FromVisual(m);
                if (fonte == null) return;
                double f = fonte.CompositionTarget.TransformToDevice.M22;
                // o WPF calcula a posição antes do menu ter o tamanho final: com ele pronto, encosta a base do menu
                // um pouco acima do topo do alvo (o topo do ícone ou da dock), onde a dock não cobre
                var alvo = m.PlacementTarget as FrameworkElement;
                if (alvo != null)
                {
                    var topoAlvo = alvo.PointToScreen(new Point(0, 0)).Y;
                    var base0 = m.PointToScreen(new Point(0, m.ActualHeight)).Y;
                    int dy = (int)(topoAlvo - Acima * f - base0);
                    if (Math.Abs(dy) > 1)
                    {
                        var topo = m.PointToScreen(new Point(0, 0));
                        SetWindowPos(fonte.Handle, IntPtr.Zero, (int)topo.X, (int)topo.Y + dy, 0, 0, 0x0001 | 0x0004 | 0x0010);   // sem tamanho, sem ordem, sem ativar
                    }
                }
                var p = m.PointToScreen(new Point(Recuo, Recuo));
                // logo abaixo do menu na ordem das janelas: o acrylic desfoca o fundo e o menu fica por cima
                SetWindowPos(hVidro, fonte.Handle, (int)p.X, (int)p.Y, (int)((m.ActualWidth - 2 * Recuo) * f), (int)((m.ActualHeight - 2 * Recuo) * f), 0x0010);
            }
            catch (Exception e) { Comum.Log("dock.log", "menu vidro: " + e.Message); }
        }
    }
}
