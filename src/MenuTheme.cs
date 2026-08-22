// Windows 11-style dark styling for the context menu.
//
// The templates are XAML, parsed at RUN time by XamlReader. That looks odd next to the rest of
// this codebase, which builds every visual by hand, and the reason is worth stating: csc.exe
// cannot compile XAML (that needs MSBuild and XamlBuildTask), but XamlReader.Parse is just an
// API, so the markup path stays open at run time. Expressing these ControlTemplates through
// FrameworkElementFactory instead would run to several hundred lines of unreadable setup for
// exactly the same result.
//
// Attribute values use SINGLE quotes throughout. XAML accepts them, and it keeps the C#
// verbatim string below free of doubled quote characters.
//
// A parse failure is never fatal: Apply swallows it and the menu falls back to the stock WPF
// look. A cosmetic feature must not be able to take the widget down.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace Vibespan
{
    public static class MenuTheme
    {
        // Windows 11 menu surface: near-black fill, hairline border, 8px corner, 4px row radius.
        const string Xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>

  <SolidColorBrush x:Key='Bg'  Color='#2C2C2C'/>
  <SolidColorBrush x:Key='Bd'  Color='#484848'/>
  <SolidColorBrush x:Key='Fg'  Color='#F2F2F2'/>
  <SolidColorBrush x:Key='Dim' Color='#9A9A9A'/>
  <SolidColorBrush x:Key='Dis' Color='#6B6B6B'/>
  <SolidColorBrush x:Key='Hi'  Color='#3D3D3D'/>
  <SolidColorBrush x:Key='Sep' Color='#3A3A3A'/>

  <Style TargetType='{x:Type ContextMenu}'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='SnapsToDevicePixels' Value='True'/>
    <Setter Property='HasDropShadow' Value='True'/>
    <Setter Property='FontFamily' Value='Segoe UI Variable Text, Segoe UI'/>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='Foreground' Value='{StaticResource Fg}'/>
    <!-- Cancels the 8px shadow margin below, so the menu still lands under the cursor. -->
    <Setter Property='HorizontalOffset' Value='-8'/>
    <Setter Property='VerticalOffset' Value='-8'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type ContextMenu}'>
          <Border Background='{StaticResource Bg}' BorderBrush='{StaticResource Bd}'
                  BorderThickness='1' CornerRadius='8' Padding='0,4' Margin='8'>
            <Border.Effect>
              <DropShadowEffect BlurRadius='16' ShadowDepth='3' Direction='270'
                                Opacity='0.55' Color='#000000'/>
            </Border.Effect>
            <ItemsPresenter/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Separators inside a menu are styled through this key, NOT through an implicit
       TargetType style: WPF looks up MenuItem.SeparatorStyleKey, and an implicit
       Separator style is simply never consulted. -->
  <Style x:Key='{x:Static MenuItem.SeparatorStyleKey}' TargetType='{x:Type Separator}'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type Separator}'>
          <Rectangle Height='1' Margin='12,5,12,5' Fill='{StaticResource Sep}'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='{x:Type MenuItem}'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='SnapsToDevicePixels' Value='True'/>
    <Setter Property='Foreground' Value='{StaticResource Fg}'/>
    <Setter Property='FontFamily' Value='Segoe UI Variable Text, Segoe UI'/>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='Height' Value='32'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type MenuItem}'>
          <Grid Background='Transparent'>
            <Border x:Name='Row' CornerRadius='4' Margin='4,0,4,0' Background='Transparent'/>
            <Grid Margin='4,0,4,0'>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width='32'/>
                <ColumnDefinition Width='*'/>
                <ColumnDefinition Width='Auto'/>
                <ColumnDefinition Width='22'/>
              </Grid.ColumnDefinitions>

              <ContentPresenter Grid.Column='0' ContentSource='Icon'
                                HorizontalAlignment='Center' VerticalAlignment='Center'/>
              <Path x:Name='Check' Grid.Column='0' Visibility='Collapsed'
                    Data='M0,4.2 L3.4,7.6 L9.6,0.6' Stroke='{StaticResource Fg}'
                    StrokeThickness='1.6' StrokeStartLineCap='Round' StrokeEndLineCap='Round'
                    HorizontalAlignment='Center' VerticalAlignment='Center'/>

              <ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True'
                                VerticalAlignment='Center' Margin='2,0,12,0'/>

              <TextBlock Grid.Column='2' Text='{TemplateBinding InputGestureText}'
                         Foreground='{StaticResource Dim}' VerticalAlignment='Center'
                         Margin='0,0,8,0'/>

              <Path x:Name='Arrow' Grid.Column='3' Visibility='Collapsed'
                    Data='M0,0 L4.5,4.5 L0,9' Stroke='{StaticResource Dim}' StrokeThickness='1.4'
                    StrokeStartLineCap='Round' StrokeEndLineCap='Round'
                    HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Grid>

            <Popup x:Name='PART_Popup' AllowsTransparency='True' Focusable='False'
                   Placement='Right' HorizontalOffset='-6' VerticalOffset='-10'
                   PopupAnimation='Fade'
                   IsOpen='{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}'>
              <Border Background='{StaticResource Bg}' BorderBrush='{StaticResource Bd}'
                      BorderThickness='1' CornerRadius='8' Padding='0,4' Margin='8'>
                <Border.Effect>
                  <DropShadowEffect BlurRadius='16' ShadowDepth='3' Direction='270'
                                    Opacity='0.55' Color='#000000'/>
                </Border.Effect>
                <ItemsPresenter/>
              </Border>
            </Popup>
          </Grid>

          <ControlTemplate.Triggers>
            <Trigger Property='IsHighlighted' Value='True'>
              <Setter TargetName='Row' Property='Background' Value='{StaticResource Hi}'/>
            </Trigger>
            <Trigger Property='IsSubmenuOpen' Value='True'>
              <Setter TargetName='Row' Property='Background' Value='{StaticResource Hi}'/>
            </Trigger>
            <Trigger Property='HasItems' Value='True'>
              <Setter TargetName='Arrow' Property='Visibility' Value='Visible'/>
            </Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='Check' Property='Visibility' Value='Visible'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Foreground' Value='{StaticResource Dis}'/>
              <Setter TargetName='Arrow' Property='Stroke' Value='{StaticResource Dis}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";

        static ResourceDictionary _cached;

        /// <summary>
        /// Brush for the radio dot. The old plain Gray was picked against a light menu and
        /// nearly vanishes on the dark surface.
        /// </summary>
        public static readonly Brush RadioDot = Frozen(Color.FromRgb(0xDA, 0x77, 0x56));

        static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public static void Apply(ContextMenu menu)
        {
            if (menu == null) return;
            try
            {
                // Parsed once. The dictionary is shared by the widget menu and the tray menu,
                // and by every rebuild after a settings change.
                if (_cached == null) _cached = (ResourceDictionary)XamlReader.Parse(Xaml);
                menu.Resources.MergedDictionaries.Add(_cached);
            }
            catch (Exception e)
            {
                Log.Write("menu theme unavailable, using the stock look: " + e.Message);
            }
        }
    }
}
