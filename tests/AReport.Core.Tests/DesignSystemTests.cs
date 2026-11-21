using System.Text.RegularExpressions;

namespace AReport.Core.Tests;

/// <summary>
/// Enforces DESIGN.md R1 (no hard-coded colours or fonts outside Tokens.xaml) and R2 (every token named in
/// DESIGN.md exists). Plain text checks, so they run on any OS even though the designer itself is Windows-only.
/// </summary>
public class DesignSystemTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static readonly string DesignerDir = Path.Combine(Root, "src", "AReport.Designer");

    private static string FindRepositoryRoot()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("AREPORT_REPO_ROOT");
        if (!string.IsNullOrEmpty(fromEnvironment)) return fromEnvironment;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "A-Report.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not find the repository root (A-Report.slnx).");
    }

    private static IEnumerable<string> DesignerSources() =>
        Directory.EnumerateFiles(DesignerDir, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => !Path.GetFileName(f).Equals("Tokens.xaml", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Every_token_named_in_DESIGN_md_exists_in_Tokens_xaml()
    {
        var design = File.ReadAllText(Path.Combine(Root, "DESIGN.md"));
        var tokens = File.ReadAllText(Path.Combine(DesignerDir, "Themes", "Tokens.xaml"));

        var named = Regex.Matches(design, @"`((?:Color|Brush|Font|Space|Size|Radius|Inset)\.[A-Za-z0-9.]+)`")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        Assert.True(named.Count > 40, $"expected many tokens in DESIGN.md, found {named.Count}");

        var missing = named.Where(token => !tokens.Contains($"x:Key=\"{token}\"")).ToList();
        Assert.True(missing.Count == 0, "Tokens named in DESIGN.md but missing from Tokens.xaml: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_colour_token_has_a_matching_brush()
    {
        var tokens = File.ReadAllText(Path.Combine(DesignerDir, "Themes", "Tokens.xaml"));
        var colors = Regex.Matches(tokens, "x:Key=\"Color\\.([A-Za-z]+)\"").Select(m => m.Groups[1].Value).ToList();

        Assert.NotEmpty(colors);
        foreach (var color in colors)
            Assert.Contains($"x:Key=\"Brush.{color}\"", tokens);
    }

    [Fact]
    public void Views_and_code_contain_no_hard_coded_hex_colours()
    {
        var offenders = new List<string>();
        foreach (var file in DesignerSources())
        {
            var text = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(text, @"#(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6})\b"))
                offenders.Add($"{Path.GetRelativePath(Root, file)}: {match.Value}");
        }
        Assert.True(offenders.Count == 0, "Use a token from Tokens.xaml instead (DESIGN.md R1):\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void Xaml_does_not_set_font_family_or_size_literals()
    {
        var offenders = new List<string>();
        foreach (var file in DesignerSources().Where(f => f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)))
        {
            var text = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(text, "(?:FontFamily|FontSize)=\"(?!\\{)[^\"]+\""))
                offenders.Add($"{Path.GetRelativePath(Root, file)}: {match.Value}");
        }
        Assert.True(offenders.Count == 0, "Use Font.* tokens (DESIGN.md R1):\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void Controls_xaml_uses_only_tokens_for_brushes()
    {
        var controls = File.ReadAllText(Path.Combine(DesignerDir, "Themes", "Controls.xaml"));
        var literal = Regex.Matches(controls, "(?:Background|Foreground|BorderBrush|Stroke|Fill)=\"(?!\\{|Transparent\")[^\"]+\"");
        Assert.True(literal.Count == 0, "Literal brushes in Controls.xaml: " + string.Join(", ", literal.Select(m => m.Value)));
    }

    [Fact]
    public void The_documented_shortcuts_are_bound_in_the_main_window()
    {
        var window = File.ReadAllText(Path.Combine(DesignerDir, "Views", "MainWindow.xaml"));
        foreach (var command in new[] { "NewCommand", "OpenCommand", "SaveCommand", "SaveAsCommand", "UndoCommand", "RedoCommand", "DuplicateCommand", "PreviewCommand", "ZoomResetCommand", "ZoomInCommand", "ZoomOutCommand" })
            Assert.Contains($"Command=\"{{Binding {command}}}\"", window);
    }

    [Fact]
    public void Icon_only_or_symbol_buttons_have_automation_names()
    {
        var window = File.ReadAllText(Path.Combine(DesignerDir, "Views", "MainWindow.xaml"));
        foreach (Match button in Regex.Matches(window, "<Button [^>]*Content=\"(?:−|\\+)\"[^>]*>"))
            Assert.Contains("AutomationProperties.Name=", button.Value);
    }
}
