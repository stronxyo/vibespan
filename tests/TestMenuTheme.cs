// Validates the runtime-parsed menu XAML without showing anything on screen.
//
// csc compiles MenuTheme.cs happily no matter what is inside that string constant - the markup
// is only ever exercised by XamlReader at run time. Without this check, a typo in a template
// would first surface as a silently unstyled menu on a user's machine.
//
// Builds no window and opens nothing: it parses the dictionary, then asks the resulting styles
// whether the templates and triggers actually resolved.
using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

class TestMenuTheme
{
    static int _fail;

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what);
        if (!ok) _fail++;
    }

    [STAThread]
    static int Main()
    {
        Console.WriteLine("menu theme");

        // The markup is a private const, so read it straight out of the compiled metadata.
        Assembly asm = Assembly.LoadFrom("Vibespan.exe");
        Type t = asm.GetType("Vibespan.MenuTheme");
        Check(t != null, "MenuTheme type present");
        if (t == null) return 1;

        FieldInfo f = t.GetField("Xaml", BindingFlags.NonPublic | BindingFlags.Static);
        Check(f != null, "Xaml constant reachable");
        if (f == null) return 1;

        string xaml = (string)f.GetRawConstantValue();
        Check(xaml.Length > 500, "markup is non-trivial (" + xaml.Length + " chars)");

        ResourceDictionary dict = null;
        try
        {
            dict = (ResourceDictionary)XamlReader.Parse(xaml);
            Check(true, "XAML parses");
        }
        catch (Exception e)
        {
            Check(false, "XAML parses -> " + e.Message);
            return 1;
        }

        // A style that parsed but whose Template setter failed would still yield a Style object,
        // so check the setters themselves rather than merely that lookup succeeded.
        Style ctx = dict[typeof(ContextMenu)] as Style;
        Check(ctx != null, "ContextMenu style present");
        Check(ctx != null && HasTemplate(ctx), "ContextMenu has a ControlTemplate");

        Style mi = dict[typeof(MenuItem)] as Style;
        Check(mi != null, "MenuItem style present");
        Check(mi != null && HasTemplate(mi), "MenuItem has a ControlTemplate");

        // Separators in a menu resolve through this key; an implicit Separator style is ignored.
        Check(dict.Contains(MenuItem.SeparatorStyleKey), "separator uses MenuItem.SeparatorStyleKey");

        ControlTemplate tpl = TemplateOf(mi);
        Check(tpl != null && tpl.Triggers.Count >= 5,
              "MenuItem template carries its triggers (" + (tpl == null ? 0 : tpl.Triggers.Count) + ")");

        // The submenu popup must keep the part name WPF looks for, or submenus never open -
        // which would be invisible in a compile and obvious only when clicking.
        Check(tpl != null && tpl.Resources != null, "template resources intact");
        Check(xaml.Contains("PART_Popup"), "submenu popup keeps the PART_Popup name");
        Check(xaml.Contains("ContentSource='Icon'"), "icon column bound (radio dots, swatches)");
        Check(xaml.Contains("ContentSource='Header'"), "header column bound");

        // Applying it to a real ContextMenu is the path the app actually takes.
        try
        {
            var menu = new ContextMenu();
            MethodInfo apply = t.GetMethod("Apply", BindingFlags.Public | BindingFlags.Static);
            apply.Invoke(null, new object[] { menu });
            Check(menu.Resources.MergedDictionaries.Count == 1, "Apply merges the dictionary");

            var item = new MenuItem { Header = "x" };
            menu.Items.Add(item);
            Check(menu.Items.Count == 1, "items still attach after theming");
        }
        catch (Exception e)
        {
            Check(false, "Apply on a live ContextMenu -> " + e.Message);
        }

        Console.WriteLine(_fail == 0 ? "\nall checks passed" : "\n" + _fail + " FAILED");
        return _fail == 0 ? 0 : 1;
    }

    static bool HasTemplate(Style s) { return TemplateOf(s) != null; }

    static ControlTemplate TemplateOf(Style s)
    {
        if (s == null) return null;
        foreach (SetterBase sb in s.Setters)
        {
            Setter st = sb as Setter;
            if (st != null && st.Property == Control.TemplateProperty)
                return st.Value as ControlTemplate;
        }
        return null;
    }
}
