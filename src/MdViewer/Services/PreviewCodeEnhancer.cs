using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ColorCode;
using ColorCode.Styling;

namespace MdViewer.Services;

/// <summary>
/// Shared language aliases, keywords and framework annotations (Spring, Lombok, JPA, React…).
/// </summary>
public static class LanguageProfiles
{
    public static string Normalize(string? language)
    {
        var key = (language ?? string.Empty).Trim().ToLowerInvariant();
        return key switch
        {
            "cs" or "c#" or "csharp" => "csharp",
            "js" or "javascript" or "mjs" or "cjs" => "javascript",
            "jsx" => "jsx",
            "ts" or "typescript" => "typescript",
            "tsx" => "tsx",
            "py" or "python" => "python",
            "htm" or "html" => "html",
            "yml" or "yaml" => "yaml",
            "sh" or "bash" or "shell" or "zsh" => "bash",
            "ps1" or "powershell" => "powershell",
            "c++" or "cpp" or "cc" or "cxx" or "hpp" or "h" => "cpp",
            "rb" or "ruby" => "ruby",
            "rs" or "rust" => "rust",
            "go" or "golang" => "go",
            "kt" or "kotlin" => "kotlin",
            "java" or "spring" or "springboot" => "java",
            "sql" or "pgsql" or "postgres" or "postgresql" or "plsql" => "sql",
            "dockerfile" or "docker" => "docker",
            "gradle" or "groovy" => "groovy",
            "xml" or "pom" or "svg" or "xaml" => "xml",
            "json" => "json",
            "properties" or "props" => "properties",
            "md" or "markdown" => "markdown",
            _ => key
        };
    }

    public static ILanguage? ResolveColorCodeLanguage(string? language)
    {
        var key = Normalize(language);
        return key switch
        {
            "csharp" => Languages.CSharp,
            "javascript" or "jsx" or "json" => Languages.JavaScript,
            "typescript" or "tsx" => Languages.Typescript,
            "java" or "kotlin" or "groovy" => Languages.Java,
            "sql" => Languages.Sql,
            "html" => Languages.Html,
            "xml" => Languages.Xml,
            "css" or "scss" or "less" => Languages.Css,
            "powershell" => Languages.PowerShell,
            "php" => Languages.Php,
            "cpp" or "c" => Languages.Cpp,
            "haskell" => Languages.Haskell,
            "fsharp" => Languages.FSharp,
            "vb" => Languages.VbDotNet,
            "python" => Languages.Python,
            _ => null
        };
    }

    public static bool SupportsAnnotations(string normalizedLanguage) =>
        normalizedLanguage is "java" or "kotlin" or "groovy" or "csharp";

    public static HashSet<string> GetKeywords(string normalizedLanguage) => normalizedLanguage switch
    {
        "csharp" =>
        [
            "abstract","as","base","bool","break","byte","case","catch","char","checked","class","const","continue",
            "decimal","default","delegate","do","double","else","enum","event","explicit","extern","false","finally",
            "fixed","float","for","foreach","goto","if","implicit","in","int","interface","internal","is","lock",
            "long","namespace","new","null","object","operator","out","override","params","private","protected",
            "public","readonly","ref","return","sbyte","sealed","short","sizeof","stackalloc","static","string",
            "struct","switch","this","throw","true","try","typeof","uint","ulong","unchecked","unsafe","ushort",
            "using","virtual","void","volatile","while","var","async","await","record","required","init","nint","nuint"
        ],
        "javascript" or "typescript" or "jsx" or "tsx" =>
        [
            "break","case","catch","class","const","continue","debugger","default","delete","do","else","export",
            "extends","false","finally","for","function","if","import","in","instanceof","let","new","null",
            "return","super","switch","this","throw","true","try","typeof","var","void","while","with","yield",
            "async","await","of","from","as","type","interface","enum","implements","private","public","protected",
            "readonly","undefined"
        ],
        "python" =>
        [
            "False","None","True","and","as","assert","async","await","break","class","continue","def","del","elif",
            "else","except","finally","for","from","global","if","import","in","is","lambda","nonlocal","not","or",
            "pass","raise","return","try","while","with","yield","match","case"
        ],
        "java" or "kotlin" or "groovy" =>
        [
            "abstract","assert","boolean","break","byte","case","catch","char","class","const","continue","default",
            "do","double","else","enum","extends","final","finally","float","for","goto","if","implements","import",
            "instanceof","int","interface","long","native","new","package","private","protected","public","return",
            "short","static","strictfp","super","switch","synchronized","this","throw","throws","transient","try",
            "void","volatile","while","true","false","null","var","record","sealed","permits","non-sealed",
            // Kotlin extras
            "fun","val","data","object","companion","when","is","in","typealias","suspend","override","open","inner"
        ],
        "sql" =>
        [
            "select","from","where","insert","into","values","update","set","delete","create","table","alter","drop",
            "join","left","right","inner","outer","full","cross","on","and","or","not","null","as","order","by",
            "group","having","limit","offset","distinct","union","all","primary","key","foreign","references",
            "index","view","with","recursive","returning","conflict","upsert","serial","bigserial","uuid","jsonb",
            "json","text","varchar","boolean","integer","bigint","numeric","timestamp","timestamptz","date","time",
            "constraint","unique","check","default","cascade","restrict","schema","database","grant","revoke",
            "begin","commit","rollback","transaction","explain","analyze","vacuum","materialized","lateral"
        ],
        "docker" =>
        [
            "FROM","AS","RUN","CMD","LABEL","EXPOSE","ENV","ADD","COPY","ENTRYPOINT","VOLUME","USER","WORKDIR",
            "ARG","ONBUILD","STOPSIGNAL","HEALTHCHECK","SHELL","MAINTAINER"
        ],
        "bash" or "powershell" =>
        [
            "if","then","else","elif","fi","for","while","do","done","case","esac","function","return","in",
            "echo","exit","export","local","readonly","select","until","time","coproc"
        ],
        "go" =>
        [
            "break","case","chan","const","continue","default","defer","else","fallthrough","for","func","go","goto",
            "if","import","interface","map","package","range","return","select","struct","switch","type","var"
        ],
        "rust" =>
        [
            "as","async","await","break","const","continue","crate","dyn","else","enum","extern","false","fn","for",
            "if","impl","in","let","loop","match","mod","move","mut","pub","ref","return","self","Self","static",
            "struct","super","trait","true","type","unsafe","use","where","while"
        ],
        "yaml" or "properties" =>
        [
            "true","false","null","yes","no","on","off"
        ],
        _ => []
    };

    /// <summary>Framework / library annotations and decorators to emphasize.</summary>
    public static HashSet<string> GetAnnotations(string normalizedLanguage)
    {
        if (normalizedLanguage is "java" or "kotlin" or "groovy")
        {
            return
            [
                // Spring Web / Boot
                "SpringBootApplication","RestController","Controller","Service","Repository","Component",
                "Configuration","Bean","Autowired","Qualifier","Value","Primary","Lazy","Profile",
                "RequestMapping","GetMapping","PostMapping","PutMapping","DeleteMapping","PatchMapping",
                "RequestBody","ResponseBody","PathVariable","RequestParam","RequestHeader","CrossOrigin",
                "ResponseStatus","ExceptionHandler","ControllerAdvice","RestControllerAdvice",
                "EnableAutoConfiguration","EnableWebSecurity","EnableMethodSecurity","EnableJpaRepositories",
                "Transactional","Scheduled","Async","Cacheable","CacheEvict","EventListener",
                "Valid","Validated","NotNull","NotBlank","NotEmpty","Size","Min","Max","Email","Pattern",
                // JPA / Hibernate
                "Entity","Table","Id","GeneratedValue","Column","JoinColumn","OneToMany","ManyToOne",
                "OneToOne","ManyToMany","Enumerated","Embedded","Embeddable","Transient","Lob","Query",
                "Modifying","Param","EntityListeners","CreatedDate","LastModifiedDate",
                // Lombok
                "Data","Builder","Getter","Setter","ToString","EqualsAndHashCode","AllArgsConstructor",
                "NoArgsConstructor","RequiredArgsConstructor","Slf4j","Log","Value","With","SuperBuilder",
                "Accessors","Singular","NonNull","SneakyThrows","UtilityClass",
                // Testing
                "Test","BeforeEach","AfterEach","DisplayName","MockBean","Mock","InjectMocks","ExtendWith",
                "SpringBootTest","WebMvcTest","DataJpaTest","MockitoExtension"
            ];
        }

        if (normalizedLanguage is "csharp")
        {
            return
            [
                "ApiController","Route","HttpGet","HttpPost","HttpPut","HttpDelete","HttpPatch",
                "FromBody","FromQuery","FromRoute","FromHeader","FromServices","Authorize","AllowAnonymous",
                "Produces","Consumes","ProducesResponseType","Injectable","Inject","Service","Controller",
                "ObservableObject","RelayCommand","JsonPropertyName","Required","MaxLength","MinLength","Range","EmailAddress"
            ];
        }

        if (normalizedLanguage is "javascript" or "typescript" or "jsx" or "tsx")
        {
            // JS decorators / common React identifiers treated as emphasis targets
            return
            [
                "Component","Injectable","NgModule","Input","Output","HostListener","Prop","State",
                "useState","useEffect","useMemo","useCallback","useRef","useContext","useReducer","memo","forwardRef"
            ];
        }

        return [];
    }
}

/// <summary>
/// Offline preview highlighter (ColorCode) + annotation emphasis. No CDN required.
/// </summary>
public static class PreviewCodeEnhancer
{
    private static readonly Regex CodeBlockRegex = new(
        @"<pre[^>]*>\s*<code(?:\s+[^>]*?\bclass\s*=\s*[""'](?:[^""']*\s)?language-([^""'\s]+)[^""']*[""'][^>]*)?>(.*?)</code>\s*</pre>",
        RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Enhance(string html, bool darkTheme)
    {
        var style = darkTheme ? PastelDark : PastelLight;
        var formatter = new HtmlFormatter(style);

        return CodeBlockRegex.Replace(html, match =>
        {
            var language = match.Groups[1].Success ? match.Groups[1].Value : string.Empty;
            var normalized = LanguageProfiles.Normalize(language);
            var encoded = match.Groups[2].Value;
            var code = WebUtility.HtmlDecode(encoded);

            if (normalized is "mermaid")
                return match.Value;

            try
            {
                var colorLanguage = LanguageProfiles.ResolveColorCodeLanguage(normalized);
                string highlighted;

                if (colorLanguage is not null)
                {
                    highlighted = formatter.GetHtmlString(code, colorLanguage);
                    highlighted = UnwrapColorCode(highlighted);
                }
                else if (ShouldHeuristicHighlight(normalized))
                {
                    highlighted = HeuristicHighlight(code, normalized, darkTheme);
                }
                else
                {
                    // Plain fences / ASCII diagrams: escape only — never highlight encoded HTML.
                    highlighted = WebUtility.HtmlEncode(code);
                }

                highlighted = EmphasizeAnnotations(highlighted, normalized, darkTheme);
                var langClass = string.IsNullOrWhiteSpace(language) ? "" : $" language-{WebUtility.HtmlEncode(language)}";
                return $"<pre class=\"code-highlight\"><code class=\"hljs{langClass}\">{highlighted}</code></pre>";
            }
            catch
            {
                return $"<pre class=\"code-highlight\"><code class=\"hljs\">{WebUtility.HtmlEncode(code)}</code></pre>";
            }
        });
    }

    private static readonly StyleDictionary PastelLight = CreatePastel(dark: false);
    private static readonly StyleDictionary PastelDark = CreatePastel(dark: true);

    private static StyleDictionary CreatePastel(bool dark)
    {
        var source = dark ? StyleDictionary.DefaultDark : StyleDictionary.DefaultLight;
        var map = dark
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Plain Text"] = "#C5C0D0",
                ["Comment"] = "#7A7488",
                ["HTML Comment"] = "#7A7488",
                ["XML Comment"] = "#7A7488",
                ["XML Doc Comment"] = "#7A7488",
                ["String"] = "#B0C4D8",
                ["String (C# @ Verbatim)"] = "#B0C4D8",
                ["Json String"] = "#B0C4D8",
                ["Keyword"] = "#D4B0B8",
                ["Control Keyword"] = "#D4B0B8",
                ["Preprocessor Keyword"] = "#D4B0B8",
                ["Number"] = "#A8B4D4",
                ["Json Number"] = "#A8B4D4",
                ["Type"] = "#D4C0A0",
                ["Class Name"] = "#D4C0A0",
                ["Attribute"] = "#E0C8A8",
                ["Name Space"] = "#C5B8D8",
                ["Constructor"] = "#C5B8D8",
                ["Predefined"] = "#D4B0B8",
                ["Pseudo Keyword"] = "#D4B0B8",
                ["Built In Function"] = "#B8C8A8",
                ["Built In Value"] = "#B8C8A8",
                ["SQL System Function"] = "#B8C8A8",
                ["Json Key"] = "#D4C0A0",
                ["Json Const"] = "#D4B0B8",
                ["HTML Element ScopeName"] = "#D4B0B8",
                ["Html Tag Delimiter"] = "#A8B4D4",
                ["HTML Attribute ScopeName"] = "#D4C0A0",
                ["HTML Attribute Value"] = "#B0C4D8",
                ["XML Name"] = "#D4B0B8",
                ["XML Attribute"] = "#D4C0A0",
                ["XML Attribute Value"] = "#B0C4D8",
                ["XML Delimiter"] = "#A8B4D4",
                ["CSS Selector"] = "#D4B0B8",
                ["CSS Property Name"] = "#D4C0A0",
                ["CSS Property Value"] = "#B0C4D8",
            }
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Plain Text"] = "#5A6270",
                ["Comment"] = "#B0A8BC",
                ["HTML Comment"] = "#B0A8BC",
                ["XML Comment"] = "#B0A8BC",
                ["XML Doc Comment"] = "#B0A8BC",
                ["String"] = "#7D92B8",
                ["String (C# @ Verbatim)"] = "#7D92B8",
                ["Json String"] = "#7D92B8",
                ["Keyword"] = "#C4A0A8",
                ["Control Keyword"] = "#C4A0A8",
                ["Preprocessor Keyword"] = "#C4A0A8",
                ["Number"] = "#8B9DC3",
                ["Json Number"] = "#8B9DC3",
                ["Type"] = "#A89070",
                ["Class Name"] = "#A89070",
                ["Attribute"] = "#B89A78",
                ["Name Space"] = "#9A8AB0",
                ["Constructor"] = "#9A8AB0",
                ["Predefined"] = "#C4A0A8",
                ["Pseudo Keyword"] = "#C4A0A8",
                ["Built In Function"] = "#8A9A78",
                ["Built In Value"] = "#8A9A78",
                ["SQL System Function"] = "#8A9A78",
                ["Json Key"] = "#A89070",
                ["Json Const"] = "#C4A0A8",
                ["HTML Element ScopeName"] = "#C4A0A8",
                ["Html Tag Delimiter"] = "#8B9DC3",
                ["HTML Attribute ScopeName"] = "#A89070",
                ["HTML Attribute Value"] = "#7D92B8",
                ["XML Name"] = "#C4A0A8",
                ["XML Attribute"] = "#A89070",
                ["XML Attribute Value"] = "#7D92B8",
                ["XML Delimiter"] = "#8B9DC3",
                ["CSS Selector"] = "#C4A0A8",
                ["CSS Property Name"] = "#A89070",
                ["CSS Property Value"] = "#7D92B8",
            };

        var result = new StyleDictionary();
        foreach (var style in source)
        {
            var clone = new Style(style.ScopeName)
            {
                Foreground = map.TryGetValue(style.ScopeName, out var fg) ? fg : style.Foreground,
                Background = style.Background,
                Bold = style.Bold,
                Italic = style.Italic,
                ReferenceName = style.ReferenceName
            };
            result.Add(clone);
        }

        return result;
    }

    private static bool ShouldHeuristicHighlight(string language) =>
        language is "docker" or "bash" or "powershell" or "yaml" or "properties"
            or "go" or "rust" or "ruby" or "kotlin" or "groovy"
        || LanguageProfiles.GetKeywords(language).Count > 0;

    private static string UnwrapColorCode(string colorCodeHtml)
    {
        // ColorCode wraps as <div ...><pre>...</pre></div>
        var pre = Regex.Match(colorCodeHtml, @"<pre[^>]*>(.*?)</pre>", RegexOptions.Singleline);
        return pre.Success ? pre.Groups[1].Value : colorCodeHtml;
    }

    /// <summary>
    /// Highlight on plain text first, then HTML-encode. Never run regex on encoded entities
    /// (that would split &#233; on '#' / digits and show entities literally in the preview).
    /// </summary>
    private static string HeuristicHighlight(string code, string language, bool dark)
    {
        var keywordColor = dark ? "#D4B0B8" : "#C4A0A8";
        var stringColor = dark ? "#B0C4D8" : "#7D92B8";
        var commentColor = dark ? "#7A7488" : "#B0A8BC";
        var numberColor = dark ? "#A8B4D4" : "#8B9DC3";

        var spans = new List<(int Start, int End, string Color)>();
        var occupied = new bool[code.Length];

        void Mark(Match m, string color)
        {
            if (m.Length == 0 || m.Index < 0 || m.Index + m.Length > code.Length) return;
            for (var i = m.Index; i < m.Index + m.Length; i++)
                if (occupied[i]) return;
            for (var i = m.Index; i < m.Index + m.Length; i++)
                occupied[i] = true;
            spans.Add((m.Index, m.Index + m.Length, color));
        }

        var hashComments = language is "bash" or "powershell" or "python" or "yaml" or "properties" or "docker" or "ruby";
        var commentPattern = hashComments
            ? @"//.*?$|/\*.*?\*/|#.*?$"
            : @"//.*?$|/\*.*?\*/";

        foreach (Match m in Regex.Matches(code, commentPattern, RegexOptions.Multiline | RegexOptions.Singleline))
            Mark(m, commentColor);

        foreach (Match m in Regex.Matches(code, @"(""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'|`(?:\\.|[^`\\])*`)"))
            Mark(m, stringColor);

        foreach (Match m in Regex.Matches(code, @"\b\d+(\.\d+)?\b"))
            Mark(m, numberColor);

        var keywords = LanguageProfiles.GetKeywords(language);
        if (keywords.Count > 0)
        {
            var ignoreCase = language is "sql" or "docker" or "yaml" or "properties" or "bash" or "powershell"
                ? RegexOptions.IgnoreCase
                : RegexOptions.None;
            var pattern = $@"\b(?:{string.Join("|", keywords.Select(Regex.Escape))})\b";
            foreach (Match m in Regex.Matches(code, pattern, ignoreCase))
                Mark(m, keywordColor);
        }

        spans.Sort((a, b) => a.Start.CompareTo(b.Start));

        var sb = new StringBuilder(code.Length + spans.Count * 40);
        var cursor = 0;
        foreach (var (start, end, color) in spans)
        {
            if (start < cursor) continue;
            if (start > cursor)
                sb.Append(WebUtility.HtmlEncode(code[cursor..start]));
            sb.Append("<span style=\"color:").Append(color).Append("\">");
            sb.Append(WebUtility.HtmlEncode(code[start..end]));
            sb.Append("</span>");
            cursor = end;
        }

        if (cursor < code.Length)
            sb.Append(WebUtility.HtmlEncode(code[cursor..]));

        return sb.ToString();
    }

    private static string EmphasizeAnnotations(string highlightedHtml, string language, bool dark)
    {
        var annotationColor = dark ? "#E0C8A8" : "#B89A78";
        var known = LanguageProfiles.GetAnnotations(language);

        if (LanguageProfiles.SupportsAnnotations(language))
        {
            if (language is "csharp")
            {
                highlightedHtml = TransformTextNodes(highlightedHtml, text =>
                    Regex.Replace(
                        text,
                        @"\[([A-Za-z_][\w]*)(?:Attribute)?((?:\s*\([^)\]]*\))?)]",
                        m =>
                        {
                            var simple = m.Groups[1].Value;
                            if (simple.EndsWith("Attribute", StringComparison.Ordinal))
                                simple = simple[..^"Attribute".Length];
                            if (!known.Contains(simple))
                                return m.Value;
                            return $"<span style=\"color:{annotationColor};font-weight:600\">{m.Value}</span>";
                        }));
            }
            else
            {
                highlightedHtml = TransformTextNodes(highlightedHtml, text =>
                    Regex.Replace(
                        text,
                        @"(@[A-Za-z_][\w]*(?:\.[A-Za-z_][\w]*)*)",
                        m =>
                        {
                            var name = m.Value.TrimStart('@');
                            var simple = name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;
                            var weight = known.Contains(simple) ? "600" : "500";
                            return $"<span style=\"color:{annotationColor};font-weight:{weight}\">{m.Value}</span>";
                        }));
            }
        }

        if ((language is "javascript" or "typescript" or "jsx" or "tsx") && known.Count > 0)
        {
            var pattern = $@"\b(?:{string.Join("|", known.Select(Regex.Escape))})\b";
            highlightedHtml = TransformTextNodes(highlightedHtml, text =>
                Regex.Replace(text, pattern,
                    m => $"<span style=\"color:{annotationColor};font-weight:600\">{m.Value}</span>"));
        }

        return highlightedHtml;
    }

    /// <summary>Apply a transform only to HTML text nodes (outside tags).</summary>
    private static string TransformTextNodes(string html, Func<string, string> transform)
    {
        var sb = new StringBuilder(html.Length + 64);
        var i = 0;
        while (i < html.Length)
        {
            if (html[i] == '<')
            {
                var end = html.IndexOf('>', i);
                if (end < 0)
                {
                    sb.Append(html.AsSpan(i));
                    break;
                }

                sb.Append(html, i, end - i + 1);
                i = end + 1;
                continue;
            }

            var next = html.IndexOf('<', i);
            if (next < 0) next = html.Length;
            var chunk = html[i..next];
            sb.Append(transform(chunk));
            i = next;
        }

        return sb.ToString();
    }
}
